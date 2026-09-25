using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DiagnoseServerLag
{
    /// <summary>
    /// Asks SpreadTheLoad, if it is there, which players it is steering work away from.
    ///
    /// Server side only. SpreadTheLoad is a server-only mod and this readout is drawn on the
    /// client, so the answer has to travel down with the server report - there is no other route.
    ///
    /// Reached by reflection rather than by reference, deliberately. This mod has to work unchanged
    /// on a server that has never heard of SpreadTheLoad, which is every server but Greg's, and a
    /// hard reference would make that a missing-assembly failure at load. The cost is a string name
    /// to keep in step; the type it points at is public and exists for this one purpose.
    ///
    /// Everything degrades to "nobody is yielding", which is the right answer when the mod is
    /// absent, disabled, or has nobody configured - and also the right answer if this lookup breaks
    /// entirely, because a grey row is a hint and never worth a failure.
    /// </summary>
    internal static class YieldInfo
    {
        private const string ApiType = "SpreadTheLoad.SpreadTheLoadApi";

        private static bool _looked;
        private static PropertyInfo _active;
        private static MethodInfo _isYielding;

        private static readonly object[] _oneArg = new object[1];

        /// <summary>True when SpreadTheLoad is present, on, and steering somebody right now.</summary>
        internal static bool Active
        {
            get
            {
                Look();
                if (_active == null) return false;
                try { return (bool)_active.GetValue(null, null); }
                catch { return false; }
            }
        }

        internal static bool IsYielding(long uid)
        {
            Look();
            if (_isYielding == null) return false;
            try
            {
                _oneArg[0] = uid;
                return (bool)_isYielding.Invoke(null, _oneArg);
            }
            catch { return false; }
        }

        /// <summary>
        /// One lookup per session. A miss is cached as firmly as a hit: on a server without
        /// SpreadTheLoad this would otherwise search every loaded assembly once a second forever.
        /// </summary>
        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                var type = AccessTools.TypeByName(ApiType);
                if (type == null) return;
                _active = AccessTools.Property(type, "Active");
                _isYielding = AccessTools.Method(type, "IsYielding", new[] { typeof(long) });
                if (_isYielding != null)
                    DiagnoseServerLagMod.Log.LogInfo(
                        "[DiagnoseServerLag] SpreadTheLoad found; players it is steering work away from " +
                        "will be greyed on everyone's readout.");
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning(
                    $"[DiagnoseServerLag] could not read SpreadTheLoad's yield list: {e.Message}");
            }
        }

        /// <summary>
        /// The peers being steered away from, for the report. Empty unless the mod is active.
        ///
        /// Walks the live peer list rather than the report's own per-peer table, because that table
        /// is empty whenever the sockets cannot be read - which is the normal case on a dedicated
        /// server without Steamworks initialised, and exactly the server this matters on.
        /// </summary>
        internal static List<long> Collect()
        {
            var yielding = new List<long>();
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !Active) return yielding;
                foreach (var peer in znet.GetConnectedPeers())
                {
                    if (peer == null || !peer.IsReady()) continue;
                    if (IsYielding(peer.m_uid)) yielding.Add(peer.m_uid);
                }
            }
            catch { }
            return yielding;
        }
    }
}
