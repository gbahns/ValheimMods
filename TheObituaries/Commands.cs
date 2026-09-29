using System;
using System.Collections.Generic;
using System.Text;

namespace TheObituaries
{
    /// <summary>
    /// The console command (F5): <c>obituary</c> previews a random death line on this screen
    /// only, <c>obituary &lt;cause&gt; [weapon]</c> previews one cause (a hit type such as fall,
    /// drowning or self; a creature such as troll or draugr; or pvp) with an optional weapon
    /// kind (bow, sword, axe, club, spear, log, fire, ...), and <c>obituary all</c> runs
    /// through every hit type. Nothing is sent to other players; it is for checking the display
    /// settings without dying for it.
    /// </summary>
    internal static class Commands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("obituary",
                "The Obituaries: 'obituary' previews a random death line here only, 'obituary <cause> [weapon]' previews one cause (fall, drowning, troll, draugr bow, pvp club, ...), 'obituary all' previews every hit type, 'obituary causes' lists them",
                (Terminal.ConsoleEvent)(args =>
                {
                    string[] a = args.Args;
                    string cause  = a.Length > 1 ? a[1] : "";
                    string weapon = a.Length > 2 ? a[2] : "";
                    foreach (var line in Run(cause, weapon).Split('\n')) args.Context?.AddString(line);
                }),
                optionsFetcher: () =>
                {
                    var opts = new List<string> { "all", "causes", "pvp" };
                    foreach (var t in Enum.GetNames(typeof(HitData.HitType))) opts.Add(t.ToLowerInvariant());
                    foreach (var c in Obituary.KnownCreatures()) opts.Add(c.ToLowerInvariant());
                    return opts;
                });
        }

        private static readonly System.Random Rng = new System.Random();

        private static string Run(string cause, string weapon)
        {
            var me = Player.m_localPlayer;
            if (me == null) return "The Obituaries: load into a world first.";

            if (cause.Equals("causes", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder("Hit types: ");
                sb.Append(string.Join(", ", Enum.GetNames(typeof(HitData.HitType))).ToLowerInvariant());
                sb.Append("\nCreatures: ");
                sb.Append(string.Join(", ", Obituary.KnownCreatures()).ToLowerInvariant());
                sb.Append("\nWeapons (second word, optional): ");
                sb.Append(string.Join(", ", Enum.GetNames(typeof(WeaponKind))).ToLowerInvariant());
                sb.Append("\nAnd: pvp");
                return sb.ToString();
            }

            if (cause.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                int shown = 0;
                foreach (var name in Enum.GetNames(typeof(HitData.HitType)))
                {
                    var n = Obituary.Sample(name, "", me);
                    if (n == null) continue;
                    DeathNetwork.Show(n);
                    shown++;
                }
                foreach (var extra in new[] { "pvp", "troll", "draugr" })
                {
                    var n = Obituary.Sample(extra, "", me);
                    if (n != null) { DeathNetwork.Show(n); shown++; }
                }
                return $"Previewed {shown} obituaries (shown here only).";
            }

            if (cause.Length == 0)
            {
                var pool = new List<string> { "pvp" };
                foreach (var t in Enum.GetNames(typeof(HitData.HitType))) pool.Add(t);
                foreach (var c in Obituary.KnownCreatures()) pool.Add(c);
                cause = pool[Rng.Next(pool.Count)];
            }

            var notice = Obituary.Sample(cause, weapon, me);
            if (notice == null)
                return $"Unknown cause or weapon '{cause} {weapon}'. Try a hit type (fall, drowning, self, ...), a creature (troll, draugr, ...) or pvp, with an optional weapon (bow, axe, club, ...); 'obituary causes' lists them.";
            DeathNetwork.Show(notice);
            return "Previewed (shown here only): " + notice.Plain();
        }
    }
}
