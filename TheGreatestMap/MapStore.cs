using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>An erased marker, remembered so it cannot come back from an older copy elsewhere.</summary>
    internal sealed class Tombstone
    {
        public PinKey Key;
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
            foreach (var p in ChangedPins) d.Pins[p.Key] = p.Clone();
            foreach (var t in NewTombstones) d.Tombstones[t.Key] = t.DeletedAt;
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
        ///
        /// 6 drops the GUID every marker used to carry and identifies a marker by what it already
        /// says about itself instead -- see <see cref="PinKey"/>. That halves what crosses the
        /// wire, and unlike every bump before it, it cannot be read by an older client: the
        /// framing lets a reader step over a field it does not know, not over one that is no
        /// longer there. A server on 6 therefore turns away clients that predate it, with a
        /// reason, rather than letting them read nonsense.
        ///
        /// 7 drops the fields a reader can work out again -- the pin type, the auto flag, and an
        /// icon or kind that only repeats what the catalog already says.
        ///
        /// 8 stops writing a marker at a time and writes a field at a time: a column per field,
        /// strings held once in a table and referred to by number, positions in centimetres as
        /// deltas, timestamps in seconds from 2020.
        ///
        /// 8 is deliberately a number of its own rather than a second shape of 7. Both were
        /// written on the same day, and for a few hours the columnar layout went out still
        /// calling itself 7 -- which meant a per-record 7 and a columnar 7 existed with no way to
        /// tell them apart, and a reader handed the wrong one would take the string table for
        /// marker records. One encoding, one number, always: a version that does not change when
        /// the bytes do is worse than no version at all.
        /// </summary>
        public const int FormatVersion = 8;

        public readonly Dictionary<PinKey, SharedPin> Pins = new Dictionary<PinKey, SharedPin>();
        public readonly Dictionary<PinKey, long> Tombstones = new Dictionary<PinKey, long>();
        public readonly List<Suppression> Suppressions = new List<Suppression>();

        /// <summary>
        /// Erasures recorded before the rekey, still keyed by the GUID of the marker they erased.
        ///
        /// A tombstone exists to stop an erased marker coming back from a copy of the map that
        /// has not heard about the erasure. The ones written under format 5 name a GUID, and the
        /// marker they name is gone, so there is nothing left to work out its new key from --
        /// they cannot be converted, only carried. Dropping them instead would have resurrected
        /// every marker anyone had erased: on the live map that is 659 of them, the crop markers
        /// and the trader markers among them.
        ///
        /// Nothing is ever added here. A marker arriving from an older map still carries its old
        /// id, which is checked against this; once no such map exists anywhere, this empties out
        /// and can go.
        /// </summary>
        public readonly Dictionary<string, long> LegacyTombstones = new Dictionary<string, long>();

        /// <summary>
        /// Which format this store was read out of, or the current one for a store built here.
        /// A map read from an older format has been converted in memory and the copy on disk is
        /// still the old one, so whoever loaded it should write it back rather than leaving the
        /// conversion to be redone on every load.
        /// </summary>
        public int ReadVersion = FormatVersion;

        /// <summary>
        /// How many markers in the map just read turned out to be another marker already in it.
        ///
        /// Only happens converting a map written before markers said what they stand for, where
        /// the whole of a key is the owner and the position: two records at one point are then
        /// one record, and the later of them is kept. They are duplicates in any case -- the same
        /// plant written down twice, once as a thistle and once as a dandelion -- so folding them
        /// is the repair this mod exists to make. It is counted because a marker disappearing
        /// without anybody saying so is how trust in a map is lost.
        /// </summary>
        public int Collapsed;

        public static long Now => DateTime.UtcNow.Ticks;

        // ── local edits ─────────────────────────────────────────────────────────────

        public void Upsert(SharedPin pin)
        {
            pin.Modified = Now;
            Pins[pin.Key] = pin;
            Tombstones.Remove(pin.Key);
        }

        /// <summary>
        /// Erase a marker, leaving a dated tombstone so the erasure travels. Erasing a recorded
        /// marker by hand also suppresses recording there again, since the player is saying they
        /// do not want it; pass suppress false where the marker is being corrected rather than
        /// rejected, such as a portal marker whose portal is gone and might be rebuilt.
        /// </summary>
        public bool Delete(PinKey key, out SharedPin removed, bool suppress = true)
        {
            removed = null;
            if (key.IsNone || !Pins.TryGetValue(key, out removed)) return false;
            Pins.Remove(key);
            Tombstones[key] = Now;
            if (suppress && removed.Auto) AddSuppression(new Suppression { Type = removed.Type, Icon = removed.Icon, Pos = removed.Pos, Name = removed.Name });
            return true;
        }

        /// <summary>
        /// Take a marker out without leaving an erasure behind: it is not gone, it is being
        /// written down again under another name.
        ///
        /// Re-identifying used to erase and re-add, which left a tombstone each time. That was
        /// right when an erasure was the only way to take a key off the map, and ruinous at
        /// scale: a map that had never been identified had every berry and mushroom filled in at
        /// once, and each fill left its ghost. Four thousand four hundred of them appeared on the
        /// live map in a single hour, and they outweighed the markers.
        ///
        /// Nothing needs the tombstone. Every client works the same answer out of the same
        /// catalog, so they all re-file a marker the same way and arrive at the same place
        /// without being told. If a copy that has not yet done so sends the old one back, there
        /// are briefly two markers at one spot -- which the folding pass already settles, keeping
        /// the one that knows what it is.
        /// </summary>
        public bool Lift(PinKey key, out SharedPin removed)
        {
            removed = null;
            if (key.IsNone || !Pins.TryGetValue(key, out removed)) return false;
            Pins.Remove(key);
            Tombstones.Remove(key);
            return true;
        }

        /// <summary>
        /// Drop erasures that are only the former name of a marker still standing in the same
        /// place. Returns how many went.
        ///
        /// Each of these says "the thing that used to be here is gone", about a spot where that
        /// very thing is still recorded under the name it has since been given. They are not
        /// keeping anything erased, and they cost more than the markers do.
        /// </summary>
        public int ForgetGhosts()
        {
            if (Tombstones.Count == 0) return 0;
            var standing = new Dictionary<long, List<SharedPin>>();
            foreach (var pin in Pins.Values)
            {
                long at = Place(pin.Pos);
                if (!standing.TryGetValue(at, out var here)) standing[at] = here = new List<SharedPin>();
                here.Add(pin);
            }
            var ghosts = new List<PinKey>();
            foreach (var kv in Tombstones)
            {
                var key = kv.Key;
                if (!standing.TryGetValue(Place(key.Pos), out var here)) continue;
                foreach (var pin in here)
                {
                    if (pin.OwnerId != key.Owner) continue;
                    if (pin.Pos != key.Pos) continue;
                    if (string.Equals(pin.Source, key.Source, StringComparison.Ordinal)) continue;
                    ghosts.Add(key);
                    break;
                }
            }
            foreach (var key in ghosts) Tombstones.Remove(key);
            return ghosts.Count;
        }

        /// <summary>A position as one number, for gathering what stands in the same spot.</summary>
        private static long Place(Vector3 pos) =>
            ((long)SharedPin.Hundredths(pos.x) << 32) ^ (uint)SharedPin.Hundredths(pos.z);

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
                var key = theirs.Key;
                if (key.IsNone) continue;
                if (Tombstones.TryGetValue(key, out long myTomb) && myTomb >= theirs.Modified) continue; // erased here after their last change
                // The same question asked of the erasures recorded before the rekey. A marker
                // coming from a map still in the old format carries the id it was written with,
                // and that id is the only thing those tombstones know it by.
                if (!string.IsNullOrEmpty(theirs.LegacyId)
                    && LegacyTombstones.TryGetValue(theirs.LegacyId, out long oldTomb)
                    && oldTomb >= theirs.Modified) continue;
                if (Pins.TryGetValue(key, out var mine))
                {
                    if (theirs.Modified <= mine.Modified) continue;
                    Pins[key] = theirs.Clone();
                    result.Updated++;
                }
                else
                {
                    Pins[key] = theirs.Clone();
                    Tombstones.Remove(key);
                    result.Added++;
                }
                result.ChangedPins.Add(Pins[key]);
            }

            foreach (var kv in other.Tombstones)
            {
                var key = kv.Key;
                long at = kv.Value;
                if (Pins.TryGetValue(key, out var mine) && mine.Modified <= at)
                {
                    Pins.Remove(key);
                    result.Deleted++;
                    result.NewTombstones.Add(new Tombstone { Key = key, DeletedAt = at });
                }
                if (!Tombstones.TryGetValue(key, out long myAt) || at > myAt) Tombstones[key] = at;
            }

            // Old erasures travel too, and they can still bite: a marker of ours read from a map
            // written before the rekey remembers the id it had, and an erasure of that id is an
            // erasure of this marker however it is keyed now.
            foreach (var kv in other.LegacyTombstones)
            {
                if (!LegacyTombstones.TryGetValue(kv.Key, out long myAt) || kv.Value > myAt)
                    LegacyTombstones[kv.Key] = kv.Value;
            }
            if (other.LegacyTombstones.Count > 0)
            {
                var doomed = new List<PinKey>();
                foreach (var kv in Pins)
                {
                    var pin = kv.Value;
                    if (string.IsNullOrEmpty(pin.LegacyId)) continue;
                    if (LegacyTombstones.TryGetValue(pin.LegacyId, out long at) && pin.Modified <= at)
                        doomed.Add(kv.Key);
                }
                foreach (var key in doomed)
                {
                    long at = LegacyTombstones[Pins[key].LegacyId];
                    Pins.Remove(key);
                    Tombstones[key] = at;
                    result.Deleted++;
                    result.NewTombstones.Add(new Tombstone { Key = key, DeletedAt = at });
                }
            }

            foreach (var s in other.Suppressions)
            {
                if (AddSuppression(s)) { result.Suppressed++; result.NewSuppressions.Add(s); }
            }
            return result;
        }

        // ── serialization ───────────────────────────────────────────────────────────

        /// <summary>
        /// Seconds from the first of January 2020, which every time in the map is counted from.
        /// Ticks are hundred-nanosecond counts since the year 1 and are eight bytes of almost
        /// entirely the same leading digits; a map is not interested in anything finer than a
        /// second, and nothing here is: the one rule that reads a time closely is which of two
        /// changes to a marker came later, and two changes to one marker inside a second have a
        /// settled answer already.
        /// </summary>
        private const long Epoch = 637134336000000000L;
        private const long TicksPerSecond = 10000000L;

        private static long ToSeconds(long ticks) => ticks <= Epoch ? 0L : (ticks - Epoch) / TicksPerSecond;
        private static long FromSeconds(long seconds) => Epoch + seconds * TicksPerSecond;

        /// <summary>
        /// Markers ordered so that neighbours on the map are neighbours in the file, which is
        /// what makes a position worth writing as the step from the one before. The order is of
        /// no consequence to anything else: a marker's name is worked out from what it says
        /// about itself, so the file may hold them in any order it likes.
        /// </summary>
        private List<SharedPin> InWritingOrder()
        {
            var ordered = new List<SharedPin>(Pins.Values);
            ordered.Sort((a, b) =>
            {
                int ax = Mathf.FloorToInt(a.Pos.x / 32f), bx = Mathf.FloorToInt(b.Pos.x / 32f);
                if (ax != bx) return ax.CompareTo(bx);
                int az = Mathf.FloorToInt(a.Pos.z / 32f), bz = Mathf.FloorToInt(b.Pos.z / 32f);
                if (az != bz) return az.CompareTo(bz);
                int byX = a.Pos.x.CompareTo(b.Pos.x);
                return byX != 0 ? byX : a.Pos.z.CompareTo(b.Pos.z);
            });
            return ordered;
        }

        public void Write(ZPackage pkg)
        {
            pkg.Write(FormatVersion);
            var pins = InWritingOrder();
            pkg.Write(pins.Count);

            var owners = new StringTable();
            var authors = new StringTable();
            var sources = new StringTable();
            var icons = new StringTable();
            var kinds = new StringTable();
            var ownerIndex = new ColumnWriter();
            var authorIndex = new ColumnWriter();
            var sourceIndex = new ColumnWriter();
            var iconIndex = new ColumnWriter();
            var kindIndex = new ColumnWriter();
            var names = new ColumnWriter();
            var x = new ColumnWriter();
            var y = new ColumnWriter();
            var z = new ColumnWriter();
            var flags = new ColumnWriter();
            var created = new ColumnWriter();
            var modified = new ColumnWriter();

            long lastX = 0, lastY = 0, lastZ = 0;
            foreach (var p in pins)
            {
                ownerIndex.Var(owners.Of(p.OwnerId.ToString()));
                authorIndex.Var(authors.Of(p.Author));
                sourceIndex.Var(sources.Of(p.Source));
                iconIndex.Var(icons.Of(p.IconToStore));
                kindIndex.Var(kinds.Of(p.KindToStore));
                names.Str(p.Name);

                long px = SharedPin.Hundredths(p.Pos.x);
                long py = SharedPin.Hundredths(p.Pos.y);
                long pz = SharedPin.Hundredths(p.Pos.z);
                x.Zig(px - lastX); lastX = px;
                y.Zig(py - lastY); lastY = py;
                z.Zig(pz - lastZ); lastZ = pz;

                flags.Byte((byte)(p.Checked ? 1 : 0));
                long madeAt = ToSeconds(p.Created);
                created.Var(madeAt);
                // Against its own creation rather than against the marker before it: a marker is
                // nearly always changed in the moment it is made, so this is usually a zero.
                modified.Zig(ToSeconds(p.Modified) - madeAt);
            }

            var tables = new ColumnWriter();
            owners.Write(tables);
            authors.Write(tables);
            sources.Write(tables);
            icons.Write(tables);
            kinds.Write(tables);

            pkg.Write(tables.Done());
            pkg.Write(ownerIndex.Done());
            pkg.Write(authorIndex.Done());
            pkg.Write(sourceIndex.Done());
            pkg.Write(iconIndex.Done());
            pkg.Write(kindIndex.Done());
            pkg.Write(names.Done());
            pkg.Write(x.Done());
            pkg.Write(y.Done());
            pkg.Write(z.Done());
            pkg.Write(flags.Done());
            pkg.Write(created.Done());
            pkg.Write(modified.Done());
            pkg.Write(Tombstones.Count);
            foreach (var t in Tombstones) { t.Key.Write(pkg); pkg.Write(t.Value); }
            pkg.Write(Suppressions.Count);
            foreach (var s in Suppressions) pkg.Write(Blob(s.Write));
            // Last, as its own section, so that it is an addition to the stream rather than a
            // change to the middle of it: a reader that does not know about it stops here and
            // still has a whole map.
            pkg.Write(LegacyTombstones.Count);
            foreach (var t in LegacyTombstones) { pkg.Write(t.Key); pkg.Write(t.Value); }
        }

        /// <summary>One record on its own, so a reader can step over what it does not understand.</summary>
        private static byte[] Blob(System.Action<ZPackage> write)
        {
            var inner = new ZPackage();
            write(inner);
            return inner.GetArray();
        }

        /// <summary>The markers of a format 7 map, a field at a time.</summary>
        private static void ReadColumns(ZPackage pkg, int n, MapStore store)
        {
            var tables = new ColumnReader(pkg.ReadByteArray());
            var owners = StringTable.Read(tables);
            var authors = StringTable.Read(tables);
            var sources = StringTable.Read(tables);
            var icons = StringTable.Read(tables);
            var kinds = StringTable.Read(tables);

            var ownerIndex = new ColumnReader(pkg.ReadByteArray());
            var authorIndex = new ColumnReader(pkg.ReadByteArray());
            var sourceIndex = new ColumnReader(pkg.ReadByteArray());
            var iconIndex = new ColumnReader(pkg.ReadByteArray());
            var kindIndex = new ColumnReader(pkg.ReadByteArray());
            var names = new ColumnReader(pkg.ReadByteArray());
            var x = new ColumnReader(pkg.ReadByteArray());
            var y = new ColumnReader(pkg.ReadByteArray());
            var z = new ColumnReader(pkg.ReadByteArray());
            var flags = new ColumnReader(pkg.ReadByteArray());
            var created = new ColumnReader(pkg.ReadByteArray());
            var modified = new ColumnReader(pkg.ReadByteArray());

            long lastX = 0, lastY = 0, lastZ = 0;
            for (int i = 0; i < n; i++)
            {
                var pin = new SharedPin();
                pin.OwnerId = Pick(owners, ownerIndex.Var(), "0") is string owner
                              && long.TryParse(owner, out long id) ? id : 0L;
                pin.Author = Pick(authors, authorIndex.Var(), "");
                pin.Name = names.Str();

                lastX += x.Zig(); lastY += y.Zig(); lastZ += z.Zig();
                pin.Pos = new Vector3(lastX / 100f, lastY / 100f, lastZ / 100f);

                pin.Source = Pick(sources, sourceIndex.Var(), "");
                pin.Icon = Pick(icons, iconIndex.Var(), "");
                pin.Kind = Pick(kinds, kindIndex.Var(), "");
                pin.Checked = (flags.Byte() & 1) != 0;
                long madeAt = created.Var();
                pin.Created = FromSeconds(madeAt);
                pin.Modified = FromSeconds(madeAt + modified.Zig());

                var key = pin.Key;
                if (key.IsNone) continue;
                if (store.Pins.TryGetValue(key, out var seen) && seen.Modified > pin.Modified) continue;
                store.Pins[key] = pin;
            }
        }

        private static string Pick(List<string> values, long at, string whenMissing) =>
            at >= 0 && at < values.Count ? values[(int)at] : whenMissing;

        /// <summary>The erasures and suppressions, which every version from 4 on holds alike.</summary>
        private static void ReadTail(ZPackage pkg, int version, MapStore store)
        {
            int t = pkg.ReadInt();
            for (int i = 0; i < t; i++)
            {
                var key = PinKey.Read(pkg);
                long at = pkg.ReadLong();
                if (!key.IsNone) store.Tombstones[key] = at;
            }
            int m = pkg.ReadInt();
            for (int i = 0; i < m; i++)
                store.Suppressions.Add(Suppression.Read(new ZPackage(pkg.ReadByteArray()), version));
            if (pkg.GetPos() >= pkg.Size()) return;
            int l = pkg.ReadInt();
            for (int i = 0; i < l; i++)
            {
                string id = pkg.ReadString();
                long at = pkg.ReadLong();
                if (!string.IsNullOrEmpty(id)) store.LegacyTombstones[id] = at;
            }
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
            var store = new MapStore { ReadVersion = version };
            int n = pkg.ReadInt();
            // 8 and up are columnar. 7 is a marker at a time like everything before it, and is
            // read by the loop below -- SharedPin.Read knows its shape.
            if (version >= 8)
            {
                ReadColumns(pkg, n, store);
                ReadTail(pkg, version, store);
                return store;
            }
            for (int i = 0; i < n; i++)
            {
                var p = version >= 5
                    ? SharedPin.Read(new ZPackage(pkg.ReadByteArray()), version)
                    : SharedPin.Read(pkg, version);
                // A marker written before the rekey is converted as it is read: its key comes out
                // of the owner, the position and the source it already carried. Two old markers
                // can land on one key only if they really were the same marker twice over, and
                // the later-modified one is the one to keep.
                var key = p.Key;
                if (key.IsNone) continue;
                if (store.Pins.ContainsKey(key)) store.Collapsed++;
                if (store.Pins.TryGetValue(key, out var seen) && seen.Modified > p.Modified) continue;
                store.Pins[key] = p;
            }
            if (version >= 4)
            {
                int t = pkg.ReadInt();
                for (int i = 0; i < t; i++)
                {
                    if (version >= 6)
                    {
                        var key = PinKey.Read(pkg);
                        long at = pkg.ReadLong();
                        if (!key.IsNone) store.Tombstones[key] = at;
                    }
                    else
                    {
                        // Keyed by the GUID of a marker that no longer exists, so there is nothing
                        // to convert it from. Carried as it is; see LegacyTombstones.
                        string id = pkg.ReadString();
                        long at = pkg.ReadLong();
                        if (!string.IsNullOrEmpty(id)) store.LegacyTombstones[id] = at;
                    }
                }
            }
            int m = pkg.ReadInt();
            for (int i = 0; i < m; i++)
                store.Suppressions.Add(version >= 5
                    ? Suppression.Read(new ZPackage(pkg.ReadByteArray()), version)
                    : Suppression.Read(pkg, version));
            if (version >= 6 && pkg.GetPos() < pkg.Size())
            {
                int l = pkg.ReadInt();
                for (int i = 0; i < l; i++)
                {
                    string id = pkg.ReadString();
                    long at = pkg.ReadLong();
                    if (!string.IsNullOrEmpty(id)) store.LegacyTombstones[id] = at;
                }
            }
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
