using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>
    /// Portal pins on the large map. They use a pin type of their own, with vanilla's portal icon,
    /// so they never mix with player-placed portal pins, cannot be deleted or checked by vanilla
    /// clicks (they are not saved pins), and are unaffected by the icon filter buttons.
    ///
    /// The number is 1000, and it is a reservation rather than a guess. This was 150, chosen as
    /// "clear of TheGreatestMap's 100+ range" - but that mod does not use a fixed range. It hands
    /// out one type per distinct icon key a map has recorded, counting up from 100, so it reaches
    /// 150 on a client whose map has seen 51 kinds of thing and then both mods own the same
    /// number. What follows is not subtle: registering an icon for a type removes any existing
    /// entry for it, and TheGreatestMap matches pins by type when it asks whether a marker is
    /// already somewhere. TheGreatestMap now refuses to allocate at or above 1000, so this is the
    /// one number both mods agree on. Nothing is saved with a type in it, on either side, so the
    /// change needs no migration.
    /// </summary>
    internal static class PortalPins
    {
        internal const int PinTypeValue = 1000;

        private static Sprite _sprite;
        private static readonly Dictionary<long, Minimap.PinData> _pins = new Dictionary<long, Minimap.PinData>();
        private static readonly Dictionary<Minimap.PinData, long> _ids = new Dictionary<Minimap.PinData, long>();

        // Scratch for the diff in Show, kept rather than allocated: it runs on every snapshot.
        private static readonly HashSet<long> _live = new HashSet<long>();
        private static readonly List<long> _gone = new List<long>();

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

        /// <summary>
        /// Draws one pin per catalog portal, labelled by <paramref name="label"/>.
        ///
        /// A portal that has not changed keeps the pin object it already had. This used to clear
        /// the lot and build them again, which was fine when it ran on opening the map - but it
        /// also runs on every catalog snapshot, and the server sends one whenever anybody
        /// anywhere builds or removes a portal. On a server where portals come and go, every pin
        /// and every label on everyone's map was destroyed and recreated each time, which reads
        /// as a flicker: the marker and its name blink out and come back.
        /// </summary>
        internal static void Show(Func<PortalInfo, string> label)
        {
            var map = Minimap.instance;
            if (map == null) return;
            EnsureArrays(map);
            bool haveIcon = false;
            foreach (var data in map.m_icons) if ((int)data.m_name == PinTypeValue) { haveIcon = true; break; }
            if (!haveIcon) Register(map);

            bool changed = false;
            _live.Clear();
            foreach (var p in Catalog.All)
            {
                _live.Add(p.Id);
                string text = label(p);
                if (_pins.TryGetValue(p.Id, out var existing) && existing != null && Retext(existing, text))
                {
                    // Position and name are all a pin carries that a catalog update can change.
                    if (existing.m_pos != p.Pos) { existing.m_pos = p.Pos; changed = true; }
                    continue;
                }
                if (existing != null) { map.RemovePin(existing); _ids.Remove(existing); _pins.Remove(p.Id); }
                var pin = map.AddPin(p.Pos, (Minimap.PinType)PinTypeValue, text, save: false, isChecked: false, 0L);
                if (pin == null) continue;
                _pins[p.Id] = pin;
                _ids[pin] = p.Id;
                changed = true;
            }

            // Portals that are no longer in the catalog: torn down, or renamed away by an id change.
            _gone.Clear();
            foreach (var kv in _pins) if (!_live.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (long id in _gone)
            {
                if (_pins.TryGetValue(id, out var pin) && pin != null) { map.RemovePin(pin); _ids.Remove(pin); }
                _pins.Remove(id);
                changed = true;
            }

            if (changed) Access.PinUpdateRequired(map) = true;
        }

        /// <summary>
        /// Gives a pin a new label, or says it cannot.
        ///
        /// Vanilla reads m_name once, in PinNameData.SetTextAndGameObject, and never looks at it
        /// again - so the text has to be written to the label itself. A pin that was built with no
        /// name has no label object at all, and only AddPin makes one, so renaming that one means
        /// building the pin again; false says so and the caller does that for just that pin.
        /// </summary>
        private static bool Retext(Minimap.PinData pin, string text)
        {
            text = text ?? "";
            if (pin.m_name == text) return true;
            if (string.IsNullOrEmpty(pin.m_name) || string.IsNullOrEmpty(text)) return false;
            var label = pin.m_NamePinData;
            if (label == null || label.PinNameText == null) return false;
            pin.m_name = text;
            label.PinNameText.text = Localization.instance.Localize(text);
            return true;
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
