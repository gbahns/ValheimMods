using HarmonyLib;
using UnityEngine;

namespace TheGreatestPortal
{
    // ── lifecycle ─────────────────────────────────────────────────────────────────

    // Game.Start runs once per session on both the server and the client, after ZNet has
    // created ZRoutedRpc (a fresh instance per session, so registration repeats on every login).
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            PortalNetwork.Reset();
            PortalNetwork.Register();
            Favorites.Reset();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNet_Shutdown_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            PortalPanel.Close();
            MapPicker.End();
            PortalNetwork.Reset();
            Favorites.Reset();
        }
    }

    // A player who spawns asks for the portal list.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) PortalNetwork.RequestCatalog();
        }
    }

    // ── server: vanilla tag pairing is replaced by stored destinations ────────────

    [HarmonyPatch(typeof(Game), nameof(Game.ConnectPortals))]
    internal static class Game_ConnectPortals_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix() => false;
    }

    // ── the portal itself ─────────────────────────────────────────────────────────

    // Use opens this mod's panel instead of vanilla's tag prompt.
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
    internal static class TeleportWorld_Interact_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, ref bool __result)
        {
            if (hold) { __result = false; return false; }
            if (!PrivateArea.CheckAccess(__instance.transform.position))
            {
                human.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                __result = true;
                return false;
            }
            PortalPanel.Open(__instance);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
    internal static class TeleportWorld_GetHoverText_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TeleportWorld __instance, ref string __result)
        {
            __result = PortalPanel.HoverText(__instance);
            return false;
        }
    }

    // An open portal (no destination) counts as connected, so it lights up and whirls when a
    // player who may teleport stands in front of it, the way a paired portal does.
    [HarmonyPatch(typeof(TeleportWorld), "HaveTarget")]
    internal static class TeleportWorld_HaveTarget_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TeleportWorld __instance, ref bool __result)
        {
            if (!MapPicker.IsOpenPortal(__instance)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), "TargetFound")]
    internal static class TeleportWorld_TargetFound_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TeleportWorld __instance, ref bool __result)
        {
            if (!MapPicker.IsOpenPortal(__instance)) return true;
            __result = true;
            return false;
        }
    }

    // Stepping into an open portal shows the map; a portal with a destination teleports as vanilla.
    [HarmonyPatch(typeof(TeleportWorldTrigger), "OnTriggerEnter")]
    internal static class TeleportWorldTrigger_OnTriggerEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TeleportWorldTrigger __instance, Collider colliderIn)
        {
            var player = colliderIn != null ? colliderIn.GetComponent<Player>() : null;
            if (player == null || player != Player.m_localPlayer) return true;
            var portal = __instance.GetComponentInParent<TeleportWorld>();
            if (portal == null) return true;
            // Holding the detour key turns any portal into an open one for this trip only.
            if (!MapPicker.IsOpenPortal(portal) && Keys.IsHeld(TgpConfig.DetourKey.Value))
            {
                MapPicker.BeginTravel(portal, __instance.GetComponent<Collider>(), colliderIn, detour: true);
                return false;
            }
            if (!MapPicker.IsOpenPortal(portal))
            {
                string blocked = MapPicker.BlockedReason(portal);
                if (blocked != null)
                {
                    player.Message(MessageHud.MessageType.Center, blocked);
                    return false;
                }
                return true;
            }
            MapPicker.BeginTravel(portal, __instance.GetComponent<Collider>(), colliderIn);
            return false;
        }
    }

    // A trip through a portal with a set destination is the game's own teleport. It is recorded
    // with both of its ends, and only once it has really begun: the game refuses some trips (ore,
    // a boss fight) without telling the caller, so the teleport state is compared before and after.
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class TeleportWorld_Teleport_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Player player, out bool __state)
        {
            __state = player != null && player.IsTeleporting();
        }

        [HarmonyPostfix]
        private static void Postfix(TeleportWorld __instance, Player player, bool __state)
        {
            if (player == null || player != Player.m_localPlayer || __state || !player.IsTeleporting()) return;
            var nview = __instance.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null) return;
            var to = Catalog.ByZdo(PortalData.Connection(zdo));
            Favorites.RecordTrip(PortalData.GetId(zdo), to != null ? to.Id : PortalData.GetTarget(zdo));
        }
    }

    // A portal you build gets its permanent id at once and, if you have a default portal, its
    // destination. Every step says in the log what it decided, because "my new portal did not
    // point at my default" is the one thing players report and, from the outside, a portal that
    // adopted nothing looks exactly like a portal built with no default set.
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class Piece_SetCreator_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Piece __instance, long uid)
        {
            var portal = __instance.GetComponent<TeleportWorld>();
            if (portal == null) return;
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (uid != player.GetPlayerID())
            {
                Log($"A portal was placed as player {uid}, who is not you ({player.GetPlayerID()}); leaving it alone.");
                return;
            }
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                Log("A portal was placed but has no valid network object, so it gets no id or destination here; the server will give it an id and it will be an open portal.");
                return;
            }
            if (!nview.IsOwner())
            {
                Log("A portal was placed that you do not own, so its destination is not yours to set; it will be an open portal.");
                return;
            }
            var zdo = nview.GetZDO();
            long already = PortalData.GetId(zdo);
            if (already != 0L)
            {
                Log($"A portal was placed that already carries id {already}, so it is not freshly built; leaving its destination alone.");
                return;
            }
            long id = PortalData.NewId();
            zdo.Set(PortalData.IdHash, id);

            long def = Favorites.DefaultId;
            if (def == 0L)
            {
                Log($"Built portal {id}: no default portal is set, so it is an open portal.");
                return;
            }
            var target = Catalog.Get(def);
            if (target == null)
            {
                Log($"Built portal {id}: your default portal {def} is not in the portal list " +
                    $"({Catalog.Count} portal(s){(Catalog.HasSnapshot ? "" : ", and nothing has arrived from the server yet")}), " +
                    "so it is an open portal. Tick the default box again on the portal you want.");
                TheGreatestPortalMod.Message("Your default portal is gone; this one is open");
                return;
            }
            zdo.Set(PortalData.ToHash, def);
            zdo.Set(PortalData.SetHash, 1);
            Log($"Built portal {id}: destination set to your default portal '{target.DisplayName}' ({def}).");
            TheGreatestPortalMod.Message("Portal will connect to " + target.DisplayName);
        }

        private static void Log(string text) => TheGreatestPortalMod.Log.LogInfo("[TheGreatestPortal] " + text);
    }

    // ── input blocking while the panel is up ──────────────────────────────────────

    // Everything vanilla holds back for its own text prompt (movement, hotbar, the map key, the
    // ESC menu, chat, mouse capture) it holds back for this mod's panel, for the frame after the
    // panel closes (so the Enter that closed it does not open the chat), and for the search box
    // on the map while it has the keyboard.
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class TextInput_IsVisible_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (!__result && (PortalPanel.IsOpen || PortalPanel.JustClosed || UiKit.TextFocused())) __result = true;
        }
    }

    // ── the map ───────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Minimap), "Start")]
    internal static class Minimap_Start_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap __instance) => PortalPins.OnMinimapStart(__instance);
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    internal static class Minimap_SetMapMode_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap.MapMode mode) => MapPicker.OnMapModeChanged(mode);
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    internal static class Minimap_OnMapLeftClick_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Minimap __instance) => !MapPicker.HandleLeftClick(__instance);
    }

    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    internal static class Minimap_RemovePinUnderPointer_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Minimap __instance) => !MapPicker.HandleRightClick(__instance);
    }

    // No new vanilla pin from a quick double-click on a portal while choosing.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick))]
    internal static class Minimap_OnMapDblClick_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !MapPicker.IsSelecting;
    }

    // Belt and braces: if another mod replaces m_visibleIconTypes with a shorter array, grow it
    // back before vanilla indexes it with our type. Afterwards, hide the other pins if asked.
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Minimap_UpdatePins_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Minimap __instance) => PortalPins.EnsureArrays(__instance);
    }

    // The mouse wheel is for the lists: while the panel is open it must not zoom the camera
    // (GameCamera's zoom check ignores text prompts), and over the map list it must not zoom the
    // map. The lists scroll through UI pointer events, which this does not touch.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            if (__result != 0f && (PortalPanel.IsOpen || MapPicker.ListPointerOver)) __result = 0f;
        }
    }
}
