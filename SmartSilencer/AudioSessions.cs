using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace SmartSilencer
{
    /// <summary>One process that is putting sound through the default playback device right now.</summary>
    internal struct AudibleApp
    {
        public uint Pid;
        public string Name;   // the executable's name without ".exe", or "pid N" when it cannot be read
        public float Peak;    // 0..1, the session's peak sample level at the moment of the poll
    }

    /// <summary>
    /// Asks Windows which other programs are making sound, through the Windows Audio Session API:
    /// the same per-app list the volume mixer shows, with each app's live level meter.
    ///
    /// Everything here is raw COM through vtable calls (a function pointer per method) rather than
    /// [ComImport] interfaces, because that path needs nothing from the runtime's COM interop layer
    /// beyond P/Invoke, and so behaves the same under the game's Mono as it would anywhere else.
    /// Nothing is kept across polls except the session manager, which is re-fetched every so often
    /// so a change of default device (headphones plugged in) is picked up.
    ///
    /// On anything that is not Windows the first call fails, and after a few failures the class
    /// declares itself unavailable and stays quiet; the focus trigger does not depend on it.
    /// </summary>
    internal static class AudioSessions
    {
        // ── Win32 ────────────────────────────────────────────────────────────────────

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint clsContext, ref Guid iid, out IntPtr ppv);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        private static Guid CLSID_MMDeviceEnumerator  = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static Guid IID_IMMDeviceEnumerator   = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
        private static Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        private static Guid IID_IAudioSessionControl2 = new Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
        private static Guid IID_IAudioMeterInformation = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");

        private const uint CLSCTX_ALL = 0x17;
        private const int  eRender = 0, eConsole = 0;
        private const int  AudioSessionStateActive = 1;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int  RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

        // vtable slots, counted from IUnknown's three
        private const int IMMDeviceEnumerator_GetDefaultAudioEndpoint = 4;
        private const int IMMDevice_Activate = 3;
        private const int IAudioSessionManager2_GetSessionEnumerator = 5;
        private const int IAudioSessionEnumerator_GetCount = 3;
        private const int IAudioSessionEnumerator_GetSession = 4;
        private const int IAudioSessionControl_GetState = 3;
        private const int IAudioSessionControl2_GetProcessId = 14;
        private const int IAudioMeterInformation_GetPeakValue = 3;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDefaultAudioEndpointFn(IntPtr self, int dataFlow, int role, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ActivateFn(IntPtr self, ref Guid iid, uint clsCtx, IntPtr activationParams, out IntPtr iface);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutPtrFn(IntPtr self, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutIntFn(IntPtr self, out int result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutUIntFn(IntPtr self, out uint result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutFloatFn(IntPtr self, out float result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSessionFn(IntPtr self, int index, out IntPtr session);

        private static readonly Dictionary<IntPtr, Delegate> _methods = new Dictionary<IntPtr, Delegate>();

        private static T Method<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            if (!_methods.TryGetValue(fn, out var d) || !(d is T))
            {
                d = Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
                _methods[fn] = d;
            }
            return (T)(object)d;
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new COMException(what + " failed", hr);
        }

        private static void Release(ref IntPtr p)
        {
            if (p == IntPtr.Zero) return;
            Marshal.Release(p);
            p = IntPtr.Zero;
        }

        // ── state ────────────────────────────────────────────────────────────────────

        private static bool   _initialized;
        private static IntPtr _enumerator;      // IMMDeviceEnumerator, kept for the process lifetime
        private static IntPtr _manager;         // IAudioSessionManager2 for the current default device
        private static float  _managerTime = -1000f;
        private const  float  ManagerRefresh = 15f;   // seconds; how soon a new default device is noticed
        private static int    _failures;
        private static uint   _ourPid;

        private static readonly Dictionary<uint, string> _names = new Dictionary<uint, string>();
        private static float _namesCleared;
        private const  float NamesLifetime = 60f;     // seconds; pids get reused, so forget names now and then

        /// <summary>False once Windows audio could not be reached several times in a row (or this is not Windows).</summary>
        internal static bool Available { get; private set; } = true;

        /// <summary>Why it gave up, for the log and the status command.</summary>
        internal static string Failure { get; private set; }

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _ourPid = GetCurrentProcessId();
            // The game's main thread most likely has COM already; RPC_E_CHANGED_MODE says exactly
            // that and is fine, since everything used here is free-threaded.
            int hr = CoInitializeEx(IntPtr.Zero, 0 /* COINIT_MULTITHREADED */);
            if (hr < 0 && hr != RPC_E_CHANGED_MODE) Check(hr, "CoInitializeEx");
            Check(CoCreateInstance(ref CLSID_MMDeviceEnumerator, IntPtr.Zero, CLSCTX_ALL, ref IID_IMMDeviceEnumerator, out _enumerator), "CoCreateInstance(MMDeviceEnumerator)");
        }

        private static void RefreshManager()
        {
            Release(ref _manager);
            IntPtr device = IntPtr.Zero;
            try
            {
                Check(Method<GetDefaultAudioEndpointFn>(_enumerator, IMMDeviceEnumerator_GetDefaultAudioEndpoint)(_enumerator, eRender, eConsole, out device), "GetDefaultAudioEndpoint");
                Check(Method<ActivateFn>(device, IMMDevice_Activate)(device, ref IID_IAudioSessionManager2, CLSCTX_ALL, IntPtr.Zero, out _manager), "IMMDevice.Activate(IAudioSessionManager2)");
                _managerTime = Time.unscaledTime;
            }
            finally
            {
                Release(ref device);
            }
        }

        /// <summary>
        /// Fills <paramref name="result"/> with every other process whose audio session is active
        /// and whose level meter is at or above <paramref name="threshold"/>. Returns false when the
        /// poll could not be made, in which case the list is empty and the caller should treat the
        /// world as unknown rather than silent.
        /// </summary>
        internal static bool TryPoll(float threshold, List<AudibleApp> result)
        {
            result.Clear();
            if (!Available) return false;
            try
            {
                Initialize();
                if (_manager == IntPtr.Zero || Time.unscaledTime - _managerTime > ManagerRefresh) RefreshManager();
                if (Time.unscaledTime - _namesCleared > NamesLifetime) { _names.Clear(); _namesCleared = Time.unscaledTime; }

                IntPtr sessions = IntPtr.Zero;
                try
                {
                    Check(Method<OutPtrFn>(_manager, IAudioSessionManager2_GetSessionEnumerator)(_manager, out sessions), "GetSessionEnumerator");
                    Check(Method<OutIntFn>(sessions, IAudioSessionEnumerator_GetCount)(sessions, out int count), "GetCount");
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr session = IntPtr.Zero;
                        try
                        {
                            if (Method<GetSessionFn>(sessions, IAudioSessionEnumerator_GetSession)(sessions, i, out session) < 0) continue;
                            Inspect(session, threshold, result);
                        }
                        finally
                        {
                            Release(ref session);
                        }
                    }
                }
                finally
                {
                    Release(ref sessions);
                }
                _failures = 0;
                return true;
            }
            catch (Exception e)
            {
                result.Clear();
                Release(ref _manager);
                if (++_failures >= 5)
                {
                    Available = false;
                    Failure = e.GetType().Name + ": " + e.Message;
                    SmartSilencerMod.Log.LogWarning($"Cannot read Windows audio sessions ({Failure}). The music and video triggers are off for this run; tabbing out still works.");
                }
                return false;
            }
        }

        private static void Inspect(IntPtr session, float threshold, List<AudibleApp> result)
        {
            // Only sessions with a stream currently open. A paused player's session is Inactive,
            // a closed program's lingers as Expired; neither can make a sound.
            if (Method<OutIntFn>(session, IAudioSessionControl_GetState)(session, out int state) < 0 || state != AudioSessionStateActive) return;

            uint pid;
            IntPtr control2 = IntPtr.Zero;
            try
            {
                if (Marshal.QueryInterface(session, ref IID_IAudioSessionControl2, out control2) < 0) return;
                // AUDCLNT_S_NO_SINGLE_PROCESS is a success code; the pid is still the best one on offer.
                if (Method<OutUIntFn>(control2, IAudioSessionControl2_GetProcessId)(control2, out pid) < 0) return;
            }
            finally
            {
                Release(ref control2);
            }
            // pid 0 is Windows' own system-sounds session; our own process is the game.
            if (pid == 0 || pid == _ourPid) return;

            float peak;
            IntPtr meter = IntPtr.Zero;
            try
            {
                if (Marshal.QueryInterface(session, ref IID_IAudioMeterInformation, out meter) < 0) return;
                if (Method<OutFloatFn>(meter, IAudioMeterInformation_GetPeakValue)(meter, out peak) < 0) return;
            }
            finally
            {
                Release(ref meter);
            }
            if (peak < threshold) return;

            result.Add(new AudibleApp { Pid = pid, Name = NameOf(pid), Peak = peak });
        }

        /// <summary>The process's executable name without its extension, cached per pid.</summary>
        private static string NameOf(uint pid)
        {
            if (_names.TryGetValue(pid, out var known)) return known;
            string name = "pid " + pid;
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var path = new StringBuilder(1024);
                    uint size = (uint)path.Capacity;
                    if (QueryFullProcessImageNameW(handle, 0, path, ref size))
                    {
                        try { name = Path.GetFileNameWithoutExtension(path.ToString()); }
                        catch (ArgumentException) { name = path.ToString(); }
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            _names[pid] = name;
            return name;
        }
    }
}
