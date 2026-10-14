using System;
using HarmonyLib;

namespace SpreadTheLoad
{
    /// <summary>
    /// What simulation distance this server will grant, as a setting rather than a registry value.
    ///
    /// "Draw distance" in the graphics menu is Valheim 1.0's SimulationDistance, and it is a
    /// negotiation: a client sends what it wants, the server answers
    /// RPC_RequestValidSimulationDistance with min(request, its own) and remembers that on the
    /// peer. The ceiling is therefore the server's own setting - and on a dedicated server that
    /// comes from PlatformPrefs, which on Windows is PlayerPrefs, which is HKCU for whatever
    /// account the service runs as.
    ///
    /// That is a bad place to keep a server setting. A service under a virtual account
    /// (NT SERVICE\&lt;name&gt;, which is what nssm creates) has no profile directory, so there may be
    /// no mounted hive to read at all: a value written there is simply never seen. On the server
    /// this was written for, three attempts to raise the ceiling by hand left every capture still
    /// reporting "asked 6, capped by the server" at the compiled default of 2, and nothing said
    /// why. It is also invisible - a hashed DWORD under a SID - and a hive Windows may discard.
    ///
    /// So the ceiling is set here instead, and the handler is answered directly. The server's own
    /// pref stops mattering.
    ///
    /// The cap is still a cap, and still worth having: a client's simulation distance is a cost the
    /// SERVER pays, because it sends every peer the objects in that peer's active area. Render
    /// Limits hit the same problem and added the same limit, saying so outright - it "prevents
    /// players from freezing the server by requesting too high generated area". This only decides
    /// where that limit sits.
    ///
    /// Levels, as the slider shows them: 0 Low (square 224 m), 1 Medium (288), 2 Classic
    /// (square 288, the default), 3 High (352), 4 Very High (416), 5 Ultra (480), 6 Extreme (544).
    /// Reach is (level + 2.5) x 64 m, and the level is the near distance in zones.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "RPC_RequestValidSimulationDistance")]
    internal static class SimDistance
    {
        /// <summary>
        /// Replaces the vanilla handler when a ceiling is configured. Returning false skips it.
        ///
        /// Deliberately a whole replacement rather than a tweak of the server's own value: setting
        /// ZNet.m_simulationDistance would also change what the server simulates for itself, which
        /// is not what a cap on clients should do.
        /// </summary>
        private static bool Prefix(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return true;

                int ceiling = SpreadTheLoadMod.MaxSimulationDistance != null
                    ? SpreadTheLoadMod.MaxSimulationDistance.Value
                    : -1;
                if (ceiling < 0) return true;              // not configured: vanilla decides

                var asked = SimulationDistance.Deserialize(ref pkg);
                var allowed = SimulationDistance.GetSimulationDistance(ceiling);

                // Still a minimum, so a client asking for less than the ceiling keeps its own
                // choice. A slow machine lowering its draw distance must not be overridden into
                // loading more than it asked for.
                var granted = asked < allowed ? asked : allowed;

                // ZNet.GetPeer(ZRpc) is private, and a publicized reference throws at run
                // time here, so the peer is found through the public list instead.
                ZNetPeer peer = null;
                foreach (var p in znet.GetPeers())
                {
                    if (p != null && p.m_rpc == rpc) { peer = p; break; }
                }
                if (peer != null) peer.m_simulationDistance = granted;

                var reply = new ZPackage();
                granted.Serialize(ref reply);
                rpc.Invoke("RPC_ValidatedSimulationDistance", reply);

                if (peer != null && asked.NearSimulationDistance != granted.NearSimulationDistance)
                    SpreadTheLoadMod.Log.LogInfo(
                        $"[SpreadTheLoad] {peer.m_playerName} asked for simulation distance " +
                        $"{asked.NearSimulationDistance} and was given {granted.NearSimulationDistance} " +
                        $"(ceiling {ceiling}).");
                return false;
            }
            catch (Exception e)
            {
                // Never leave a joining client unanswered: on any surprise, hand it back to vanilla,
                // which will cap at the server's own pref as it always did.
                SpreadTheLoadMod.Log.LogWarning(
                    $"[SpreadTheLoad] could not answer the simulation distance request: {e.Message}");
                return true;
            }
        }
    }
}
