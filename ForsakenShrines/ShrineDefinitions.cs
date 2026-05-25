using Jotunn.Configs;

namespace ForsakenShrines
{
    internal sealed class ShrineDefinition
    {
        public readonly string BossKey;       // ZoneSystem global key, e.g. "defeated_eikthyr"
        public readonly string PowerPrefab;   // GP_ prefab name used by Player.SetGuardianPower
        public readonly string PieceName;     // our prefab/piece name, e.g. "shrine_eikthyr"
        public readonly string DisplayName;   // English display name, e.g. "Shrine of Eikthyr"
        public readonly string Description;   // English description shown in the build menu
        public readonly string BasePrefab;    // vanilla prefab we clone for the visual
        public readonly string IconItem;      // item prefab whose icon appears in the build menu grid
        public readonly RequirementConfig[] Requirements;

        public ShrineDefinition(
            string bossKey, string powerPrefab, string pieceName,
            string displayName, string description, string basePrefab, string iconItem,
            RequirementConfig[] requirements)
        {
            BossKey      = bossKey;
            PowerPrefab  = powerPrefab;
            PieceName    = pieceName;
            DisplayName  = displayName;
            Description  = description;
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
                displayName: "Shrine of Eikthyr",
                description: "A monument to Eikthyr, the felled stag-lord of the Meadows. Channel its power.",
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
                displayName: "Shrine of The Elder",
                description: "A monument to The Elder, the felled tree-king of the Black Forest. Channel its power.",
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
                displayName: "Shrine of Bonemass",
                description: "A monument to Bonemass, the felled rot-lord of the Swamp. Channel its power.",
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
                displayName: "Shrine of Moder",
                description: "A monument to Moder, the felled dragon-queen of the Mountains. Channel its power.",
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
                displayName: "Shrine of Yagluth",
                description: "A monument to Yagluth, the felled god-king of the Plains. Channel its power.",
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
                displayName: "Shrine of The Queen",
                description: "A monument to The Queen, the felled seeker-queen of the Mistlands. Channel its power.",
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
                displayName: "Shrine of Fader",
                description: "A monument to Fader, the felled father-lord of the Ashlands. Channel its power.",
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
