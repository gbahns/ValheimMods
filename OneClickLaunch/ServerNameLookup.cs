using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace OneClickLaunch
{
    /// <summary>
    /// Asks a dedicated server for its name. The game never does this for a private address:
    /// its Steam backend returns nothing for any non-public IP without querying, so a local or
    /// LAN server can never get a name through the normal path. The query machinery is there,
    /// though (ZSteamMatchmaking pings a server's query port and reads the name it reports),
    /// and this drives it for the servers in the history that have no name yet.
    /// </summary>
    internal static class ServerNameLookup
    {
        private static readonly MethodInfo EnqueuePing = AccessTools.Method(typeof(ZSteamMatchmaking), "EnqueuePing");

        // Addresses asked this menu visit, so an unreachable server is pinged once per visit,
        // not once per rebuild.
        private static readonly HashSet<string> s_asked = new HashSet<string>();

        internal static void NewMenuVisit()
        {
            s_asked.Clear();
        }

        /// <summary>Ping the entry's server; onName runs with the name it reports, if it answers with one.</summary>
        internal static void Request(Entry entry, Action<string> onName)
        {
            if (entry == null || !entry.IsServer || entry.serverType != Entry.ServerDedicated) return;
            if (EnqueuePing == null || ZSteamMatchmaking.instance == null) return;
            if (!s_asked.Add(entry.serverAddress)) return;

            ServerJoinData join = MenuPatches.BuildJoinData(entry);
            if (!join.IsValid) return;
            ServerJoinDataDedicated dedicated = join.Dedicated;
            string address = entry.serverAddress;

            try
            {
                MultiBackendMatchmaking.GetServerIPAsync(dedicated, (succeeded, ip) =>
                {
                    if (!succeeded || !ip.HasValue || ZSteamMatchmaking.instance == null) return;
                    try
                    {
                        var endPoint = new NetworkingUtils.IPEndPoint(ip.Value, dedicated.m_port);
                        ZSteamMatchmaking.ServerPingCompletedHandler handler = data =>
                        {
                            string name = data.m_matchmakingData.m_serverName;
                            if (data.m_matchmakingData.IsValid && !string.IsNullOrEmpty(name)) onName?.Invoke(name);
                            else OneClickLaunchMod.Log.LogInfo($"{address} did not answer with a name.");
                        };
                        EnqueuePing.Invoke(ZSteamMatchmaking.instance, new object[] { endPoint, handler });
                    }
                    catch (Exception ex)
                    {
                        OneClickLaunchMod.Log.LogDebug($"Could not ping {address}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                OneClickLaunchMod.Log.LogDebug($"Could not resolve {address}: {ex.Message}");
            }
        }
    }
}
