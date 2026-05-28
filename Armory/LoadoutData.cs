using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Armory
{
    [Serializable]
    public class SavedItem
    {
        public string SharedName; // m_shared.m_name localization key, e.g. "$item_helmet_troll"
        public int Quality;
        public int Variant;
        public int Stack;         // stack count at the time of saving (relevant for hotbar items)
        public int GridX = -1;    // -1 = no specific position (used for Azu extended consumable slots)
        public int GridY = -1;
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

        // Bottom-row inventory slots (the 1-8 hotbar).  Indexed by x position (0..7).
        // Null entries mean "no item saved for this slot".
        public SavedItem[] Hotbar = new SavedItem[8];

        // Items the player had parked in extended inventory slots — i.e. grid positions outside
        // the standard 8×4 bag.  Used by mods like AzuExtendedPlayerInventory, which exposes
        // dedicated slots for food, potions, and trinkets at positions x >= 8 or y >= 4.  We
        // save the exact (GridX, GridY) on each item so Load can restore them to the same slot.
        public List<SavedItem> Extended = new List<SavedItem>();

        public bool IsEmpty() =>
            Helmet == null && Chest == null && Legs == null &&
            Shoulder == null && Utility == null &&
            RightHand == null && LeftHand == null &&
            (Hotbar == null || Hotbar.All(h => h == null || string.IsNullOrEmpty(h.SharedName))) &&
            (Extended == null || Extended.Count == 0);
    }

    [Serializable]
    public class ArmoryData
    {
        public List<LoadoutSlot> Slots = new List<LoadoutSlot>();
    }

    /// <summary>
    /// Plain-text serializer for ArmoryData.  Replaces UnityEngine.JsonUtility which silently
    /// returns "{}" for our types in this Valheim runtime (stripped JsonSerializeModule).
    ///
    /// Format: line-based, slots separated by "SLOT" markers, each line is KEY=VALUE.
    /// Item values are pipe-delimited:  SharedName|Quality|Variant|Stack.
    /// Slot names are URI-escaped so they can contain any character without breaking the parser.
    /// </summary>
    internal static class ArmorySerializer
    {
        private const string SLOT_MARKER = "SLOT";

        public static string Serialize(ArmoryData data)
        {
            var sb = new StringBuilder();
            if (data?.Slots == null) return sb.ToString();

            foreach (var slot in data.Slots)
            {
                sb.AppendLine(SLOT_MARKER);
                sb.Append("NAME=").AppendLine(Uri.EscapeDataString(slot.Name ?? "Loadout"));
                AppendItem(sb, "HELMET",   slot.Helmet);
                AppendItem(sb, "CHEST",    slot.Chest);
                AppendItem(sb, "LEGS",     slot.Legs);
                AppendItem(sb, "SHOULDER", slot.Shoulder);
                AppendItem(sb, "UTILITY",  slot.Utility);
                AppendItem(sb, "RIGHT",    slot.RightHand);
                AppendItem(sb, "LEFT",     slot.LeftHand);
                if (slot.Hotbar != null)
                {
                    for (int i = 0; i < slot.Hotbar.Length; i++)
                        AppendItem(sb, "HOTBAR" + i, slot.Hotbar[i]);
                }
                if (slot.Extended != null)
                {
                    foreach (var ext in slot.Extended)
                        AppendItem(sb, "EXT", ext);
                }
            }
            return sb.ToString();
        }

        // Item line format:  KEY=SharedName|Quality|Variant|Stack[|GridX|GridY]
        // GridX/GridY are only written when set (>= 0) — used for extended-inventory entries.
        private static void AppendItem(StringBuilder sb, string key, SavedItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.SharedName)) return;
            sb.Append(key).Append('=')
              .Append(item.SharedName).Append('|')
              .Append(item.Quality).Append('|')
              .Append(item.Variant).Append('|')
              .Append(item.Stack);
            if (item.GridX >= 0 || item.GridY >= 0)
                sb.Append('|').Append(item.GridX).Append('|').Append(item.GridY);
            sb.AppendLine();
        }

        public static ArmoryData Deserialize(string text)
        {
            var data = new ArmoryData();
            if (string.IsNullOrEmpty(text)) return data;

            LoadoutSlot current = null;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrEmpty(line)) continue;

                if (line == SLOT_MARKER)
                {
                    current = new LoadoutSlot { Hotbar = new SavedItem[8], Extended = new List<SavedItem>() };
                    data.Slots.Add(current);
                    continue;
                }
                if (current == null) continue;

                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var key = line.Substring(0, eq);
                var val = line.Substring(eq + 1);

                if (key == "NAME") { current.Name = Uri.UnescapeDataString(val); continue; }

                var item = ParseItem(val);
                if (item == null) continue;

                switch (key)
                {
                    case "HELMET":   current.Helmet    = item; break;
                    case "CHEST":    current.Chest     = item; break;
                    case "LEGS":     current.Legs      = item; break;
                    case "SHOULDER": current.Shoulder  = item; break;
                    case "UTILITY":  current.Utility   = item; break;
                    case "RIGHT":    current.RightHand = item; break;
                    case "LEFT":     current.LeftHand  = item; break;
                    case "EXT":      current.Extended.Add(item); break;
                    default:
                        if (key.StartsWith("HOTBAR")
                            && int.TryParse(key.Substring(6), out int idx)
                            && idx >= 0 && idx < current.Hotbar.Length)
                            current.Hotbar[idx] = item;
                        break;
                }
            }
            return data;
        }

        private static SavedItem ParseItem(string val)
        {
            var parts = val.Split('|');
            if (parts.Length < 1 || string.IsNullOrEmpty(parts[0])) return null;
            var item = new SavedItem { SharedName = parts[0] };
            if (parts.Length > 1) int.TryParse(parts[1], out item.Quality);
            if (parts.Length > 2) int.TryParse(parts[2], out item.Variant);
            if (parts.Length > 3) int.TryParse(parts[3], out item.Stack);
            if (parts.Length > 4) int.TryParse(parts[4], out item.GridX);
            if (parts.Length > 5) int.TryParse(parts[5], out item.GridY);
            return item;
        }
    }
}
