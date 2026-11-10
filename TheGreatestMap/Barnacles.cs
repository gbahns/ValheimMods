using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// Barnacles are the one deposit that swims away. They are mined off the back of a leviathan,
    /// and a few hits in it shakes, dives, and takes them with it -- so a marker written where they
    /// were is a lie within a minute of being written, and the ground it points at is open ocean.
    ///
    /// The Ashlands' flametal is the same arrangement in lava: a different prefab lying in a
    /// different liquid, but the same Leviathan component underneath, so the one patch below
    /// catches both without being told about the second.
    ///
    /// Sinking is treated as mining them out: the markers are crossed off rather than erased, so
    /// the record of where a leviathan once was is kept, which is the same bargain struck for a
    /// deposit worked to nothing. It does not come back -- a leviathan that dives is gone for
    /// good, as a flametal deposit that sinks into the lava is -- so the crossed-off marker is
    /// the final word on that spot rather than a note until next time.
    /// </summary>
    internal static class Barnacles
    {
        /// <summary>A leviathan is a big animal, and its barnacles are spread over all of it.</summary>
        private const float Reach = 40f;

        internal static void Sank(Leviathan leviathan)
        {
            if (leviathan == null || Player.m_localPlayer == null || !PersonalMap.Loaded) return;
            int n = ClientPins.MarkClearedNear(leviathan.transform.position, Reach, Category.Ore);
            if (n <= 0) return;
            ClientPins.Restyle();
            string prefab = Catalog.PrefabName(leviathan.gameObject) ?? "";
            bool lava = prefab.IndexOf("Lava", System.StringComparison.OrdinalIgnoreCase) >= 0;
            string what = lava ? "flametal" : "barnacle";
            TheGreatestMapMod.Message(n == 1
                ? $"The leviathan sank; its {what} marker is crossed off."
                : $"The leviathan sank; {n} {what} markers are crossed off.");
        }
    }

    // The dive is decided by whoever owns the leviathan and announced to everyone, so this is the
    // one moment every client hears about, the player who was mining it included.
    [HarmonyPatch(typeof(Leviathan), "RPC_Left")]
    internal static class Leviathan_RPC_Left_Patch
    {
        private static void Postfix(Leviathan __instance) => Barnacles.Sank(__instance);
    }
}
