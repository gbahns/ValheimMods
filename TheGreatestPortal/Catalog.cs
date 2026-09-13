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

        public string DisplayName => string.IsNullOrEmpty(Name) ? "Unnamed portal" : Name;

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
            for (int i = 0; i < count; i++)
            {
                var p = PortalInfo.Read(pkg);
                if (p.Id == 0L || _byId.ContainsKey(p.Id)) continue;
                _byId[p.Id] = p;
                _all.Add(p);
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
