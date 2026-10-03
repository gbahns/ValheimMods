using System;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// What this machine actually is, gathered once.
    ///
    /// Every other measurement here is a rate or a duration, and comparing two machines' rates
    /// without knowing what they are invites the wrong conclusion. One capture had two players
    /// doing identical CPU work - 222% of a core against 219% - for 27 ms frames and 52 ms frames,
    /// which looked damning until it turned out one of them was rendering 5120x1440 with texture
    /// mods and near-maximum settings. Same work in, half the frames out, is a hardware finding or
    /// a settings finding depending entirely on facts this mod was not recording.
    ///
    /// Static for the session, so it travels in the capture's header rather than in every sample:
    /// a GPU name repeated eighteen hundred times is eighteen hundred copies of one fact.
    ///
    /// Everything is best-effort. A machine that will not answer one of these reports an empty
    /// string or zero, which reads as "not known" rather than as a measurement.
    /// </summary>
    internal static class Hardware
    {
        internal static string Gpu { get; private set; } = "";
        internal static int VramMB { get; private set; }
        internal static string Cpu { get; private set; } = "";
        internal static int Cores { get; private set; }
        internal static int RamMB { get; private set; }
        internal static int ScreenWidth { get; private set; }
        internal static int ScreenHeight { get; private set; }
        internal static string Os { get; private set; } = "";
        internal static string GraphicsApi { get; private set; } = "";

        private static bool _done;

        /// <summary>
        /// Reads what Unity will tell us. Resolution is re-read each time rather than cached: it is
        /// the one thing here that changes without the game restarting, and a player who drops their
        /// resolution to chase frames is exactly the case worth catching.
        /// </summary>
        internal static void Refresh()
        {
            try
            {
                ScreenWidth = Screen.width;
                ScreenHeight = Screen.height;
                if (_done) return;
                _done = true;

                Gpu = SystemInfo.graphicsDeviceName ?? "";
                VramMB = SystemInfo.graphicsMemorySize;
                Cpu = SystemInfo.processorType ?? "";
                Cores = SystemInfo.processorCount;
                RamMB = SystemInfo.systemMemorySize;
                Os = SystemInfo.operatingSystem ?? "";
                GraphicsApi = SystemInfo.graphicsDeviceType.ToString();
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] could not read hardware details: {e.Message}");
            }
        }

        /// <summary>One line for a report, or an empty string when nothing is known.</summary>
        internal static string Describe()
        {
            Refresh();
            if (string.IsNullOrEmpty(Gpu) && string.IsNullOrEmpty(Cpu)) return "";
            string res = ScreenWidth > 0 ? $"{ScreenWidth}x{ScreenHeight}" : "?";
            string vram = VramMB > 0 ? $" {VramMB / 1024f:0.#} GB" : "";
            string ram = RamMB > 0 ? $", {RamMB / 1024f:0.#} GB RAM" : "";
            return $"{Cpu} ({Cores}c){ram}, {Gpu}{vram}, {res}";
        }
    }
}
