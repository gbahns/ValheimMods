using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// Puts the owner's name on a creature's floating health bar.
    ///
    /// The readout can tell you that Brane is simulating seven creatures. It cannot tell you that
    /// *this* greydwarf, the one not responding to your axe, is one of them. That is the gap this
    /// closes: the routing becomes visible on the thing it is routing.
    ///
    /// It works through Character.GetHoverName rather than by touching the health bar, because
    /// EnemyHud sets the label from exactly that:
    ///
    ///     value.m_name.text = Localization.instance.Localize(c.GetHoverName());
    ///
    /// so a suffix there arrives on the nameplate with no UI code at all. The cost is that
    /// GetHoverName has other callers - hovering a tame, mostly - which will also show the suffix.
    /// That is a fair trade for a diagnostic that is off by default, and arguably right: a tame
    /// somebody else owns behaves the same way.
    ///
    /// Names are resolved from a snapshot refreshed once a second. GetHoverName runs per frame for
    /// every visible creature, and walking the player list that often would make a diagnostic into
    /// a cost.
    /// </summary>
    internal static class OwnerNames
    {
        private static readonly Dictionary<long, string> _names = new Dictionary<long, string>();
        private static readonly List<ZNet.PlayerInfo> _players = new List<ZNet.PlayerInfo>();
        private static float _nextRefresh;
        private static long _me;

        internal static bool Enabled =>
            DslConfig.ShowOwnerNames != null && DslConfig.ShowOwnerNames.Value;

        internal static void Toggle()
        {
            DslConfig.ShowOwnerNames.Value = !DslConfig.ShowOwnerNames.Value;
            DiagnoseServerLagMod.Message(DslConfig.ShowOwnerNames.Value
                ? "Owner names on"
                : "Owner names off");
        }

        /// <summary>The owner's name for a ZDO, or null when it is ours, ownerless or unknown.</summary>
        internal static string For(ZDO zdo)
        {
            if (zdo == null) return null;
            long owner = zdo.GetOwner();
            if (owner == 0L) return null;

            Refresh();
            if (owner == _me) return null;              // ours: nothing worth saying
            return _names.TryGetValue(owner, out string name) ? name : "another player";
        }

        private static void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;

            _names.Clear();
            _me = 0L;
            try
            {
                var znet = ZNet.instance;
                if (znet == null) return;

                var server = znet.GetServerPeer();
                if (server != null) _names[server.m_uid] = "server";

                _players.Clear();
                _players.AddRange(znet.GetPlayerList());
                foreach (var info in _players)
                {
                    long uid = info.m_characterID.UserID;
                    if (!_names.ContainsKey(uid)) _names[uid] = info.m_name;
                }

                // Our own id, so the common case - we own it - costs one comparison and says
                // nothing. A nameplate on everything would be noise rather than a diagnostic.
                var me = Player.m_localPlayer;
                if (me != null)
                {
                    var view = me.GetComponent<ZNetView>();
                    if (view != null && view.IsValid()) _me = view.GetZDO().GetOwner();
                }
            }
            catch { /* a name we cannot resolve is one we simply do not show */ }
        }
    }

    /// <summary>
    /// Appends the owner to a creature's name. See <see cref="OwnerNames"/> for why this method and
    /// not the health bar itself.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    internal static class HoverNamePatch
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            try
            {
                if (!OwnerNames.Enabled || __instance == null) return;
                var view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) return;

                string owner = OwnerNames.For(view.GetZDO());
                if (owner == null) return;
                // Dimmed, so the creature's own name still reads first.
                __result = $"{__result} <color=#B0A08C>[{owner}]</color>";
            }
            catch { /* a label that cannot be built is simply not added */ }
        }
    }
}
