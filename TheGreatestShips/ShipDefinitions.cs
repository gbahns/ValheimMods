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

        // Top speeds in full wind at the best angle (64° off the stern), from Greg's CaptainsLog
        // sailing logs: Karve 7.3, OdinShipPlus Cargo Ship 8.7, Fast Ship Skuldelev 12.2. The
        // Longship has no full-sail log; 9.4 follows from its sail force and the logged hulls.
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
                BaseTopSpeed       = 9.4f,
                DisplayName        = "Fast Longship",
                Description        = "A lean longship under a linen sail, the fastest hull on the water. Two thirds of a longship's hold.",
                StorageWidth       = 4,
                StorageHeight      = 3,
                DefaultRecipe      = "FineWood:40,ElderBark:40,IronNails:100,LinenThread:20,Resin:30",
                DefaultSpeed       = 1.28f,   // about 12, just under the Fast Ship Skuldelev's 12.2
                DefaultHealth      = 800f,    // Longship 1000: a lighter hull
                DefaultRudderSpeed = 1f,
                DefaultSailColor   = Blue,
                DefaultHullColor   = Red,
                DefaultHullStripes = 6,
                DefaultWidth       = 0.85f,
            },
            new ShipDefinition
            {
                PrefabName         = "DM_CargoLongship",
                BasePrefab         = "VikingShip",
                BaseName           = "Longship",
                BaseTopSpeed       = 9.4f,
                DisplayName        = "Cargo Longship",
                Description        = "A deep, heavy longship built to haul. Slower and clumsier than a longship, but sturdier, with nearly twice its hold.",
                StorageWidth       = 8,
                StorageHeight      = 4,
                DefaultRecipe      = "FineWood:40,ElderBark:60,IronNails:180,DeerHide:10,RoundLog:30",
                OldDefaultRecipe   = "FineWood:40,ElderBark:60,IronNails:150,DeerHide:10,RoundLog:30",
                DefaultSpeed       = 0.89f,   // about 8.4
                DefaultHealth      = 1500f,   // Longship 1000
                DefaultRudderSpeed = 0.8f,    // Longship 1.0, as OdinShipPlus's cargo ships
                DefaultSailColor   = Amber,
                DefaultHullColor   = Green,
                DefaultHullStripes = 6,
                DefaultWidth       = 1.25f,
            },
        };
    }
}
