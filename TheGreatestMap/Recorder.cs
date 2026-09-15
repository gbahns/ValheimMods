using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// While the pocket map has been out for the dwell time, writes down found things within the
    /// record radius as shared markers. A find is skipped, and stays pending, while a marker with
    /// the same icon already stands within the marker spacing or such a marker was erased there;
    /// it is written the moment that is no longer true. Labels follow the per-kind label spacing:
    /// below zero never, zero always, otherwise one label per that many meters.
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
            if (!CanWrite(player, out string blocked)) { Blocked(blocked); return; }
            _blockedNote = null;
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            if (player.InInterior()) return;

            Vector3 here = player.transform.position;
            float range = TgmConfig.RecordRadius.Value; // 0 = anything found recently, wherever you are
            foreach (var found in DiscoveryLedger.Pending())
            {
                if (range > 0f && Geo.FlatDistance(here, found.Pos) > range) continue;
                if (!TgmConfig.CategoryEnabled.TryGetValue(found.Cat, out var enabled) || !enabled.Value)
                {
                    DiscoveryLedger.MarkRecorded(found.Key);
                    continue;
                }
                // Locations deduplicate over their whole radius around their origin, so a farm
                // whose marker sits on the house is still one marker.
                float spacing = TgmConfig.MarkerSpacing.TryGetValue(found.Cat, out var s) ? s.Value : 1f;
                float dedupeRadius = Mathf.Max(spacing, found.Radius);
                Vector3 dedupeAt = found.DedupeCenter;
                // Already marked, but with the wrong picture: correct it rather than adding a
                // second marker beside it. Only dungeons, and only from seeing the place.
                if (ClientPins.CorrectDungeonIconNear(found, dedupeRadius))
                {
                    DiscoveryLedger.MarkRecorded(found.Key);
                    Announce(found, $"{found.Name}: marker icon corrected");
                    continue;
                }
                if (ClientPins.HasPinNear(found.Icon, dedupeAt, dedupeRadius))
                {
                    Announce(found, $"{found.Name}: already marked within {dedupeRadius:0.#} m");
                    continue;
                }
                if (ClientPins.IsSuppressed(found.Icon, dedupeAt, dedupeRadius))
                {
                    Announce(found, $"{found.Name}: a marker here was erased, not recording");
                    continue;
                }
                float labelSpacing = TgmConfig.LabelSpacing.TryGetValue(found.Cat, out var l) ? l.Value : 0f;
                float labelRadius = labelSpacing > 0f ? Mathf.Max(labelSpacing, found.Radius) : labelSpacing;
                bool label = labelSpacing >= 0f && !ClientPins.HasLabeledPinNear(found.Icon, found.Name, dedupeAt, labelRadius);
                DiscoveryLedger.MarkRecorded(found.Key);
                ClientPins.CreateShared(label ? found.Name : "", found.Pos, found.Icon, found.Cat.ToString(), auto: true, isChecked: Searched.WasSearched(found.Key));
                TheGreatestMapMod.Message("Recorded: " + found.Name);
            }
        }

        private static void Announce(Found found, string text)
        {
            if (!_announced.Add(found.Key)) return;
            TheGreatestMapMod.Message(text);
        }

        // ── writing needs a steady hand ──────────────────────────────────────────────

        private static float _lastHit = -999f;
        private static string _blockedNote;
        private static float _nextBlockedNote;

        /// <summary>Something hit the local player. Recorded here so writing can pause for a moment.</summary>
        internal static void NoteHit() => _lastHit = Time.time;

        internal static bool UnderAttack =>
            TgmConfig.BlockWhileAttacked != null && TgmConfig.BlockWhileAttacked.Value
            && Time.time - _lastHit < TgmConfig.AttackedSeconds.Value;

        /// <summary>
        /// You can write on the map while enemies are about, unlike resting, which asks you not to
        /// know of any. What you cannot do is write while they are landing blows, and, if asked
        /// for, while on the move.
        /// </summary>
        internal static bool CanWrite(Player player, out string why)
        {
            why = null;
            if (UnderAttack) { why = "You cannot write on your map while under attack."; return false; }
            var mode = TgmConfig.BlockWhileMoving != null ? TgmConfig.BlockWhileMoving.Value : Movement.Never;
            if (mode == Movement.Never || player == null) return true;
            if (mode == Movement.Running && player.IsRunning()) { why = "You cannot write on your map while running."; return false; }
            if (mode == Movement.Moving)
            {
                var v = player.GetVelocity();
                if (new Vector2(v.x, v.z).magnitude > 0.5f) { why = "Stand still for a moment to write on your map."; return false; }
            }
            return true;
        }

        /// <summary>Say why nothing is being written, but only when the reason changes, and not more than every few seconds.</summary>
        private static void Blocked(string why)
        {
            if (why == null) return;
            if (why == _blockedNote && Time.time < _nextBlockedNote) return;
            _blockedNote = why;
            _nextBlockedNote = Time.time + 5f;
            TheGreatestMapMod.Message(why);
        }
    }
}
