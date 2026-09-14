using BepInEx.Configuration;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// All configuration entries. Bind() once from DiagnoseServerLagMod.Awake.
    ///
    /// The thresholds are separated from the code that uses them on purpose. A verdict that
    /// accuses the server is only worth as much as the number it accused it with, and the right
    /// number depends on hardware nobody here can see. Every one of them is documented with what
    /// it means physically, so a server that turns out to sit just the wrong side of a default can
    /// be corrected rather than argued with.
    ///
    /// The Server section is read only by the machine running the server; the rest is read only by
    /// clients. Both sets are bound on both sides so one config file explains the whole mod
    /// wherever you open it.
    /// </summary>
    internal static class DslConfig
    {
        // ── keys and display ────────────────────────────────────────────────────────
        internal static ConfigEntry<KeyboardShortcut> OpenKey;
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<KeyboardShortcut> HudKey;
        internal static ConfigEntry<string> PanelSize;
        internal static ConfigEntry<string> PanelPosition;
        internal static ConfigEntry<int> ListScrollRows;

        // ── what counts as a stall, and over what span ──────────────────────────────
        internal static ConfigEntry<float> StallMs;
        internal static ConfigEntry<int> WindowSeconds;
        internal static ConfigEntry<int> BaselineSeconds;

        // ── how often the server is asked ───────────────────────────────────────────
        internal static ConfigEntry<float> WatchSeconds;
        internal static ConfigEntry<float> BackgroundSeconds;

        // ── server side ─────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> AnswerClients;
        internal static ConfigEntry<bool> SharePeerDetail;

        // ── thresholds ──────────────────────────────────────────────────────────────
        internal static ConfigEntry<float> ServerTickWarnMs;
        internal static ConfigEntry<float> ServerTickSevereMs;
        internal static ConfigEntry<float> ClientFrameWarnMs;
        internal static ConfigEntry<float> QueueWarnBytes;
        internal static ConfigEntry<float> QueueSevereBytes;
        internal static ConfigEntry<float> QualityWarn;
        internal static ConfigEntry<float> QualitySevere;
        internal static ConfigEntry<float> PingJitterWarnMs;
        internal static ConfigEntry<float> ChurnFloor;
        internal static ConfigEntry<float> ChurnFactor;
        internal static ConfigEntry<float> InstanceRiseWarn;

        internal static void Bind(DiagnoseServerLagMod mod)
        {
            // F8, after 0.1.0 shipped on F10 and hit two conflicts at once: AutomaticFuel binds F10
            // by default, and F9 cycles the controller layout, so the neighboring key was no escape
            // either. F8 is free in vanilla and unused by every other mod in this repo. Check your
            // own before changing it.
            //
            // The superseded default migrates anyone still on 0.1.0's F10; see BindKey.
            OpenKey = mod.BindKey("Keys", "Open Report", new KeyboardShortcut(KeyCode.F8),
                "Opens and closes the lag report. Escape closes it too. Ignored while typing in chat, the " +
                "console or a text box.",
                new KeyboardShortcut(KeyCode.F10));
            HudKey = mod.BindKey("Keys", "Toggle Readout", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftShift),
                "Turns the small corner readout on and off without opening the full report.",
                new KeyboardShortcut(KeyCode.F10, KeyCode.LeftShift));

            ShowHud = mod.Bind("Display", "Show Readout", false,
                "A small always-on corner readout: your frame time, ping, queue and the server's tick time. " +
                "Off by default because the point of the mod is the verdict, not another number to watch.");
            PanelSize = mod.Bind("Display", "Panel Size", "860,640",
                "Width and height of the report, remembered when you drag its bottom-right corner.");
            PanelPosition = mod.Bind("Display", "Panel Position", "0,0",
                "Where the report sits, as an offset from the screen center, remembered when you drag it by " +
                "its title. Set to 0,0 to put it back in the middle.");
            ListScrollRows = mod.BindRangeInt("Display", "List Scroll Rows", 4, 1, 20,
                "How many rows a list moves per notch of the mouse wheel.");

            StallMs = mod.BindRange("Measurement", "Stall Milliseconds", 100f, 20f, 1000f,
                "A frame longer than this counts as a stall. 100 ms is a single frame at 10 per second, which " +
                "is comfortably past the point where a person notices. Lower it to catch smaller hitches; " +
                "raise it if a machine that plays fine is reporting stalls constantly.");
            WindowSeconds = mod.BindRangeInt("Measurement", "Window Seconds", 10, 3, 120,
                "How many recent seconds the verdict is made from. Short enough that a bad patch is not " +
                "diluted by the good minute after it.");
            BaselineSeconds = mod.BindRangeInt("Measurement", "Baseline Seconds", 60, 10, 600,
                "How much history counts as 'normal for this server' when judging whether something has got " +
                "worse. Rules with no universal right answer - object churn especially - are judged against " +
                "this rather than against a fixed number.");

            WatchSeconds = mod.BindRange("Measurement", "Watch Seconds", 1f, 0.5f, 30f,
                "How often to ask the server for its numbers while the report is open. Once a second keeps " +
                "the verdict live; the request and its answer are a few hundred bytes.");
            BackgroundSeconds = mod.BindRange("Measurement", "Background Seconds", 5f, 0f, 120f,
                "How often to ask while the report is closed, so history exists before you go looking for it. " +
                "0 asks only while the report is open, which means a client that never opens it sends nothing " +
                "at all.");

            AnswerClients = mod.Bind("Server", "Answer Clients", true,
                "SERVER SIDE. Answer clients that ask for the server's own measurements. Turning this off " +
                "makes the server indistinguishable from one without the mod, and every client falls back to " +
                "diagnosing its own end alone.");
            SharePeerDetail = mod.Bind("Server", "Share Peer Detail", true,
                "SERVER SIDE. Include the per-player table - each player's ping, connection quality, queued " +
                "bytes and distance from the world center - in the answer to everyone, not only admins. The " +
                "aggregate numbers that diagnose the server always go to everyone; this is the part that names " +
                "who is on a bad line. Admins receive it either way.");

            ServerTickWarnMs = mod.BindRange("Thresholds", "Server Tick Warn Ms", 33f, 5f, 500f,
                "Server milliseconds per tick above which the server is called slow. A healthy dedicated " +
                "server ticks in single-digit milliseconds - it draws nothing and its loop is uncapped - so " +
                "33 ms, which is 30 ticks a second, is already far outside normal and is set generously on " +
                "purpose.");
            ServerTickSevereMs = mod.BindRange("Thresholds", "Server Tick Severe Ms", 66f, 10f, 1000f,
                "Server milliseconds per tick above which the server is called badly starved: roughly 15 ticks " +
                "a second, where position updates arrive too late to hide and everyone online rubber-bands.");
            ClientFrameWarnMs = mod.BindRange("Thresholds", "Client Frame Warn Ms", 33f, 8f, 500f,
                "Your own milliseconds per frame above which your machine is called the bottleneck. 33 ms is " +
                "30 frames a second.");

            QueueWarnBytes = mod.BindRange("Thresholds", "Queue Warn Bytes", 16384f, 1024f, 1048576f,
                "Bytes handed to the socket and not yet sent, above which the link is called full. Some queue " +
                "is normal and harmless; what matters is whether it drains, which is why the rule also looks " +
                "at whether the number is climbing.");
            QueueSevereBytes = mod.BindRange("Thresholds", "Queue Severe Bytes", 65536f, 4096f, 4194304f,
                "Queued bytes above which the link is called saturated regardless of direction of travel.");

            QualityWarn = mod.BindRange("Thresholds", "Quality Warn", 0.98f, 0.5f, 1f,
                "Connection quality below which packet loss is reported. This is Steam's own figure: a " +
                "fraction of packets arriving, where 1.0 is no measured loss. 0.98 is about two percent lost, " +
                "which Valheim already feels.");
            QualitySevere = mod.BindRange("Thresholds", "Quality Severe", 0.9f, 0.1f, 1f,
                "Connection quality below which loss is reported as the headline cause.");
            PingJitterWarnMs = mod.BindRange("Thresholds", "Ping Jitter Warn Ms", 40f, 5f, 500f,
                "Average change in ping from one second to the next, above which the link is called unstable. " +
                "Jitter is judged separately from latency because they feel nothing alike: a steady 140 ms is " +
                "playable, while a ping swinging between 20 and 220 averages better and plays far worse.");

            ChurnFloor = mod.BindRange("Thresholds", "Churn Floor", 150f, 10f, 5000f,
                "Object updates a second below which churn is never reported, however much it has risen. " +
                "Tripling a very small number is not news.");
            ChurnFactor = mod.BindRange("Thresholds", "Churn Factor", 3f, 1.2f, 20f,
                "How many times the baseline rate of object updates counts as churn. Judged against this " +
                "server's own recent normal, because a quiet forest and a working base legitimately differ by " +
                "an order of magnitude and no fixed number is right for both.");
            InstanceRiseWarn = mod.BindRange("Thresholds", "Instance Rise Warn", 40f, 5f, 2000f,
                "New objects a second being built into the scene which, happening at the same time as a stall, " +
                "attributes that stall to loading the world rather than to the machine being too slow.");
        }
    }
}
