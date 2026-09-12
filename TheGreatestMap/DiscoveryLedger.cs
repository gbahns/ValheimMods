using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// What the player has actually found. Three honest signals, nothing else:
    ///  1. interacted with it (picked, mined, read, used);
    ///  2. it was under the crosshair within vanilla interaction range;
    ///  3. they looked straight at it with clear line of sight for a moment.
    /// No proximity radar: an object behind a tree or below the ground is never "found".
    /// The ledger is in memory only; things are found again by seeing them again.
    /// </summary>
    internal static class DiscoveryLedger
    {
        private static readonly Dictionary<string, Found> _pending = new Dictionary<string, Found>();
        private static readonly HashSet<string> _recorded = new HashSet<string>();
        private static readonly RaycastHit[] _hits = new RaycastHit[24];
        private static float _nextScan;
        private static string _lookKey;
        private static float _lookSince;

        internal static int PendingCount => _pending.Count;
        internal static int RecordedCount => _recorded.Count;

        internal static void Update()
        {
            if (!TgmConfig.RecordEnabled.Value) return;
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 0.2f;

            if (player.InInterior()) { _lookKey = null; return; }

            var hover = player.GetHoverObject();
            if (hover != null && Catalog.TryClassify(hover, out var hovered))
                MarkFound(hovered);

            var cam = GameCamera.instance;
            if (cam == null) return;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits,
                TgmConfig.LookDistance.Value, Access.InteractMask(player));
            RaycastHit best = default;
            float bestDistance = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || IsPlayerCollider(hit.collider, player)) continue;
                if (hit.distance < bestDistance) { bestDistance = hit.distance; best = hit; any = true; }
            }
            if (!any || !Catalog.TryClassify(best.collider.gameObject, out var looked))
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
            if (_recorded.Contains(found.Key) || _pending.ContainsKey(found.Key)) return;
            _pending[found.Key] = found;
        }

        internal static List<Found> Pending() => new List<Found>(_pending.Values);

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
    }

    // ── interaction signals ────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class Pickable_Interact_Patch
    {
        private static void Prefix(Pickable __instance, Humanoid character)
        {
            if (character != null && character == Player.m_localPlayer) DiscoveryLedger.NoteInteraction(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
    internal static class MineRock5_Damage_Patch
    {
        private static void Prefix(MineRock5 __instance, HitData hit)
        {
            if (DiscoveryLedger.IsLocalAttacker(hit)) DiscoveryLedger.NoteInteraction(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
    internal static class MineRock_Damage_Patch
    {
        private static void Prefix(MineRock __instance, HitData hit)
        {
            if (DiscoveryLedger.IsLocalAttacker(hit)) DiscoveryLedger.NoteInteraction(__instance.gameObject);
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

    // Opening a chest or a door inside a ruin is as good a sign of having found it as any.
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class Container_Interact_Patch
    {
        private static void Prefix(Container __instance, Humanoid character)
        {
            if (character != null && character == Player.m_localPlayer) DiscoveryLedger.NoteInteraction(__instance.gameObject);
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
}
