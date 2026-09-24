using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Gives a ship to whoever is steering it.
    ///
    /// Vanilla gets close and then picks badly. Ship.UpdateOwner only fires when the current owner
    /// is *not* aboard, and when it does it takes GetNewOwnerID(), which is the first entry in
    /// m_players - an arbitrary passenger, not the captain. So a ship whose owner is aboard but
    /// idle stays with that passenger indefinitely.
    ///
    /// That matters because steering is routed to the owner and batched:
    ///
    ///     if (Time.time - m_sendRudderTime > 0.2f) m_nview.InvokeRPC("Rudder", m_rudderValue);
    ///
    /// and the physics that acts on it runs only there:
    ///
    ///     if ((bool)m_nview &amp;&amp; !m_nview.IsOwner()) return;
    ///
    /// A captain who does not own the ship therefore waits up to 0.2s of batching plus a round
    /// trip before the hull answers the helm - about a quarter second, every turn. A captain who
    /// does own it has no delay at all, because the rudder value it integrates locally is the one
    /// the forces are computed from.
    ///
    /// This runs from the mod's own update rather than through the ownership handout, for two
    /// reasons: the handout's predicate never sees which object it is being asked about, and doing
    /// it independently means this half still works on a server where another mod has replaced
    /// ReleaseNearbyZDOS.
    /// </summary>
    internal static class Ships
    {
        /// <summary>
        /// Ship prefabs, found by component rather than by name, so modded ships - TheGreatestShips'
        /// DM_* hulls, anybody else's - are covered without a list to maintain.
        /// </summary>
        private static readonly List<string> _prefabs = new List<string>();
        private static bool _discovered;

        // A sweep walks every sector, 400 at a time, which is what the iterative API is shaped for.
        // One slice per frame per prefab: a full cycle takes a few seconds on a large world, which
        // is ample when vanilla's own ownership pass only runs every two.
        private static int _prefabIndex;
        private static int _scanIndex;
        private static readonly List<ZDO> _found = new List<ZDO>();
        private static float _resumeAt;

        /// <summary>Gap between finishing one full cycle and starting the next.</summary>
        private const float CycleSeconds = 5f;

        /// <summary>playerID (what a ship records) -> peer uid (what ownership uses).</summary>
        private static readonly Dictionary<long, long> _playerUid = new Dictionary<long, long>();

        private static int _movedThisCycle;

        internal static void Tick(float now)
        {
            try
            {
                if (SpreadTheLoadMod.AssignShipToCaptain == null || !SpreadTheLoadMod.AssignShipToCaptain.Value) return;
                var znet = ZNet.instance;
                var zdoMan = ZDOMan.instance;
                if (znet == null || zdoMan == null || !znet.IsServer()) return;
                if (now < _resumeAt) return;

                Discover();
                if (_prefabs.Count == 0) return;

                // The map is rebuilt at the start of each cycle rather than per ship: it is keyed on
                // connected players, and a ship whose captain logged out mid-cycle simply fails the
                // lookup and is left alone.
                if (_prefabIndex == 0 && _scanIndex == 0) { BuildPlayerMap(znet, zdoMan); _movedThisCycle = 0; }

                if (!zdoMan.GetAllZDOsWithPrefabIterative(_prefabs[_prefabIndex], _found, ref _scanIndex)) return;

                foreach (var zdo in _found) Consider(zdo);
                _found.Clear();
                _scanIndex = 0;
                _prefabIndex++;

                if (_prefabIndex < _prefabs.Count) return;
                _prefabIndex = 0;
                _resumeAt = now + CycleSeconds;
                Report();
            }
            catch (Exception e)
            {
                // Never take the server down over a ship. Reset the sweep so a bad cycle does not
                // leave the scan stuck half way through a prefab.
                _found.Clear(); _scanIndex = 0; _prefabIndex = 0;
                _resumeAt = now + CycleSeconds;
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] ship pass failed: {e.Message}");
            }
        }

        private static void Consider(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return;

            // "user" is the player at the helm, written by ShipControlls. Zero means nobody is
            // steering, and an unattended ship is left entirely to the ordinary yield rules.
            long captainPlayerId = zdo.GetLong(ZDOVars.s_user, 0L);
            if (captainPlayerId == 0L) return;

            if (!_playerUid.TryGetValue(captainPlayerId, out long captainUid)) return;
            if (zdo.GetOwner() == captainUid) return;

            // The one judgement call in the mod, and the reason it is a setting rather than a rule.
            // Handing the helm to a struggling machine costs everyone aboard: during a stall past
            // Unity's maximumDeltaTime the hull loses simulated time and lurches. Withholding it
            // costs the captain about a quarter second on every turn, constantly, and a captain
            // fighting a mushy helm puts the boat into rocks - which costs everyone too. Reasoned
            // out rather than measured, so it is left switchable and defaults to letting them steer.
            if (Ownership.IsYielding(captainUid) &&
                SpreadTheLoadMod.YieldingRetainsHelm != null && !SpreadTheLoadMod.YieldingRetainsHelm.Value)
                return;

            zdo.SetOwner(captainUid);
            _movedThisCycle++;
        }

        /// <summary>
        /// Maps the helm's playerID onto the peer uid that owns things. They are different id
        /// spaces: a ship records Player.GetPlayerID(), while ownership is by network peer. The
        /// bridge is the player's own character ZDO, which carries both.
        /// </summary>
        private static void BuildPlayerMap(ZNet znet, ZDOMan zdoMan)
        {
            _playerUid.Clear();
            try
            {
                foreach (var info in znet.GetPlayerList())
                {
                    var charZdo = zdoMan.GetZDO(info.m_characterID);
                    if (charZdo == null) continue;
                    long playerId = charZdo.GetLong(ZDOVars.s_playerID, 0L);
                    if (playerId == 0L) continue;
                    _playerUid[playerId] = info.m_characterID.UserID;
                }
            }
            catch { /* an unmappable player is one whose ship we leave alone */ }
        }

        private static void Discover()
        {
            if (_discovered) return;
            var scene = ZNetScene.instance;
            if (scene == null) return;
            _discovered = true;

            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;
                if (prefab.GetComponent<Ship>() == null) continue;
                _prefabs.Add(prefab.name);
            }
            SpreadTheLoadMod.Log.LogInfo(
                _prefabs.Count == 0
                    ? "[SpreadTheLoad] no ship prefabs found; helm ownership will do nothing."
                    : $"[SpreadTheLoad] watching {_prefabs.Count} ship type(s) for helm ownership: {string.Join(", ", _prefabs.ToArray())}");
        }

        private static void Report()
        {
            if (_movedThisCycle == 0) return;
            if (SpreadTheLoadMod.LogActivity == null || !SpreadTheLoadMod.LogActivity.Value) return;
            SpreadTheLoadMod.Log.LogInfo($"[SpreadTheLoad] handed {_movedThisCycle} ship(s) to the player steering them.");
        }
    }
}
