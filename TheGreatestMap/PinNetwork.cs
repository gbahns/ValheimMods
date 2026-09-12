using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// Routed RPCs between clients and the server. Clients send requests to the server; the
    /// server applies them to PinStore and broadcasts the result to everybody (itself included,
    /// which is how the hosting player in a non-dedicated game sees its own changes).
    /// </summary>
    internal static class PinNetwork
    {
        private const string RequestSync = "TGM_RequestSync";
        private const string FullSync    = "TGM_FullSync";
        private const string Add         = "TGM_Add";
        private const string Remove      = "TGM_Remove";
        private const string Update      = "TGM_Update";
        private const string Wipe        = "TGM_Wipe";
        private const string Unsuppress  = "TGM_Unsuppress";
        private const string BAdd        = "TGM_BAdd";
        private const string BRemove     = "TGM_BRemove";
        private const string BUpdate     = "TGM_BUpdate";
        private const string BWipe       = "TGM_BWipe";
        private const string BUnsuppress = "TGM_BUnsuppress";

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            rpc.Register(RequestSync, RPC_RequestSync);
            rpc.Register<ZPackage>(FullSync, RPC_FullSync);
            rpc.Register<ZPackage>(Add, RPC_Add);
            rpc.Register<string>(Remove, RPC_Remove);
            rpc.Register<ZPackage>(Update, RPC_Update);
            rpc.Register<int>(Wipe, RPC_Wipe);
            rpc.Register(Unsuppress, RPC_Unsuppress);
            rpc.Register<ZPackage>(BAdd, RPC_BAdd);
            rpc.Register<ZPackage>(BRemove, RPC_BRemove);
            rpc.Register<ZPackage>(BUpdate, RPC_BUpdate);
            rpc.Register<int>(BWipe, RPC_BWipe);
            rpc.Register(BUnsuppress, RPC_BUnsuppress);
        }

        // ── client → server ─────────────────────────────────────────────────────────

        private static bool Ready => ZRoutedRpc.instance != null && ZNet.instance != null;

        internal static void SendRequestSync()
        {
            if (Ready) ZRoutedRpc.instance.InvokeRoutedRPC(RequestSync);
        }

        internal static void SendAdd(SharedPin pin)
        {
            if (!Ready) return;
            var pkg = new ZPackage();
            pin.Write(pkg);
            ZRoutedRpc.instance.InvokeRoutedRPC(Add, pkg);
        }

        internal static void SendRemove(string id)
        {
            if (Ready) ZRoutedRpc.instance.InvokeRoutedRPC(Remove, id);
        }

        internal static void SendUpdate(string id, string name, bool isChecked)
        {
            if (!Ready) return;
            var pkg = new ZPackage();
            pkg.Write(id);
            pkg.Write(name ?? "");
            pkg.Write(isChecked);
            ZRoutedRpc.instance.InvokeRoutedRPC(Update, pkg);
        }

        internal static void SendWipe(bool autoOnly)
        {
            if (Ready) ZRoutedRpc.instance.InvokeRoutedRPC(Wipe, autoOnly ? 1 : 0);
        }

        internal static void SendUnsuppress()
        {
            if (Ready) ZRoutedRpc.instance.InvokeRoutedRPC(Unsuppress);
        }

        // ── server handlers ─────────────────────────────────────────────────────────

        private static void Broadcast(string method, params object[] args)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, method, args);
        }

        private static bool IsAdmin(long sender)
        {
            var znet = ZNet.instance;
            if (znet == null) return false;
            if (sender == Access.RoutedId(ZRoutedRpc.instance)) return true; // the hosting player
            var peer = znet.GetPeer(sender);
            if (peer == null || peer.m_socket == null) return false;
            string host = peer.m_socket.GetHostName();
            return !string.IsNullOrEmpty(host) && znet.IsAdmin(host);
        }

        private static void RPC_RequestSync(long sender)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, FullSync, PinStore.BuildFullSync());
        }

        private static void RPC_Add(long sender, ZPackage pkg)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            var pin = SharedPin.Read(pkg, PinStore.FileVersion);
            if (!PinStore.Add(pin)) return;
            var outPkg = new ZPackage();
            pin.Write(outPkg);
            Broadcast(BAdd, outPkg);
        }

        private static void RPC_Remove(long sender, string id)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            if (!PinStore.Remove(id, out var suppression)) return;
            var outPkg = new ZPackage();
            outPkg.Write(id);
            outPkg.Write(suppression != null);
            if (suppression != null) suppression.Write(outPkg);
            Broadcast(BRemove, outPkg);
        }

        private static void RPC_Update(long sender, ZPackage pkg)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            string id = pkg.ReadString();
            string name = pkg.ReadString();
            bool isChecked = pkg.ReadBool();
            if (!PinStore.Update(id, name, isChecked)) return;
            var outPkg = new ZPackage();
            outPkg.Write(id);
            outPkg.Write(name);
            outPkg.Write(isChecked);
            Broadcast(BUpdate, outPkg);
        }

        private static void RPC_Wipe(long sender, int autoOnly)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            if (!IsAdmin(sender))
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Ignored wipe request from non-admin peer {sender}.");
                return;
            }
            int n = PinStore.Wipe(autoOnly != 0);
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Wiped {n} shared markers (autoOnly={autoOnly != 0}) on request of peer {sender}.");
            Broadcast(BWipe, autoOnly);
        }

        private static void RPC_Unsuppress(long sender)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            if (!IsAdmin(sender)) return;
            PinStore.ClearSuppressions();
            Broadcast(BUnsuppress);
        }

        // ── client handlers (also run on the server process, where Minimap is null) ─

        private static void RPC_FullSync(long sender, ZPackage pkg)
        {
            if (Minimap.instance == null) return;
            ClientPins.ApplyFullSync(pkg);
        }

        private static void RPC_BAdd(long sender, ZPackage pkg)
        {
            if (Minimap.instance == null) return;
            ClientPins.ApplyAdd(SharedPin.Read(pkg, PinStore.FileVersion));
        }

        private static void RPC_BRemove(long sender, ZPackage pkg)
        {
            if (Minimap.instance == null) return;
            string id = pkg.ReadString();
            Suppression suppression = pkg.ReadBool() ? Suppression.Read(pkg, PinStore.FileVersion) : null;
            ClientPins.ApplyRemove(id, suppression);
        }

        private static void RPC_BUpdate(long sender, ZPackage pkg)
        {
            if (Minimap.instance == null) return;
            string id = pkg.ReadString();
            string name = pkg.ReadString();
            bool isChecked = pkg.ReadBool();
            ClientPins.ApplyUpdate(id, name, isChecked);
        }

        private static void RPC_BWipe(long sender, int autoOnly)
        {
            if (Minimap.instance == null) return;
            ClientPins.ApplyWipe(autoOnly != 0);
        }

        private static void RPC_BUnsuppress(long sender)
        {
            if (Minimap.instance == null) return;
            ClientPins.ClearSuppressions();
        }
    }

    // ── lifecycle ──────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Patch
    {
        private static void Postfix()
        {
            PinNetwork.Register();
            ClientPins.Reset();
            DiscoveryLedger.Clear();
            Recorder.Reset();
            LocationIndex.Clear();
            PocketMap.ResetState();
            TableSync.Reset();
            if (PinStore.IsServer) PinStore.Load();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned_Patch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) PinNetwork.SendRequestSync();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNet_Shutdown_Patch
    {
        private static void Prefix()
        {
            PinStore.Unload();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SaveWorldAndPlayerProfiles))]
    internal static class ZNet_SaveWorld_Patch
    {
        private static void Postfix()
        {
            PinStore.SaveIfDirty();
        }
    }
}
