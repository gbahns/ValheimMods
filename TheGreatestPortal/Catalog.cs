using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>One portal as the server describes it to everyone.</summary>
    internal sealed class PortalInfo
    {
        public long Id;            // permanent id (PortalData.IdHash)
        public ZDOID ZdoId;        // this session's ZDOID
        public string Name = "";
        public Vector3 Pos;
        public Quaternion Rot = Quaternion.identity;
        public long TargetId;      // permanent id of the destination, 0 = none
        public bool AllowAllItems; // the prefab's TeleportWorld.m_allowAllItems
        public float ExitDistance = 1f;
        public string Prefab = "";
        public long Born;          // world second it was built, 0 = unknown
        public long UsedAt;        // world second someone last travelled through it, 0 = never
        public string UsedBy = ""; // and who that was
        public string BornBy = ""; // who built it, "" = not recorded
        public string UsedLog = ""; // everyone who has been through it, newest first (packed)
        public long OwnerId;       // the character that built it (vanilla's creator), 0 = unknown
        public bool Private;       // its owner has made it theirs alone
        public long OpenUntil;     // ... but is letting everyone in until this world second

        /// <summary>Whether this private portal is open to everyone just now.</summary>
        public bool OpenToAll => Private && PortalData.OpenToAllNow(OpenUntil);

        /// <summary>How long that has left to run, in seconds.</summary>
        public long OpenLeft => PortalData.OpenSecondsLeft(OpenUntil);

        /// <summary>Whether the server should keep this portal from everybody but its owner. A
        /// portal standing open is sent to everyone, which is the whole point of opening it.</summary>
        public bool HiddenFromOthers => Private && !PortalData.OpenToAllNow(OpenUntil);

        /// <summary>Whether this portal is this character's to make private, rename from afar and
        /// so on. A portal with no owner recorded is nobody's.</summary>
        public bool Mine
        {
            get
            {
                long me = PortalData.LocalPlayerId();
                return me != 0L && OwnerId == me;
            }
        }

        // "Portal" rather than "Unnamed portal": it is what the game and the other mods that draw
        // portals call one, and a map with both on it reads as two different things.
        public string DisplayName => string.IsNullOrEmpty(Name) ? "Portal" : Name;

        public void Write(ZPackage pkg)
        {
            pkg.Write(Id);
            pkg.Write(ZdoId);
            pkg.Write(Name ?? "");
            pkg.Write(Pos);
            pkg.Write(Rot);
            pkg.Write(TargetId);
            pkg.Write(AllowAllItems);
            pkg.Write(ExitDistance);
            pkg.Write(Prefab ?? "");
        }

        /// <summary>The later additions, written after every record rather than inside one.</summary>
        public void WriteExtra(ZPackage pkg)
        {
            pkg.Write(Born);
            pkg.Write(UsedAt);
            pkg.Write(UsedBy ?? "");
        }

        public void ReadExtra(ZPackage pkg)
        {
            Born = pkg.ReadLong();
            UsedAt = pkg.ReadLong();
            UsedBy = pkg.ReadString();
        }

        // A block of its own rather than a fourth field in the one above, for the same reason that
        // one is appended rather than folded into the records: a client that knows only the first
        // block stops at the end of it, and both sides still understand each other either way round.
        public void WriteMore(ZPackage pkg)
        {
            pkg.Write(BornBy ?? "");
            pkg.Write(UsedLog ?? "");
        }

        /// <summary>The owner and whether they have made it private. A private portal only ever
        /// reaches the client that may see it, so this block says "yours" more often than not.</summary>
        public void WriteOwner(ZPackage pkg)
        {
            pkg.Write(OwnerId);
            pkg.Write(Private);
        }

        public void ReadOwner(ZPackage pkg)
        {
            OwnerId = pkg.ReadLong();
            Private = pkg.ReadBool();
        }

        /// <summary>When a private portal that is standing open shuts again. An absolute world
        /// second rather than a countdown: the world clock is the server's, shared with every
        /// client, so one number means the same thing everywhere and does not go stale in
        /// transit or between one list and the next.</summary>
        public void WriteOpen(ZPackage pkg)
        {
            pkg.Write(OpenUntil);
        }

        public void ReadOpen(ZPackage pkg)
        {
            OpenUntil = pkg.ReadLong();
        }

        public void ReadMore(ZPackage pkg)
        {
            BornBy = pkg.ReadString();
            UsedLog = pkg.ReadString();
        }

        public static PortalInfo Read(ZPackage pkg)
        {
            return new PortalInfo
            {
                Id = pkg.ReadLong(),
                ZdoId = pkg.ReadZDOID(),
                Name = pkg.ReadString(),
                Pos = pkg.ReadVector3(),
                Rot = pkg.ReadQuaternion(),
                TargetId = pkg.ReadLong(),
                AllowAllItems = pkg.ReadBool(),
                ExitDistance = pkg.ReadSingle(),
                Prefab = pkg.ReadString(),
            };
        }
    }

    /// <summary>
    /// Every portal in the world, as last told by the server. Clients only hold the ZDOs of
    /// loaded areas, so the list of all portals (for the panel, the map and hover text) has to
    /// come from the server, which holds every ZDO. On a hosted game the host is the server and
    /// fills its own catalog directly.
    /// </summary>
    internal static class Catalog
    {
        private const int Version = 1;

        /// <summary>
        /// Version of the block appended after the records. It is appended rather than folded into
        /// them on purpose: a client built before it stops reading once it has its portals and
        /// never sees the extra bytes, so old and new still understand each other either way round.
        /// </summary>
        private const int Extra = 1;

        /// <summary>Version of the second block, appended after the first. Same bargain again.</summary>
        private const int More = 2;

        /// <summary>Version of the third block. Same bargain: a client that does not know it reads
        /// the two blocks it does know and stops, and sees every portal it is allowed to see.</summary>
        private const int Own = 3;

        /// <summary>Version of the fourth block. Same bargain once more: a 1.5.0 client reads the
        /// three it knows and stops, and sees a portal standing open as the plain portal it is.</summary>
        private const int Open = 4;

        private static readonly Dictionary<long, PortalInfo> _byId = new Dictionary<long, PortalInfo>();
        private static readonly List<PortalInfo> _all = new List<PortalInfo>();

        internal static IReadOnlyList<PortalInfo> All => _all;
        internal static int Count => _all.Count;
        internal static bool HasSnapshot { get; private set; }

        /// <summary>Raised on the client after a new snapshot has been applied.</summary>
        internal static event Action Changed;

        internal static PortalInfo Get(long id)
        {
            return id != 0L && _byId.TryGetValue(id, out var p) ? p : null;
        }

        internal static PortalInfo ByZdo(ZDOID id)
        {
            if (id == ZDOID.None) return null;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].ZdoId == id) return _all[i];
            return null;
        }

        internal static PortalInfo ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < _all.Count; i++)
                if (string.Equals(_all[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _all[i];
            return null;
        }

        internal static void Clear()
        {
            _byId.Clear();
            _all.Clear();
            HasSnapshot = false;
        }

        internal static void Apply(ZPackage pkg)
        {
            int version = pkg.ReadInt();
            if (version != Version)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Ignoring a portal list of format {version}; this build speaks {Version}.");
                return;
            }
            int count = pkg.ReadInt();
            _byId.Clear();
            _all.Clear();
            var order = new List<PortalInfo>(count);   // including any dropped, so the extras line up
            for (int i = 0; i < count; i++)
            {
                var p = PortalInfo.Read(pkg);
                order.Add(p);
                if (p.Id == 0L || _byId.ContainsKey(p.Id)) continue;
                _byId[p.Id] = p;
                _all.Add(p);
            }
            if (pkg.GetPos() < pkg.Size() && pkg.ReadInt() == Extra)
            {
                for (int i = 0; i < order.Count && pkg.GetPos() < pkg.Size(); i++) order[i].ReadExtra(pkg);
                if (pkg.GetPos() < pkg.Size() && pkg.ReadInt() == More)
                {
                    for (int i = 0; i < order.Count && pkg.GetPos() < pkg.Size(); i++) order[i].ReadMore(pkg);
                    if (pkg.GetPos() < pkg.Size() && pkg.ReadInt() == Own)
                    {
                        for (int i = 0; i < order.Count && pkg.GetPos() < pkg.Size(); i++) order[i].ReadOwner(pkg);
                        if (pkg.GetPos() < pkg.Size() && pkg.ReadInt() == Open)
                            for (int i = 0; i < order.Count && pkg.GetPos() < pkg.Size(); i++) order[i].ReadOpen(pkg);
                    }
                }
            }
            HasSnapshot = true;
            try { Changed?.Invoke(); }
            catch (Exception e) { TheGreatestPortalMod.Log.LogError($"[TheGreatestPortal] Catalog listener failed: {e}"); }
        }

        /// <summary>Serialises a list of infos in the order given (server side).</summary>
        internal static ZPackage Pack(List<PortalInfo> infos)
        {
            var pkg = new ZPackage();
            pkg.Write(Version);
            pkg.Write(infos.Count);
            for (int i = 0; i < infos.Count; i++) infos[i].Write(pkg);
            pkg.Write(Extra);
            for (int i = 0; i < infos.Count; i++) infos[i].WriteExtra(pkg);
            pkg.Write(More);
            for (int i = 0; i < infos.Count; i++) infos[i].WriteMore(pkg);
            pkg.Write(Own);
            for (int i = 0; i < infos.Count; i++) infos[i].WriteOwner(pkg);
            pkg.Write(Open);
            for (int i = 0; i < infos.Count; i++) infos[i].WriteOpen(pkg);
            return pkg;
        }

        // ── prefab facts (server and client) ───────────────────────────────────────

        private struct PrefabFacts { public bool AllowAll; public float Exit; public string Name; }
        private static readonly Dictionary<int, PrefabFacts> _prefabs = new Dictionary<int, PrefabFacts>();

        internal static void PrefabFor(int hash, out bool allowAll, out float exitDistance, out string name)
        {
            if (!_prefabs.TryGetValue(hash, out var f))
            {
                f = new PrefabFacts { AllowAll = false, Exit = 1f, Name = "" };
                var scene = ZNetScene.instance;
                var prefab = scene != null ? scene.GetPrefab(hash) : null;
                if (prefab != null)
                {
                    f.Name = prefab.name;
                    var tw = prefab.GetComponent<TeleportWorld>();
                    if (tw != null) { f.AllowAll = tw.m_allowAllItems; f.Exit = tw.m_exitDistance; }
                }
                _prefabs[hash] = f;
            }
            allowAll = f.AllowAll;
            exitDistance = f.Exit;
            name = f.Name;
        }

        internal static void ResetPrefabCache() => _prefabs.Clear();
    }
}
