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
        /// <summary>
        /// 5 adds what a marker stands for, and the framing that should make this the last
        /// version anybody has to upgrade in step for. Up to 4 a marker was written straight into
        /// the stream, so a reader that did not know the newest field lost its place and every
        /// marker after it came out as nonsense -- which is why those readers refuse a newer
        /// format outright, and why each bump split the group until everyone updated. From 5 on
        /// each marker travels as its own length-counted blob: an older reader takes the fields
        /// it knows and steps over the rest, and an unknown section at the end is simply not
        /// read. Append to the end of a marker and to the end of the stream and nothing breaks;
        /// reorder or remove, and all of this is void.
        /// </summary>
        public const int FormatVersion = 5;

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
            foreach (var p in Pins.Values) pkg.Write(Blob(p.Write));
            pkg.Write(Tombstones.Count);
            foreach (var t in Tombstones) { pkg.Write(t.Key); pkg.Write(t.Value); }
            pkg.Write(Suppressions.Count);
            foreach (var s in Suppressions) pkg.Write(Blob(s.Write));
        }

        /// <summary>One record on its own, so a reader can step over what it does not understand.</summary>
        private static byte[] Blob(System.Action<ZPackage> write)
        {
            var inner = new ZPackage();
            write(inner);
            return inner.GetArray();
        }

        public static MapStore Read(ZPackage pkg)
        {
            int version = pkg.ReadInt();
            // No upper bound. A version beyond this one is readable because of the framing from 5
            // on: every record is length-counted, so the fields we know are taken and the rest is
            // stepped over. Refusing it instead would leave the player with no shared map at all
            // and split the group in two, which is the one failure worth going out of the way to
            // avoid -- read every shape, enforce none.
            if (version < 1) throw new InvalidDataException("Unsupported map format " + version);
            var store = new MapStore();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
            {
                var p = version >= 5
                    ? SharedPin.Read(new ZPackage(pkg.ReadByteArray()), version)
                    : SharedPin.Read(pkg, version);
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
            for (int i = 0; i < m; i++)
                store.Suppressions.Add(version >= 5
                    ? Suppression.Read(new ZPackage(pkg.ReadByteArray()), version)
                    : Suppression.Read(pkg, version));
            return store;
        }

        /// <summary>
        /// Written where a format version would be, to say the map that follows is compressed.
        /// No real version is negative, so a reader can tell the two apart by looking.
        /// </summary>
        private const int CompressedMark = -2;

        /// <summary>
        /// The map, compressed, for crossing the wire.
        ///
        /// Markers are extremely alike -- the same icon keys, kind names and prefabs over and
        /// over -- so they shrink enormously, and the uncompressed map had outgrown what the game
        /// can send. Steam carries at most half a megabyte in one reliable message and this map
        /// reached 543 KB: the send simply failed, the queue behind it stopped draining, and the
        /// player was disconnected at a cartography table or on meeting someone in the field,
        /// which are the two places the whole map is sent.
        /// </summary>
        public void WriteFor(ZPackage pkg)
        {
            pkg.Write(CompressedMark);
            pkg.Write(Utils.Compress(ToBytes()));
        }

        /// <summary>
        /// Read a map that may be compressed or plain. Both shapes are accepted and always will
        /// be: refusing one is how players end up unable to see each other's maps.
        /// </summary>
        public static MapStore ReadFrom(ZPackage pkg)
        {
            int at = pkg.GetPos();
            int first = pkg.ReadInt();
            if (first == CompressedMark) return Read(new ZPackage(Utils.Decompress(pkg.ReadByteArray())));
            pkg.SetPos(at);
            return Read(pkg);
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
