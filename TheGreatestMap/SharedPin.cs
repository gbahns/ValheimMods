using System;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>One map marker, on a personal map or the shared one. "Later change wins" when maps merge.</summary>
    internal sealed class SharedPin
    {
        public long OwnerId;
        public string Author = "";
        public string Name = "";
        public Vector3 Pos;
        // ── what is stored, and what is worked out again ───────────────────────────
        //
        // From format 7 the record holds only what nothing else can supply. Icon, Kind, Type and
        // Auto all follow from Source, so storing them is keeping a second copy of an answer the
        // record already gives -- and a second copy is one that can disagree. It did: a stored
        // Kind of "Camp" on forty-two greydwarf nests outlived the catalog moving nests out of
        // camps, and nothing noticed, because the stale copy was the one being read.
        //
        // They are all still readable here. Only the written record loses them: in memory a
        // marker answers every question it used to, so the hundred-odd places that ask go on
        // asking and none of them had to change.

        private string _icon = "";
        private string _kindText = "";
        private string _source = "";

        /// <summary>
        /// What the marker stands for: the prefab the catalog matched when it was recorded
        /// ("SunkenCrypt4"), "Manual" for one a player placed, "kind:Runestone" where the kind is
        /// the whole answer, and empty only for one nothing has identified yet. Part of the key.
        /// </summary>
        public string Source
        {
            get { return _source; }
            set { _source = value ?? ""; _kindParsed = false; _kind = null; }
        }

        /// <summary>
        /// How this is drawn. Written down only where it cannot be derived: a marker placed by
        /// hand, whose icon is the player's own choice, and one whose source names something no
        /// catalog line covers because a prefab left the lists or its mod was uninstalled.
        /// </summary>
        public string Icon
        {
            get
            {
                if (!string.IsNullOrEmpty(_icon)) return _icon;
                string derived = Catalog.IconFor(_source);
                return string.IsNullOrEmpty(derived) ? "" : derived;
            }
            set { _icon = value ?? ""; }
        }

        /// <summary>The icon as actually written down, empty where it is left to be derived.</summary>
        public string StoredIcon { get { return _icon; } }

        /// <summary>
        /// The kind this is filed under, from the source through the catalog -- so moving a
        /// prefab from one list to another refiles every marker of it at once, rather than
        /// needing a pass to rewrite them all and leaving the ones it missed saying the old thing.
        /// </summary>
        public string Kind
        {
            get
            {
                if (!string.IsNullOrEmpty(_kindText)) return _kindText;
                if (IsKindOnly(_source)) return _source.Substring(5);
                var cat = Catalog.CategoryOf(_source);
                return cat.HasValue ? cat.Value.ToString() : "";
            }
            set { _kindText = value ?? ""; _kindParsed = false; _kind = null; }
        }

        /// <summary>The kind as actually written down, empty where it is left to be derived.</summary>
        public string StoredKind { get { return _kindText; } }

        /// <summary>The creating client's local pin type, worked out from the icon.</summary>
        public int Type { get { return IconRegistry.TypeFor(Icon); } }

        /// <summary>
        /// Recorded by the mod rather than put down by a player, which the source says: a marker
        /// somebody placed says "Manual" and everything else was recorded.
        /// </summary>
        public bool Auto { get { return !string.Equals(_source, Manual, StringComparison.Ordinal); } }

        public bool Checked;
        public long Created;    // DateTime.UtcNow.Ticks
        public long Modified;   // DateTime.UtcNow.Ticks of the last change; decides merges

        /// <summary>
        /// The GUID this marker was written with, for a marker read from format 4 or older. Not
        /// written back out and not what identifies the marker any more -- kept only so a merge
        /// can ask whether that old id was tombstoned before the tombstones were rekeyed.
        /// </summary>
        public string LegacyId = "";

        /// <summary>
        /// Which marker this is. Derived rather than stored; see <see cref="PinKey"/>. Recomputed
        /// on each call because the alternative is a cached copy going stale behind a field that
        /// was assigned directly, and a key that disagrees with its own record is the one failure
        /// here that loses markers silently.
        /// </summary>
        public PinKey Key => new PinKey(OwnerId, Pos, Source);

        private Category? _kind;
        private bool _kindParsed;

        /// <summary>The category this marker was recorded under, if known.</summary>
        public Category? KindCategory
        {
            get
            {
                if (!_kindParsed)
                {
                    _kindParsed = true;
                    if (!string.IsNullOrEmpty(Kind) && Enum.TryParse(Kind, true, out Category parsed)) _kind = parsed;
                }
                return _kind;
            }
        }

        /// <summary>
        /// What <see cref="Source"/> says for a marker a player placed themselves. Not a prefab
        /// and never matched against the catalog: it means "a person put this here", which is a
        /// complete answer to what the marker stands for, and a different answer from the blank
        /// left by a marker written before the question was asked.
        /// </summary>
        public const string Manual = "Manual";

        /// <summary>
        /// What <see cref="Source"/> says for a marker whose kind is the whole answer: a
        /// runestone is a runestone and the catalog has no list of kinds of them, so there is
        /// nothing further to work out. Written "kind:Runestone", in the same shape as the icon
        /// keys, because no prefab can be called that -- and Valheim really does have a prefab
        /// named "portal", so a bare kind name here would have been a trap waiting.
        ///
        /// It exists so that blank means one thing. While blank meant both "nobody has worked
        /// this out" and "there is nothing to work out", every rule that read it was guessing,
        /// and one of them -- which copy of a marker survives a fold -- threw away 325 markers'
        /// identities in an evening before anybody noticed.
        /// </summary>
        public static string KindOnly(Category kind) => "kind:" + kind;

        /// <summary>True for a source that names a kind rather than a prefab.</summary>
        public static bool IsKindOnly(string source) =>
            !string.IsNullOrEmpty(source) && source.StartsWith("kind:", StringComparison.Ordinal);

        /// <summary>Set the kind (for markers stored before kinds existed) and forget the cached parse.</summary>
        public void SetKind(string kind)
        {
            Kind = kind ?? "";
            _kindParsed = false;
            _kind = null;
        }

        public SharedPin Clone()
        {
            return new SharedPin
            {
                OwnerId = OwnerId, Author = Author, Name = Name, Pos = Pos,
                Source = Source, Checked = Checked, Created = Created, Modified = Modified,
                LegacyId = LegacyId,
                // The stored halves, not the derived ones: copying Icon through its getter would
                // bake a derived answer into the copy and quietly make it a thing written down.
                Icon = StoredIcon, Kind = StoredKind,
            };
        }

        /// <summary>
        /// Format 7. Four fields are gone -- Type, Auto, and Icon and Kind wherever they can be
        /// worked out again -- and Source has moved up, because everything else now leans on it.
        ///
        /// Icon and Kind are written only when deriving them would not give back what the record
        /// holds. That check is made rather than assumed, which is what keeps a map readable
        /// after a prefab leaves the catalog or a mod is uninstalled: derivation then answers
        /// nothing, the stored value differs, and it is written.
        /// </summary>
        public void Write(ZPackage pkg)
        {
            pkg.Write(OwnerId);
            pkg.Write(Author ?? "");
            pkg.Write(Name ?? "");
            pkg.Write(Pos);
            pkg.Write(Source ?? "");
            pkg.Write(Keep(_icon, Catalog.IconFor(_source)));
            pkg.Write(Keep(_kindText, DerivedKind()));
            pkg.Write(Checked);
            pkg.Write(Created);
            pkg.Write(Modified);
        }

        /// <summary>What to write for a field that can usually be worked out: nothing, usually.</summary>
        private static string Keep(string stored, string derived)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            return string.Equals(stored, derived, StringComparison.Ordinal) ? "" : stored;
        }

        private string DerivedKind()
        {
            if (IsKindOnly(_source)) return _source.Substring(5);
            var cat = Catalog.CategoryOf(_source);
            if (cat.HasValue) return cat.Value.ToString();
            // Nothing in the catalog answers for a marker somebody placed, but its icon does --
            // the player chose a kind and got that kind's icon, so the icon leads back. This is
            // the same step ClientPins.KindOf takes when a marker arrives without a kind; taking
            // it here too means the writer stops keeping a copy of what the reader works out.
            var fromIcon = KindInference.FromIcon(Icon);
            return fromIcon.HasValue ? fromIcon.Value.ToString() : "";
        }

        public static SharedPin Read(ZPackage pkg, int version)
        {
            // Up to 5 a marker led with the GUID it was written under. From 6 it does not have
            // one: which marker this is comes out of the owner, the position and the source,
            // which were all in the record already. The old id is still read, because a marker
            // arriving from an older map needs it to be matched against tombstones written
            // before the rekey.
            if (version >= 7)
            {
                var fresh = new SharedPin
                {
                    OwnerId = pkg.ReadLong(),
                    Author  = pkg.ReadString(),
                    Name    = pkg.ReadString(),
                    Pos     = pkg.ReadVector3(),
                    Source  = pkg.ReadString(),
                };
                fresh.Icon     = pkg.ReadString();
                fresh.Kind     = pkg.ReadString();
                fresh.Checked  = pkg.ReadBool();
                fresh.Created  = pkg.ReadLong();
                fresh.Modified = pkg.ReadLong();
                return fresh;
            }

            var pin = new SharedPin
            {
                LegacyId = version < 6 ? pkg.ReadString() : "",
                OwnerId = pkg.ReadLong(),
                Author  = pkg.ReadString(),
                Name    = pkg.ReadString(),
                Pos     = pkg.ReadVector3(),
            };
            int type     = pkg.ReadInt();
            pin.Icon     = version >= 2 ? pkg.ReadString() : IconRegistry.LegacyKey(type);
            pin.Kind     = version >= 3 ? pkg.ReadString() : "";
            pin.Checked  = pkg.ReadBool();
            bool auto    = pkg.ReadBool();
            pin.Created  = pkg.ReadLong();
            pin.Modified = version >= 4 ? pkg.ReadLong() : pin.Created;
            // What the thing is, as opposed to the picture standing for it. Guarded on what is
            // left in the package rather than on the version alone, so a marker written by a
            // later version that has grown another field still reads correctly up to here.
            pin.Source = version >= 5 && pkg.GetPos() < pkg.Size() ? pkg.ReadString() : "";
            // Before 4.0.0 a marker placed by hand said so in a flag of its own and left the
            // source empty. The flag is gone and the source carries it, so an old hand-placed
            // marker is given the word now -- otherwise it would read as recorded-but-unknown,
            // and be asked about by every repair from here on.
            if (!auto && string.IsNullOrEmpty(pin.Source)) pin.Source = Manual;
            return pin;
        }
    }

    /// <summary>
    /// A spot where an auto-recorded marker was deliberately erased. The recorder will not put
    /// the same kind of marker back near it.
    /// </summary>
    internal sealed class Suppression
    {
        public int Type;
        public string Icon = "";
        public Vector3 Pos;
        public string Name = "";

        public void Write(ZPackage pkg)
        {
            pkg.Write(Type);
            pkg.Write(Icon ?? "");
            pkg.Write(Pos);
            pkg.Write(Name ?? "");
        }

        public static Suppression Read(ZPackage pkg, int version)
        {
            var s = new Suppression { Type = pkg.ReadInt() };
            s.Icon = version >= 2 ? pkg.ReadString() : IconRegistry.LegacyKey(s.Type);
            s.Pos = pkg.ReadVector3();
            s.Name = pkg.ReadString();
            return s;
        }
    }

    internal static class Geo
    {
        /// <summary>Horizontal distance; dungeons and their interiors share x/z but not y.</summary>
        internal static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
