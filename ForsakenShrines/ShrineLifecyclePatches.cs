using HarmonyLib;

namespace ForsakenShrines
{
    /// <summary>
    /// World-load hooks that replace what Jotunn's PieceManager used to do for this mod (see the
    /// note in ForsakenShrinesMod.Awake for why PieceManager is off-limits on Valheim 1.0):
    /// configure the shrine pieces once, then on every world load make sure the clones are in
    /// the Hammer's piece table and in ZNetScene's prefab registry, and refresh unlock state.
    /// ObjectDB.Awake runs once per scene (menu and world); ZNetScene.Awake once per world load.
    /// Both callees catch their own exceptions — an exception escaping an Awake postfix would
    /// abort the game's own initialisation.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDBAwakePatch
    {
        // Priority.Last: run after other mods' ObjectDB.Awake postfixes (item registrations and
        // the like), the same slot Jotunn's OnPiecesRegistered event used.
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix() => ShrinePieces.OnObjectDBAwake();
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix() => ShrinePieces.OnZNetSceneAwake();
    }
}
