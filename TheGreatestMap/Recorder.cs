using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// While the pocket map has been out for the dwell time, writes down found things within the
    /// record radius as shared markers. A find is skipped, and stays pending, while a marker with
    /// the same icon already stands within the marker spacing or such a marker was erased there;
    /// it is written the moment that is no longer true. Labels follow the per-kind label spacing:
    /// below zero never, zero always, otherwise one label per that many metres.
    /// </summary>
    internal static class Recorder
    {
        private static readonly HashSet<string> _announced = new HashSet<string>();
        private static float _outSince = -1f;
        private static float _next;

        internal static void Reset()
        {
            _announced.Clear();
            _outSince = -1f;
        }

        internal static void Update()
        {
            var player = Player.m_localPlayer;
            bool active = PocketMap.IsOut || !TgmConfig.RequireMapOutToRecord.Value;
            if (player == null || !active || !TgmConfig.RecordEnabled.Value)
            {
                _outSince = -1f;
                return;
            }
            if (_outSince < 0f) _outSince = Time.time;
            if (Time.time - _outSince < TgmConfig.RecordDwell.Value) return;
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            if (player.InInterior()) return;

            Vector3 here = player.transform.position;
            float radius = TgmConfig.RecordRadius.Value;
            foreach (var found in DiscoveryLedger.Pending())
            {
                if (Geo.FlatDistance(here, found.Pos) > radius) continue;
                if (!TgmConfig.CategoryEnabled.TryGetValue(found.Cat, out var enabled) || !enabled.Value)
                {
                    DiscoveryLedger.MarkRecorded(found.Key);
                    continue;
                }
                float spacing = TgmConfig.MarkerSpacing.TryGetValue(found.Cat, out var s) ? s.Value : 1f;
                if (ClientPins.HasPinNear(found.Icon, found.Pos, spacing))
                {
                    Announce(found, $"{found.Name}: already marked within {spacing:0.#} m");
                    continue;
                }
                if (ClientPins.IsSuppressed(found.Icon, found.Pos, spacing))
                {
                    Announce(found, $"{found.Name}: a marker here was erased, not recording");
                    continue;
                }
                float labelSpacing = TgmConfig.LabelSpacing.TryGetValue(found.Cat, out var l) ? l.Value : 0f;
                bool label = labelSpacing >= 0f && !ClientPins.HasLabeledPinNear(found.Icon, found.Name, found.Pos, labelSpacing);
                DiscoveryLedger.MarkRecorded(found.Key);
                ClientPins.CreateShared(label ? found.Name : "", found.Pos, found.Icon, auto: true);
                TheGreatestMapMod.Message("Recorded: " + found.Name);
            }
        }

        private static void Announce(Found found, string text)
        {
            if (!_announced.Add(found.Key)) return;
            TheGreatestMapMod.Message(text);
        }
    }
}
