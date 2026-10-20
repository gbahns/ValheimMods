using System;
using HarmonyLib;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Catches the two things Valheim throws away when it records a kill: whether the animal was
    /// somebody's pet, and how many stars it had.
    ///
    /// The awkward part is who knows. Character.OnDeath runs on whichever client owns the creature,
    /// and that is not necessarily the player who killed it -- but the owner is the one holding the
    /// tame flag and the star level, and it is already the one that tells each player who damaged
    /// the creature to count a kill, by routing RegisterKill to them one at a time. So this does the
    /// same thing with the same list: the owner reads off the details and sends them to each credited
    /// player, who files them in their own log.
    ///
    /// If the owner does not run this mod nobody is told, and the kill simply lands in the part of
    /// the history nothing is known about, which <see cref="KillSplit"/> already has to account for.
    /// </summary>
    internal static class KillWatch
    {
        private static bool _failed;

        internal static bool Enabled => !_failed && DwamsConfig.RecordKills != null && DwamsConfig.RecordKills.Value;

        /// <summary>
        /// Tells everyone who damaged this creature what it was. Called from the owner's client only,
        /// the same condition vanilla puts on its own crediting.
        /// </summary>
        internal static void Announce(Character creature)
        {
            if (!Enabled || creature == null) return;
            try
            {
                // Players are left out on purpose. Vanilla files a player kill under the dead
                // player's name and only for whoever landed the last blow, which is a different rule
                // from the one below, and a player is never a pet.
                if (creature.IsPlayer()) return;

                string key = (creature.m_name ?? "").Trim();
                if (key.Length == 0) return;
                bool tame = creature.IsTamed();
                int level = creature.GetLevel();

                var znet = ZNet.instance;
                var nview = creature.GetComponent<ZNetView>();
                var zdo = nview != null ? nview.GetZDO() : null;
                if (znet == null || zdo == null) return;

                foreach (var player in znet.GetPlayerList())
                {
                    // The same flag vanilla reads, built the same odd way it builds it: the hash
                    // constant is an int and the player name is a string, so this concatenates.
                    if (!zdo.GetBool(ZDOVars.s_attackers + player.m_name)) continue;
                    long peer = player.m_characterID.UserID;
                    if (peer == ZNet.GetUID()) KillLog.Record(key, tame, level);
                    else StatsNetwork.SendKill(peer, key, tame, level);
                }
            }
            catch (Exception e)
            {
                if (_failed) return;
                _failed = true;
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    $"[DudeWhatAreMyStats] Could not record a kill; kill details are off for the rest of this session. {e}");
            }
        }
    }

    /// <summary>
    /// Where a creature's death is processed. Humanoid does not override OnDeath, so this one patch
    /// covers every creature in the game.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath_KillWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance)
        {
            if (__instance == null || !KillWatch.Enabled) return;
            // Vanilla's own gate for the crediting half of OnDeath. Without it every client that has
            // the creature loaded would announce the same kill.
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsOwner()) return;
            KillWatch.Announce(__instance);
        }
    }
}
