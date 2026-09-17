using HarmonyLib;

namespace TheGreatestShips
{
    /// <summary>
    /// World-load hooks: on every world load put the ships in the Hammer's piece table and in
    /// ZNetScene's prefab registry, and resolve their recipes.  Both callees catch their own
    /// exceptions, since one escaping an Awake postfix would abort the game's initialization.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDBAwakePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix() => ShipPrefabs.OnObjectDBAwake();
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix() => ShipPrefabs.OnZNetSceneAwake();
    }
}
