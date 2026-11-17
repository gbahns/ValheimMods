using System;
using HarmonyLib;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Everything that can only be learned on the machine that owned the creature that just died.
    ///
    /// Two things come out of here. The first is what Valheim throws away when it records a kill:
    /// whether the animal was somebody's pet, and how many stars it had. The awkward part is who
    /// knows. Character.OnDeath runs on whichever client owns the creature, and that is not
    /// necessarily the player who killed it -- but the owner is the one holding the tame flag and
    /// the star level, and it is already the one that tells each player who damaged the creature to
    /// count a kill, by routing RegisterKill to them one at a time. So this does the same thing with
    /// the same list: the owner reads off the details and sends them to each credited player, who
    /// files them in their own log.
    ///
    /// The second is the death itself, counted once for the whole world. Vanilla credits the kill to
    /// everyone who damaged the creature, so the players' own tallies cannot be added up without
    /// counting a shared kill twice. Here there is no overlap to undo: this runs on one machine, one
    /// time, for one dead creature. See <see cref="WorldKills"/>.
    ///
    /// If the owner does not run this mod nobody is told and nothing is counted, and the kill simply
    /// lands in the part of the history nothing is known about, which <see cref="KillSplit"/> already
    /// has to account for.
    /// </summary>
    internal static class KillWatch
    {
        private static bool _failed;

        /// <summary>Whether the per-player detail is wanted: the pet flag and the star level.</summary>
        internal static bool Enabled => !_failed && DwamsConfig.RecordKills != null && DwamsConfig.RecordKills.Value;

        /// <summary>Whether either half is wanted, which is what decides if the patch does anything.</summary>
        internal static bool Watching => !_failed && (Enabled || WorldKills.Recording);

        /// <summary>
        /// A creature died and we owned it. Called from the owner's game only, the same condition
        /// vanilla puts on its own crediting.
        /// </summary>
        internal static void Observe(Character creature)
        {
            if (_failed || creature == null) return;
            try
            {
                // Players are left out on purpose. Vanilla files a player kill under the dead
                // player's name and only for whoever landed the last blow, which is a different rule
                // from the one below, and a player is never a pet.
                if (creature.IsPlayer()) return;

                string key = (creature.m_name ?? "").Trim();
                if (key.Length == 0) return;

                var znet = ZNet.instance;
                var nview = creature.GetComponent<ZNetView>();
                var zdo = nview != null ? nview.GetZDO() : null;
                if (znet == null || zdo == null) return;

                bool tame = creature.IsTamed();
                int level = creature.GetLevel();
                // Vanilla's own count of how many players damaged it. Written once per player --
                // RPC_Damage only bumps it behind the same flag it sets -- so it is a count of
                // people, not of hits.
                int attackers = zdo.GetInt(ZDOVars.s_attackers);
                bool cheated = zdo.GetBool(ZDOVars.s_cheated);

                // The world's ledger wants kills the group made. Something that drowned itself or
                // was eaten by a troll had no player in it, and something spawned by a console
                // command was not hunted, so neither is the world's doing.
                if (attackers >= 1 && !cheated) WorldKills.Report(key, tame, level, attackers);

                if (Enabled) Announce(znet, zdo, key, tame, level);
            }
            catch (Exception e)
            {
                _failed = true;
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    "[DudeWhatAreMyStats] Could not record a kill; kill details and world kill counts are off " +
                    $"for the rest of this session. {e}");
            }
        }

        /// <summary>Tells everyone who damaged this creature what it was.</summary>
        private static void Announce(ZNet znet, ZDO zdo, string key, bool tame, int level)
        {
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
            if (__instance == null || !KillWatch.Watching) return;
            // Vanilla's own gate for the crediting half of OnDeath, and the whole reason the count
            // below cannot double up. Without it every client that has the creature loaded would
            // report the same death.
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsOwner()) return;
            KillWatch.Observe(__instance);
        }
    }
}
