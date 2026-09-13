using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Works out the kind of a recorded marker that was stored before kinds existed, from its
    /// icon: the catalog says which prefabs belong to which kind, and each prefab's icon is what
    /// it gives or drops, so the icon leads back to the kind. Built once per session from the
    /// player's own catalog settings.
    /// </summary>
    internal static class KindInference
    {
        private static readonly Dictionary<string, Category> _byIcon = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        private static bool _built;

        internal static void Invalidate() => _built = false;

        internal static Category? FromIcon(string icon)
        {
            string key = IconRegistry.Normalize(icon);
            if (key == null) return null;
            if (!_built) Build();
            return _byIcon.TryGetValue(key, out var cat) ? cat : (Category?)null;
        }

        private static void Add(string icon, Category cat)
        {
            string key = IconRegistry.Normalize(icon);
            if (key != null && !_byIcon.ContainsKey(key)) _byIcon[key] = cat;
        }

        private static void Build()
        {
            _byIcon.Clear();
            _built = true;
            foreach (var cat in Categories.All)
            {
                if (!TgmConfig.CategoryPrefabs.TryGetValue(cat, out var entry)) continue;
                foreach (var raw in (entry.Value ?? "").Split(','))
                {
                    string item = raw.Trim();
                    if (item.Length == 0) continue;
                    string prefab = item, icon = null;
                    int bar = item.IndexOf('|');
                    if (bar >= 0) { icon = item.Substring(bar + 1).Trim(); item = item.Substring(0, bar); prefab = item; }
                    int eq = item.IndexOf('=');
                    if (eq > 0) prefab = item.Substring(0, eq).Trim();
                    prefab = prefab.TrimEnd('*');
                    if (prefab.Length == 0) continue;
                    string resolved = icon ?? Categories.KnownIcon(prefab) ?? IconOfPrefab(prefab);
                    if (resolved != null) Add(resolved, cat);
                }
            }
            // Category fallbacks and the vanilla-icon kinds, lowest priority.
            foreach (var cat in Categories.All) Add(Categories.DefaultIcon(cat), cat);
        }

        /// <summary>What a world prefab gives or drops, as an icon key, or null.</summary>
        private static string IconOfPrefab(string prefab)
        {
            try
            {
                var scene = ZNetScene.instance;
                if (scene == null) return null;
                var go = scene.GetPrefab(prefab);
                if (go == null) return null;
                var pickable = go.GetComponent<Pickable>();
                if (pickable != null) return IconRegistry.ItemKey(pickable.m_itemPrefab);
                var rock5 = go.GetComponent<MineRock5>();
                if (rock5 != null) return FirstDrop(rock5.m_dropItems);
                var rock = go.GetComponent<MineRock>();
                if (rock != null) return FirstDrop(rock.m_dropItems);
                var drops = go.GetComponent<DropOnDestroyed>();
                if (drops != null) return FirstDrop(drops.m_dropWhenDestroyed);
            }
            catch { /* prefab lookups can fail during load; the fallbacks still apply */ }
            return null;
        }

        private static string FirstDrop(DropTable table)
        {
            if (table == null || table.m_drops == null) return null;
            foreach (var drop in table.m_drops)
                if (drop.m_item != null) return IconRegistry.ItemKey(drop.m_item);
            return null;
        }
    }
}
