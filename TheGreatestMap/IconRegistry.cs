using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Marker icons travel with each shared marker as a key: "item:Dandelion" (that item's
    /// inventory icon) or "pin:Boss" (a vanilla pin sprite). Each client maps a key to a local
    /// pin type on first use, starting at 100, and registers the sprite with the minimap, so
    /// every client draws the same picture even if it met the keys in a different order.
    /// </summary>
    internal static class IconRegistry
    {
        internal const int Base = 100;
        internal const string FallbackKey = "pin:Icon3";

        private static readonly Dictionary<string, int> _typeByKey = new Dictionary<string, int>();
        private static readonly List<string> _keys = new List<string>();

        internal static int MaxType => Base + _keys.Count - 1;

        /// <summary>"Dandelion" → "item:Dandelion"; "pin:boss" → "pin:boss"; blank → null.</summary>
        internal static string Normalize(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            key = key.Trim();
            if (key.Length == 0) return null;
            if (key.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) return "item:" + key.Substring(5).Trim();
            if (key.StartsWith("pin:", StringComparison.OrdinalIgnoreCase)) return "pin:" + key.Substring(4).Trim();
            return "item:" + key;
        }

        internal static bool SameKey(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryVanilla(string normalized, out Minimap.PinType type)
        {
            type = Minimap.PinType.Icon3;
            return normalized != null && normalized.StartsWith("pin:") && Enum.TryParse(normalized.Substring(4), true, out type);
        }

        /// <summary>The local pin type for an icon key, allocating and registering it on first use.</summary>
        internal static int TypeFor(string iconKey)
        {
            string key = Normalize(iconKey);
            if (key == null) return (int)Minimap.PinType.Icon3;
            if (key.StartsWith("pin:")) return TryVanilla(key, out var vanilla) ? (int)vanilla : (int)Minimap.PinType.Icon3;
            if (_typeByKey.TryGetValue(key, out int type)) return type;
            type = Base + _keys.Count;
            _keys.Add(key);
            _typeByKey[key] = type;
            Register(Minimap.instance, key, type);
            return type;
        }

        internal static string KeyForVanilla(int type) => "pin:" + ((Minimap.PinType)type).ToString();

        /// <summary>
        /// True when this icon is one of the pin types vanilla's own icon buttons hide and show
        /// (its m_selectedIcons: the five placeable icons, death and boss). Markers drawn with one
        /// of those are already covered by vanilla's button, so this mod does not offer its own.
        /// The memorial pin, which runestones use, has no vanilla button and is not one of these.
        /// </summary>
        internal static bool VanillaFilters(string iconKey)
        {
            if (!TryVanilla(Normalize(iconKey), out var type)) return false;
            switch (type)
            {
                case Minimap.PinType.Icon0:
                case Minimap.PinType.Icon1:
                case Minimap.PinType.Icon2:
                case Minimap.PinType.Icon3:
                case Minimap.PinType.Icon4:
                case Minimap.PinType.Death:
                case Minimap.PinType.Boss:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A readable name for an icon key: the item's translated name, or what the vanilla pin icon shows.</summary>
        internal static string DisplayName(string iconKey)
        {
            string key = Normalize(iconKey);
            if (key == null) return "marker";
            string raw = key.Substring(key.IndexOf(':') + 1);
            if (key.StartsWith("pin:"))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "icon0": return "Fire";
                    case "icon1": return "House";
                    case "icon2": return "Hammer";
                    case "icon3": return "Dot";
                    case "icon4": return "Portal";
                    default: return raw;
                }
            }
            try
            {
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(raw) : null;
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string name = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
                if (!string.IsNullOrEmpty(name) && Localization.instance != null)
                {
                    string localized = Localization.instance.Localize(name);
                    if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("[")) return localized;
                }
            }
            catch (Exception) { }
            return raw;
        }

        internal static string ItemKey(GameObject itemPrefab)
        {
            return itemPrefab == null ? null : "item:" + Utils.GetPrefabName(itemPrefab);
        }

        /// <summary>Icon for markers stored before icons were keyed (format 1).</summary>
        internal static string LegacyKey(int type)
        {
            switch (type)
            {
                case 100: return "item:Raspberry";
                case 101: return "item:Mushroom";
                case 102: return "item:Thistle";
                case 103: return "item:CopperOre";
                case 104: return "item:CryptKey";
                case 105: return "pin:Memorial";
                case 106: return "item:Coins";
                case 107: return "pin:Icon0";
                default:  return type < Base ? KeyForVanilla(type) : FallbackKey;
            }
        }

        /// <summary>A new Minimap instance (world load): re-register every icon met so far.</summary>
        internal static void OnMinimapStart(Minimap map)
        {
            for (int i = 0; i < _keys.Count; i++) Register(map, _keys[i], Base + i);
            EnsureArrays(map);
        }

        private static void Register(Minimap map, string key, int type)
        {
            if (map == null) return;
            Sprite sprite = Resolve(map, key);
            if (sprite == null)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] No icon for '{key}'; using the plain dot.");
                sprite = Resolve(map, FallbackKey);
            }
            map.m_icons.RemoveAll(x => (int)x.m_name == type);
            map.m_icons.Add(new Minimap.SpriteData { m_name = (Minimap.PinType)type, m_icon = sprite });
            EnsureArrays(map);
        }

        internal static Sprite Resolve(Minimap map, string key)
        {
            key = Normalize(key);
            if (key == null || map == null) return null;
            if (key.StartsWith("pin:"))
            {
                if (!TryVanilla(key, out var vanilla)) return null;
                foreach (var data in map.m_icons)
                    if (data.m_name == vanilla) return data.m_icon;
                return null;
            }
            try
            {
                if (ObjectDB.instance == null) return null;
                var prefab = ObjectDB.instance.GetItemPrefab(key.Substring(5));
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                return drop != null && drop.m_itemData != null ? drop.m_itemData.GetIcon() : null;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not load icon '{key}': {e.Message}");
                return null;
            }
        }

        /// <summary>Grow Minimap's per-type visibility array so vanilla can index our types.</summary>
        internal static void EnsureArrays(Minimap map)
        {
            if (map == null) return;
            int need = Math.Max(MaxType + 1, Enum.GetValues(typeof(Minimap.PinType)).Length);
            var old = Access.VisibleIconTypes(map);
            if (old != null && old.Length >= need) return;
            var arr = new bool[need];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = old == null || i >= old.Length || old[i];
            Access.VisibleIconTypes(map) = arr;
        }
    }
}
