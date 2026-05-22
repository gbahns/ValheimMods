using Jotunn.Configs;

namespace ForesakenShrines
{
    internal sealed class ShrineDefinition
    {
        public readonly string BossKey;       // ZoneSystem global key, e.g. "defeated_eikthyr"
        public readonly string PowerPrefab;   // GP_ prefab name used by Player.SetGuardianPower
        public readonly string PieceName;     // our prefab/piece name, e.g. "shrine_eikthyr"
        public readonly string DisplayName;   // localization token, e.g. "$shrine_eikthyr_name"
        public readonly string BasePrefab;    // vanilla prefab we clone for the visual
        public readonly string IconItem;      // item prefab whose icon appears in the build menu grid
        public readonly RequirementConfig[] Requirements;

        public ShrineDefinition(
            string bossKey, string powerPrefab, string pieceName,
            string displayName, string basePrefab, string iconItem,
            RequirementConfig[] requirements)
        {
            BossKey      = bossKey;
            PowerPrefab  = powerPrefab;
            PieceName    = pieceName;
            DisplayName  = displayName;
            BasePrefab   = basePrefab;
            IconItem     = iconItem;
            Requirements = requirements;
        }
    }

    internal static class ShrineDefinitions
    {
        internal static readonly ShrineDefinition[] All =
        {
            // ── Eikthyr — Meadows ──────────────────────────────────────────────────
            new ShrineDefinition(
                bossKey:     "defeated_eikthyr",
                powerPrefab: "GP_Eikthyr",
                pieceName:   "shrine_eikthyr",
                displayName: "$shrine_eikthyr_name",
                basePrefab:  "BossStone_Eikthyr",
                iconItem:    "TrophyEikthyr",
                requirements: new[]
                {
                    new RequirementConfig { Item = "Stone", Recover = true,      Amount = 20 },
                    new RequirementConfig { Item = "Chain", Recover = true,      Amount = 2  },
                    new RequirementConfig { Item = "Iron", Recover = true,       Amount = 2  },
                    new RequirementConfig { Item = "TrophyBjorn",Amount = 2,  Recover = false },
                    new RequirementConfig { Item = "TrophyDeer", Amount = 5,  Recover = false },
                }),

            // ── Elder — Black Forest ────────────────────────────────────────────────
            new ShrineDefinition(
                bossKey:     "defeated_gdking",
                powerPrefab: "GP_TheElder",
                pieceName:   "shrine_elder",
                displayName: "$shrine_elder_name",
                basePrefab:  "BossStone_TheElder",
                iconItem:    "TrophyTheElder",
                requirements: new[]
                {
                    new RequirementConfig { Item = "Stone", Recover = true,                Amount = 30 },
                    new RequirementConfig { Item = "Chain", Recover = true,                Amount = 2  },
                    new RequirementConfig { Item = "Iron", Recover = true,                 Amount = 2  },
                    new RequirementConfig { Item = "TrophySkeletonHildir", Amount = 1,  Recover = false },
                    new RequirementConfig { Item = "TrophyForestTroll",    Amount = 4,  Recover = false },
                }),

            // ── Bonemass — Swamp ────────────────────────────────────────────────────
            new ShrineDefinition(
                bossKey:     "defeated_bonemass",
                powerPrefab: "GP_Bonemass",
                pieceName:   "shrine_bonemass",
                displayName: "$shrine_bonemass_name",
                basePrefab:  "BossStone_Bonemass",
                iconItem:    "TrophyBonemass",
                requirements: new[]
                {
                    new RequirementConfig { Item = "Stone", Recover = true,             Amount = 40 },
                    new RequirementConfig { Item = "Chain", Recover = true,             Amount = 3  },
                    new RequirementConfig { Item = "Iron", Recover = true,              Amount = 2  },
                    new RequirementConfig { Item = "TrophyWraith",      Amount = 4,  Recover = false },
                    new RequirementConfig { Item = "TrophyAbomination", Amount = 4,  Recover = false },
                }),

            // ── Moder — Mountain ────────────────────────────────────────────────────
            // TrophyGeirrhafa: boss of the Whispering Caves (Hildir's Request dungeon).
            // TrophyFenring: verify exact ObjectDB name — may need adjustment.
            new ShrineDefinition(
                bossKey:     "defeated_dragon",
                powerPrefab: "GP_Moder",
                pieceName:   "shrine_moder",
                displayName: "$shrine_moder_name",
                basePrefab:  "BossStone_DragonQueen",
                iconItem:    "TrophyDragonQueen",
                requirements: new[]
                {
                    new RequirementConfig { Item = "Stone", Recover = true,            Amount = 50 },
                    new RequirementConfig { Item = "Chain", Recover = true,            Amount = 3  },
                    new RequirementConfig { Item = "Iron", Recover = true,             Amount = 2  },
                    new RequirementConfig { Item = "TrophyGeirrhafa",  Amount = 1,  Recover = false },
                    new RequirementConfig { Item = "TrophyFenring",    Amount = 2,  Recover = false },
                }),

            // ── Yagluth — Plains ────────────────────────────────────────────────────
            // Zil = TrophyGoblinBruteBrosShaman, Thungr = TrophyGoblinBruteBrosBrute
            // (Sealed Tower twins, Hildir's Request dungeon)
            new ShrineDefinition(
                bossKey:     "defeated_goblinking",
                powerPrefab: "GP_Yagluth",
                pieceName:   "shrine_yagluth",
                displayName: "$shrine_yagluth_name",
                basePrefab:  "BossStone_Yagluth",
                iconItem:    "TrophyGoblinKing",
                requirements: new[]
                {
                    new RequirementConfig { Item = "Stone", Recover = true,                       Amount = 60 },
                    new RequirementConfig { Item = "Chain", Recover = true,                       Amount = 3  },
                    new RequirementConfig { Item = "Iron", Recover = true,                        Amount = 2  },
                    new RequirementConfig { Item = "TrophyGoblinBruteBrosShaman", Amount = 1,  Recover = false },
                    new RequirementConfig { Item = "TrophyGoblinBruteBrosBrute",  Amount = 1,  Recover = false },
                }),

            // ── Queen — Mistlands ───────────────────────────────────────────────────
            // TrophyGjall: verify exact ObjectDB name — may need adjustment.
            new ShrineDefinition(
                bossKey:     "defeated_queen",
                powerPrefab: "GP_Queen",
                pieceName:   "shrine_queen",
                displayName: "$shrine_queen_name",
                basePrefab:  "BossStone_TheQueen",
                iconItem:    "TrophySeekerQueen",
                requirements: new[]
                {
                    new RequirementConfig { Item = "Stone", Recover = true,             Amount = 70 },
                    new RequirementConfig { Item = "Chain", Recover = true,             Amount = 5  },
                    new RequirementConfig { Item = "Iron", Recover = true,              Amount = 2  },
                    new RequirementConfig { Item = "TrophyGjall",       Amount = 3,  Recover = false },
                    new RequirementConfig { Item = "TrophySeekerBrute", Amount = 5,  Recover = false },
                }),

            // ── Fader — Ashlands ────────────────────────────────────────────────────
            new ShrineDefinition(
                bossKey:     "defeated_fader",
                powerPrefab: "GP_Fader",
                pieceName:   "shrine_fader",
                displayName: "$shrine_fader_name",
                basePrefab:  "BossStone_Fader",
                iconItem:    "TrophyFader",
                requirements: new[]
                {
                    new RequirementConfig { Item = "Stone", Recover = true,                Amount = 60 },
                    new RequirementConfig { Item = "Chain", Recover = true,                Amount = 5  },
                    new RequirementConfig { Item = "Iron", Recover = true,                 Amount = 2  },
                    new RequirementConfig { Item = "TrophyFallenValkyrie", Amount = 2,  Recover = false },
                    new RequirementConfig { Item = "TrophyCharredMage",    Amount = 2,  Recover = false },
                }),
        };
    }
}
