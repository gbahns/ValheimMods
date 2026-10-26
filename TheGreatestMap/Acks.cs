using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TheGreatestMap
{
    /// <summary>
    /// Who has seen which erasures, so an erasure can stop existing once it has done its work.
    ///
    /// <para>An erasure is kept because some copy of the map out there might still hold the
    /// marker it rubbed out. Once every player has taken it in, nothing can resurrect that
    /// marker and the record is dead weight -- but nothing was ever counting, so they
    /// accumulated forever. 660 of them on the live map were from before a format change two
    /// versions ago and could not match anything at all.</para>
    ///
    /// <para><b>Counted in changes, not in days.</b> What puts a returning player out of step is
    /// not how long they were away but how much the map moved while they were: a month away from
    /// a sleeping server costs nothing, a week away from five people playing nightly is plenty.
    /// The server already counts every change it accepts, so that number is the measure, and it
    /// has the property no clock has -- it does not advance while nobody is playing, and two
    /// machines cannot disagree about it.</para>
    ///
    /// <para>A player who falls far enough behind is set aside rather than waited for, or one
    /// person who stops playing would pin every erasure made afterwards for good. They are not
    /// forgotten: they keep their row, and taking them back into account costs one sync.</para>
    /// </summary>
    internal static class Acks
    {
        /// <summary>
        /// How far behind a player may fall and still be waited for. A few busy evenings; past
        /// that their copy is stale enough that the group should not be holding records open on
        /// its behalf.
        /// </summary>
        private const long LapsedAfterChanges = 2000L;

        private sealed class Seen
        {
            public string Name = "";
            public long Changes;        // the count when they last completed a sync
            public long At;             // wall clock, for a human reading the file
        }

        private static readonly Dictionary<string, Seen> _players = new Dictionary<string, Seen>(StringComparer.Ordinal);

        /// <summary>When each erasure was taken in, by the same count the players are measured in.</summary>
        private static readonly Dictionary<PinKey, long> _erasedAt = new Dictionary<PinKey, long>();

        private static long _changes;
        private static string _path;
        private static bool _dirty;

        /// <summary>Changes accepted over the life of this map, across restarts.</summary>
        internal static long Changes => _changes;

        internal static void Reset()
        {
            _players.Clear();
            _erasedAt.Clear();
            _changes = 0L;
            _path = null;
            _dirty = false;
        }

        /// <summary>Note what a merge did, so the erasures it made can be followed.</summary>
        internal static void Record(MergeResult result, MapStore store)
        {
            if (result == null || !result.Any) return;
            _changes += result.Added + result.Updated + result.Deleted;
            foreach (var t in result.NewTombstones)
                if (!_erasedAt.ContainsKey(t.Key)) _erasedAt[t.Key] = _changes;
            // An erasure already here when this ledger started has no count of its own. Give it
            // the one it is first seen at, so it is held for a full round of the group rather
            // than dropped the moment everybody syncs once.
            foreach (var key in store.Tombstones.Keys)
                if (!_erasedAt.ContainsKey(key)) _erasedAt[key] = _changes;
            _dirty = true;
        }

        /// <summary>This player has the map as it stands.</summary>
        internal static void Synced(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_players.TryGetValue(id, out var seen)) _players[id] = seen = new Seen();
            if (!string.IsNullOrEmpty(name)) seen.Name = name;
            seen.Changes = _changes;
            seen.At = DateTime.UtcNow.Ticks;
            _dirty = true;
        }

        /// <summary>
        /// Drop every erasure that each player still being waited for has already taken in.
        /// Returns how many went.
        /// </summary>
        internal static int Prune(MapStore store)
        {
            if (store == null || store.Tombstones.Count == 0 || _players.Count == 0) return 0;
            long behind = long.MaxValue;
            int waiting = 0;
            foreach (var seen in _players.Values)
            {
                if (_changes - seen.Changes > LapsedAfterChanges) continue;   // set aside, not waited for
                waiting++;
                if (seen.Changes < behind) behind = seen.Changes;
            }
            if (waiting == 0 || behind == long.MaxValue) return 0;

            var done = new List<PinKey>();
            foreach (var key in store.Tombstones.Keys)
                if (_erasedAt.TryGetValue(key, out long made) && made <= behind) done.Add(key);
            foreach (var key in done)
            {
                store.Tombstones.Remove(key);
                _erasedAt.Remove(key);
            }
            if (done.Count > 0) _dirty = true;
            return done.Count;
        }

        /// <summary>Who is being waited for and who has been set aside, for the log and tgm_status.</summary>
        internal static string Describe()
        {
            if (_players.Count == 0) return "nobody has synced yet";
            var parts = new List<string>();
            foreach (var seen in _players.Values)
            {
                long back = _changes - seen.Changes;
                parts.Add($"{(string.IsNullOrEmpty(seen.Name) ? "?" : seen.Name)} " +
                          (back > LapsedAfterChanges ? $"{back} behind, set aside" : $"{back} behind"));
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("; ", parts.ToArray());
        }

        // ── kept beside the map, not inside it ──────────────────────────────────────
        //
        // A file of its own rather than a section of the map, because it is not part of what a
        // map is: no client ever receives this, nothing merges it, and a map carried to another
        // server should not bring one group's attendance with it. It is also the kind of thing
        // that can be deleted to start again, and that should not mean losing markers.

        internal static void Load(string mapPath)
        {
            Reset();
            if (string.IsNullOrEmpty(mapPath)) return;
            _path = mapPath + ".acks";
            if (!File.Exists(_path)) return;
            try
            {
                foreach (var line in File.ReadAllLines(_path))
                {
                    var bits = line.Split('\t');
                    if (bits.Length == 0) continue;
                    if (bits[0] == "changes" && bits.Length >= 2) _changes = long.Parse(bits[1]);
                    else if (bits[0] == "player" && bits.Length >= 5)
                        _players[bits[1]] = new Seen { Name = bits[2], Changes = long.Parse(bits[3]), At = long.Parse(bits[4]) };
                    else if (bits[0] == "erased" && bits.Length >= 3 && PinKey.TryDecode(bits[1], out var key))
                        _erasedAt[key] = long.Parse(bits[2]);
                }
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] {_changes} change(s) to this map so far; {_players.Count} player(s) known: {Describe()}.");
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] Could not read who has seen which erasures (" + e.Message +
                    "); starting that count again. No markers are affected.");
                Reset();
                _path = mapPath + ".acks";
            }
        }

        internal static void Save()
        {
            if (!_dirty || string.IsNullOrEmpty(_path)) return;
            try
            {
                var sb = new StringBuilder();
                sb.Append("changes\t").Append(_changes).Append('\n');
                foreach (var kv in _players)
                    sb.Append("player\t").Append(kv.Key).Append('\t').Append(kv.Value.Name).Append('\t')
                      .Append(kv.Value.Changes).Append('\t').Append(kv.Value.At).Append('\n');
                foreach (var kv in _erasedAt)
                    sb.Append("erased\t").Append(kv.Key.Encode()).Append('\t').Append(kv.Value).Append('\n');
                File.WriteAllText(_path, sb.ToString());
                _dirty = false;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not write " + _path + ": " + e.Message);
            }
        }
    }
}
