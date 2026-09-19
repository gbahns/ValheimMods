using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// One ship this mod adds: a clone of a vanilla hull with its own speed, handling,
    /// durability, storage, paint and recipe.  The config section is the display name.
    /// </summary>
    internal sealed class ShipDefinition
    {
        // Saved into worlds as a hash: never rename.
        public string PrefabName;
        public string BasePrefab;
        public string BaseName;       // for config descriptions
        public float  BaseTopSpeed;   // the base hull's top speed (see the note on All)
        public string DisplayName;
        public string Description;
        // The config section a saved .cfg file has this ship's settings under is named after
        // DisplayName (see ShipConfig.Bind).  List every previous DisplayName here when renaming
        // a ship, newest first, so ShipConfig can move a saved section across instead of
        // orphaning it.
        public string[] OldDisplayNames;
        public int    StorageWidth;
        public int    StorageHeight;
        public string DefaultRecipe;
        public float  DefaultSpeed;   // multiplier on the base hull's top speed
        public float  DefaultHealth;
        public float  DefaultRudderSpeed;
        public Color  DefaultSailColor;
        public Color  DefaultHullColor;
        public int    DefaultHullStripes; // painted bands on the hull; 0 paints it solid
        public float  DefaultWidth;       // sideways scale of the whole ship
        public float  DefaultLength = 1f; // bow-to-stern scale of the whole ship
        public float  DefaultScale  = 1f; // uniform scale of the whole ship, all three axes
        public PenSpec Pen;               // a livestock pen on deck, or null

        // Defaults an earlier release shipped.  A saved config still holding one of these was
        // never changed by hand, so it is moved to the current default (see ShipConfig.Migrate).
        public float    OldDefaultSpeed = float.NaN;
        public string[] OldDefaultRecipes;
        public float[]  OldDefaultHealths;   // earlier defaults for Health, Hull Width and Hull Length: a saved
        public float[]  OldDefaultWidths;    // value still at one of these moves to the current default
        public float[]  OldDefaultLengths;
    }

    internal static class ShipDefinitions
    {
        private static readonly Color Blue  = new Color(0.375f, 0.55f, 0.875f, 1f);
        private static readonly Color Amber = new Color(1f, 0.8f, 0.5f, 1f);
        private static readonly Color Red   = new Color(1f, 0.62f, 0.55f, 1f);
        private static readonly Color Green = new Color(0.6f, 0.85f, 0.6f, 1f);
        private static readonly Color Brown = new Color(0.55f, 0.4f, 0.3f, 1f);

        // Top speeds in full wind at the best angle (64° off the stern), from Greg's CaptainsLog
        // sailing logs: Karve 7.3, Longship 9.65, OdinShipPlus Cargo Ship 8.7, Fast Ship
        // Skuldelev 12.2 -- all measured (260+ steady full-sail samples for the Longship, fit
        // to within 1.1% average error).
        // (0.9.0 used 8.8 and 9.5 from the ValheimGuides page, which were never measured.)
        //
        // Vanilla prefab values for reference: Karve health 500, rudder 1.0; Longship 1000, 1.0;
        // Drakkar 3000, 0.5.  OdinShipPlus's cargo ships use 1000-1500 and rudder 0.8.
        public static readonly ShipDefinition[] All =
        {
            new ShipDefinition
            {
                PrefabName         = "DM_FastKarve",
                BasePrefab         = "Karve",
                BaseName           = "Karve",
                BaseTopSpeed       = 7.3f,
                DisplayName        = "Fast Karve",
                Description        = "A light racing karve with an oversized sail. Faster than a longship, with room for little more than a spare cloak.",
                StorageWidth       = 2,
                StorageHeight      = 1,
                DefaultRecipe      = "FineWood:30,ElderBark:20,BronzeNails:60,TrollHide:8,Resin:20",
                DefaultSpeed       = 1.37f,   // about 10; 1.14 in 0.9.0 gave 8.4, slower than a longship
                OldDefaultSpeed    = 1.14f,
                DefaultHealth      = 400f,    // Karve 500: a lighter hull
                DefaultRudderSpeed = 1f,
                DefaultSailColor   = Blue,
                DefaultHullColor   = Red,
                DefaultHullStripes = 6,
                DefaultWidth       = 0.85f,
            },
            new ShipDefinition
            {
                PrefabName         = "DM_FastLongship",
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.65f,
                DisplayName        = "Fast Longship",
                Description        = "A long, lean longship under an oversized sail, braced with troll hide and extra iron and sealed with swamp guck: the fastest hull on the water. Two thirds of a longship's hold, and a lighter one.",
                StorageWidth       = 4,
                StorageHeight      = 3,
                DefaultRecipe      = "FineWood:40,ElderBark:40,IronNails:120,DeerHide:10,TrollHide:10",   // weighs 250; five ingredients, the build HUD's limit
                OldDefaultRecipes  = new[] { "FineWood:40,ElderBark:40,IronNails:120,DeerHide:10,TrollHide:10,Guck:10",
                                             "FineWood:40,ElderBark:40,IronNails:100,LinenThread:20,Resin:30" },
                DefaultSpeed       = 1.342f,  // sqrt(0.09/0.05): the Fast Ship Skuldelev's sail force exactly, about 12.95
                OldDefaultSpeed    = 1.28f,   // 0.9.0's default (about 12.4)
                DefaultHealth      = 800f,    // Longship 1000: a lighter hull
                DefaultRudderSpeed = 1f,
                DefaultSailColor   = Blue,
                DefaultHullColor   = Red,
                DefaultHullStripes = 6,
                DefaultWidth       = 0.85f,
                DefaultLength      = 1.1f,    // long and narrow, like a real fast hull
            },
            new ShipDefinition
            {
                PrefabName         = "DM_CargoLongship",   // saved hash: keep even though it's now the Knarr
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.65f,
                DisplayName        = "Knarr",
                OldDisplayNames    = new[] { "Cargo Longship" },
                Description        = "A deep, heavy longship built to haul. Slower and clumsier than a longship, but sturdier, with nearly twice its hold. The smallest of three cargo tiers -- see also the Busse and the Big Busse.",
                StorageWidth       = 8,
                StorageHeight      = 4,
                DefaultRecipe      = "FineWood:40,ElderBark:50,IronNails:120,DeerHide:10,TrollHide:10",   // weighs 270; wood and nails ~1.05x a longship's per unit of hull planking
                OldDefaultRecipes  = new[] { "FineWood:40,ElderBark:30,IronNails:120,DeerHide:10,TrollHide:10,RoundLog:20",
                                             "FineWood:40,ElderBark:40,IronNails:140,DeerHide:10,TrollHide:10,RoundLog:20",
                                             "FineWood:40,ElderBark:60,IronNails:180,DeerHide:10,TrollHide:10,RoundLog:30",
                                             "FineWood:40,ElderBark:60,IronNails:180,TrollHide:10,RoundLog:30",
                                             "FineWood:40,ElderBark:60,IronNails:180,DeerHide:10,RoundLog:30",
                                             "FineWood:40,ElderBark:60,IronNails:180,DeerHide:10,RoundLog:30,Chain:2",
                                             "FineWood:40,ElderBark:60,IronNails:150,DeerHide:10,RoundLog:30" },
                DefaultSpeed       = 0.87f,   // about 8.6, measured 8.58
                DefaultHealth      = 1100f,   // Longship 1000; health follows the wood in the recipe (1000 per 80)
                OldDefaultHealths  = new[] { 1500f },
                DefaultRudderSpeed = 0.8f,    // Longship 1.0, as OdinShipPlus's cargo ships
                DefaultSailColor   = Amber,
                DefaultHullColor   = Green,
                DefaultHullStripes = 6,
                DefaultWidth       = 1.25f,
            },
            new ShipDefinition
            {
                PrefabName         = "DM_Busse",
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.65f,
                DisplayName        = "Busse",
                Description        = "A longship built out further than the Knarr. Slower still and even clumsier, but holds more and shrugs off more damage.",
                StorageWidth       = 9,
                StorageHeight      = 4,
                DefaultRecipe      = "FineWood:50,ElderBark:60,IronNails:140,DeerHide:15,TrollHide:15",   // weighs 335
                OldDefaultRecipes  = new[] { "FineWood:50,ElderBark:30,IronNails:140,DeerHide:15,TrollHide:15,RoundLog:30",
                                             "FineWood:50,ElderBark:40,IronNails:160,DeerHide:15,TrollHide:15,RoundLog:30",
                                             "FineWood:50,ElderBark:50,IronNails:240,DeerHide:15,TrollHide:15,RoundLog:40",
                                             "FineWood:50,ElderBark:60,IronNails:240,TrollHide:15,RoundLog:40",
                                             "FineWood:50,ElderBark:60,IronNails:240,DeerHide:15,RoundLog:40",
                                             "FineWood:50,ElderBark:60,IronNails:240,DeerHide:15,RoundLog:40,Chain:4",
                                             "FineWood:55,ElderBark:85,IronNails:250,DeerHide:15,RoundLog:45,Chain:4",
                                             "FineWood:55,ElderBark:85,IronNails:250,DeerHide:15,RoundLog:45" },
                DefaultSpeed       = 0.83f,   // about 8.0
                DefaultHealth      = 1400f,
                OldDefaultHealths  = new[] { 1800f },
                DefaultRudderSpeed = 0.7f,
                DefaultSailColor   = Amber,
                DefaultHullColor   = Green,
                DefaultHullStripes = 6,
                DefaultWidth       = 1.3f,
                DefaultLength      = 1.15f,
            },
            new ShipDefinition
            {
                PrefabName         = "DM_BigBusse",
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.65f,
                DisplayName        = "Big Busse",
                Description        = "The largest hold of the three cargo tiers. Slow and hard to turn, but tougher than the Busse and hauls far more.",
                StorageWidth       = 8,
                StorageHeight      = 8,
                DefaultRecipe      = "FineWood:60,ElderBark:90,IronNails:180,DeerHide:20,TrollHide:20",   // weighs 450
                OldDefaultRecipes  = new[] { "FineWood:60,ElderBark:50,IronNails:180,DeerHide:20,TrollHide:20,RoundLog:40",
                                             "FineWood:60,ElderBark:50,IronNails:190,DeerHide:20,TrollHide:20,RoundLog:30",
                                             "FineWood:70,ElderBark:80,IronNails:320,DeerHide:20,TrollHide:20,RoundLog:40",
                                             "FineWood:70,ElderBark:90,IronNails:320,TrollHide:20,RoundLog:40",
                                             "FineWood:70,ElderBark:90,IronNails:320,DeerHide:20,RoundLog:40",
                                             "FineWood:70,ElderBark:90,IronNails:320,DeerHide:20,RoundLog:40,Chain:6",
                                             "FineWood:80,ElderBark:130,IronNails:350,DeerHide:25,RoundLog:70,Chain:6",
                                             "FineWood:80,ElderBark:130,IronNails:350,DeerHide:25,RoundLog:70" },
                DefaultSpeed       = 0.78f,   // about 7.5
                DefaultHealth      = 1900f,
                OldDefaultHealths  = new[] { 2200f },
                DefaultRudderSpeed = 0.6f,
                DefaultSailColor   = Amber,
                DefaultHullColor   = Green,
                DefaultHullStripes = 6,
                DefaultWidth       = 1.5f,    // 1.5 x 1.5: the Greater Byrding's footprint, at longship height, so 64
                DefaultLength      = 1.5f,    // slots is the Knarr's cargo density (1.4x a longship's per deck area), not 2x
                OldDefaultWidths   = new[] { 1.4f },
                OldDefaultLengths  = new[] { 1.3f },
            },
            new ShipDefinition
            {
                PrefabName         = "DM_StableShip",   // saved hash: keep even though it's now the Small Byrding
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.65f,
                DisplayName        = "Small Byrding",
                OldDisplayNames    = new[] { "Byrding", "Stable Ship" },   // must come before the Byrding, which now owns that section name
                Description        = "A longship with a small fenced pen aft of the mast, so a frightened animal can't jump overboard: a boar or two. Two thirds of a longship's hold: the rest is fodder and tack.",
                StorageWidth       = 6,
                StorageHeight      = 2,   // one row off the vanilla Longship's 6x3: fodder and tack take space
                DefaultRecipe      = "FineWood:40,ElderBark:40,IronNails:100,DeerHide:12,Wood:24",   // Wood = the pen's pieces exactly; weighs 270
                OldDefaultRecipes  = new[] { "FineWood:40,ElderBark:40,IronNails:100,DeerHide:10,Wood:24",
                                             "FineWood:40,ElderBark:40,IronNails:100,DeerHide:10,Wood:40",
                                             "FineWood:40,ElderBark:40,IronNails:100,DeerHide:10,Wood:80" },
                DefaultSpeed       = 0.9f,    // about 8.7, measured 8.59: 10% under a longship, the pen's other price
                OldDefaultSpeed    = 1f,
                DefaultHealth      = 1000f,   // unchanged from the vanilla Longship
                DefaultRudderSpeed = 1f,      // unchanged from the vanilla Longship
                DefaultSailColor   = Color.white,
                DefaultHullColor   = Brown,
                DefaultHullStripes = 0,
                DefaultWidth       = 1f,
                DefaultLength      = 1f,
                // Aft of the mast, between it (z = 0.28) and the benches (z = -2.78).  Two rails
                // 0.4 thick at 0.4 and 0.85 make a solid band from 0.2 to 1.05: a boar can't get
                // under, over, or see out.
                Pen = new PenSpec { Length = 2.4f, Width = 3.4f, CenterZ = -1.4f, PostHeight = 1f, RailHeights = new[] { 0.4f, 0.85f } },
            },
            new ShipDefinition
            {
                PrefabName         = "DM_Byrding",
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.65f,
                DisplayName        = "Byrding",
                Description        = "A longship a quarter again the size, with a pen the length of its deck: room for a few boars. A longship's hold, and a little slower to turn.",
                StorageWidth       = 6,
                StorageHeight      = 3,
                DefaultRecipe      = "FineWood:55,ElderBark:55,IronNails:150,DeerHide:15,Wood:36",   // Wood = the pen's pieces exactly; weighs 382
                OldDefaultRecipes  = new[] { "FineWood:55,ElderBark:55,IronNails:150,DeerHide:12,Wood:36",
                                             "FineWood:50,ElderBark:50,IronNails:125,DeerHide:12,Wood:36",
                                             "FineWood:50,ElderBark:50,IronNails:125,DeerHide:12,Wood:50",
                                             "FineWood:50,ElderBark:50,IronNails:125,DeerHide:12,Wood:100" },
                DefaultSpeed       = 0.85f,   // about 8.2, measured 8.25: 15% under a longship
                OldDefaultSpeed    = 0.97f,
                DefaultHealth      = 1400f,   // follows the wood in the recipe (1000 per 80)
                OldDefaultHealths  = new[] { 1250f },
                DefaultRudderSpeed = 0.9f,
                DefaultSailColor   = Color.white,
                DefaultHullColor   = Brown,
                DefaultHullStripes = 0,
                DefaultWidth       = 1f,
                DefaultLength      = 1f,
                DefaultScale       = 1.25f,
                // The whole open deck, mast inside it, from the aft benches to the chest; boar-height
                // rails.  In hull units, so at 1.25x this is 4.25 x 6.75 m with rails to 1.3 m.
                Pen = new PenSpec { Length = 5.4f, Width = 3.4f, CenterZ = 0.15f, PostHeight = 1f, RailHeights = new[] { 0.4f, 0.85f } },
            },
            new ShipDefinition
            {
                PrefabName         = "DM_GreaterByrding",
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.65f,
                DisplayName        = "Greater Byrding",
                Description        = "A longship half again the size, with a high-railed pen the length of its deck: room for a lox, and a fence a wolf can't clear. Slow to turn.",
                StorageWidth       = 6,
                StorageHeight      = 4,
                DefaultRecipe      = "FineWood:65,ElderBark:65,IronNails:200,Wood:52,LoxPelt:20",   // Wood = the pen's pieces exactly; weighs 484 (two trips, or one on Troll Endurance); Lox Pelt makes it Plains-tier, which a lox carrier is anyway
                OldDefaultRecipes  = new[] { "FineWood:65,ElderBark:65,IronNails:200,Wood:52,DeerHide:20,LoxPelt:10",
                                             "FineWood:60,ElderBark:60,IronNails:150,Wood:52,DeerHide:20,LoxPelt:10",
                                             "FineWood:60,ElderBark:60,IronNails:150,Wood:52,LoxPelt:4",
                                             "FineWood:60,ElderBark:60,IronNails:150,Wood:60,LoxPelt:4",
                                             "FineWood:60,ElderBark:60,IronNails:150,Wood:60,LoxPelt:4,WolfPelt:4",
                                             "FineWood:60,ElderBark:60,IronNails:150,Wood:120,LoxPelt:4,WolfPelt:4",
                                             "FineWood:60,ElderBark:60,IronNails:150,DeerHide:15,Wood:120,LoxPelt:4,WolfPelt:4" },
                DefaultSpeed       = 0.8f,    // about 7.7: 20% under a longship
                OldDefaultSpeed    = 0.95f,
                DefaultHealth      = 1600f,   // follows the wood in the recipe (1000 per 80)
                OldDefaultHealths  = new[] { 1500f },
                DefaultRudderSpeed = 0.8f,
                DefaultSailColor   = Color.white,
                DefaultHullColor   = Brown,
                DefaultHullStripes = 0,
                DefaultWidth       = 1f,
                DefaultLength      = 1f,
                DefaultScale       = 1.5f,
                // The whole open deck, mast inside it, from the aft benches to the chest.  In hull
                // units, so at 1.5x this is 5.1 x 8.1 m with rails to 2.25 m -- a wolf jumps, a
                // lox doesn't fit anything smaller.
                Pen = new PenSpec { Length = 5.4f, Width = 3.4f, CenterZ = 0.15f, PostHeight = 1.5f, RailHeights = new[] { 0.4f, 0.85f, 1.3f } },
            },
        };
    }
}
