using System;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// What makes a marker that marker: who wrote it, where it stands, and what it is.
    ///
    /// Markers used to carry a GUID, made fresh on the machine that wrote them. It worked, and it
    /// was the single most expensive thing on the map -- thirty-three bytes a record, a quarter of
    /// the file, and the one field compression could do nothing with, because random bytes are
    /// exactly what a compressor has no answer for. Dropping it halves what crosses the wire.
    ///
    /// Nothing is lost by dropping it, because a marker was never really identified by it. The
    /// world is the same world for everybody: a copper deposit stands at one position for every
    /// player who walks past it, so the owner, the position and the thing itself already say which
    /// marker this is, and they say it the same way on every machine. A made-up number was only
    /// ever a way of writing that down.
    ///
    /// Three parts, and each is there for a reason:
    ///
    /// - <b>Owner</b>, because two players who find the same deposit each keep their own marker of
    ///   it. Without this their markers would be one marker, and erasing yours would erase theirs.
    /// - <b>Pos</b>, because position is what a marker is about, and it is identical everywhere.
    /// - <b>Source</b>, what the thing is, because two different things really do stand at one
    ///   point: a sunken crypt and a skeleton trophy at (-885.46, 60.29, -776.93) on the live map,
    ///   copper and silver in one rock at (1664.95, 299.63, 994.69). Eight such pairs today.
    ///   Without this third part each pair would collapse into one and a discovery would be lost.
    ///
    /// Source also makes recording idempotent, which is worth as much as the collisions it avoids:
    /// recording the same thing at the same place twice writes the same key twice, so it is one
    /// marker. A ledger lost across a relog can no longer leave a duplicate behind.
    ///
    /// <para><b>The key must not change under a marker.</b> Owner and position never do. Source is
    /// written once, when the marker is recorded -- except for the markers written before Source
    /// existed, which tgm_identify fills in, and the rare wrong answer it takes back. Both go
    /// through <see cref="ClientPins.Reidentify"/>, which erases the old marker properly and
    /// writes a new one, because under this scheme that is honestly what it is.</para>
    /// </summary>
    internal readonly struct PinKey : IEquatable<PinKey>, IComparable<PinKey>
    {
        public readonly long Owner;
        public readonly Vector3 Pos;

        /// <summary>The prefab this marker stands for; empty for one placed by hand.</summary>
        public readonly string Source;

        private readonly int _hash;

        public PinKey(long owner, Vector3 pos, string source)
        {
            Owner = owner;
            Pos = new Vector3(Grid(pos.x), Grid(pos.y), Grid(pos.z));
            Source = source ?? "";
            unchecked
            {
                int h = Owner.GetHashCode();
                h = (h * 397) ^ Pos.x.GetHashCode();
                h = (h * 397) ^ Pos.y.GetHashCode();
                h = (h * 397) ^ Pos.z.GetHashCode();
                h = (h * 397) ^ StringComparer.Ordinal.GetHashCode(Source);
                _hash = h;
            }
        }

        /// <summary>
        /// A coordinate as the key sees it: on the centimetre grid, and never negative zero.
        ///
        /// Both halves matter, and the first was learned the hard way. A marker snaps its own
        /// position when it is set, so a pin stands at -363.56; an erasure read from a map
        /// written before the grid existed still carries -363.55914306640625. Equal markers,
        /// unequal keys -- which meant an erasure from an older map matched nothing, and the
        /// pass that drops erasures made obsolete by a rename found 4,401 of them and removed
        /// none. Worse, it meant a deletion written before 4.3.0 would silently stop travelling,
        /// and an erased marker coming back is the one thing this mod was built to prevent.
        ///
        /// The position has to be put on the grid in the same place for everyone who asks what a
        /// marker is, and this is that place: a key made from a pin and a key read off the wire
        /// now agree by construction rather than by both happening to have been rounded already.
        ///
        /// Negative zero is the older half: it equals positive zero but need not hash alike,
        /// which would put two equal keys in different buckets and lose one of them.
        /// </summary>
        private static float Grid(float v)
        {
            float snapped = SharedPin.Snap(v);
            return snapped == 0f ? 0f : snapped;
        }

        public PinKey(SharedPin pin) : this(pin.OwnerId, pin.Pos, pin.Source) { }

        /// <summary>No owner, no marker: the one value that can never belong to a real marker.</summary>
        public static readonly PinKey None = default(PinKey);

        public bool IsNone => Owner == 0L;

        public bool Equals(PinKey other)
        {
            // Component by component, deliberately. Unity's Vector3 == is an approximate compare
            // -- it calls two positions equal when they are within about a hundredth of a
            // millimeter -- which is a reasonable thing for geometry and a disastrous one for a
            // dictionary key, since it is not transitive and would make lookups depend on which
            // marker happened to be asked about first.
            return Owner == other.Owner
                && Pos.x == other.Pos.x && Pos.y == other.Pos.y && Pos.z == other.Pos.z
                && string.Equals(Source, other.Source, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is PinKey other && Equals(other);

        public override int GetHashCode() => _hash;

        /// <summary>A stable order, so every machine picks the same winner among equals.</summary>
        public int CompareTo(PinKey other)
        {
            int c = Owner.CompareTo(other.Owner);
            if (c != 0) return c;
            c = Pos.x.CompareTo(other.Pos.x); if (c != 0) return c;
            c = Pos.y.CompareTo(other.Pos.y); if (c != 0) return c;
            c = Pos.z.CompareTo(other.Pos.z); if (c != 0) return c;
            return string.CompareOrdinal(Source ?? "", other.Source ?? "");
        }

        /// <summary>Short enough for a log line, exact enough to find the marker again.</summary>
        public override string ToString() =>
            Owner.ToString("x") + "@" + Pos.x.ToString("0.##") + "," + Pos.z.ToString("0.##")
            + (string.IsNullOrEmpty(Source) ? "" : "/" + Source);

        /// <summary>
        /// The key as text that reads back exactly, for the few places a key has to live in a
        /// string: the hidden-marker list kept in the character file, and nothing else.
        ///
        /// The positions go as their raw bits rather than as decimals. A float printed and parsed
        /// back is not reliably the same float, and printing one at all drags in the player's
        /// locale, where a decimal comma would quietly make every saved key unreadable on a
        /// machine set to a different language. Bits have neither problem.
        /// </summary>
        public string Encode()
        {
            return Owner.ToString("x") + "|" + Bits(Pos.x).ToString("x8") + "|"
                 + Bits(Pos.y).ToString("x8") + "|" + Bits(Pos.z).ToString("x8") + "|" + Source;
        }

        public static bool TryDecode(string text, out PinKey key)
        {
            key = None;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split('|');
            if (parts.Length < 5) return false;
            if (!long.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber,
                               System.Globalization.CultureInfo.InvariantCulture, out long owner)) return false;
            var pos = new Vector3();
            for (int i = 0; i < 3; i++)
            {
                if (!uint.TryParse(parts[i + 1], System.Globalization.NumberStyles.HexNumber,
                                   System.Globalization.CultureInfo.InvariantCulture, out uint bits)) return false;
                float v = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                if (i == 0) pos.x = v; else if (i == 1) pos.y = v; else pos.z = v;
            }
            // A source with a bar in it would have been split; prefab names have none, and
            // joining the rest back is the forgiving reading rather than the clever one.
            key = new PinKey(owner, pos, string.Join("|", parts, 4, parts.Length - 4));
            return !key.IsNone;
        }

        private static uint Bits(float v) => BitConverter.ToUInt32(BitConverter.GetBytes(v), 0);

        public void Write(ZPackage pkg)
        {
            pkg.Write(Owner);
            pkg.Write(Pos);
            pkg.Write(Source ?? "");
        }

        public static PinKey Read(ZPackage pkg)
        {
            long owner = pkg.ReadLong();
            Vector3 pos = pkg.ReadVector3();
            return new PinKey(owner, pos, pkg.ReadString());
        }
    }
}
