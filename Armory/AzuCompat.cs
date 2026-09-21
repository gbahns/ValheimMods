using System;
using System.Reflection;
using HarmonyLib;

namespace Armory
{
    /// <summary>
    /// Optional bridge to AzuExtendedPlayerInventory. Azu grows the player's inventory grid
    /// downward, and not every added row means the same thing: "Extra Inventory Rows" are
    /// plain bag rows, while the equipment row and the quick slots are dedicated cells with
    /// fixed positions. Only the dedicated cells belong in a loadout, so the capture asks Azu
    /// which is which rather than guessing from the coordinates — a guess by row number was
    /// how building materials in an extra bag row ended up saved as part of a loadout.
    ///
    /// Everything goes through reflection so Azu stays optional: no reference, no hard
    /// dependency, and a game without it (or with an Azu too old to answer) simply reports
    /// that no cell is special, which is the truth for a vanilla 8×4 bag.
    /// </summary>
    internal static class AzuCompat
    {
        private static bool _resolved;
        private static MethodInfo _isQuickCell;
        private static MethodInfo _isEquipmentCell;
        private static bool _warnedInvoke;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var api = AccessTools.TypeByName("AzuEPI.API");
            if (api == null)
            {
                Jotunn.Logger.LogInfo("[Armory] AzuExtendedPlayerInventory not loaded; only worn gear and the hotbar are captured.");
                return;
            }

            // public static bool IsQuickCell(Inventory inv, int x, int y, out int slotIndex)
            var sig = new[] { typeof(Inventory), typeof(int), typeof(int), typeof(int).MakeByRefType() };
            _isQuickCell     = AccessTools.Method(api, "IsQuickCell", sig);
            _isEquipmentCell = AccessTools.Method(api, "IsEquipmentCell", sig);

            if (_isQuickCell == null || _isEquipmentCell == null)
                Jotunn.Logger.LogWarning("[Armory] AzuExtendedPlayerInventory is loaded but its API lacks IsQuickCell/IsEquipmentCell (too old?); quick-slot items will not be captured.");
            else
                Jotunn.Logger.LogInfo("[Armory] AzuExtendedPlayerInventory detected; quick-slot and equipment cells will be captured with loadouts.");
        }

        /// <summary>True when Azu is loaded and answers cell queries.</summary>
        public static bool IsAvailable { get { Resolve(); return _isQuickCell != null && _isEquipmentCell != null; } }

        /// <summary>A cell Azu reserves for a specific purpose: a quick slot or an equipment slot.</summary>
        public static bool IsDedicatedCell(Inventory inv, int x, int y)
            => Call(_isQuickCell, inv, x, y) || Call(_isEquipmentCell, inv, x, y);

        public static bool IsQuickCell(Inventory inv, int x, int y)     => Call(_isQuickCell, inv, x, y);
        public static bool IsEquipmentCell(Inventory inv, int x, int y) => Call(_isEquipmentCell, inv, x, y);

        private static bool Call(MethodInfo method, Inventory inv, int x, int y)
        {
            Resolve();
            if (method == null || inv == null) return false;
            try
            {
                return (bool)method.Invoke(null, new object[] { inv, x, y, 0 });
            }
            catch (Exception e)
            {
                if (!_warnedInvoke)
                {
                    _warnedInvoke = true;
                    Jotunn.Logger.LogWarning($"[Armory] AzuExtendedPlayerInventory cell query failed; treating cells as ordinary bag cells: {e.Message}");
                }
                return false;
            }
        }
    }
}
