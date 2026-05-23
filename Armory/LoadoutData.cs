using System;
using System.Collections.Generic;

namespace Armory
{
    [Serializable]
    public class SavedItem
    {
        public string SharedName; // m_shared.m_name localization key, e.g. "$item_helmet_troll"
        public int Quality;
        public int Variant;
    }

    [Serializable]
    public class LoadoutSlot
    {
        public string Name = "New Loadout";
        public SavedItem Helmet;
        public SavedItem Chest;
        public SavedItem Legs;
        public SavedItem Shoulder;
        public SavedItem Utility;
        public SavedItem RightHand;
        public SavedItem LeftHand;

        public bool IsEmpty() =>
            Helmet == null && Chest == null && Legs == null &&
            Shoulder == null && Utility == null &&
            RightHand == null && LeftHand == null;
    }

    [Serializable]
    public class ArmoryData
    {
        public List<LoadoutSlot> Slots = new List<LoadoutSlot>();
    }
}
