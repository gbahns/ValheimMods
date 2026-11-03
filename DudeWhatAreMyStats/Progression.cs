using System.Collections.Generic;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// How far through the game this world has got, on the same 1-to-8 biome scale
    /// <see cref="CreatureCatalog"/> uses for where a creature lives.
    ///
    /// Boss kills are world state rather than player state: ZoneSystem's global keys are shared by
    /// everyone in the world, and that is what makes this number comparable between players on a
    /// server. "Before the Elder" means the same thing to all of them, and a client that joined
    /// yesterday reads the same keys as the one that killed the boss.
    ///
    /// Beating them out of order is allowed for. Somebody who skips Bonemass and kills Moder is
    /// Plains-ready by the only measure that matters -- the gear and the portals are open -- so the
    /// highest gate passed wins, not the longest unbroken run of them.
    /// </summary>
    internal static class Progression
    {
        /// <summary>
        /// Each boss and the tier that beating it opens up. Beating nothing at all leaves you in
        /// the Meadows, which is why the table starts at 2. There is no Deep North boss to put at 9.
        /// </summary>
        private static readonly KeyValuePair<string, int>[] Gates =
        {
            new KeyValuePair<string, int>("defeated_eikthyr", 2),
            new KeyValuePair<string, int>("defeated_gdking", 3),
            new KeyValuePair<string, int>("defeated_bonemass", 4),
            new KeyValuePair<string, int>("defeated_dragon", 5),
            new KeyValuePair<string, int>("defeated_goblinking", 6),
            new KeyValuePair<string, int>("defeated_queen", 7),
            new KeyValuePair<string, int>("defeated_fader", 8),
        };

        /// <summary>The top of the scale, which is also the length of a per-tier bucket array.</summary>
        internal const int Highest = 8;

        /// <summary>1 (Meadows) to 8 (Deep North), or 0 when no world is loaded to ask.</summary>
        internal static int Tier()
        {
            var zones = ZoneSystem.instance;
            if (zones == null) return 0;
            int tier = 1;
            foreach (var gate in Gates)
            {
                if (gate.Value <= tier) continue;
                if (zones.GetGlobalKey(gate.Key)) tier = gate.Value;
            }
            return tier;
        }

        /// <summary>The stage a tier means, phrased for the middle of a sentence.</summary>
        internal static string Describe(int tier)
        {
            switch (tier)
            {
                case 1: return "before Eikthyr";
                case 2: return "before the Elder";
                case 3: return "before Bonemass";
                case 4: return "before Moder";
                case 5: return "before Yagluth";
                case 6: return "before the Queen";
                case 7: return "before Fader";
                case 8: return "after Fader";
                default: return "stage unknown";
            }
        }

        /// <summary>
        /// How far above your station a kill was, in biomes, said in words. The sign is the whole
        /// point, so "below you" is spelled out rather than left as a minus to work out.
        /// </summary>
        internal static string GapText(int gap)
        {
            if (gap >= 2) return gap + " biomes above you";
            if (gap == 1) return "a biome above you";
            if (gap == 0) return "your own biome";
            if (gap == -1) return "a biome below you";
            return -gap + " biomes below you";
        }
    }
}
