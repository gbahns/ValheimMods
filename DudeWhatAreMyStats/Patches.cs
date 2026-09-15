using HarmonyLib;

namespace DudeWhatAreMyStats
{
    // ── world lifetime ────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            // Reset here as well as on shutdown: not every way out of a world goes through
            // ZNet.Shutdown (ShutdownWithoutSave does not), and a roster carried into the next
            // world would list the last one's players.
            StatsNetwork.Reset();
            StatsNetwork.Register();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNet_Shutdown_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            StatsPanel.Close();
            StatsNetwork.Reset();
        }
    }

    // ── input blocking while the panel is up ──────────────────────────────────────

    // Everything vanilla holds back for its own text prompt (movement, hotbar, the map key, the
    // ESC menu, chat, mouse capture) it holds back for this mod's panel, and for the frame after
    // the panel closes, so the key that closed it does not also act on the world.
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class TextInput_IsVisible_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (!__result && (StatsPanel.IsOpen || StatsPanel.JustClosed)) __result = true;
        }
    }

    // The mouse wheel belongs to the stats list while the panel is open. GameCamera's zoom check
    // looks at chat, the console, the inventory, the store, the menu, the map, piece selection and
    // the radial, but not at text prompts, so without this the camera zooms in and out behind the
    // panel the whole time you scroll a long list. The list itself scrolls through UI pointer
    // events, which this does not touch.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            if (__result != 0f && StatsPanel.IsOpen) __result = 0f;
        }
    }

    // ── a vanilla bug: chests counted again on every open ─────────────────────────

    // Valheim sends the chest discovery RPC under a name it never registered. Container.Awake
    // does
    //     m_nview.Register<int>("RPC_Discovered", RPC_Discovered);
    // and Container.Interact sends
    //     m_nview.InvokeRPC("discovered", str.GetStableHashCode());
    // Those two names hash to 1566429772 and 327122920, so the call reaches the chest's owner,
    // finds nothing registered under that hash and logs "Failed to find rpc method 327122920".
    //
    // What the RPC was meant to do is write a "Discovered_<player name>" flag onto the chest's
    // ZDO, and that flag is what keeps a discovery from counting twice: Interact increments
    // TreasureDungeonFound (or the buried and location siblings) only while it is unset. The flag
    // is therefore never written, so every reopening of the same chest counts as another treasure
    // found, and the numbers this mod reports read high. Registering the name the game actually
    // sends, with the body vanilla wrote for it, makes the flag stick and the count honest.
    //
    // Only chests that keep a discovery stat send it, so only those are touched. The handler runs
    // on whoever owns the chest's ZDO, which in a dungeon you usually are; a chest owned by a
    // player without the mod still miscounts for them, and nothing client-side can change that.
    //
    // Delete this patch if Valheim ever fixes the name.
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Container_Awake_Patch
    {
        // The name Interact sends, which is the one missing from Awake's registrations.
        private const string SentName = "discovered";

        [HarmonyPostfix]
        private static void Postfix(Container __instance)
        {
            if (!DwamsConfig.FixTreasureDiscoveryCount.Value) return;
            if (__instance.m_discoverStat == PlayerStatType.None) return;
            // m_nview is private, and this mod builds against the real assemblies rather than
            // publicized ones, so the view is found the way Awake itself finds it.
            ZNetView nview = __instance.m_rootObjectOverride != null
                ? __instance.m_rootObjectOverride.GetComponent<ZNetView>()
                : __instance.GetComponent<ZNetView>();
            if (nview == null || nview.GetZDO() == null) return;
            // ZNetView.Register throws on a name already registered, so clear it first. That also
            // keeps this harmless if the game, or another mod, starts registering the name too.
            nview.Unregister(SentName);
            nview.Register<int>(SentName, (sender, keyHash) =>
            {
                ZDO zdo = nview.GetZDO();
                if (zdo != null) zdo.Set(keyHash, value: true);
            });
        }
    }
}
