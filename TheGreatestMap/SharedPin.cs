using System;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>One shared map marker. The server's copy is authoritative.</summary>
    internal sealed class SharedPin
    {
        public string Id = "";
        public long OwnerId;
        public string Author = "";
        public string Name = "";
        public Vector3 Pos;
        public int Type;        // the creating client's local pin type; receivers recompute from Icon
        public string Icon = ""; // "item:Dandelion" or "pin:Boss" (see IconRegistry)
        public bool Checked;
        public bool Auto;       // recorded automatically (erasing it suppresses re-recording there)
        public long Created;    // DateTime.UtcNow.Ticks

        public static string NewId() => Guid.NewGuid().ToString("N");

        public void Write(ZPackage pkg)
        {
            pkg.Write(Id ?? "");
            pkg.Write(OwnerId);
            pkg.Write(Author ?? "");
            pkg.Write(Name ?? "");
            pkg.Write(Pos);
            pkg.Write(Type);
            pkg.Write(Icon ?? "");
            pkg.Write(Checked);
            pkg.Write(Auto);
            pkg.Write(Created);
        }

        public static SharedPin Read(ZPackage pkg, int version)
        {
            var pin = new SharedPin
            {
                Id      = pkg.ReadString(),
                OwnerId = pkg.ReadLong(),
                Author  = pkg.ReadString(),
                Name    = pkg.ReadString(),
                Pos     = pkg.ReadVector3(),
                Type    = pkg.ReadInt(),
            };
            pin.Icon    = version >= 2 ? pkg.ReadString() : IconRegistry.LegacyKey(pin.Type);
            pin.Checked = pkg.ReadBool();
            pin.Auto    = pkg.ReadBool();
            pin.Created = pkg.ReadLong();
            return pin;
        }
    }

    /// <summary>
    /// A spot where an auto-recorded marker was deliberately erased. The recorder will not put
    /// the same kind of marker back near it, which is what keeps erased markers erased.
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
