using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// A temporary listening post on the game's own routed-call dispatcher.
    ///
    /// The repair asks the server what stands at a place and the server never answers. Everything
    /// that can be read has been: both sides register the handler and say so in the log, the name
    /// hashes do not collide, the send goes through the same overload that carries a 70 KB map
    /// sync without trouble, and nothing throws on either machine. The question is therefore the
    /// one thing the log cannot currently tell apart -- whether the call reaches the server.
    ///
    /// <para>Two outcomes, pointing opposite ways. A line naming our call means it arrives and
    /// the dispatcher has nothing filed under it, so the registration went onto an instance that
    /// is no longer the live one. Silence means it never arrives, and the fault is in addressing
    /// or transport.</para>
    ///
    /// <para>Delete this file once the answer is in. It is a diagnostic, not a feature.</para>
    /// </summary>
    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    internal static class RpcProbe
    {
        private static readonly Dictionary<int, string> Ours = new Dictionary<int, string>
        {
            { "TGM_WhatStandsHere".GetStableHashCode(),   "TGM_WhatStandsHere" },
            { "TGM_WhatStandsHereIs".GetStableHashCode(), "TGM_WhatStandsHereIs" },
            { "TGM_Sync".GetStableHashCode(),             "TGM_Sync (the one that works)" },
        };

        private static void Prefix(ZRoutedRpc.RoutedRPCData data)
        {
            if (data == null) return;
            if (!Ours.TryGetValue(data.m_methodHash, out string name)) return;
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] A routed call for {name} reached this machine " +
                $"(from {data.m_senderPeerID}, addressed to {data.m_targetPeerID}, we are {Me()}, " +
                $"registered here: {Known(data.m_methodHash)}).");
        }

        /// <summary>Whether the dispatcher has anything filed under that hash on this machine.</summary>
        private static bool Known(int hash)
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return false;
            var field = AccessTools.Field(typeof(ZRoutedRpc), "m_functions");
            var map = field != null ? field.GetValue(rpc) as IDictionary : null;
            return map != null && map.Contains(hash);
        }

        private static long Me()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return 0L;
            var field = AccessTools.Field(typeof(ZRoutedRpc), "m_id");
            return field != null ? (long)field.GetValue(rpc) : 0L;
        }
    }
}
