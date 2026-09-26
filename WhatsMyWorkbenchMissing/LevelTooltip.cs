using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WhatsMyWorkbenchMissing
{
    /// <summary>
    /// Puts the report on the level badge of the crafting panel.
    ///
    /// InventoryGui.UpdateRecipe runs every frame the inventory is open and is where the game
    /// writes the station's name, icon and level into the panel. This postfix gives the level
    /// badge one of the game's own UITooltip components, borrowing the tooltip prefab the craft
    /// button already uses so it looks like every other tooltip in the panel, and keeps its text
    /// current: at once when the station or its level changes, otherwise twice a second, which
    /// is enough for the material counts to follow the inventory while the tooltip is up.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class LevelTooltip
    {
        private const float RefreshInterval = 0.5f;

        private static UITooltip _tooltip;
        private static bool _warnedNoPrefab;
        private static CraftingStation _lastStation;
        private static int _lastLevel = -1;
        private static float _nextRefresh;

        private static void Postfix(InventoryGui __instance, Player player)
        {
            var root = __instance.m_craftingStationLevelRoot;
            if (root == null || player == null) return;

            var station = WhatsMyWorkbenchMissingMod.ModEnabled.Value ? player.GetCurrentCraftingStation() : null;
            if (station == null)
            {
                // Nothing to say: the badge is hidden when there is no station, and an empty
                // tooltip is how UITooltip is told to show nothing.
                if (_tooltip != null) _tooltip.Set("", "");
                _lastStation = null;
                _lastLevel = -1;
                return;
            }

            if (_tooltip == null || _tooltip.gameObject != root.gameObject) _tooltip = Attach(__instance, root);
            if (_tooltip == null) return;

            int level = station.GetLevel();
            if (station == _lastStation && level == _lastLevel && Time.unscaledTime < _nextRefresh) return;
            _lastStation = station;
            _lastLevel = level;
            _nextRefresh = Time.unscaledTime + RefreshInterval;

            UpgradeReport.Build(station, player, out string topic, out string text);
            _tooltip.Set(topic, text);
        }

        /// <summary>
        /// The UITooltip on the level badge, made if the badge has none yet. Whatever the badge
        /// draws with is made a raycast target so the pointer registers over it, and if it draws
        /// nothing itself a clear image is added to catch the pointer over its whole rect.
        /// </summary>
        private static UITooltip Attach(InventoryGui gui, RectTransform root)
        {
            var existing = root.GetComponent<UITooltip>();
            if (existing != null) return existing;

            GameObject prefab = null;
            var craftTip = gui.m_craftButton != null ? gui.m_craftButton.GetComponent<UITooltip>() : null;
            if (craftTip != null) prefab = craftTip.m_tooltipPrefab;
            if (prefab == null)
            {
                foreach (var tip in gui.GetComponentsInChildren<UITooltip>(true))
                {
                    if (tip.m_tooltipPrefab != null) { prefab = tip.m_tooltipPrefab; break; }
                }
            }
            if (prefab == null)
            {
                if (!_warnedNoPrefab)
                {
                    _warnedNoPrefab = true;
                    WhatsMyWorkbenchMissingMod.Log.LogWarning("No tooltip prefab found in the inventory panel; the level tooltip is off.");
                }
                return null;
            }

            bool rootDraws = false;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = true;
                if (graphic.gameObject == root.gameObject) rootDraws = true;
            }
            if (!rootDraws)
            {
                var catcher = root.gameObject.AddComponent<Image>();
                catcher.color = Color.clear;
                catcher.raycastTarget = true;
            }

            var tooltip = root.gameObject.AddComponent<UITooltip>();
            tooltip.m_tooltipPrefab = prefab;
            WhatsMyWorkbenchMissingMod.Log.LogInfo("Level tooltip attached to the crafting panel.");
            return tooltip;
        }
    }
}
