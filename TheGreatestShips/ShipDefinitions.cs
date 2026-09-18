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
        // DisplayName (see ShipConfig.Bind).  Set this to the previous DisplayName when renaming
        // a ship so ShipConfig can move a saved section across instead of orphaning it.
        public string OldDisplayName;
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

        // Defaults an earlier release shipped.  A saved config still holding one of these was
        // never changed by hand, so it is moved to the current default (see ShipConfig.Migrate).
        public float  OldDefaultSpeed  = float.NaN;
        public string OldDefaultRecipe;
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
                DefaultRecipe      = "FineWood:40,ElderBark:40,IronNails:120,DeerHide:10,TrollHide:10,Guck:10",
                OldDefaultRecipe   = "FineWood:40,ElderBark:40,IronNails:100,LinenThread:20,Resin:30",
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
                OldDisplayName     = "Cargo Longship",
                Description        = "A deep, heavy longship built to haul. Slower and clumsier than a longship, but sturdier, with nearly twice its hold. The smallest of three cargo tiers -- see also the Busse and the Big Busse.",
                StorageWidth       = 8,
                StorageHeight      = 4,
                DefaultRecipe      = "FineWood:40,ElderBark:60,IronNails:180,DeerHide:10,RoundLog:30,Chain:2",
                OldDefaultRecipe   = "FineWood:40,ElderBark:60,IronNails:150,DeerHide:10,RoundLog:30",
                DefaultSpeed       = 0.87f,   // about 8.6, measured 8.58
                DefaultHealth      = 1500f,   // Longship 1000
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
                DefaultRecipe      = "FineWood:55,ElderBark:85,IronNails:250,DeerHide:15,RoundLog:45,Chain:4",
                DefaultSpeed       = 0.83f,   // about 8.0
                DefaultHealth      = 1800f,
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
                DefaultRecipe      = "FineWood:80,ElderBark:130,IronNails:350,DeerHide:25,RoundLog:70,Chain:6",
                DefaultSpeed       = 0.78f,   // about 7.5
                DefaultHealth      = 2200f,
                DefaultRudderSpeed = 0.6f,
                DefaultSailColor   = Amber,
                DefaultHullColor   = Green,
                DefaultHullStripes = 6,
                DefaultWidth       = 1.4f,
                DefaultLength      = 1.3f,
            },
            new ShipDefinition
            {
                PrefabName         = "DM_StableShip",
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.65f,
                DisplayName        = "Byrding",
                OldDisplayName     = "Stable Ship",
                Description        = "A longship with a fenced pen on deck, so a frightened animal can't jump overboard. Two thirds of a longship's hold: the rest is fodder and tack.",
                StorageWidth       = 6,
                StorageHeight      = 2,   // one row off the vanilla Longship's 6x3: fodder and tack take space
                DefaultRecipe      = "FineWood:40,ElderBark:40,IronNails:100,DeerHide:10,Wood:80",
                DefaultSpeed       = 1f,      // unchanged from the vanilla Longship: 9.65
                DefaultHealth      = 1000f,   // unchanged from the vanilla Longship
                DefaultRudderSpeed = 1f,      // unchanged from the vanilla Longship
                DefaultSailColor   = Color.white,
                DefaultHullColor   = Brown,
                DefaultHullStripes = 0,
                DefaultWidth       = 1f,
                DefaultLength      = 1f,
            },
        };
    }
}
