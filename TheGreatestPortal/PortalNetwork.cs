using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>
    /// The server is the single authority over portal names and destinations.
    ///
    ///  * Every two seconds (sooner after a change) it walks every portal ZDO in the world:
    ///    assigns permanent ids, adopts pre-existing vanilla/XPortal pairs once, and makes the
    ///    vanilla portal connection match the stored destination. Vanilla's own tag pairing
    ///    (Game.ConnectPortals) is switched off.
    ///  * It sends the full portal list to everybody whenever it changes, and to a player who
    ///    asks (on spawn).
    ///  * Clients send edits (name + destination) as requests; the server validates and applies
    ///    them by taking ownership of the portal's ZDO, the way Game.SetConnection does.
    /// </summary>
    internal static class PortalNetwork
    {
        private const string RpcCatalog = "TGP_Catalog";   // server -> clients: ZPackage (Catalog.Pack)
        private const string RpcRequest = "TGP_Request";   // client -> server: send me the list
        private const string RpcSet     = "TGP_SetPortal"; // client -> server: ZDOID portal, string name, long target
        private const string RpcSetAll  = "TGP_SetAll";    // client -> server: long target (every portal leads there)
        private const string RpcRename  = "TGP_Rename";    // client -> server: long portal id, string name
        private const string RpcNotice  = "TGP_Notice";    // server -> client: string message

        private const float Interval = 2f;

        private static float _nextMaintain;
        private static bool _dirty;
        private static byte[] _lastSnapshot;
        private static readonly List<PortalInfo> _infos = new List<PortalInfo>();
        private static readonly Dictionary<long, ZDO> _byId = new Dictionary<long, ZDO>();
        private static readonly List<ZDO> _portals = new List<ZDO>();

        internal static int ServerPortalCount { get; private set; }
        internal static int ServerConnectionChanges { get; private set; }

        // ── lifecycle ───────────────────────────────────────────────────────────────

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            rpc.Register<ZPackage>(RpcCatalog, RPC_Catalog);
            rpc.Register(RpcRequest, RPC_Request);
            rpc.Register<ZPackage>(RpcSet, RPC_SetPortal);
            rpc.Register<long>(RpcSetAll, RPC_SetAll);
            rpc.Register<long, string>(RpcRename, RPC_Rename);
            rpc.Register<string>(RpcNotice, RPC_Notice);
        }

        internal static void Reset()
        {
            _nextMaintain = 0f;
            _dirty = false;
            _lastSnapshot = null;
            _infos.Clear();
            _byId.Clear();
            _portals.Clear();
            ServerPortalCount = 0;
            ServerConnectionChanges = 0;
            Catalog.Clear();
            Catalog.ResetPrefabCache();
        }

        internal static void Update()
        {
            var znet = ZNet.instance;
            if (znet == null || ZRoutedRpc.instance == null || ZDOMan.instance == null) return;
            if (!znet.IsServer()) return;
            if (Time.time < _nextMaintain && !_dirty) return;
            _dirty = false;
            _nextMaintain = Time.time + Interval;
            try
            {
                Maintain();
                BroadcastIfChanged();
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogError($"[TheGreatestPortal] Server portal maintenance failed: {e}");
            }
        }

        internal static void MarkDirty() => _dirty = true;

        // ── client API ──────────────────────────────────────────────────────────────

        internal static void RequestCatalog()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcRequest);
        }

        /// <summary>Asks the server to rename a portal and point it at a destination (0 = none).</summary>
        internal static void SendSetPortal(ZDOID portal, string name, long targetId)
        {
            if (ZRoutedRpc.instance == null || portal == ZDOID.None) return;
            var pkg = new ZPackage();
            pkg.Write(portal);
            pkg.Write(name ?? "");
            pkg.Write(targetId);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcSet, pkg);
        }

        /// <summary>Asks the server to rename any portal, near or far, by its permanent id.</summary>
        internal static void SendRename(long portalId, string name)
        {
            if (ZRoutedRpc.instance == null || portalId == 0L) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcRename, portalId, name ?? "");
        }

        /// <summary>Asks the server to point every portal in the world at one portal.</summary>
        internal static void SendSetAll(long targetId)
        {
            if (ZRoutedRpc.instance == null || targetId == 0L) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcSetAll, targetId);
        }

        private static void RPC_Catalog(long sender, ZPackage pkg)
        {
            var znet = ZNet.instance;
            if (znet == null) return;
            if (!znet.IsServer())
            {
                var server = znet.GetServerPeer();
                if (server != null && sender != server.m_uid) return;   // only the server decides
            }
            Catalog.Apply(pkg);
        }

        private static void RPC_Notice(long sender, string text)
        {
            if (Player.m_localPlayer == null) return;
            TheGreatestPortalMod.Message(text, always: true);
        }

        // ── server side ─────────────────────────────────────────────────────────────

        private static void RPC_Request(long sender)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            Maintain();
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcCatalog, Catalog.Pack(_infos));
        }

        private static void RPC_SetPortal(long sender, ZPackage pkg)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            ZDOID portalId = pkg.ReadZDOID();
            string name = PortalData.CleanName(pkg.ReadString(), TgpConfig.MaxNameLength.Value);
            long target = pkg.ReadLong();

            var zdo = ZDOMan.instance.GetZDO(portalId);
            if (zdo == null || !IsPortalPrefab(zdo.GetPrefab()))
            {
                Notice(sender, "That portal is not known to the server yet. Try again in a moment.");
                return;
            }
            long selfId = EnsureId(zdo);
            if (target != 0L)
            {
                if (target == selfId)
                {
                    Notice(sender, "A portal cannot lead to itself.");
                    return;
                }
                if (FindById(target) == null)
                {
                    Notice(sender, "That destination portal no longer exists.");
                    return;
                }
            }

            Own(zdo);
            zdo.Set(ZDOVars.s_tag, name);
            zdo.Set(PortalData.ToHash, target);
            zdo.Set(PortalData.SetHash, 1);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] {PeerName(znet, sender)} set portal '{name}' ({selfId}) -> {(target == 0L ? "none (map)" : target.ToString())}.");
            _dirty = true;
        }

        private static void RPC_SetAll(long sender, long target)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            string who = PeerName(znet, sender);
            if (!TgpConfig.AnyoneCanRedirectAll.Value && !IsAdmin(znet, sender))
            {
                TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] {who} asked to redirect all portals but is not an admin.");
                Notice(sender, "Only an admin can point every portal at one place");
                return;
            }
            var targetZdo = FindById(target);
            if (targetZdo == null)
            {
                Notice(sender, "That portal is not known to the server yet. Try again in a moment.");
                return;
            }
            int count = 0;
            foreach (var zdo in ZDOMan.instance.GetPortalList())
            {
                if (zdo == null || !zdo.IsValid() || zdo == targetZdo) continue;
                if (PortalData.GetTarget(zdo) == target && PortalData.IsConfigured(zdo)) continue;
                Own(zdo);
                zdo.Set(PortalData.ToHash, target);
                zdo.Set(PortalData.SetHash, 1);
                ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                count++;
            }
            string name = PortalData.GetName(targetZdo);
            if (string.IsNullOrEmpty(name)) name = "the chosen portal";
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] {who} pointed {count} portal(s) at '{name}' ({target}).");
            Notice(sender, count == 0 ? "Every portal already leads to " + name : $"{count} portal(s) now lead to {name}");
            _dirty = true;
        }

        private static void RPC_Rename(long sender, long portalId, string name)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            string who = PeerName(znet, sender);
            if (!TgpConfig.AnyoneCanRenameRemote.Value && !IsAdmin(znet, sender))
            {
                Notice(sender, "Only an admin can rename a portal from afar");
                return;
            }
            var zdo = FindById(portalId);
            if (zdo == null)
            {
                Notice(sender, "That portal no longer exists");
                return;
            }
            name = PortalData.CleanName(name, TgpConfig.MaxNameLength.Value);
            string old = PortalData.GetName(zdo);
            if (name == old) return;
            Own(zdo);
            zdo.Set(ZDOVars.s_tag, name);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] {who} renamed portal '{old}' ({portalId}) to '{name}'.");
            _dirty = true;
        }

        private static bool IsAdmin(ZNet znet, long sender)
        {
            if (sender == ZDOMan.GetSessionID()) return true;     // the hosting player
            var peer = znet.GetPeer(sender);
            if (peer == null || peer.m_socket == null) return false;
            string host = peer.m_socket.GetHostName();
            return !string.IsNullOrEmpty(host) && znet.IsAdmin(host);
        }

        private static void Notice(long peer, string text)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcNotice, text);
        }

        private static string PeerName(ZNet znet, long sender)
        {
            if (sender == ZDOMan.GetSessionID())
                return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "the server";
            var peer = znet.GetPeer(sender);
            return peer != null && !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : "a player";
        }

        private static bool IsPortalPrefab(int hash)
        {
            var game = Game.instance;
            return game != null && game.PortalPrefabHash.Contains(hash);
        }

        private static ZDO FindById(long id)
        {
            if (id == 0L) return null;
            if (_byId.TryGetValue(id, out var zdo) && zdo != null && zdo.IsValid()) return zdo;
            // The index may be a couple of seconds old; look again.
            foreach (var p in ZDOMan.instance.GetPortalList())
                if (PortalData.GetId(p) == id) return p;
            return null;
        }

        /// <summary>The server takes the ZDO so its writes are the ones that get sent.</summary>
        private static void Own(ZDO zdo)
        {
            long me = ZDOMan.GetSessionID();
            if (zdo.GetOwner() != me) zdo.SetOwner(me);
        }

        private static long EnsureId(ZDO zdo)
        {
            long id = PortalData.GetId(zdo);
            if (id != 0L) return id;
            id = PortalData.NewId();
            Own(zdo);
            zdo.Set(PortalData.IdHash, id);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            return id;
        }

        /// <summary>
        /// Walks every portal: ids, one-time adoption of old pairs, and vanilla connections that
        /// mirror the stored destinations. Rebuilds the catalog list as a side effect.
        /// </summary>
        private static void Maintain()
        {
            _portals.Clear();
            _portals.AddRange(ZDOMan.instance.GetPortalList());
            _byId.Clear();
            _infos.Clear();

            for (int i = 0; i < _portals.Count; i++)
            {
                var zdo = _portals[i];
                if (zdo == null || !zdo.IsValid()) continue;
                long id = EnsureId(zdo);
                if (_byId.ContainsKey(id))
                {
                    // A copied piece (some build mods clone ZDO data) would share an id; give it a new one.
                    id = PortalData.NewId();
                    Own(zdo);
                    zdo.Set(PortalData.IdHash, id);
                    ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                }
                _byId[id] = zdo;
            }

            int changes = 0;
            for (int i = 0; i < _portals.Count; i++)
            {
                var zdo = _portals[i];
                if (zdo == null || !zdo.IsValid()) continue;
                long id = PortalData.GetId(zdo);
                long to = PortalData.GetTarget(zdo);

                if (!PortalData.IsConfigured(zdo) && TgpConfig.AdoptExistingConnections.Value)
                {
                    ZDO old = Adoptable(zdo);
                    if (old != null)
                    {
                        to = PortalData.GetId(old);
                        Own(zdo);
                        zdo.Set(PortalData.ToHash, to);
                        zdo.Set(PortalData.SetHash, 1);
                        ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                        TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] Adopted existing connection '{PortalData.GetName(zdo)}' -> '{PortalData.GetName(old)}'.");
                    }
                }

                ZDOID desired = ZDOID.None;
                if (to != 0L && _byId.TryGetValue(to, out var target) && target.m_uid != zdo.m_uid) desired = target.m_uid;
                if (PortalData.Connection(zdo) != desired)
                {
                    Own(zdo);
                    zdo.SetConnection(ZDOExtraData.ConnectionType.Portal, desired);
                    ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                    changes++;
                }

                Catalog.PrefabFor(zdo.GetPrefab(), out bool allowAll, out float exit, out string prefabName);
                _infos.Add(new PortalInfo
                {
                    Id = id,
                    ZdoId = zdo.m_uid,
                    Name = PortalData.GetName(zdo),
                    Pos = zdo.GetPosition(),
                    Rot = zdo.GetRotation(),
                    TargetId = to,
                    AllowAllItems = allowAll,
                    ExitDistance = exit,
                    Prefab = prefabName,
                });
            }
            ServerPortalCount = _infos.Count;
            ServerConnectionChanges += changes;
            if (changes > 0) TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] Updated {changes} portal connection(s); {_infos.Count} portal(s) in the world.");
        }

        /// <summary>
        /// The vanilla pair (tag matching) this portal had before this mod, if that portal still
        /// exists. XPortal's stored target is deliberately not read: it is a ZDOID, and ZDOIDs are
        /// renumbered on every world load, so after a restart it could name any object at all.
        /// </summary>
        private static ZDO Adoptable(ZDO zdo)
        {
            ZDOID conn = PortalData.Connection(zdo);
            if (conn == ZDOID.None) return null;
            var t = ZDOMan.instance.GetZDO(conn);
            return t != null && t != zdo && IsPortalPrefab(t.GetPrefab()) ? t : null;
        }

        private static void BroadcastIfChanged()
        {
            var pkg = Catalog.Pack(_infos);
            byte[] bytes = pkg.GetArray();
            if (_lastSnapshot != null && SameBytes(bytes, _lastSnapshot)) return;
            _lastSnapshot = bytes;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcCatalog, pkg);
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        internal static string Status()
        {
            var znet = ZNet.instance;
            if (znet == null) return "Not in a game.";
            string s = $"catalog: {Catalog.Count} portal(s){(Catalog.HasSnapshot ? "" : " (nothing received yet)")}";
            if (znet.IsServer()) s = $"server: {ServerPortalCount} portal(s), {ServerConnectionChanges} connection change(s) this session | " + s;
            return s;
        }
    }
}
