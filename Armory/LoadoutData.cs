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

    /// <summary>
    /// How the panel groups a loadout's cells.  Worn gear is grouped by the slot it is worn in;
    /// hotbar and quick-slot items by what they are.  Flags only so a set of them can be held
    /// in one value; nothing is saved by category — inclusion is per cell.
    /// </summary>
    [Flags]
    public enum LoadoutCategory
    {
        None        = 0,
        Armor       = 1,    // helmet, chest, legs
        Cape        = 2,
        Accessories = 4,    // utility belt, trinket
        Weapons     = 8,    // whatever rides the hotbar that isn't ammo or a consumable
        Ammo        = 16,
        Food        = 32,
        Meads       = 64,
    }

    /// <summary>The worn slots a loadout records, as flags: the keys for per-slot inclusion.</summary>
    [Flags]
    public enum WornSlot
    {
        None    = 0,
        Helmet  = 1,
        Chest   = 2,
        Legs    = 4,
        Cape    = 8,
        Belt    = 16,
        Trinket = 32,
        All     = Helmet | Chest | Legs | Cape | Belt | Trinket,
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
        public SavedItem Trinket;

        // Hotbar: the top row of the grid (y=0).  Indexed by x position (0..7).
        // Null entries mean "no item saved for this slot".
        public SavedItem[] Hotbar = new SavedItem[8];

        // Items the player had parked in cells AzuExtendedPlayerInventory reserves: its quick
        // slots (food, potions) and its equipment row.  Which cells those are is Azu's answer,
        // not a coordinate rule — Azu can also add plain bag rows, and those are not captured.
        // We save the exact (GridX, GridY) on each item so Load can restore it to the same cell.
        public List<SavedItem> Extended = new List<SavedItem>();

        // ── What Load applies ──────────────────────────────────────────────────────
        // Every cell is opt-in and keyed by its position, never by what is in it, so re-saving
        // a slot keeps the player's choices while the items behind them change.  Save records
        // everything regardless; these decide what Load, the "wearing it" dot, and the summary
        // pay attention to.  A worn slot that is included but empty means "wear nothing there".
        public const WornSlot DefaultIncludeWorn = WornSlot.Helmet | WornSlot.Chest | WornSlot.Legs | WornSlot.Cape;

        public WornSlot IncludeWorn   = DefaultIncludeWorn;
        public int      IncludeHotbar = 0;                        // bit x = hotbar cell x
        public List<Vector2i> IncludeExtended = new List<Vector2i>();
        // Racks written before per-cell inclusion existed loaded everything.  Reading one sets
        // this so they still do; it clears the first time a quick-slot cell is toggled.
        public bool IncludeAllExtended;

        public bool IncludesWorn(WornSlot slot)     => (IncludeWorn & slot) != 0;
        public bool IncludesHotbar(int x)           => x >= 0 && x < 8 && (IncludeHotbar & (1 << x)) != 0;
        public bool IncludesExtended(int x, int y)  => IncludeAllExtended || IncludeExtended.Contains(new Vector2i(x, y));

        public void SetWorn(WornSlot slot, bool on)
        {
            if (on) IncludeWorn |= slot; else IncludeWorn &= ~slot;
        }

        public void SetHotbar(int x, bool on)
        {
            if (x < 0 || x >= 8) return;
            if (on) IncludeHotbar |= 1 << x; else IncludeHotbar &= ~(1 << x);
        }

        public void SetExtended(int x, int y, bool on)
        {
            if (IncludeAllExtended)
            {
                // Make "everything" explicit before changing one cell of it.
                IncludeAllExtended = false;
                IncludeExtended = Extended
                    .Where(e => e != null && !string.IsNullOrEmpty(e.SharedName))
                    .Select(e => new Vector2i(e.GridX, e.GridY)).Distinct().ToList();
            }
            var cell = new Vector2i(x, y);
            if (on) { if (!IncludeExtended.Contains(cell)) IncludeExtended.Add(cell); }
            else IncludeExtended.Remove(cell);
        }

        /// <summary>Carry another slot's choices over, cell for cell — what Save does.</summary>
        public void CopyInclusionFrom(LoadoutSlot other)
        {
            if (other == null) return;
            IncludeWorn        = other.IncludeWorn;
            IncludeHotbar      = other.IncludeHotbar;
            IncludeAllExtended = other.IncludeAllExtended;
            IncludeExtended    = other.IncludeExtended != null ? new List<Vector2i>(other.IncludeExtended) : new List<Vector2i>();
        }

        public void IncludeEverything()
        {
            IncludeWorn        = WornSlot.All;
            IncludeHotbar      = 0xFF;
            IncludeAllExtended = true;
            IncludeExtended    = new List<Vector2i>();
        }

        public bool IsEmpty() =>
            Helmet == null && Chest == null && Legs == null &&
            Shoulder == null && Utility == null && Trinket == null &&
            (Hotbar == null || Hotbar.All(h => h == null || string.IsNullOrEmpty(h.SharedName))) &&
            (Extended == null || Extended.Count == 0);
    }

    [Serializable]
    public class ArmoryData
    {
        public List<LoadoutSlot> Slots = new List<LoadoutSlot>();

        // True when this came from a rack that has been saved to at least once.  A rack that
        // never has gets the starting set of empty slots; one that has keeps exactly the slots
        // it was left with, even none.
        public bool Saved;
    }

    /// <summary>
    /// Plain-text serializer for ArmoryData.  Replaces UnityEngine.JsonUtility which silently
    /// returns "{}" for our types in this Valheim runtime (stripped JsonSerializeModule).
    ///
    /// Format: line-based, slots separated by "SLOT" markers, each line is KEY=VALUE.
    /// Item values are pipe-delimited:  SharedName|Quality|Variant|Stack.
    /// Slot names are URI-escaped so they can contain any character without breaking the parser.
    ///
    /// Versions before 1.4.0 skip keys they do not know (INCLUDE, TRINKET), so a rack written by
    /// this version still reads there — as a loadout that loads everything and has no trinket.
    /// Should such a version save the slot again, both are lost.  The other way round, a slot
    /// with no INCLUDE line was written by one of those versions and is read as loading
    /// everything, which is what it did there.
    /// </summary>
    internal static class ArmorySerializer
    {
        private const string SLOT_MARKER = "SLOT";
        private const string HEADER      = "ARMORY";   // so a rack with no slots left still reads as saved

        public static string Serialize(ArmoryData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HEADER);
            if (data?.Slots == null) return sb.ToString();

            foreach (var slot in data.Slots)
            {
                sb.AppendLine(SLOT_MARKER);
                sb.Append("NAME=").AppendLine(Uri.EscapeDataString(slot.Name ?? "Loadout"));
                sb.Append("INCLUDE=")
                  .Append((int)slot.IncludeWorn).Append('|')
                  .Append(slot.IncludeHotbar).Append('|')
                  .AppendLine(slot.IncludeAllExtended
                      ? "*"
                      : string.Join(";", (slot.IncludeExtended ?? new List<Vector2i>()).Select(c => $"{c.x},{c.y}")));
                AppendItem(sb, "HELMET",   slot.Helmet);
                AppendItem(sb, "CHEST",    slot.Chest);
                AppendItem(sb, "LEGS",     slot.Legs);
                AppendItem(sb, "SHOULDER", slot.Shoulder);
                AppendItem(sb, "UTILITY",  slot.Utility);
                AppendItem(sb, "TRINKET",  slot.Trinket);
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
            data.Saved = true;   // anything at all in the ZDO means someone saved this rack

            LoadoutSlot current = null;
            bool sawInclude = false;

            // A slot from before per-cell inclusion has no INCLUDE line; it loaded everything.
            // An empty one was never saved, so it takes the defaults like a new slot.
            void Finish()
            {
                if (current != null && !sawInclude && !current.IsEmpty()) current.IncludeEverything();
            }

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrEmpty(line)) continue;

                if (line == SLOT_MARKER)
                {
                    Finish();
                    current = new LoadoutSlot { Hotbar = new SavedItem[8], Extended = new List<SavedItem>() };
                    sawInclude = false;
                    data.Slots.Add(current);
                    continue;
                }
                if (current == null) continue;

                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var key = line.Substring(0, eq);
                var val = line.Substring(eq + 1);

                if (key == "NAME") { current.Name = Uri.UnescapeDataString(val); continue; }
                if (key == "INCLUDE") { ParseInclude(current, val); sawInclude = true; continue; }

                var item = ParseItem(val);
                if (item == null) continue;

                switch (key)
                {
                    case "HELMET":   current.Helmet    = item; break;
                    case "CHEST":    current.Chest     = item; break;
                    case "LEGS":     current.Legs      = item; break;
                    case "SHOULDER": current.Shoulder  = item; break;
                    case "UTILITY":  current.Utility   = item; break;
                    case "TRINKET":  current.Trinket   = item; break;
                    case "EXT":      current.Extended.Add(item); break;
                    default:
                        // RIGHT/LEFT from versions that recorded the hands land here and are dropped.
                        if (key.StartsWith("HOTBAR")
                            && int.TryParse(key.Substring(6), out int idx)
                            && idx >= 0 && idx < current.Hotbar.Length)
                            current.Hotbar[idx] = item;
                        break;
                }
            }
            Finish();
            return data;
        }

        // INCLUDE=<worn flags>|<hotbar bit mask>|<x,y;x,y or * for all>
        private static void ParseInclude(LoadoutSlot slot, string val)
        {
            var parts = val.Split('|');
            if (parts.Length > 0 && int.TryParse(parts[0], out int worn)) slot.IncludeWorn = (WornSlot)worn & WornSlot.All;
            if (parts.Length > 1 && int.TryParse(parts[1], out int hot))  slot.IncludeHotbar = hot & 0xFF;
            slot.IncludeExtended = new List<Vector2i>();
            slot.IncludeAllExtended = false;
            if (parts.Length > 2)
            {
                if (parts[2] == "*") slot.IncludeAllExtended = true;
                else foreach (var cell in parts[2].Split(';'))
                {
                    var xy = cell.Split(',');
                    if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y))
                        slot.IncludeExtended.Add(new Vector2i(x, y));
                }
            }
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
