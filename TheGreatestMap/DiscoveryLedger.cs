using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// What the player has actually found, and when. Three honest signals, nothing else:
    ///  1. interacted with it (picked, mined, read, used, opened);
    ///  2. it was under the crosshair within vanilla interaction range;
    ///  3. they looked straight at it with clear line of sight for a moment, within the
    ///     sighting distance for that kind of thing (a tower counts from further away than a
    ///     dandelion).
    /// No proximity radar: an object behind a tree or below the ground is never "found".
    /// Finds are forgotten after the memory window and are saved with the character so a
    /// relog inside the window does not lose them.
    /// </summary>
    internal static class DiscoveryLedger
    {
        private const int SaveVersion = 2; // 2: location center and radius per find

        private static readonly Dictionary<string, Found> _pending = new Dictionary<string, Found>();
        private static readonly HashSet<string> _recorded = new HashSet<string>();
        // Large: RaycastNonAlloc returns an arbitrary subset when the buffer overflows, and the
        // nearest hit must never be the one left out.
        private static readonly RaycastHit[] _hits = new RaycastHit[256];
        private static float _nextScan;
        private static string _lookKey;
        private static float _lookSince;

        internal static int PendingCount => _pending.Count;
        internal static int RecordedCount => _recorded.Count;
        internal static bool IsPending(string key) => key != null && _pending.ContainsKey(key);
        internal static bool IsRecorded(string key) => key != null && _recorded.Contains(key);

        internal static void Update()
        {
            if (!TgmConfig.RecordEnabled.Value) return;
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 0.2f;
            Prune();

            if (player.InInterior()) { _lookKey = null; return; }

            var hover = player.GetHoverObject();
            if (hover != null && Catalog.TryClassify(hover, out var hovered))
                MarkFound(hovered);

            var cam = GameCamera.instance;
            if (cam == null) return;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits,
                TgmConfig.MaxLookDistance(), Access.InteractMask(player));
            RaycastHit best = default;
            float bestDistance = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || IsPlayerCollider(hit.collider, player)) continue;
                if (hit.distance < bestDistance) { bestDistance = hit.distance; best = hit; any = true; }
            }
            if (!any || !Catalog.TryClassify(best.collider.gameObject, out var looked)
                || bestDistance > TgmConfig.LookDistanceFor(looked.Cat))
            {
                _lookKey = null;
                return;
            }
            if (looked.Key != _lookKey)
            {
                _lookKey = looked.Key;
                _lookSince = Time.time;
            }
            if (Time.time - _lookSince >= TgmConfig.LookDwell.Value)
                MarkFound(looked);
        }

        private static bool IsPlayerCollider(Collider collider, Player player)
        {
            var body = collider.attachedRigidbody;
            if (body != null && body.gameObject == player.gameObject) return true;
            return collider.GetComponentInParent<Player>() == player;
        }

        /// <summary>
        /// You built it, so you have certainly found it. Placing a piece is the one discovery that
        /// needs no looking at: the portal you just raised goes straight into the ledger, and is
        /// written to the map under the usual rules, which in practice means the next time you take
        /// the map out, since both hands are busy while you are building. Only things the catalog
        /// recognizes are noted, and a house you build is not one of them, because a structure has
        /// to be part of a location the world generated.
        /// </summary>
        internal static void NotePlaced(GameObject go) => NoteInteraction(go);

        internal static void NoteInteraction(GameObject go)
        {
            if (!TgmConfig.RecordEnabled.Value) return;
            var player = Player.m_localPlayer;
            if (player == null || go == null || player.InInterior()) return;
            if (Catalog.TryClassify(go, out var found)) MarkFound(found);
        }

        internal static void MarkFound(Found found)
        {
            if (found == null || string.IsNullOrEmpty(found.Key)) return;
            // Correcting a marker that is already on the map is not recording a discovery, so it
            // does not wait for the pocket map to come out: the moment the place is seen, what the
            // map says about it can be made true. Writing a new marker still waits, as it should.
            ClientPins.CorrectDungeonIconNear(found, Mathf.Max(found.Radius, 20f));
            if (_recorded.Contains(found.Key)) return;
            if (_pending.TryGetValue(found.Key, out var existing))
            {
                existing.FoundAt = DateTime.UtcNow.Ticks; // seen again: the memory starts over
                return;
            }
            found.FoundAt = DateTime.UtcNow.Ticks;
            _pending[found.Key] = found;
        }

        /// <summary>Finds still inside the memory window.</summary>
        internal static List<Found> Pending()
        {
            Prune();
            return new List<Found>(_pending.Values);
        }

        private static void Prune()
        {
            float minutes = TgmConfig.FoundMemoryMinutes.Value;
            if (minutes <= 0f || _pending.Count == 0) return;
            long cutoff = DateTime.UtcNow.Ticks - (long)(minutes * TimeSpan.TicksPerMinute);
            var expired = new List<string>();
            foreach (var kv in _pending) if (kv.Value.FoundAt < cutoff) expired.Add(kv.Key);
            foreach (var key in expired) _pending.Remove(key);
        }

        /// <summary>Drop a pending find without recording it: the thing is gone for good.</summary>
        internal static void Forget(string key)
        {
            if (key != null) _pending.Remove(key);
        }

        internal static void MarkRecorded(string key)
        {
            _pending.Remove(key);
            _recorded.Add(key);
        }

        internal static void Clear()
        {
            _pending.Clear();
            _recorded.Clear();
            _lookKey = null;
        }

        internal static bool IsLocalAttacker(HitData hit)
        {
            return hit != null && Player.m_localPlayer != null && hit.GetAttacker() == Player.m_localPlayer;
        }

        // ── persistence in the character save ───────────────────────────────────────

        internal static string Serialize()
        {
            Prune();
            var pkg = new ZPackage();
            pkg.Write(SaveVersion);
            pkg.Write(_pending.Count);
            foreach (var f in _pending.Values)
            {
                pkg.Write(f.Key ?? "");
                pkg.Write((int)f.Cat);
                pkg.Write(f.Icon ?? "");
                pkg.Write(f.Name ?? "");
                pkg.Write(f.Pos);
                pkg.Write(f.FoundAt);
                pkg.Write(f.Center);
                pkg.Write(f.Radius);
            }
            return pkg.GetBase64();
        }

        internal static void Deserialize(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var pkg = new ZPackage(data);
                int version = pkg.ReadInt();
                if (version < 1 || version > SaveVersion) return;
                int n = pkg.ReadInt();
                for (int i = 0; i < n; i++)
                {
                    var f = new Found
                    {
                        Key = pkg.ReadString(),
                        Cat = (Category)pkg.ReadInt(),
                        Icon = pkg.ReadString(),
                        Name = pkg.ReadString(),
                        Pos = pkg.ReadVector3(),
                        FoundAt = pkg.ReadLong(),
                    };
                    if (version >= 2)
                    {
                        f.Center = pkg.ReadVector3();
                        f.Radius = pkg.ReadSingle();
                    }
                    else
                    {
                        f.Center = f.Pos;
                    }
                    if (!string.IsNullOrEmpty(f.Key) && !_pending.ContainsKey(f.Key)) _pending[f.Key] = f;
                }
                Prune();
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read saved finds: {e.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class Player_Save_Finds_Patch
    {
        private static void Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || __instance.m_customData == null) return;
            __instance.m_customData[DiscoveryLedgerKeys.CustomData] = DiscoveryLedger.Serialize();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    internal static class Player_Load_Finds_Patch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance.m_customData != null && __instance.m_customData.TryGetValue(DiscoveryLedgerKeys.CustomData, out var data))
                DiscoveryLedger.Deserialize(data);
        }
    }

    internal static class DiscoveryLedgerKeys
    {
        internal const string CustomData = "TheGreatestMap.Found";
    }

    // ── interaction signals ────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class Pickable_Interact_Patch
    {
        // Classified before the pick, because afterwards a plant that never grows back no longer
        // counts as anything findable, which is the whole point.
        private static void Prefix(Pickable __instance, Humanoid character, ref Found __state)
        {
            __state = null;
            if (__instance == null || character == null || character != Player.m_localPlayer) return;
            DiscoveryLedger.NoteInteraction(__instance.gameObject);
            if (__instance.m_respawnTimeMinutes <= 0f && Catalog.TryClassify(__instance.gameObject, out var found))
                __state = found;
        }

        /// <summary>
        /// Picking the last of something that never grows back makes its marker a lie, so the
        /// marker goes, for everyone. The spot is not suppressed: nothing is being rejected here,
        /// and if the world ever puts something there again it deserves recording.
        /// </summary>
        private static void Postfix(bool __result, Found __state)
        {
            if (!__result || __state == null) return;
            DiscoveryLedger.Forget(__state.Key);
            if (ClientPins.MarkCleared(__state.Icon, __state.Pos, 2f))
                TheGreatestMapMod.Message($"Picked the last of the {__state.Name}; marked as cleared.");
        }
    }

    // Mining a deposit out removes it from the world, which makes its marker a lie. The same
    // shape as picking a plant that never grows back: the map is corrected, not a marker
    // rejected, so the spot is not suppressed and an ore vein that reappears is recorded again.
    internal static class Mined
    {
        internal static void Gone(GameObject go)
        {
            if (Player.m_localPlayer == null || go == null) return;
            if (!Catalog.TryClassify(go, out var found)) return;
            DiscoveryLedger.Forget(found.Key);
            if (ClientPins.MarkCleared(found.Icon, found.Pos, 6f))
                TheGreatestMapMod.Message($"Mined out the {found.Name}; marked as cleared.");
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
    internal static class MineRock5_Damage_Patch
    {
        // One prefix only: Harmony finds patch methods by name, so an overload would be ambiguous.
        // The deposit is noted as found on the way in, and remembered so the postfix can tell
        // whether this blow was the one that finished it.
        private static void Prefix(MineRock5 __instance, HitData hit, ref GameObject __state)
        {
            __state = null;
            if (__instance == null || !DiscoveryLedger.IsLocalAttacker(hit)) return;
            DiscoveryLedger.NoteInteraction(__instance.gameObject);
            __state = __instance.gameObject;
        }

        private static void Postfix(MineRock5 __instance, GameObject __state)
        {
            if (__state == null || __instance == null) return;
            if (Access.RockAllDestroyed(__instance)) Mined.Gone(__state);
        }
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
    internal static class MineRock_Damage_Patch
    {
        private static void Prefix(MineRock __instance, HitData hit, ref GameObject __state)
        {
            __state = null;
            if (__instance == null || !DiscoveryLedger.IsLocalAttacker(hit)) return;
            DiscoveryLedger.NoteInteraction(__instance.gameObject);
            __state = __instance.gameObject;
        }

        // Old-style deposits mine out the same way, and whichever kind tin turns out to be is
        // covered. The damage runs through an RPC on the owner, so the check is a moment late
        // on someone else's deposit; it still lands before the next blow.
        private static void Postfix(MineRock __instance, GameObject __state)
        {
            if (__state == null || __instance == null) return;
            if (Access.MineRockAllDestroyed(__instance)) Mined.Gone(__state);
        }
    }

    [HarmonyPatch(typeof(Destructible), nameof(Destructible.Destroy))]
    internal static class Destructible_Destroy_Patch
    {
        private static void Prefix(Destructible __instance, HitData hit)
        {
            if (DiscoveryLedger.IsLocalAttacker(hit) && __instance != null) Mined.Gone(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
    internal static class Destructible_Damage_Patch
    {
        private static void Prefix(Destructible __instance, HitData hit)
        {
            if (DiscoveryLedger.IsLocalAttacker(hit)) DiscoveryLedger.NoteInteraction(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(RuneStone), nameof(RuneStone.Interact))]
    internal static class RuneStone_Interact_Patch
    {
        private static void Prefix(RuneStone __instance, Humanoid character)
        {
            if (character != null && character == Player.m_localPlayer) DiscoveryLedger.NoteInteraction(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(Vegvisir), nameof(Vegvisir.Interact))]
    internal static class Vegvisir_Interact_Patch
    {
        private static void Prefix(Vegvisir __instance, Humanoid character)
        {
            if (character != null && character == Player.m_localPlayer) DiscoveryLedger.NoteInteraction(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(Teleport), nameof(Teleport.Interact))]
    internal static class Teleport_Interact_Patch
    {
        private static void Prefix(Teleport __instance, Humanoid character)
        {
            if (character != null && character == Player.m_localPlayer) DiscoveryLedger.NoteInteraction(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
    internal static class TeleportWorld_Interact_Patch
    {
        private static void Prefix(TeleportWorld __instance, Humanoid human)
        {
            if (human != null && human == Player.m_localPlayer) DiscoveryLedger.NoteInteraction(__instance.gameObject);
        }
    }

    // Opening a chest, harvesting a beehive or using a door inside a ruin is as good a sign of
    // having found it as any; the first two also count as having searched the building.
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class Container_Interact_Patch
    {
        private static void Prefix(Container __instance, Humanoid character)
        {
            if (character == null || character != Player.m_localPlayer) return;
            DiscoveryLedger.NoteInteraction(__instance.gameObject);
            Searched.OnSearched(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.Interact))]
    internal static class Beehive_Interact_Patch
    {
        private static void Prefix(Beehive __instance, Humanoid character)
        {
            if (character == null || character != Player.m_localPlayer) return;
            DiscoveryLedger.NoteInteraction(__instance.gameObject);
            Searched.OnSearched(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.Interact))]
    internal static class Door_Interact_Patch
    {
        private static void Prefix(Door __instance, Humanoid character)
        {
            if (character != null && character == Player.m_localPlayer) DiscoveryLedger.NoteInteraction(__instance.gameObject);
        }
    }

    // Building something is a discovery you cannot miss, and it is the only one where nothing has
    // to be looked at. SetCreator runs once, on the freshly placed piece, and only for a piece a
    // player put down, which makes it the natural place to notice.
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class Piece_SetCreator_Patch
    {
        private static void Postfix(Piece __instance)
        {
            if (__instance == null || Player.m_localPlayer == null) return;
            if (__instance.GetCreator() != Player.m_localPlayer.GetPlayerID()) return;
            DiscoveryLedger.NotePlaced(__instance.gameObject);
        }
    }
}
