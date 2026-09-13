using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// The locations spawned around this client, by position and exterior radius. Valheim
    /// instantiates a location's networked pieces (walls, floors, chests, doors) as independent
    /// world objects, so a hit on a house wall has no Location parent; this index lets the
    /// catalog attribute such a hit to the building it stands in.
    /// </summary>
    internal static class LocationIndex
    {
        internal sealed class Entry
        {
            public LocationProxy Proxy;
            public string Prefab;
            public Vector3 Pos;
            public float Radius;
            public bool HasInterior;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        internal static int Count => _entries.Count;

        internal static void Register(LocationProxy proxy)
        {
            if (proxy == null) return;
            var instance = Access.ProxyInstance(proxy);
            if (instance == null) return;
            var location = instance.GetComponent<Location>();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Proxy == proxy) { _entries.RemoveAt(i); break; }
            }
            _entries.Add(new Entry
            {
                Proxy = proxy,
                Prefab = Utils.GetPrefabName(instance),
                Pos = proxy.transform.position,
                Radius = location != null ? Mathf.Max(location.m_exteriorRadius, 4f) : 20f,
                HasInterior = location != null && location.m_hasInterior,
            });
        }

        /// <summary>The spawned location whose exterior radius contains the point, nearest relative to its size.</summary>
        internal static bool TryFind(Vector3 point, out Entry best)
        {
            best = null;
            float bestScore = float.MaxValue;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Proxy == null) { _entries.RemoveAt(i); continue; }
                float d = Geo.FlatDistance(point, e.Pos);
                if (d > e.Radius) continue;
                float score = d / e.Radius;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best != null;
        }

        /// <summary>The closest indexed locations to a point, nearest first, whether or not their radius contains it.</summary>
        internal static List<Entry> Nearest(Vector3 point, int count)
        {
            var live = new List<Entry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Proxy == null) { _entries.RemoveAt(i); continue; }
                live.Add(_entries[i]);
            }
            live.Sort((a, b) => Geo.FlatDistance(point, a.Pos).CompareTo(Geo.FlatDistance(point, b.Pos)));
            if (live.Count > count) live.RemoveRange(count, live.Count - count);
            return live;
        }

        internal static void Clear() => _entries.Clear();
    }

    [HarmonyPatch(typeof(LocationProxy), "SpawnLocation")]
    internal static class LocationProxy_SpawnLocation_Patch
    {
        private static void Postfix(LocationProxy __instance, bool __result)
        {
            if (__result) LocationIndex.Register(__instance);
        }
    }
}
