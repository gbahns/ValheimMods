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
        private const string RpcUsed    = "TGP_Used";     // client -> server: long from, long to
        private const string RpcPrivate = "TGP_Private";  // client -> server: long portal id, int 1 = private
        private const string RpcReveal  = "TGP_Reveal";   // client -> server: int 1 = an admin asking to see every private portal

        private const float Interval = 2f;

        private static float _nextMaintain;
        private static bool _dirty;
        private static byte[] _lastSnapshot;

        /// <summary>What each peer was last sent, while private portals mean each gets its own
        /// list. Empty while nobody has made a portal private, which is the ordinary case.</summary>
        private static readonly Dictionary<long, byte[]> _lastPerPeer = new Dictionary<long, byte[]>();

        /// <summary>Peer uid to the id of the character playing on it, rebuilt each sweep. A portal
        /// records the character that built it, and the server knows peers, so the two are joined
        /// through the player's own character ZDO, which carries both.</summary>
        private static readonly Dictionary<long, long> _peerPlayer = new Dictionary<long, long>();

        /// <summary>The peers that have asked to be shown every private portal. Being in here is
        /// only a request; whether it is honored is decided again at every send, because adminship
        /// can be taken away between one and the next.</summary>
        private static readonly HashSet<long> _wantsAll = new HashSet<long>();
        private static readonly List<PortalInfo> _infos = new List<PortalInfo>();
        private static readonly Dictionary<long, ZDO> _byId = new Dictionary<long, ZDO>();
        private static readonly List<ZDO> _portals = new List<ZDO>();
        private static readonly HashSet<long> _seen = new HashSet<long>();   // ids already reported in the log
        private static bool _sweptOnce;

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
            rpc.Register<long, long>(RpcUsed, RPC_Used);
            rpc.Register<long, int>(RpcPrivate, RPC_Private);
            rpc.Register<int>(RpcReveal, RPC_Reveal);
        }

        internal static void Reset()
        {
            _nextMaintain = 0f;
            _dirty = false;
            _lastSnapshot = null;
            _lastPerPeer.Clear();
            _peerPlayer.Clear();
            _wantsAll.Clear();
            _infos.Clear();
            _byId.Clear();
            _portals.Clear();
            _seen.Clear();
            _sweptOnce = false;
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

        /// <summary>Asks the server to make one of your own portals private, or public again.</summary>
        internal static void SendPrivate(long portalId, bool on)
        {
            if (ZRoutedRpc.instance == null || portalId == 0L) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcPrivate, portalId, on ? 1 : 0);
        }

        /// <summary>
        /// Tells the server whether this client is asking to see other players' private portals.
        ///
        /// Sent on every connection and whenever the setting changes, because the server has to be
        /// told again each time: it keeps the wish against the peer, and a peer is a connection.
        /// An ordinary player may send this all day; the server will not honor it.
        /// </summary>
        internal static void SendReveal()
        {
            if (ZRoutedRpc.instance == null) return;
            bool on = TgpConfig.ShowPrivatePortals != null && TgpConfig.ShowPrivatePortals.Value;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcReveal, on ? 1 : 0);
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
            var t = Catalog.Get(targetId);
            string dest = targetId == 0L ? "nothing, so it is an open portal" : DescribeTarget(t, targetId);
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] Asked the server to name portal {portal} '{name}' and point it at {dest}.");
        }

        private static string DescribeTarget(PortalInfo t, long targetId)
        {
            return t != null ? $"'{t.DisplayName}' ({targetId})" : $"portal {targetId}, which is not in the list";
        }

        /// <summary>Asks the server to rename any portal, near or far, by its permanent id.</summary>
        internal static void SendRename(long portalId, string name)
        {
            if (ZRoutedRpc.instance == null || portalId == 0L) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcRename, portalId, name ?? "");
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] Asked the server to rename portal {portalId} to '{name}'.");
        }

        /// <summary>Asks the server to point every portal in the world at one portal.</summary>
        internal static void SendSetAll(long targetId)
        {
            if (ZRoutedRpc.instance == null || targetId == 0L) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcSetAll, targetId);
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] Asked the server to point every portal at {targetId}.");
        }

        /// <summary>
        /// Tells the server both ends of a trip just taken, so everyone's list can show where
        /// people have been lately. Only the server writes the times, so they share one clock.
        /// </summary>
        internal static void SendUsed(long fromId, long toId)
        {
            if (ZRoutedRpc.instance == null || (fromId == 0L && toId == 0L)) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcUsed, fromId, toId);
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
            SendCatalogTo(znet, sender);
        }

        /// <summary>An admin asking to see every private portal, or giving that up again.</summary>
        private static void RPC_Reveal(long sender, int on)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            bool want = on != 0;
            bool had = _wantsAll.Contains(sender);
            if (want == had) return;
            if (want) _wantsAll.Add(sender); else _wantsAll.Remove(sender);
            if (want && !IsAdmin(znet, sender))
            {
                TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] {PeerName(znet, sender)} asked to see every private portal but is not an admin.");
                Notice(sender, "Only an admin can see other players private portals");
                return;
            }
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] {PeerName(znet, sender)} is {(want ? "now" : "no longer")} being shown every private portal.");
            SendCatalogTo(znet, sender);
        }

        /// <summary>
        /// Making a portal private, or public again.
        ///
        /// Only its owner may, which is the character that built it. An admin who has asked to be
        /// shown private portals may as well -- that is the whole point of asking, since a portal
        /// nobody can reach any more is cleaned up by making it public first.
        /// </summary>
        private static void RPC_Private(long sender, long portalId, int on)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            if (!PortalData.PrivateAllowed)
            {
                Notice(sender, "Private portals are turned off on this server");
                return;
            }
            if (!_byId.TryGetValue(portalId, out var zdo) || zdo == null)
            {
                Notice(sender, "That portal is not in the list yet");
                return;
            }
            long who = PlayerIdOf(sender);
            long owner = PortalData.GetCreator(zdo);
            bool mine = who != 0L && owner != 0L && owner == who;
            bool cleaning = _wantsAll.Contains(sender) && IsAdmin(znet, sender);
            if (!mine && !cleaning)
            {
                TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] {PeerName(znet, sender)} asked to change a portal they do not own.");
                Notice(sender, owner == 0L ? "Nobody owns that portal, so it cannot be made private" : "Only the portal's owner can do that");
                return;
            }
            bool want = on != 0;
            if (PortalData.GetPrivate(zdo) == want) return;
            Own(zdo);
            zdo.Set(PortalData.PrivateHash, want);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] {PeerName(znet, sender)} made portal {portalId} {(want ? "private" : "public")}.");
            _dirty = true;
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

        private static void RPC_Used(long sender, long fromId, long toId)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            string who = PeerName(znet, sender);
            long now = PortalData.Now();
            bool any = false;
            foreach (long id in new[] { fromId, toId })
            {
                if (id == 0L) continue;
                var zdo = FindById(id);
                if (zdo == null) continue;
                Own(zdo);
                zdo.Set(PortalData.UsedHash, now);
                zdo.Set(PortalData.UserHash, who);
                zdo.Set(PortalData.LogHash, PortalData.NoteVisit(PortalData.GetLog(zdo), who, now));
                ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                any = true;
            }
            if (any) _dirty = true;
        }

        /// <summary>The character id playing on a peer, or 0 while that cannot be told.</summary>
        private static long PlayerIdOf(long peer)
        {
            long id;
            return _peerPlayer.TryGetValue(peer, out id) ? id : 0L;
        }

        /// <summary>
        /// Which peer each character is playing on, rebuilt as the sweep runs.
        ///
        /// A portal carries the id of the character that built it; ownership of anything on the
        /// wire is by peer. They are different id spaces, and the bridge is the player's own
        /// character ZDO, which carries both: its uid names the peer, and it holds the character
        /// id inside. A player whose character ZDO the server has not got yet is nobody, and sees
        /// public portals only until it arrives -- which is the safe way round.
        /// </summary>
        private static void BuildPlayerMap(ZNet znet)
        {
            _peerPlayer.Clear();
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;
            try
            {
                ForgetGonePeers(znet);
                foreach (var info in znet.GetPlayerList())
                {
                    var zdo = zdoMan.GetZDO(info.m_characterID);
                    if (zdo == null) continue;
                    long playerId = zdo.GetLong(ZDOVars.s_playerID, 0L);
                    if (playerId != 0L) _peerPlayer[info.m_characterID.UserID] = playerId;
                }
            }
            catch { /* a player who cannot be placed sees the public portals, and no more */ }
        }

        /// <summary>
        /// Drops what is remembered about peers that have gone.
        ///
        /// Two things are kept against a connection rather than a player: the last list it was
        /// sent, and whether it asked to see every private portal. Both have to go when it does --
        /// the first is so much dead weight, and the second is a wish nobody made that would be
        /// waiting if the id ever came round again.
        /// </summary>
        private static void ForgetGonePeers(ZNet znet)
        {
            if (_lastPerPeer.Count == 0 && _wantsAll.Count == 0) return;
            var here = new HashSet<long>();
            var peers = znet.GetPeers();
            if (peers != null) foreach (var peer in peers) here.Add(peer.m_uid);
            if (Player.m_localPlayer != null) here.Add(ZDOMan.GetSessionID());
            Prune(_lastPerPeer, here);
            _wantsAll.RemoveWhere(uid => !here.Contains(uid));
        }

        private static void Prune(Dictionary<long, byte[]> map, HashSet<long> keep)
        {
            List<long> gone = null;
            foreach (var uid in map.Keys)
                if (!keep.Contains(uid)) (gone ?? (gone = new List<long>())).Add(uid);
            if (gone == null) return;
            foreach (var uid in gone) map.Remove(uid);
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

        /// <summary>
        /// Who owns a ZDO, by name, or "" when that cannot be told.
        ///
        /// This is how the builder of a portal is known. Nothing announces a piece being placed,
        /// but the client that placed it owns it, and the sweep comes round within two seconds, so
        /// whoever owns a portal the sweep has never seen before is whoever just built it. It has
        /// to be asked before Own(), which takes the ZDO over for the server.
        /// </summary>
        private static string OwnerName(ZDO zdo)
        {
            var znet = ZNet.instance;
            if (zdo == null || znet == null) return "";
            long owner = zdo.GetOwner();
            if (owner == 0L) return "";
            if (owner == ZDOMan.GetSessionID())
                return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";
            var peer = znet.GetPeer(owner);
            return peer != null && !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : "";
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
            var znet = ZNet.instance;
            if (znet != null) BuildPlayerMap(znet);
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

                // Reported before adoption below can change it, so the log shows what the builder
                // actually stored: this is how a portal that did not take its builder's default
                // can be told apart from one built with no default set.
                if (_seen.Add(id) && _sweptOnce)
                {
                    var at = zdo.GetPosition();
                    string dest = to == 0L ? "no destination" : "destination " + to;
                    TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] New portal '{PortalData.GetName(zdo)}' ({id}) at [{at.x:0},{at.z:0}]: {dest}, configured by this mod: {PortalData.IsConfigured(zdo)}.");
                    // Only a portal that turned up after the first sweep is new; the first sweep is
                    // the world as it already stood, and stamping that would call it all brand new.
                    if (PortalData.GetBorn(zdo) == 0L)
                    {
                        string builder = OwnerName(zdo);   // before Own(), which makes this the owner
                        Own(zdo);
                        zdo.Set(PortalData.BornHash, PortalData.Now());
                        if (!string.IsNullOrEmpty(builder)) zdo.Set(PortalData.BornByHash, builder);
                        ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                    }
                }

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
                    OwnerId = PortalData.GetCreator(zdo),
                    Private = PortalData.GetPrivate(zdo),
                    Born = PortalData.GetBorn(zdo),
                    UsedAt = PortalData.GetUsed(zdo),
                    UsedBy = PortalData.GetUser(zdo),
                    BornBy = PortalData.GetBornBy(zdo),
                    UsedLog = PortalData.GetLog(zdo),
                });
            }
            // A stable order, because the snapshot is compared byte for byte to decide whether to
            // send it. The list is built in ZDOMan.GetPortalList order, which walks a dictionary's
            // values - an order nothing promises and that shifts as portals are built and torn
            // down. Reordering alone would send a snapshot saying nothing changed, and every
            // client redraws its portal pins when one arrives. Ids are unique and permanent.
            _infos.Sort((a, b) => a.Id.CompareTo(b.Id));
            _sweptOnce = true;   // the first sweep is the world as it already was, and is not news
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

        /// <summary>
        /// The list, to everyone who should have it.
        ///
        /// While no portal is private this is one package sent once to everybody, which is what it
        /// has always been. The moment one is, each player has to be sent a list of their own, and
        /// the saving of a single broadcast is given up: a private portal that reached anybody
        /// else would be a private portal in name only, and no amount of hiding it in the client
        /// would help, because the client holds whatever it was sent.
        ///
        /// So the cost is paid only by the servers that use the feature, and then only while they
        /// do -- take the last portal out of private and the next send is a broadcast again.
        /// </summary>
        private static void BroadcastIfChanged()
        {
            var znet = ZNet.instance;
            if (znet == null) return;
            bool anyPrivate = false;
            if (PortalData.PrivateAllowed)
                for (int i = 0; i < _infos.Count; i++) if (_infos[i].Private) { anyPrivate = true; break; }
            if (!anyPrivate)
            {
                var pkg = Catalog.Pack(_infos);
                byte[] bytes = pkg.GetArray();
                if (_lastPerPeer.Count > 0) _lastPerPeer.Clear();
                if (_lastSnapshot != null && SameBytes(bytes, _lastSnapshot)) return;
                _lastSnapshot = bytes;
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcCatalog, pkg);
                return;
            }
            _lastSnapshot = null;   // so that going back to none sends the shared list again
            foreach (var peer in Recipients(znet)) SendCatalogTo(znet, peer, onlyIfChanged: true);
        }

        /// <summary>Everyone the list goes to: the connected peers, and the player hosting from
        /// their own game, who is not one of them.</summary>
        private static List<long> Recipients(ZNet znet)
        {
            var who = new List<long>();
            var peers = znet.GetPeers();
            if (peers != null) foreach (var peer in peers) who.Add(peer.m_uid);
            if (Player.m_localPlayer != null) who.Add(ZDOMan.GetSessionID());
            return who;
        }

        /// <summary>The list as one player may see it: every portal that is not private, their own
        /// private ones, and everybody's if they are an admin who has asked.</summary>
        private static void SendCatalogTo(ZNet znet, long peer, bool onlyIfChanged = false)
        {
            long who = PlayerIdOf(peer);
            bool all = _wantsAll.Contains(peer) && IsAdmin(znet, peer);
            List<PortalInfo> list = _infos;
            bool filtered = false;
            if (PortalData.PrivateAllowed)
                for (int i = 0; i < _infos.Count; i++)
                    if (_infos[i].Private) { filtered = true; break; }
            if (filtered && !all)
            {
                list = new List<PortalInfo>(_infos.Count);
                for (int i = 0; i < _infos.Count; i++)
                {
                    var p = _infos[i];
                    if (!p.Private || (who != 0L && p.OwnerId == who)) list.Add(p);
                }
            }
            var pkg = Catalog.Pack(list);
            if (onlyIfChanged)
            {
                byte[] bytes = pkg.GetArray();
                byte[] last;
                if (_lastPerPeer.TryGetValue(peer, out last) && SameBytes(bytes, last)) return;
                _lastPerPeer[peer] = bytes;
            }
            else _lastPerPeer.Remove(peer);   // sent out of turn; let the next sweep compare afresh
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcCatalog, pkg);
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
