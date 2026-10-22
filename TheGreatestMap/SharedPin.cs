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
        public int Type;        // the creating client's local pin type; receivers recompute from Icon
        public string Icon = ""; // "item:Dandelion" or "pin:Boss" (see IconRegistry)
        public string Kind = ""; // Category name for recorded markers ("Herbs"); "" for placed ones
        // What the marker stands for: the prefab the catalog matched when it was recorded
        // ("SunkenCrypt4"), empty for one placed by hand. The icon above is how it was drawn at
        // the time; this is what it IS, which is the part that does not change when somebody
        // decides a draugr trophy suits a sunken crypt better than a swamp key.
        public string Source = "";
        public bool Checked;
        public bool Auto;       // recorded automatically (erasing it suppresses re-recording there)
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
                OwnerId = OwnerId, Author = Author, Name = Name, Pos = Pos, Type = Type, Icon = Icon,
                Kind = Kind, Checked = Checked, Auto = Auto, Created = Created, Modified = Modified,
                Source = Source, LegacyId = LegacyId,
            };
        }

        public void Write(ZPackage pkg)
        {
            pkg.Write(OwnerId);
            pkg.Write(Author ?? "");
            pkg.Write(Name ?? "");
            pkg.Write(Pos);
            pkg.Write(Type);
            pkg.Write(Icon ?? "");
            pkg.Write(Kind ?? "");
            pkg.Write(Checked);
            pkg.Write(Auto);
            pkg.Write(Created);
            pkg.Write(Modified);
            pkg.Write(Source ?? "");
        }

        public static SharedPin Read(ZPackage pkg, int version)
        {
            // Up to 5 a marker led with the GUID it was written under. From 6 it does not have
            // one: which marker this is comes out of the owner, the position and the source,
            // which were all in the record already. The old id is still read, because a marker
            // arriving from an older map needs it to be matched against tombstones written
            // before the rekey.
            var pin = new SharedPin
            {
                LegacyId = version < 6 ? pkg.ReadString() : "",
                OwnerId = pkg.ReadLong(),
                Author  = pkg.ReadString(),
                Name    = pkg.ReadString(),
                Pos     = pkg.ReadVector3(),
                Type    = pkg.ReadInt(),
            };
            pin.Icon     = version >= 2 ? pkg.ReadString() : IconRegistry.LegacyKey(pin.Type);
            pin.Kind     = version >= 3 ? pkg.ReadString() : "";
            pin.Checked  = pkg.ReadBool();
            pin.Auto     = pkg.ReadBool();
            pin.Created  = pkg.ReadLong();
            pin.Modified = version >= 4 ? pkg.ReadLong() : pin.Created;
            // What the thing is, as opposed to the picture standing for it. Guarded on what is
            // left in the package rather than on the version alone, so a marker written by a
            // later version that has grown another field still reads correctly up to here.
            pin.Source = version >= 5 && pkg.GetPos() < pkg.Size() ? pkg.ReadString() : "";
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
