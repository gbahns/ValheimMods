using System.Collections.Generic;
using System.Linq;

namespace Armory
{
    /// <summary>
    /// The player's loadout clipboard (#85): loadout definitions copied from one rack, carried
    /// in the character's own save (Player.m_customData, written to the .fch with everything
    /// else), and pasted into another.  Definitions only, the names and cells and what was
    /// saved in each; the items stay where they are, and a pasted loadout whose gear is not in
    /// the new rack shows it as missing the way any loadout does.  It survives logout and goes
    /// to any world the character visits.
    /// </summary>
    internal static class LoadoutClipboard
    {
        private const string Key = "armory_clipboard";

        private static Dictionary<string, string> Store => Player.m_localPlayer?.m_customData;

        /// <summary>The clipboard's text as stored, "" when empty; what Write would produce for
        /// the same loadouts, so the panel can tell "already copied" by comparing strings.</summary>
        public static string Stored => Store != null && Store.TryGetValue(Key, out var t) && t != null ? t : "";

        /// <summary>What the clipboard would hold after copying these.</summary>
        public static string Text(IEnumerable<LoadoutSlot> slots) =>
            ArmorySerializer.Serialize(new ArmoryData { Slots = slots.Where(s => s != null && !s.IsEmpty()).ToList() });

        /// <summary>Whether the clipboard already holds exactly these loadouts.</summary>
        public static bool Matches(IList<LoadoutSlot> slots) =>
            slots != null && slots.Count > 0 && slots.Any(s => s != null && !s.IsEmpty()) && Stored == Text(slots);

        /// <summary>How many loadouts the clipboard holds; 0 when nothing has been copied.</summary>
        public static int Count => TryRead(out var slots) ? slots.Count : 0;

        /// <summary>The copied loadouts, fresh copies each call, or false when there are none.</summary>
        public static bool TryRead(out List<LoadoutSlot> slots)
        {
            slots = null;
            var store = Store;
            if (store == null || !store.TryGetValue(Key, out var text) || string.IsNullOrEmpty(text)) return false;
            try { slots = ArmorySerializer.Deserialize(text).Slots.Where(s => s != null && !s.IsEmpty()).ToList(); }
            catch (System.Exception e) { Jotunn.Logger.LogWarning($"[Armory] Clipboard unreadable: {e.Message}"); return false; }
            return slots.Count > 0;
        }

        /// <summary>Replaces the clipboard with copies of these loadouts.</summary>
        public static void Write(IEnumerable<LoadoutSlot> slots)
        {
            var store = Store;
            if (store == null) return;
            var data = new ArmoryData { Slots = slots.Where(s => s != null && !s.IsEmpty()).ToList(), Saved = true };
            store[Key] = ArmorySerializer.Serialize(data);
            Jotunn.Logger.LogInfo($"[Armory] Clipboard: {data.Slots.Count} loadout(s) copied");
        }
    }
}
