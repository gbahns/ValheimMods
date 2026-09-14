using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>An erased marker, remembered so it cannot come back from an older copy elsewhere.</summary>
    internal sealed class Tombstone
    {
        public string Id = "";
        public long DeletedAt;
    }

    /// <summary>What a merge changed on the receiving side, and the pieces others would need to catch up.</summary>
    internal sealed class MergeResult
    {
        public int Added, Updated, Deleted, Suppressed;
        public readonly List<SharedPin> ChangedPins = new List<SharedPin>();
        public readonly List<Tombstone> NewTombstones = new List<Tombstone>();
        public readonly List<Suppression> NewSuppressions = new List<Suppression>();
        public bool Any => Added + Updated + Deleted + Suppressed > 0;

        /// <summary>The changes as a store of their own, for forwarding to others.</summary>
        public MapStore ToDelta()
        {
            var d = new MapStore();
            foreach (var p in ChangedPins) d.Pins[p.Id] = p.Clone();
            foreach (var t in NewTombstones) d.Tombstones[t.Id] = t.DeletedAt;
            d.Suppressions.AddRange(NewSuppressions);
            return d;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Added > 0) parts.Add($"+{Added}");
            if (Updated > 0) parts.Add($"{Updated} updated");
            if (Deleted > 0) parts.Add($"{Deleted} erased");
            return parts.Count > 0 ? string.Join(", ", parts) : "nothing new";
        }
    }

    /// <summary>
    /// A map of markers: the character's personal map on a client, the shared map on the server.
    /// Two stores merge by "later change wins", and an erasure is a tombstone that beats any
    /// older copy of the marker, which is what keeps erased markers erased.
    /// </summary>
    internal sealed class MapStore
    {
        // 1: original. 2: icon keys. 3: kind. 4: modified time on markers, tombstones.
        public const int FormatVersion = 4;

        public readonly Dictionary<string, SharedPin> Pins = new Dictionary<string, SharedPin>();
        public readonly Dictionary<string, long> Tombstones = new Dictionary<string, long>();
        public readonly List<Suppression> Suppressions = new List<Suppression>();

        public static long Now => DateTime.UtcNow.Ticks;

        // ── local edits ─────────────────────────────────────────────────────────────

        public void Upsert(SharedPin pin)
        {
            pin.Modified = Now;
            Pins[pin.Id] = pin;
            Tombstones.Remove(pin.Id);
        }

        /// <summary>
        /// Erase a marker, leaving a dated tombstone so the erasure travels. Erasing a recorded
        /// marker by hand also suppresses recording there again, since the player is saying they
        /// do not want it; pass suppress false where the marker is being corrected rather than
        /// rejected, such as a portal marker whose portal is gone and might be rebuilt.
        /// </summary>
        public bool Delete(string id, out SharedPin removed, bool suppress = true)
        {
            removed = null;
            if (string.IsNullOrEmpty(id) || !Pins.TryGetValue(id, out removed)) return false;
            Pins.Remove(id);
            Tombstones[id] = Now;
            if (suppress && removed.Auto) AddSuppression(new Suppression { Type = removed.Type, Icon = removed.Icon, Pos = removed.Pos, Name = removed.Name });
            return true;
        }

        public bool AddSuppression(Suppression s)
        {
            foreach (var existing in Suppressions)
                if (IconRegistry.SameKey(existing.Icon, s.Icon) && Geo.FlatDistance(existing.Pos, s.Pos) < 1f) return false;
            Suppressions.Add(s);
            return true;
        }

        // ── merging ─────────────────────────────────────────────────────────────────

        public MergeResult Merge(MapStore other)
        {
            var result = new MergeResult();
            if (other == null) return result;

            foreach (var kv in other.Pins)
            {
                var theirs = kv.Value;
                if (string.IsNullOrEmpty(theirs.Id)) continue;
                if (Tombstones.TryGetValue(theirs.Id, out long myTomb) && myTomb >= theirs.Modified) continue; // erased here after their last change
                if (Pins.TryGetValue(theirs.Id, out var mine))
                {
                    if (theirs.Modified <= mine.Modified) continue;
                    Pins[theirs.Id] = theirs.Clone();
                    result.Updated++;
                }
                else
                {
                    Pins[theirs.Id] = theirs.Clone();
                    Tombstones.Remove(theirs.Id);
                    result.Added++;
                }
                result.ChangedPins.Add(Pins[theirs.Id]);
            }

            foreach (var kv in other.Tombstones)
            {
                string id = kv.Key;
                long at = kv.Value;
                if (Pins.TryGetValue(id, out var mine) && mine.Modified <= at)
                {
                    Pins.Remove(id);
                    result.Deleted++;
                    result.NewTombstones.Add(new Tombstone { Id = id, DeletedAt = at });
                }
                if (!Tombstones.TryGetValue(id, out long myAt) || at > myAt) Tombstones[id] = at;
            }

            foreach (var s in other.Suppressions)
            {
                if (AddSuppression(s)) { result.Suppressed++; result.NewSuppressions.Add(s); }
            }
            return result;
        }

        // ── serialization ───────────────────────────────────────────────────────────

        public void Write(ZPackage pkg)
        {
            pkg.Write(FormatVersion);
            pkg.Write(Pins.Count);
            foreach (var p in Pins.Values) p.Write(pkg);
            pkg.Write(Tombstones.Count);
            foreach (var t in Tombstones) { pkg.Write(t.Key); pkg.Write(t.Value); }
            pkg.Write(Suppressions.Count);
            foreach (var s in Suppressions) s.Write(pkg);
        }

        public static MapStore Read(ZPackage pkg)
        {
            int version = pkg.ReadInt();
            if (version < 1 || version > FormatVersion) throw new InvalidDataException("Unsupported map format " + version);
            var store = new MapStore();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
            {
                var p = SharedPin.Read(pkg, version);
                if (!string.IsNullOrEmpty(p.Id)) store.Pins[p.Id] = p;
            }
            if (version >= 4)
            {
                int t = pkg.ReadInt();
                for (int i = 0; i < t; i++)
                {
                    string id = pkg.ReadString();
                    long at = pkg.ReadLong();
                    if (!string.IsNullOrEmpty(id)) store.Tombstones[id] = at;
                }
            }
            int m = pkg.ReadInt();
            for (int i = 0; i < m; i++) store.Suppressions.Add(Suppression.Read(pkg, version));
            return store;
        }

        public byte[] ToBytes()
        {
            var pkg = new ZPackage();
            Write(pkg);
            return pkg.GetArray();
        }

        public static MapStore FromBytes(byte[] bytes) => Read(new ZPackage(bytes));
    }
}
