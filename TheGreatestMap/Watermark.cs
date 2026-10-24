using System.Collections.Generic;

namespace TheGreatestMap
{
    /// <summary>
    /// What the server has changed since a client last heard from it, so a sync can carry the
    /// changes instead of the map.
    ///
    /// <para><b>Why the ordering is the server's and not the markers' own.</b> Every marker
    /// already carries <c>Modified</c>, and "send me everything modified after tick N" is the
    /// obvious design. It is also unsafe here: <c>Modified</c> is wall-clock from whichever
    /// machine made the change, and machines disagree. A player whose clock runs a few minutes
    /// slow records a marker stamped in the past; every peer whose watermark has already passed
    /// that moment asks for changes *after* it and is never sent one. The marker does not arrive,
    /// nothing reports an error, and the map is quietly short a discovery -- which is the single
    /// failure this mod exists to prevent. So the number that decides what to send is counted by
    /// the server, one per change it accepts, and no clock enters into it.</para>
    ///
    /// <para><b>Why it is not written to disk.</b> The counter and the session id live only as
    /// long as the server process. A restarted server issues a new session, every client's
    /// watermark is recognised as stale, and each gets one full map the first time it syncs --
    /// after which the session matches and the rest are deltas. Persisting it would save that one
    /// message per player per server restart, at the cost of a stored file that is catastrophic
    /// rather than merely wasteful when it is wrong: a watermark that claims a client has
    /// something it does not means markers that are never sent and never missed. The cheap,
    /// self-correcting version is the right one until the expensive one is worth something.</para>
    ///
    /// <para>Every unknown answers "send everything": a session that does not match, a watermark
    /// from the future, a client too old to send one at all. Falling back costs one full map;
    /// guessing costs a marker.</para>
    /// </summary>
    internal sealed class Watermark
    {
        /// <summary>This server's numbering. A client's watermark means nothing under another.</summary>
        internal long Session { get; private set; }

        /// <summary>Changes accepted so far. Only ever goes up, within a session.</summary>
        internal long Seq { get; private set; }

        private readonly Dictionary<PinKey, long> _changed = new Dictionary<PinKey, long>();
        private readonly Dictionary<PinKey, long> _erased = new Dictionary<PinKey, long>();

        internal Watermark()
        {
            // Distinct per process. The clock is fine for this -- it identifies a run, it does
            // not order anything.
            Session = System.DateTime.UtcNow.Ticks ^ System.Environment.TickCount;
            if (Session == 0L) Session = 1L;
        }

        /// <summary>Note everything a merge changed, so a later sync can find it again.</summary>
        internal void Record(MergeResult result)
        {
            if (result == null || !result.Any) return;
            foreach (var pin in result.ChangedPins)
            {
                Seq++;
                _changed[pin.Key] = Seq;
                _erased.Remove(pin.Key);
            }
            foreach (var t in result.NewTombstones)
            {
                Seq++;
                _erased[t.Key] = Seq;
                _changed.Remove(t.Key);
            }
        }

        /// <summary>
        /// Whether a client's claim can be honored. Anything else means send the whole map.
        /// </summary>
        internal bool Knows(long session, long seq) =>
            session == Session && seq >= 0L && seq <= Seq;

        /// <summary>
        /// What changed after <paramref name="seq"/>, as a store that can be merged like any
        /// other.
        ///
        /// Suppressions go whole every time. There are seventeen of them on the live map, 614
        /// bytes, and they carry no key to track them by -- tracking would cost more than
        /// sending.
        /// </summary>
        internal MapStore Since(long seq, MapStore store)
        {
            var delta = new MapStore();
            foreach (var kv in _changed)
            {
                if (kv.Value <= seq) continue;
                if (store.Pins.TryGetValue(kv.Key, out var pin)) delta.Pins[kv.Key] = pin.Clone();
            }
            foreach (var kv in _erased)
            {
                if (kv.Value <= seq) continue;
                if (store.Tombstones.TryGetValue(kv.Key, out long at)) delta.Tombstones[kv.Key] = at;
            }
            delta.Suppressions.AddRange(store.Suppressions);
            return delta;
        }
    }
}
