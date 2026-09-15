using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>
    /// Portal pins on the large map. They use a pin type of their own (150, well clear of the
    /// vanilla enum and of TheGreatestMap's 100+ range) with vanilla's portal icon, so they never
    /// mix with player-placed portal pins, cannot be deleted or checked by vanilla clicks
    /// (they are not saved pins), and are unaffected by the icon filter buttons.
    /// </summary>
    internal static class PortalPins
    {
        internal const int PinTypeValue = 150;

        private static Sprite _sprite;
        private static readonly Dictionary<long, Minimap.PinData> _pins = new Dictionary<long, Minimap.PinData>();
        private static readonly Dictionary<Minimap.PinData, long> _ids = new Dictionary<Minimap.PinData, long>();

        internal static int Count => _pins.Count;

        internal static bool IsOurs(Minimap.PinData pin) => pin != null && (int)pin.m_type == PinTypeValue;

        /// <summary>A new Minimap instance (world load): register the icon and grow the visibility array.</summary>
        internal static void OnMinimapStart(Minimap map)
        {
            _pins.Clear();
            _ids.Clear();
            Register(map);
        }

        private static void Register(Minimap map)
        {
            if (map == null) return;
            _sprite = null;
            foreach (var data in map.m_icons)
                if (data.m_name == Minimap.PinType.Icon4) { _sprite = data.m_icon; break; }
            if (_sprite == null)
            {
                TheGreatestPortalMod.Log.LogWarning("[TheGreatestPortal] No portal icon found on the minimap; portal pins will use the plain dot.");
                foreach (var data in map.m_icons)
                    if (data.m_name == Minimap.PinType.Icon3) { _sprite = data.m_icon; break; }
            }
            map.m_icons.RemoveAll(x => (int)x.m_name == PinTypeValue);
            map.m_icons.Add(new Minimap.SpriteData { m_name = (Minimap.PinType)PinTypeValue, m_icon = _sprite });
            EnsureArrays(map);
        }

        /// <summary>Grow Minimap's per-type visibility array so vanilla can index our type. Never shrinks it.</summary>
        internal static void EnsureArrays(Minimap map)
        {
            if (map == null) return;
            int need = Math.Max(PinTypeValue + 1, Enum.GetValues(typeof(Minimap.PinType)).Length);
            var old = Access.VisibleIconTypes(map);
            if (old != null && old.Length >= need)
            {
                old[PinTypeValue] = true;
                return;
            }
            var arr = new bool[need];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = old == null || i >= old.Length || old[i];
            arr[PinTypeValue] = true;
            Access.VisibleIconTypes(map) = arr;
        }

        /// <summary>Draws one pin per catalog portal, labelled by <paramref name="label"/>.</summary>
        internal static void Show(Func<PortalInfo, string> label)
        {
            var map = Minimap.instance;
            if (map == null) return;
            Clear();
            EnsureArrays(map);
            bool haveIcon = false;
            foreach (var data in map.m_icons) if ((int)data.m_name == PinTypeValue) { haveIcon = true; break; }
            if (!haveIcon) Register(map);
            foreach (var p in Catalog.All)
            {
                var pin = map.AddPin(p.Pos, (Minimap.PinType)PinTypeValue, label(p), save: false, isChecked: false, 0L);
                if (pin == null) continue;
                _pins[p.Id] = pin;
                _ids[pin] = p.Id;
            }
            Access.PinUpdateRequired(map) = true;
        }

        internal static void Clear()
        {
            var map = Minimap.instance;
            if (map != null)
            {
                foreach (var pin in _pins.Values) map.RemovePin(pin);
                Access.PinUpdateRequired(map) = true;
            }
            _pins.Clear();
            _ids.Clear();
        }

        /// <summary>The catalog portal whose pin is nearest to a world point, within the radius.</summary>
        internal static PortalInfo Closest(Vector3 worldPos, float radius)
        {
            PortalInfo best = null;
            float bestDist = float.MaxValue;
            foreach (var kv in _pins)
            {
                var p = Catalog.Get(kv.Key);
                if (p == null) continue;
                float d = Utils.DistanceXZ(worldPos, p.Pos);
                if (d < radius && d < bestDist) { best = p; bestDist = d; }
            }
            return best;
        }

        internal static Minimap.PinData PinFor(long id) => _pins.TryGetValue(id, out var pin) ? pin : null;

        /// <summary>
        /// Paints every portal pin, the hovered one included: hovering makes a portal bigger and
        /// lights it up, it does not change what color that portal is. Vanilla repaints its
        /// markers white whenever it lays the map out again, so this runs each frame.
        /// </summary>
        internal static void Tint(Color color)
        {
            foreach (var kv in _pins)
            {
                var icon = kv.Value.m_iconElement;
                if (icon != null && icon.color != color) icon.color = color;
            }
        }
    }
}
