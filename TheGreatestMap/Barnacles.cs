using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// Barnacles are the one deposit that swims away. They are mined off the back of a leviathan,
    /// and a few hits in it shakes, dives, and takes them with it -- so a marker written where they
    /// were is a lie within a minute of being written, and the ground it points at is open ocean.
    ///
    /// Sinking is treated as mining them out: the markers are crossed off rather than erased, so
    /// the record of where a leviathan once was is kept, which is the same bargain struck for a
    /// deposit worked to nothing. Nothing is suppressed, so a leviathan that surfaces there again
    /// is recorded again.
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
            TheGreatestMapMod.Message(n == 1
                ? "The leviathan sank; its barnacles are marked as cleared."
                : $"The leviathan sank; {n} barnacle markers are cleared.");
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
