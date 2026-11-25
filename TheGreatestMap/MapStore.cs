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

        /// <summary>
        /// Markers an erasure turned away: one keyed the modern way, and one written before the
        /// rekey and keyed by the GUID the marker used to carry.
        ///
        /// Counted because erasures never retire and nothing knows when they could. 660 pre-rekey
        /// erasures are 21% of the live map and can only ever match a marker arriving from a
        /// personal map saved in format 5 or older; the blank-source ones left by folding are
        /// another 17 KB. Both are certainly dead at some point and there is no way to tell when.
        ///
        /// A horizon -- drop anything older than N months -- guesses at exactly the fact this
        /// number states. So state it: if no erasure of a kind turns anything away for weeks
        /// across a group that is playing normally, dropping that kind is a measurement rather
        /// than a gamble. If one does fire, that is the proof they are still earning their keep,
        /// and it says which marker and from whom.
        /// </summary>
        public int RefusedByErasure, RefusedByOldErasure;

        /// <summary>
        /// Markers not copied onto this map because this player does not record that kind, and
        /// which kinds they were.
        ///
        /// <para>Neither an erasure nor a judgement on the map they came from: a decision about
        /// what to write on this one. Counted and named because a merge that silently keeps
        /// nothing looks exactly like a merge that had nothing to give -- a player who wonders
        /// where another's markers went deserves to be told they were turned away, and by which
        /// switch.</para>
        /// </summary>
        public int RefusedByKind;

        public readonly Dictionary<string, int> RefusedKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<SharedPin> ChangedPins = new List<SharedPin>();
        public readonly List<Tombstone> NewTombstones = new List<Tombstone>();
        public readonly List<Suppression> NewSuppressions = new List<Suppression>();
        public bool Any => Added + Updated + Deleted + Suppressed > 0;

        /// <summary>Whether an erasure turned anything away, which is worth saying even when
        /// nothing else changed: it is the only sign that the erasures are still doing work.</summary>
        public bool Refused => RefusedByErasure + RefusedByOldErasure > 0;

        /// <summary>
        /// Whether anything was turned away for being a kind this map does not record. Kept out
        /// of <see cref="Any"/> on purpose: nothing changed here, so there is nothing to redraw
        /// or to pass on -- it is only worth saying out loud.
        /// </summary>
        public bool TurnedAway => RefusedByKind > 0;

        /// <summary>The kinds turned away and how many of each, for a line somebody reads.</summary>
        public string KindsTurnedAway()
        {
            // Sorted by the kind rather than by the count, so the same two kinds come out in
            // the same order every merge.
            var kinds = new List<string>(RefusedKinds.Keys);
            kinds.Sort(StringComparer.Ordinal);
            var said = new List<string>();
            foreach (var kind in kinds) said.Add(RefusedKinds[kind] + " " + kind);
            return string.Join(", ", said);
        }

        /// <summary>
        /// Take another merge's tally into this one, for a map that arrived in several messages.
        /// The lists are added to as well, so whatever is done with the whole merge at the end --
        /// forwarding it on, naming the markers as ours to pass up -- sees all of it.
        /// </summary>
        public void Absorb(MergeResult other)
        {
            if (other == null) return;
            Added += other.Added;
            Updated += other.Updated;
            Deleted += other.Deleted;
            Suppressed += other.Suppressed;
            RefusedByErasure += other.RefusedByErasure;
            RefusedByOldErasure += other.RefusedByOldErasure;
            RefusedByKind += other.RefusedByKind;
            foreach (var kv in other.RefusedKinds)
            {
                RefusedKinds.TryGetValue(kv.Key, out int had);
                RefusedKinds[kv.Key] = had + kv.Value;
            }
            ChangedPins.AddRange(other.ChangedPins);
            NewTombstones.AddRange(other.NewTombstones);
            NewSuppressions.AddRange(other.NewSuppressions);
        }

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
            if (RefusedByErasure > 0) parts.Add($"{RefusedByErasure} refused by an erasure");
            if (RefusedByOldErasure > 0) parts.Add($"{RefusedByOldErasure} refused by a pre-rekey erasure");
            if (RefusedByKind > 0)
                parts.Add(RefusedKinds.Count == 1
                    ? $"{KindsTurnedAway()} not copied"
                    : $"{RefusedByKind} not copied ({KindsTurnedAway()})");
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
        /// strings held once in a table and referred to by number, positions in centimeters as
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

        /// <summary>
        /// Told which key each edit touched, so the map that has to say what to send can say it
        /// rather than offering everything it holds.
        ///
        /// <para>Set on the personal map and nowhere else. The shared map on the server, the
        /// deltas and the stores read off the wire have nobody to report to.</para>
        ///
        /// <para>Here rather than at the callers because here is the only place that cannot be
        /// forgotten. Nineteen places change the personal map, most of them by setting a field
        /// on a marker and writing it back, and a change nobody reported is a marker that never
        /// reaches anyone -- a silence, with nothing to see at either end. Every one of them
        /// goes through <see cref="Upsert"/>, <see cref="Delete"/> or <see cref="Lift"/>.</para>
        ///
        /// <para><see cref="Merge"/> is the exception and writes the dictionaries directly, so
        /// it reports nothing. That is deliberate: what a merge brought in is news only when it
        /// came from another player, and only the caller knows which side it came from.</para>
        /// </summary>
        public Action<PinKey> OnChanged;

        /// <summary>The erased spots changed. There are a handful, so they travel together.</summary>
        public Action OnSuppressed;

        // ── local edits ─────────────────────────────────────────────────────────────

        public void Upsert(SharedPin pin)
        {
            pin.Modified = Now;
            Pins[pin.Key] = pin;
            Tombstones.Remove(pin.Key);
            OnChanged?.Invoke(pin.Key);
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
            OnChanged?.Invoke(key);
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
            // Reported, though nothing of it travels. The key has to come off the list of what
            // the server has not been told, or it sits there forever naming a marker that is
            // not in the map and not in the erasures, and every sync looks for it and finds
            // nothing.
            OnChanged?.Invoke(key);
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
        /// <summary>How old an erasure must be before the sweep will believe it is a ghost.</summary>
        public static readonly long GhostAge = TimeSpan.FromDays(1).Ticks;

        public int ForgetGhosts() => ForgetGhosts(Now - GhostAge);

        /// <summary>
        /// Drop ghosts older than a cutoff, and nothing newer.
        ///
        /// Age is the only thing separating a ghost from an erasure that still has work to do.
        /// Both look identical -- an erasure naming a place where a marker stands under another
        /// name -- because folding two markers into one produces exactly that, and the fold's
        /// erasure is how the loser's removal reaches the server. Sweeping it before it was sent
        /// gave the server its copy back to hand over again: 129 folded, 118 swept, 118 folded,
        /// for as long as anybody kept syncing.
        ///
        /// A fold's erasure goes up at the next sync, which is minutes at the outside. A day puts
        /// the cutoff far beyond that and still clears everything left by a migration months ago.
        /// </summary>
        public int ForgetGhosts(long before)
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
                if (kv.Value >= before) continue;          // too recent to be sure it has been sent
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

        // An erasure for a crop used to be forgotten at the next load, on the reasoning that a
        // planted turnip was never a discovery and that recording refuses to write a crop on
        // tilled ground anyway, so the erasure had nothing left to hold back.
        //
        // The second half of that is only true of your own recording. An erasure is also the
        // only thing that stops a marker somebody else already wrote down from being handed back
        // at the next merge, and forgetting it reopens that gate every single login: a player
        // cleared the crops off his base, found them back the next evening, cleared them again,
        // and the copy that kept returning belonged to another player who had walked past the
        // same spot a month earlier. There was no way for him to win, and nothing said why.
        //
        // So they stay. The cost is one tombstone per crop ever rubbed out -- 141 on the live
        // map, about 7 KB -- and it is not even permanent: the erasure only outranks a marker
        // older than itself, so a crop genuinely found there again later still comes through.

        /// <summary>A position as one number, for gathering what stands in the same spot.</summary>
        private static long Place(Vector3 pos) =>
            ((long)SharedPin.Hundredths(pos.x) << 32) ^ (uint)SharedPin.Hundredths(pos.z);

        public bool AddSuppression(Suppression s)
        {
            foreach (var existing in Suppressions)
                if (IconRegistry.SameKey(existing.Icon, s.Icon) && Geo.FlatDistance(existing.Pos, s.Pos) < 1f) return false;
            Suppressions.Add(s);
            OnSuppressed?.Invoke();
            return true;
        }

        // ── merging ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Take another map's markers and erasures into this one.
        ///
        /// <para><paramref name="refusedAs"/> names the kind of a marker this map will not copy,
        /// or returns null to take it in. Left out on the server, which holds the shared map and
        /// has no settings of its own to apply -- and must not apply any player's, or one
        /// person's switch would decide what the group's map is allowed to hold.</para>
        /// </summary>
        public MergeResult Merge(MapStore other, Func<SharedPin, string> refusedAs = null)
        {
            var result = new MergeResult();
            if (other == null) return result;

            foreach (var kv in other.Pins)
            {
                var theirs = kv.Value;
                var key = theirs.Key;
                if (key.IsNone) continue;
                if (Tombstones.TryGetValue(key, out long myTomb) && myTomb >= theirs.Modified)
                {
                    result.RefusedByErasure++;                 // erased here after their last change
                    continue;
                }
                // The same question asked of the erasures recorded before the rekey. A marker
                // coming from a map still in the old format carries the id it was written with,
                // and that id is the only thing those tombstones know it by.
                if (!string.IsNullOrEmpty(theirs.LegacyId)
                    && LegacyTombstones.TryGetValue(theirs.LegacyId, out long oldTomb)
                    && oldTomb >= theirs.Modified)
                {
                    result.RefusedByOldErasure++;
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] A pre-rekey erasure kept {theirs.Source ?? ""} at " +
                        $"{theirs.Pos} erased, for {theirs.Author}. These are the erasures that cannot be " +
                        "retired yet; this line is the evidence that they are still needed.");
                    continue;
                }
                // Not a kind this map writes down. Asked after the erasures because working
                // out a marker's kind costs more than either tombstone lookup, and asked at all
                // because copying a marker off a table or another player's map is the same act
                // as writing one down on seeing the thing itself: one decision, answered once,
                // wherever the marker came from.
                //
                // Turned away rather than erased. Nothing is written, no tombstone is made and
                // nothing travels, so the map it came from keeps its marker and this one simply
                // never had it -- which is also what makes the switch free to change its mind:
                // the next merge offers the same markers again.
                if (refusedAs != null)
                {
                    string refused = refusedAs(theirs);
                    if (refused != null)
                    {
                        result.RefusedByKind++;
                        result.RefusedKinds.TryGetValue(refused, out int had);
                        result.RefusedKinds[refused] = had + 1;
                        continue;
                    }
                }
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
                if (Pins.TryGetValue(key, out var mine))
                {
                    if (mine.Modified <= at)
                    {
                        Pins.Remove(key);
                        result.Deleted++;
                        result.NewTombstones.Add(new Tombstone { Key = key, DeletedAt = at });
                    }
                    else
                    {
                        // Our record is newer than their erasure, so the erasure has been taken
                        // back since -- and taking an erasure back has to mean something, or the
                        // undo would be a local illusion that the next merge quietly reversed.
                        //
                        // Keeping both was the old behavior and it was incoherent: the marker
                        // stood on the map and a tombstone for the same key sat beside it, which
                        // drew it in the erased view, blocked any later copy of it that happened
                        // to be older, and travelled onward as a claim we no longer believed.
                        // Nothing cleared it either -- ForgetGhosts only sweeps a tombstone that
                        // a *differently* sourced marker now stands on.
                        continue;
                    }
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
        /// Split this map, put the parcels back together into a fresh one, and say whether
        /// anything was lost or changed on the way.
        ///
        /// <para>Checked against the same map written and read whole rather than against this
        /// one, because a map on its way through the format loses what the format does not keep:
        /// positions land on the centimeter and times on the second. Comparing with those
        /// already applied to both sides leaves exactly one difference to look for, which is the
        /// one splitting could cause.</para>
        /// </summary>
        internal string CheckParcels()
        {
            var whole = FromBytes(ToBytes());
            var rebuilt = new MapStore();
            var parcels = Parcels();
            int bytes = 0;
            foreach (var parcel in parcels)
            {
                bytes += parcel.Length;
                rebuilt.Merge(Read(new ZPackage(Utils.Decompress(parcel))));
            }

            var faults = new List<string>();
            if (rebuilt.Pins.Count != whole.Pins.Count)
                faults.Add($"{whole.Pins.Count} markers came back as {rebuilt.Pins.Count}");
            foreach (var kv in whole.Pins)
            {
                SharedPin copy;
                if (!rebuilt.Pins.TryGetValue(kv.Key, out copy))
                { faults.Add($"a marker is missing: {kv.Value.Source} at {kv.Value.Pos}"); break; }
                if (copy.Modified != kv.Value.Modified || copy.Checked != kv.Value.Checked
                    || copy.Name != kv.Value.Name || copy.Icon != kv.Value.Icon)
                { faults.Add($"a marker came back changed: {kv.Value.Source} at {kv.Value.Pos}"); break; }
            }
            if (rebuilt.Tombstones.Count != whole.Tombstones.Count)
                faults.Add($"{whole.Tombstones.Count} erasures came back as {rebuilt.Tombstones.Count}");
            foreach (var kv in whole.Tombstones)
            {
                long at;
                if (!rebuilt.Tombstones.TryGetValue(kv.Key, out at) || at != kv.Value)
                { faults.Add($"an erasure is missing or redated: {kv.Key}"); break; }
            }
            if (rebuilt.LegacyTombstones.Count != whole.LegacyTombstones.Count)
                faults.Add($"{whole.LegacyTombstones.Count} pre-rekey erasures came back as {rebuilt.LegacyTombstones.Count}");
            if (rebuilt.Suppressions.Count != whole.Suppressions.Count)
                faults.Add($"{whole.Suppressions.Count} erased spots came back as {rebuilt.Suppressions.Count}");

            string size = $"{parcels.Count} message(s), {bytes / 1024f:0.#} KB in all";
            return faults.Count == 0
                ? $"Split and put back together with nothing changed: {size}."
                : $"Something went wrong splitting the map ({size}): " + string.Join("; ", faults.ToArray());
        }

        /// <summary>Write a payload that <see cref="Parcels"/> has already packed.</summary>
        public static void WriteParcel(ZPackage pkg, byte[] parcel)
        {
            pkg.Write(CompressedMark);
            pkg.Write(parcel);
        }

        /// <summary>
        /// How many records go in one parcel before it is cut, counting markers, erasures and
        /// suppressions alike. 10,000 markers is about 130 KB at the density measured on the live
        /// map, which is comfortably inside <see cref="MaxParcelBytes"/> and makes the byte check
        /// below a backstop rather than the thing doing the work.
        ///
        /// <para>Low on purpose. A parcel costs one message, one decompress and one merge, and
        /// the only things that send a whole map are a first login, a restore and a server that
        /// restarted -- so four comfortable messages beat one that is close to the edge. There is
        /// nothing to be won by filling a message up: the game pins its own send rate to 150 KB a
        /// second (<c>SendRateMin</c> and <c>SendRateMax</c>, both 153,600, set in
        /// <c>ZSteamSocket.RegisterGlobalCallbacks</c>), so the time a map takes to cross is
        /// decided by how big it is and not by how it is cut up.</para>
        /// </summary>
        public const int MaxParcelRecords = 10000;

        /// <summary>
        /// The size a parcel is held to, against Steam's 512 KB limit for one message.
        ///
        /// <para>A record count cannot be trusted on its own, and this is the point of this
        /// number. Thirteen bytes a marker is what <i>our</i> map compresses to, and ours has
        /// almost no labels, five authors and a narrow spread of prefabs -- all three of which
        /// deflate beautifully. Somebody else's map with thousands of typed labels and a dozen
        /// players costs more per marker and there is no way to bound by how much, so the count
        /// is a guess and this is the measurement: a parcel is packed, weighed, and cut in half
        /// again if it came out over.</para>
        ///
        /// <para>192 KB leaves two and a half times the headroom rather than a few percent,
        /// because of what going over costs: <c>SendMessageToConnection</c> refuses an oversized
        /// message every time it is tried, <c>ZSteamSocket.SendQueuedPackages</c> breaks on a
        /// refusal and leaves it at the head of the queue, and nothing behind it moves again --
        /// the server stops hearing from the client and closes the connection after thirty
        /// seconds. A refusal for a full send buffer recovers by itself, since the queue is
        /// retried every frame, but a message that is too big never becomes small enough.</para>
        /// </summary>
        public const int MaxParcelBytes = 192 * 1024;

        /// <summary>
        /// The record cap actually in force, so a map far smaller than the real one can be made
        /// to travel in pieces on demand -- <c>tgm_resync &lt;records&gt;</c>. Our own map is a
        /// third of <see cref="MaxParcelRecords"/>, so without this the splitting would first run
        /// on somebody else's map, which is no place to find out it was wrong.
        /// </summary>
        internal static int ParcelRecords = MaxParcelRecords;

        /// <summary>
        /// The map as one or more compressed payloads, each a complete map in its own right and
        /// each small enough to send.
        ///
        /// <para><b>Why each parcel is a whole map rather than a slice of one.</b> A map with
        /// fewer markers in it is a legal map, and <see cref="Merge"/> is additive: it never
        /// removes a marker for being absent, and <see cref="AddSuppression"/> turns a repeat
        /// into nothing. So the receiver merges each parcel as it comes and needs no sequence
        /// number, no reassembly buffer and no timeout to throw away a map that never finished
        /// arriving -- the markers that landed are simply on the map, and the next sync carries
        /// the rest. Cutting the compressed bytes into blocks instead would need all three, and
        /// a half-arrived map would be worth nothing.</para>
        ///
        /// <para><b>The order is not arbitrary, though.</b> Records are taken markers first, then
        /// erasures, then the erasures from before the rekey, then suppressions -- which is the
        /// order a single <see cref="Merge"/> reads one map in, so a map in four parcels is
        /// merged exactly as the same map in one message would be. It matters in one place: a
        /// marker changed since an erasure of the same key keeps both, the newer marker and the
        /// erasure that did not beat it, and a merge that met the erasure first would drop it
        /// instead. The channel delivers in the order it was sent, so this holds;
        /// <c>tgm_resync &lt;records&gt;</c> checks that it does.</para>
        ///
        /// <para>Nothing new goes on the wire either. Each parcel is an ordinary map message, so
        /// a build on either end that has never heard of any of this reads it as the map it
        /// is.</para>
        ///
        /// <para>Markers are taken in writing order, so the ones in a parcel are neighbours on
        /// the map and their positions stay cheap to delta-code -- cutting across that order
        /// would cost most of what the columnar format buys.</para>
        /// </summary>
        public List<byte[]> Parcels()
        {
            var pins = InWritingOrder();
            var tombs = new List<PinKey>(Tombstones.Keys);
            var legacy = new List<string>(LegacyTombstones.Keys);
            int total = pins.Count + tombs.Count + legacy.Count + Suppressions.Count;
            int cap = ParcelRecords > 0 ? ParcelRecords : MaxParcelRecords;

            var parcels = new List<byte[]>();
            int at = 0;
            // At least one parcel even for an empty map: an empty sync is still a question, and
            // the watermark that rides behind it is what fetches everyone else's markers.
            do
            {
                int count = Math.Min(cap, total - at);
                Fit(parcels, pins, tombs, legacy, at, count);
                at += count;
            } while (at < total);
            return parcels;
        }

        /// <summary>
        /// Pack one span of records, and if it came out over <see cref="MaxParcelBytes"/>, cut it
        /// in half and weigh the halves. Only a compressed map knows its own size, so measuring is
        /// the only way to find out and halving is the only answer that cannot loop.
        /// </summary>
        private void Fit(List<byte[]> into, List<SharedPin> pins, List<PinKey> tombs,
                         List<string> legacy, int at, int count)
        {
            byte[] packed = Utils.Compress(Slice(pins, tombs, legacy, at, count).ToBytes());
            if (packed.Length <= MaxParcelBytes || count <= 1)
            {
                if (packed.Length > MaxParcelBytes)
                    TheGreatestMapMod.Log.LogWarning(
                        $"[TheGreatestMap] One record of the map is {packed.Length / 1024f:0.#} KB by itself, " +
                        $"over the {MaxParcelBytes / 1024} KB a message is held to. Sending it as it is, " +
                        "there being nothing left to divide.");
                into.Add(packed);
                return;
            }
            int half = count / 2;
            Fit(into, pins, tombs, legacy, at, half);
            Fit(into, pins, tombs, legacy, at + half, count - half);
        }

        /// <summary>
        /// One span of this map as a map of its own. The four kinds of record are read as a single
        /// run -- markers, then erasures, then the pre-rekey erasures, then suppressions -- so a
        /// span is a position and a count, and cutting one in half is one subtraction.
        ///
        /// <para>The markers themselves are shared rather than copied: this is only ever written
        /// out, and the far end clones whatever it keeps.</para>
        /// </summary>
        private MapStore Slice(List<SharedPin> pins, List<PinKey> tombs, List<string> legacy, int at, int count)
        {
            var slice = new MapStore();
            int end = at + count;
            for (int i = at; i < end; i++)
            {
                int k = i;
                if (k < pins.Count) { var pin = pins[k]; slice.Pins[pin.Key] = pin; continue; }
                k -= pins.Count;
                if (k < tombs.Count) { var key = tombs[k]; slice.Tombstones[key] = Tombstones[key]; continue; }
                k -= tombs.Count;
                if (k < legacy.Count) { string id = legacy[k]; slice.LegacyTombstones[id] = LegacyTombstones[id]; continue; }
                k -= legacy.Count;
                if (k < Suppressions.Count) slice.Suppressions.Add(Suppressions[k]);
            }
            return slice;
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
