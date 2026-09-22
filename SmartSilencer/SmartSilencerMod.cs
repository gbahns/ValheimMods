using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace SmartSilencer
{
    /// <summary>
    /// Smart Silencer: the game goes quiet, by itself, while something else has your ears.
    ///
    /// Three triggers, each with its own switch:
    ///   1. a music player is playing (Spotify and friends),
    ///   2. a video is playing (a browser, VLC, ...),
    ///   3. the game window is not the one in front.
    /// While any of them holds, the game's sound fades down to Quiet Volume (silent by default);
    /// when none does, it fades back up. Nothing is paused and nothing is sent anywhere. The
    /// music and video triggers come from Windows' own per-app level meters, the same ones the
    /// volume mixer shows, so they know the difference between Spotify open and Spotify playing.
    ///
    /// Client-side only, and Windows-only for the audio triggers; the focus trigger works anywhere.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    public class SmartSilencerMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.SmartSilencer";
        public const string ModName    = "Smart Silencer";
        public const string ModVersion = "1.0.0";

        internal static ManualLogSource Log { get; private set; }
        internal static SmartSilencerMod Instance { get; private set; }

        // [General]
        internal static ConfigEntry<bool>             ModEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool>             ShowMessages;
        internal static ConfigEntry<bool>             LogTransitions;
        internal static ConfigEntry<float>            QuietVolume;
        internal static ConfigEntry<float>            FadeOut;
        internal static ConfigEntry<float>            FadeIn;

        // [Triggers]
        internal static ConfigEntry<bool> OnMusic;
        internal static ConfigEntry<bool> OnVideo;
        internal static ConfigEntry<bool> OnOther;
        internal static ConfigEntry<bool> OnUnfocused;

        // [Apps]
        internal static ConfigEntry<string> MusicApps;
        internal static ConfigEntry<string> VideoApps;
        internal static ConfigEntry<string> IgnoredApps;

        // [Detection]
        internal static ConfigEntry<float> PollInterval;
        internal static ConfigEntry<float> SoundThreshold;
        internal static ConfigEntry<float> SoundDelay;
        internal static ConfigEntry<float> SilenceGrace;

        internal const string DefaultMusicApps =
            "Spotify, AppleMusic, iTunes, MusicBee, foobar2000, Winamp, AIMP, TIDAL, Deezer, " +
            "Amazon Music, MediaMonkey, Music.UI, wmplayer, Qobuz, Roon, YouTube Music, " +
            "Pandora, SoundCloud";
        internal const string DefaultVideoApps =
            "chrome, msedge, firefox, brave, opera, opera_gx, vivaldi, arc, zen, librewolf, " +
            "vlc, mpv, mpc-hc, mpc-hc64, mpc-be, mpc-be64, PotPlayerMini, PotPlayerMini64, " +
            "Netflix, Plex, Kodi, Video.UI, WWAHost, ApplicationFrameHost, Movies & TV, " +
            "Stremio, Jellyfin Media Player, wmplayer";
        internal const string DefaultIgnoredApps =
            "Discord, Teams, ms-teams, Zoom, Slack, Skype, TeamSpeak, TeamSpeak3, ts3client_win64, " +
            "Mumble, steam, steamwebhelper, explorer, ShellExperienceHost, SearchHost, " +
            "StartMenuExperienceHost, nvcontainer, RtkAudUService64, audiodg";

        private readonly List<AudibleApp> _apps = new List<AudibleApp>();
        private readonly HashSet<string> _music   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _video   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private float  _lastPoll = -1000f;
        private float  _audibleSince = -1f;      // when the current run of sound started, -1 when silent
        private float  _lastAudible = -1000f;    // when sound was last heard
        private bool   _audioWants;              // the debounced verdict of the audio triggers
        private string _audioReason = "";        // who was making the sound, for the message
        private bool   _focusWants;

        private bool   _quiet;                   // the verdict currently being acted on
        private string _quietReason = "";
        private float  _level = 1f;              // where the fade is right now
        private bool   _touched;                 // we are the ones holding AudioListener.volume

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master switch, and what the hotkey flips. Off means the game's sound is left alone. " +
                "Takes effect immediately.");
            ToggleKey = Config.Bind("General", "Toggle Key", KeyboardShortcut.Empty,
                "Hotkey that flips Mod Enabled while playing. Empty by default, since every plain key " +
                "already does something in Valheim; a modifier combination such as LeftControl + F9 " +
                "is a safe choice. The console command 'silencer' does the same.");
            ShowMessages = Config.Bind("General", "Show Messages", true,
                "Show a short top-left message when the game goes quiet and when the sound comes back.");
            LogTransitions = Config.Bind("General", "Log Transitions", false,
                "Also write each of those transitions (silenced for what, sound back, on and off) to the " +
                "BepInEx log. Handy for confirming the mod is working, noise afterwards.");
            QuietVolume = Config.Bind("General", "Quiet Volume", 0f,
                new ConfigDescription(
                    "How loud the game is while silenced, as a fraction of its normal volume. 0 is " +
                    "silent; 0.2 keeps it faintly there under the music.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FadeOut = Config.Bind("General", "Fade Out Seconds", 0.4f,
                new ConfigDescription("How long the sound takes to go down.", new AcceptableValueRange<float>(0f, 10f)));
            FadeIn = Config.Bind("General", "Fade In Seconds", 0.8f,
                new ConfigDescription("How long the sound takes to come back.", new AcceptableValueRange<float>(0f, 10f)));

            OnMusic = Config.Bind("Triggers", "Music Playing", true,
                "Go quiet while a program in the Music Apps list is making sound.");
            OnVideo = Config.Bind("Triggers", "Video Playing", true,
                "Go quiet while a program in the Video Apps list is making sound. Browsers are in that " +
                "list, so YouTube counts, and so does anything else a browser plays.");
            OnOther = Config.Bind("Triggers", "Other Apps Playing", false,
                "Go quiet while any other program is making sound: one that is in neither list and not " +
                "in Ignored Apps. Off by default so a notification ding or a voice call does not silence " +
                "the game. The console command 'silencer apps' lists what is audible right now.");
            OnUnfocused = Config.Bind("Triggers", "Game Not Focused", true,
                "Go quiet while another window is in front, and come back when the game is.");

            MusicApps = Config.Bind("Apps", "Music Apps", DefaultMusicApps,
                "Programs that count as music players, by executable name without .exe, comma " +
                "separated, case does not matter. 'silencer apps' in the console shows the names to use.");
            VideoApps = Config.Bind("Apps", "Video Apps", DefaultVideoApps,
                "Programs that count as video players. Browsers belong here: a browser is the usual " +
                "way to watch anything, and Windows cannot tell a tab's sound apart from the browser's.");
            IgnoredApps = Config.Bind("Apps", "Ignored Apps", DefaultIgnoredApps,
                "Programs whose sound never silences the game, whatever the other settings say: voice " +
                "chat, Windows itself. Only matters with Other Apps Playing on, unless a name here is " +
                "also in one of the other lists, in which case ignoring wins.");

            PollInterval = Config.Bind("Detection", "Poll Interval Seconds", 0.25f,
                new ConfigDescription("How often the per-app level meters are read.", new AcceptableValueRange<float>(0.05f, 2f)));
            SoundThreshold = Config.Bind("Detection", "Sound Threshold", 0.005f,
                new ConfigDescription(
                    "The level meter reading (0 to 1) a program must reach to count as making sound. " +
                    "Music at any listenable volume is far above this; raise it if something very quiet " +
                    "keeps triggering.",
                    new AcceptableValueRange<float>(0f, 1f)));
            SoundDelay = Config.Bind("Detection", "Sound Delay Seconds", 0.3f,
                new ConfigDescription(
                    "How long a program must have been making sound before the game goes quiet, so a " +
                    "single blip does not count.",
                    new AcceptableValueRange<float>(0f, 10f)));
            SilenceGrace = Config.Bind("Detection", "Silence Grace Seconds", 2f,
                new ConfigDescription(
                    "How long everything must have been silent before the game's sound comes back. " +
                    "Covers the gap between songs and a video buffering; make it longer if the game " +
                    "keeps peeking through.",
                    new AcceptableValueRange<float>(0f, 30f)));

            MusicApps.SettingChanged   += (_, __) => RebuildLists();
            VideoApps.SettingChanged   += (_, __) => RebuildLists();
            IgnoredApps.SettingChanged += (_, __) => RebuildLists();
            RebuildLists();

            Commands.Register();
            Log.LogInfo($"{ModName} {ModVersion} loaded.");
        }

        private void OnDestroy()
        {
            if (_touched) AudioListener.volume = 1f;
            if (Instance == this) Instance = null;
        }

        private void RebuildLists()
        {
            Fill(_music, MusicApps.Value);
            Fill(_video, VideoApps.Value);
            Fill(_ignored, IgnoredApps.Value);
        }

        private static void Fill(HashSet<string> set, string csv)
        {
            set.Clear();
            foreach (var raw in (csv ?? "").Split(','))
            {
                var name = raw.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.Length > 0) set.Add(name);
            }
        }

        internal enum Category { Ignored, Music, Video, Other }

        internal Category Classify(string name)
        {
            if (_ignored.Contains(name)) return Category.Ignored;
            if (_music.Contains(name)) return Category.Music;
            if (_video.Contains(name)) return Category.Video;
            return Category.Other;
        }

        private static bool Wanted(Category c)
        {
            switch (c)
            {
                case Category.Music: return OnMusic.Value;
                case Category.Video: return OnVideo.Value;
                case Category.Other: return OnOther.Value;
                default: return false;
            }
        }

        // ── the loop ─────────────────────────────────────────────────────────────────

        private void Update()
        {
            HandleHotkey();

            float now = Time.unscaledTime;
            bool enabled = ModEnabled.Value;

            if (enabled && (OnMusic.Value || OnVideo.Value || OnOther.Value) && AudioSessions.Available)
            {
                if (now - _lastPoll >= PollInterval.Value)
                {
                    _lastPoll = now;
                    if (AudioSessions.TryPoll(SoundThreshold.Value, _apps)) Judge(now);
                    // a failed poll leaves the last verdict standing; the grace period sorts it out
                }
            }
            else
            {
                _audioWants = false;
                _audibleSince = -1f;
            }

            _focusWants = enabled && OnUnfocused.Value && !Application.isFocused;

            bool quiet = enabled && (_audioWants || _focusWants);
            if (quiet != _quiet)
            {
                _quiet = quiet;
                _quietReason = _focusWants && !_audioWants ? "tabbed out" : _audioReason;
                if (quiet) Say(_focusWants && !_audioWants ? "Silenced while you're away" : $"Silenced for {_audioReason}");
                else Say("Sound is back");
            }
        }

        /// <summary>Turns one poll's list of audible programs into the debounced audio verdict.</summary>
        private void Judge(float now)
        {
            string reason = null;
            int others = 0;
            foreach (var app in _apps)
            {
                if (!Wanted(Classify(app.Name))) continue;
                if (reason == null) reason = app.Name; else others++;
            }

            if (reason != null)
            {
                if (_audibleSince < 0f) _audibleSince = now;
                _lastAudible = now;
                _audioReason = others == 0 ? reason : $"{reason} +{others}";
                if (now - _audibleSince >= SoundDelay.Value) _audioWants = true;
            }
            else
            {
                _audibleSince = -1f;
                if (now - _lastAudible >= SilenceGrace.Value) _audioWants = false;
            }
        }

        /// <summary>
        /// Applies the fade. LateUpdate so it lands after anything vanilla does in its own Update.
        /// Vanilla writes AudioListener.volume in exactly two places, both to 1 or 0: AudioMan's
        /// Start, and CinematicsManager around a cutscene. Master volume itself goes through the
        /// mixer, so scaling the listener leaves the player's volume settings untouched. While a
        /// cutscene plays the listener is the cutscene's; we keep our hands off and pick up after.
        /// </summary>
        private void LateUpdate()
        {
            float target = _quiet ? QuietVolume.Value : 1f;
            if (_level != target)
            {
                float seconds = target < _level ? FadeOut.Value : FadeIn.Value;
                float step = seconds <= 0.01f ? 1f : Time.unscaledDeltaTime / seconds;
                _level = Mathf.MoveTowards(_level, target, step);
            }

            if (CinematicsManager.s_instance != null && CinematicsManager.IsPlaying())
            {
                _touched = false;
                return;
            }

            if (_level < 1f)
            {
                AudioListener.volume = _level;
                _touched = true;
            }
            else if (_touched)
            {
                AudioListener.volume = 1f;
                _touched = false;
            }
        }

        private void HandleHotkey()
        {
            if (!Keys.IsDown(ToggleKey.Value) || !Keys.CanTakeInput()) return;
            SetEnabled(!ModEnabled.Value, announce: true);
        }

        internal void SetEnabled(bool on, bool announce)
        {
            ModEnabled.Value = on;
            if (announce) Say(on ? "Smart Silencer on" : "Smart Silencer off: the game keeps its sound");
        }

        // ── reporting ────────────────────────────────────────────────────────────────

        internal bool IsQuiet => _quiet;
        internal string QuietReason => _quietReason;
        internal float Level => _level;
        internal bool GameFocused => Application.isFocused;

        /// <summary>The programs audible at the last poll, with how each was classified.</summary>
        internal string DescribeAudible()
        {
            if (!AudioSessions.Available) return "Windows audio sessions are unavailable: " + AudioSessions.Failure;
            var apps = new List<AudibleApp>();
            if (!AudioSessions.TryPoll(0f, apps)) return "Could not read the audio sessions this time.";
            if (apps.Count == 0) return "No other program has an active audio session.";
            apps.Sort((a, b) => b.Peak.CompareTo(a.Peak));
            var sb = new StringBuilder();
            foreach (var app in apps)
            {
                var cat = Classify(app.Name);
                string note = app.Peak < SoundThreshold.Value ? "silent" : (Wanted(cat) ? "silences" : "does not silence");
                sb.Append($"{app.Name} (pid {app.Pid}): {cat.ToString().ToLowerInvariant()}, level {app.Peak:0.000}, {note}\n");
            }
            return sb.ToString().TrimEnd();
        }

        private void Say(string text)
        {
            if (LogTransitions.Value) Log.LogInfo(text);
            if (!ShowMessages.Value) return;
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }
    }
}
