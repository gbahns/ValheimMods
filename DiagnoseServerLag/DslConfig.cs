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
        internal static ConfigEntry<bool> PauseWhileOpen;
        internal static ConfigEntry<bool> ShowPauseButton;

        // ── what counts as a stall, and over what span ──────────────────────────────
        internal static ConfigEntry<float> StallMs;
        internal static ConfigEntry<int> WindowSeconds;
        internal static ConfigEntry<int> BaselineSeconds;
        internal static ConfigEntry<int> HistoryMinutes;

        // ── how often the server is asked ───────────────────────────────────────────
        internal static ConfigEntry<float> WatchSeconds;
        internal static ConfigEntry<float> BackgroundSeconds;

        // ── server side ─────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> AnswerClients;
        internal static ConfigEntry<bool> SharePeerDetail;
        internal static ConfigEntry<bool> ShareMyPerformance;

        // ── thresholds ──────────────────────────────────────────────────────────────
        internal static ConfigEntry<float> ServerTickWarnMs;
        internal static ConfigEntry<float> ServerTickSevereMs;
        internal static ConfigEntry<float> SteadyTickRatio;
        internal static ConfigEntry<float> ClientFrameWarnMs;
        internal static ConfigEntry<float> QueueWarnBytes;
        internal static ConfigEntry<float> QueueSevereBytes;
        internal static ConfigEntry<float> QualityWarn;
        internal static ConfigEntry<float> QualitySevere;
        internal static ConfigEntry<float> PingJitterWarnMs;
        internal static ConfigEntry<float> ChurnFloor;
        internal static ConfigEntry<float> ChurnFactor;
        internal static ConfigEntry<float> InstanceRiseWarn;
        internal static ConfigEntry<float> GcCoincidence;

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

            // Off by default, unlike the map's version. A diagnostic should not change how the game
            // runs the first time somebody opens it, least of all the part of the game it is
            // measuring - and watching the numbers move during a bad patch is a real use that a
            // pause would take away. The button in the corner is one click when you do want it.
            PauseWhileOpen = mod.Bind("Display", "Pause While Open", false,
                "Pause the game while the report is open, the way the ESC menu does. Toggle it from the button " +
                "in the report's top-right corner. This works by itself when playing solo or hosting alone; on " +
                "a dedicated server it takes the Pause My Server mod, and the button shows whether the pause " +
                "actually took effect. While the game is paused the mod stops recording, so the verdict you are " +
                "reading stays the verdict for the seconds that were actually played.");
            ShowPauseButton = mod.Bind("Display", "Show Pause Button", true,
                "Show the pause toggle in the report's top-right corner. Turning it off leaves the setting above " +
                "reachable only from this file.");

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

            HistoryMinutes = mod.BindRangeInt("Measurement", "History Minutes", 60, 5, 180,
                "How many minutes of per-second samples to keep, and therefore the longest capture dsl_bench " +
                "can summarize. A sample is about 110 bytes, so an hour costs roughly 400 KB and three hours " +
                "about 1.2 MB - the ceiling is set by what is useful to capture, not by what it costs. Longer " +
                "is not automatically better: the summary reports medians over whatever is in the window, so a " +
                "window covering ten minutes of work and ten of standing still describes neither. Match the " +
                "capture to the activity. Takes effect on restart.");

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

            // Default on, matching how DudeWhatAreMyStats treats its scoreboard: a server of
            // friends diagnosing a shared problem wants everyone in the picture, and a player who
            // would rather not be can say so without the feature needing per-request consent.
            ShareMyPerformance = mod.Bind("Display", "Share My Performance", true,
                "Answer an admin's group capture with this machine's frame times, stalls, CPU share and collection " +
                "counts for the window they asked about. It is what lets a capture tell 'everyone hitched at once', " +
                "which is the server or the network, from 'one machine hitched', which is that machine. Performance " +
                "numbers only - nothing about what you were doing. Turn it off and you are simply absent from the " +
                "group view; everything else still works.");

            ServerTickWarnMs = mod.BindRange("Thresholds", "Server Tick Warn Ms", 50f, 5f, 500f,
                "Server milliseconds per tick above which the server is called slow - but only when the tick " +
                "time is also uneven; see Steady Tick Ratio. This was 33 ms until a real server turned out to " +
                "run a rock-steady 33.3 ms frame cap, which is exactly 30 ticks a second, so the threshold sat " +
                "on top of a perfectly healthy server and accused it permanently. 50 ms is 20 ticks a second.");
            ServerTickSevereMs = mod.BindRange("Thresholds", "Server Tick Severe Ms", 100f, 10f, 1000f,
                "Server milliseconds per tick above which the server is called badly starved whatever the " +
                "shape of the measurement: 10 ticks a second, where position updates arrive too late to hide " +
                "and everyone online rubber-bands. A steady tick is forgiven below this and not above it, " +
                "because a server holding a metronomic 200 ms is still far too slow to run the game.");
            SteadyTickRatio = mod.BindRange("Thresholds", "Steady Tick Ratio", 1.5f, 1f, 10f,
                "How close the server's worst tick has to be to its median before the tick rate is read as a " +
                "deliberate frame cap rather than a struggle. A frame limiter holds every tick to nearly the " +
                "same length; a machine that genuinely cannot keep up produces variance, because the work that " +
                "overruns is not the same work every tick. Below this ratio, with no stalls, the server is " +
                "left alone however slow the number looks. Raise it to forgive more, lower it to accuse more.");
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
            GcCoincidence = mod.BindRange("Thresholds", "GC Coincidence", 0.5f, 0.1f, 1f,
                "What fraction of stalled seconds must contain a gen1 or gen2 garbage collection before the hitches " +
                "are blamed on the collector. Also required to be at least twice the rate seen in seconds that did " +
                "not stall, because a machine collecting constantly would otherwise have every stall blamed on it. " +
                "Gen0 is ignored entirely: it is cheap and continuous.");
            InstanceRiseWarn = mod.BindRange("Thresholds", "Instance Rise Warn", 40f, 5f, 2000f,
                "New objects a second being built into the scene which, happening at the same time as a stall, " +
                "attributes that stall to loading the world rather than to the machine being too slow.");
        }
    }
}
