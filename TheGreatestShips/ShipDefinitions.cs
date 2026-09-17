using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// One ship this mod adds: a clone of a vanilla hull with its own speed, storage, sail tint
    /// and recipe.  The config section is the display name.
    /// </summary>
    internal sealed class ShipDefinition
    {
        // Saved into worlds as a hash: never rename.
        public string PrefabName;
        public string BasePrefab;
        public string BaseName;       // for config descriptions
        public float  BaseTopSpeed;   // Greg's measured top speed of the base hull
        public string DisplayName;
        public string Description;
        public int    StorageWidth;
        public int    StorageHeight;
        public string DefaultRecipe;
        public float  DefaultSpeed;   // multiplier on the base hull's top speed
        public Color  DefaultSailColor;
        public Color  DefaultHullColor;
        public int    DefaultHullStripes; // painted bands on the hull; 0 paints it solid
        public float  DefaultWidth;       // sideways scale of the whole ship
    }

    internal static class ShipDefinitions
    {
        private static readonly Color Blue  = new Color(0.375f, 0.55f, 0.875f, 1f);
        private static readonly Color Amber = new Color(1f, 0.8f, 0.5f, 1f);
        private static readonly Color Red   = new Color(1f, 0.62f, 0.55f, 1f);
        private static readonly Color Green = new Color(0.6f, 0.85f, 0.6f, 1f);

        // Top speeds measured in game: Karve 8.8, Longship 9.5, and OdinShipPlus's Fast Ship
        // Skuldelev 12.2, the ceiling these ships are tuned against.
        public static readonly ShipDefinition[] All =
        {
            new ShipDefinition
            {
                PrefabName       = "DM_FastKarve",
                BasePrefab       = "Karve",
                BaseName         = "Karve",
                BaseTopSpeed     = 8.8f,
                DisplayName      = "Fast Karve",
                Description      = "A light racing karve with an oversized sail. Faster than a longship, with room for little more than a spare cloak.",
                StorageWidth     = 2,
                StorageHeight    = 1,
                DefaultRecipe    = "FineWood:30,ElderBark:20,BronzeNails:60,TrollHide:8,Resin:20",
                DefaultSpeed     = 1.14f,   // about 10
                DefaultSailColor = Blue,
                DefaultHullColor = Red,
                DefaultHullStripes = 6,
                DefaultWidth     = 0.85f,
            },
            new ShipDefinition
            {
                PrefabName       = "DM_FastLongship",
                BasePrefab       = "VikingShip",
                BaseName         = "Longship",
                BaseTopSpeed     = 9.5f,
                DisplayName      = "Fast Longship",
                Description      = "A lean longship under a linen sail, the fastest hull on the water. Half the hold of a longship.",
                StorageWidth     = 3,
                StorageHeight    = 3,
                DefaultRecipe    = "FineWood:40,ElderBark:40,IronNails:100,LinenThread:20,Resin:30",
                DefaultSpeed     = 1.28f,   // about 12.2
                DefaultSailColor = Blue,
                DefaultHullColor = Red,
                DefaultHullStripes = 6,
                DefaultWidth     = 0.85f,
            },
            new ShipDefinition
            {
                PrefabName       = "DM_CargoLongship",
                BasePrefab       = "VikingShip",
                BaseName         = "Longship",
                BaseTopSpeed     = 9.5f,
                DisplayName      = "Cargo Longship",
                Description      = "A deep, heavy longship built to haul. Slower than a karve, with nearly twice a longship's hold.",
                StorageWidth     = 8,
                StorageHeight    = 4,
                DefaultRecipe    = "FineWood:40,ElderBark:60,IronNails:150,DeerHide:10,RoundLog:30",
                DefaultSpeed     = 0.89f,   // about 8.5
                DefaultSailColor = Amber,
                DefaultHullColor = Green,
                DefaultHullStripes = 6,
                DefaultWidth     = 1.25f,
            },
        };
    }
}
