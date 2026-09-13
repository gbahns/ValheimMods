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

    // A portal you build gets its permanent id at once and, if you have a default portal, its destination.
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class Piece_SetCreator_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Piece __instance, long uid)
        {
            var portal = __instance.GetComponent<TeleportWorld>();
            if (portal == null) return;
            var player = Player.m_localPlayer;
            if (player == null || uid != player.GetPlayerID()) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            var zdo = nview.GetZDO();
            if (PortalData.GetId(zdo) != 0L) return;   // not freshly built
            zdo.Set(PortalData.IdHash, PortalData.NewId());

            long def = Favorites.DefaultId;
            var target = Catalog.Get(def);
            if (def == 0L || target == null) return;
            zdo.Set(PortalData.ToHash, def);
            zdo.Set(PortalData.SetHash, 1);
            TheGreatestPortalMod.Message("Portal will connect to " + target.DisplayName);
        }
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

        [HarmonyPostfix]
        private static void Postfix(Minimap __instance) => MapPicker.OnPinsUpdated(__instance);
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
