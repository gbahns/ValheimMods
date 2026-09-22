using System;
using System.Text;

namespace SmartSilencer
{
    /// <summary>
    /// The console command (F5): <c>silencer</c> alone prints the state; <c>silencer on|off</c> flips the
    /// master switch; <c>silencer music|video|other|focus on|off</c> flips one trigger; <c>silencer apps</c>
    /// lists every program with an audio session right now, with the name to put in a list.
    /// </summary>
    internal static class Commands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("silencer",
                "Smart Silencer: 'silencer' shows the state, 'silencer on|off' flips it, 'silencer music|video|other|focus on|off' flips one trigger, 'silencer apps' lists what is audible",
                (Terminal.ConsoleEvent)(args =>
                {
                    var mod = SmartSilencerMod.Instance;
                    if (mod == null) { args.Context?.AddString("Smart Silencer is not running."); return; }
                    string[] a = args.Args;
                    string reply;
                    if (a.Length <= 1) reply = Status(mod);
                    else
                    {
                        string first = a[1].ToLowerInvariant();
                        if (first == "apps") reply = mod.DescribeAudible();
                        else if (first == "on" || first == "off") { mod.SetEnabled(first == "on", announce: false); reply = Status(mod); }
                        else if (a.Length >= 3 && TryFlag(first, out var entry) && (a[2] == "on" || a[2] == "off"))
                        {
                            entry.Value = a[2] == "on";
                            reply = Status(mod);
                        }
                        else reply = "Usage: silencer | silencer on|off | silencer music|video|other|focus on|off | silencer apps";
                    }
                    foreach (var line in reply.Split('\n')) args.Context?.AddString(line);
                }));
        }

        private static bool TryFlag(string name, out BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            switch (name)
            {
                case "music": entry = SmartSilencerMod.OnMusic; return true;
                case "video": entry = SmartSilencerMod.OnVideo; return true;
                case "other": entry = SmartSilencerMod.OnOther; return true;
                case "focus": entry = SmartSilencerMod.OnUnfocused; return true;
                default: entry = null; return false;
            }
        }

        private static string OnOff(bool b) => b ? "on" : "off";

        private static string Status(SmartSilencerMod mod)
        {
            var sb = new StringBuilder();
            sb.Append($"Smart Silencer is {OnOff(SmartSilencerMod.ModEnabled.Value)}. ");
            sb.Append(mod.IsQuiet ? $"Quiet right now ({mod.QuietReason})" : "Sound is on right now");
            sb.Append($", level {mod.Level:0.00}.\n");
            sb.Append($"Triggers: music {OnOff(SmartSilencerMod.OnMusic.Value)}, video {OnOff(SmartSilencerMod.OnVideo.Value)}, ");
            sb.Append($"other {OnOff(SmartSilencerMod.OnOther.Value)}, focus {OnOff(SmartSilencerMod.OnUnfocused.Value)}.");
            if (!AudioSessions.Available) sb.Append("\nWindows audio sessions are unavailable: " + AudioSessions.Failure);
            return sb.ToString();
        }
    }
}
