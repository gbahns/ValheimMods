using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>
    /// The trip itself, for a destination chosen on the map. Mirrors TeleportWorld.Teleport:
    /// the same global-key and item checks, the same landing spot in front of the target.
    /// Portals with a fixed destination go through vanilla's own connection and never come here.
    /// </summary>
    internal static class Travel
    {
        /// <summary>Vanilla's rules for whether this player may teleport at all, with the vanilla message.</summary>
        internal static bool CanTeleport(Player player, bool allowAllItems, bool message = true)
        {
            if (player == null) return false;
            var zone = ZoneSystem.instance;
            if (zone != null && zone.GetGlobalKey(GlobalKeys.NoPortals))
            {
                if (message) player.Message(MessageHud.MessageType.Center, "$msg_blocked");
                return false;
            }
            if (zone != null && zone.GetGlobalKey(GlobalKeys.NoBossPortals))
            {
                bool bossActive = RandEventSystem.instance != null && RandEventSystem.instance.GetBossEvent() != null;
                if (!bossActive && zone.GetGlobalKey(GlobalKeys.activeBosses, out float value) && value > 0f) bossActive = true;
                if (bossActive)
                {
                    if (message) player.Message(MessageHud.MessageType.Center, "$msg_blockedbyboss");
                    return false;
                }
            }
            if (!player.IsTeleportable(allowAllItems))
            {
                if (message) player.Message(MessageHud.MessageType.Center, "$msg_noteleport");
                return false;
            }
            return true;
        }

        /// <summary>Teleports the local player to a portal. Returns false (with a message) when refused.</summary>
        internal static bool Go(PortalInfo target, bool sourceAllowsAllItems)
        {
            var player = Player.m_localPlayer;
            if (player == null || target == null) return false;
            if (!CanTeleport(player, sourceAllowsAllItems)) return false;

            Vector3 forward = target.Rot * Vector3.forward;
            Vector3 pos = target.Pos + forward * target.ExitDistance + Vector3.up;
            ZLog.Log("Teleporting " + player.GetPlayerName() + " (TheGreatestPortal) to " + target.DisplayName);
            if (!player.TeleportTo(pos, target.Rot, distantTeleport: true))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_noteleport");
                return false;
            }
            if (Game.instance != null) Game.instance.IncrementPlayerStat(PlayerStatType.PortalsUsed);
            TheGreatestPortalMod.Message("Travelling to " + target.DisplayName);
            return true;
        }
    }
}
