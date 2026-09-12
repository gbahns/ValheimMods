using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// Pin type helpers. Custom icons are allocated at runtime by IconRegistry from 100 upward;
    /// Valheim's PinType enum is just an int, so a new value only needs an icon entry and a large
    /// enough visibility array.
    /// </summary>
    internal static class PinTypes
    {
        internal static bool IsCustom(int type) => type >= IconRegistry.Base;

        /// <summary>The five vanilla placeable icons plus this mod's custom types.</summary>
        internal static bool IsPlayerPlaceable(int type)
        {
            return type == (int)Minimap.PinType.Icon0
                || type == (int)Minimap.PinType.Icon1
                || type == (int)Minimap.PinType.Icon2
                || type == (int)Minimap.PinType.Icon3
                || type == (int)Minimap.PinType.Icon4
                || IsCustom(type);
        }
    }

    [HarmonyPatch(typeof(Minimap), "Start")]
    internal static class Minimap_Start_PinTypes_Patch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance) => IconRegistry.OnMinimapStart(__instance);
    }

    // Belt and braces: if another mod replaces m_visibleIconTypes with a shorter array, grow it
    // back before vanilla indexes it with one of our types.
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Minimap_UpdatePins_Guard_Patch
    {
        private static void Prefix(Minimap __instance) => IconRegistry.EnsureArrays(__instance);
    }
}
