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
        private static string _mine;

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

            // An ownerless creature is worth labelling rather than skipping: nobody is running its
            // AI at all, which is why distant ones stand inert until somebody gets close enough for
            // the two-second pass to hand them over. On one solo measurement 12 of 23 nearby
            // creatures were in this state, and this is the only way to see which ones.
            if (owner == 0L) return "unowned";

            Refresh();

            // Silent about your own by default, and that is the right default: the labels exist
            // to show where an interaction is *going*, and one to your own machine goes nowhere.
            // A label on every creature you own would be clutter obscuring the few that matter.
            //
            // It is a setting only because the default makes the feature untestable alone - you
            // own everything near you, and the unowned ones are too far off for the game to draw a
            // nameplate on at all, since EnemyHud only labels what has been hovered or hit
            // recently. Turning this on is how you see it working before there is anybody to see
            // it working against.
            if (owner == _me)
                return DslConfig.ShowMyOwnOwnership != null && DslConfig.ShowMyOwnOwnership.Value
                    ? (_mine ?? "you")
                    : null;
            return _names.TryGetValue(owner, out string name) ? name : "another player";
        }

        /// <summary>Adds the owner label to a name, or returns it untouched when there is nothing to say.</summary>
        internal static string Append(string name, ZNetView view)
        {
            if (view == null || !view.IsValid()) return name;
            string owner = For(view.GetZDO());
            if (owner == null) return name;
            // Dimmed, so the creature's own name still reads first.
            return $"{name} <color=#B0A08C>[{owner}]</color>";
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
                    string mine = me.GetPlayerName();
                    _mine = string.IsNullOrEmpty(mine) ? null : mine;
                }
            }
            catch { /* a name we cannot resolve is one we simply do not show */ }
        }
    }

    /// <summary>
    /// Appends the owner to a creature's name. See <see cref="OwnerNames"/> for why this method and
    /// not the health bar itself.
    ///
    /// Skips anything with a Tameable, because Character.GetHoverName does not name those itself -
    /// it hands straight off to Tameable.GetHoverName, which is patched separately below. Without
    /// this guard a tame would be labelled twice, once by each.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    internal static class HoverNamePatch
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            try
            {
                if (!OwnerNames.Enabled || __instance == null) return;
                if (__instance.GetComponent<Tameable>() != null) return;   // handled by TameHoverNamePatch
                __result = OwnerNames.Append(__result, __instance.GetComponent<ZNetView>());
            }
            catch { /* a label that cannot be built is simply not added */ }
        }
    }

    /// <summary>
    /// The same label for tamed animals, which never reach the method above:
    ///
    ///     public virtual string GetHoverName() {
    ///         Tameable component = GetComponent&lt;Tameable&gt;();
    ///         if ((bool)component) return component.GetHoverName();
    ///
    /// so wolves, boars and lox were the one category the first version could not label - and they
    /// are a category worth labelling, since a tame somebody else owns behaves exactly like any
    /// other object of theirs. Tameable.GetHoverName also runs RemoveRichTextTags over its own
    /// text, which is why the label is appended after it rather than woven into it.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverName))]
    internal static class TameHoverNamePatch
    {
        private static void Postfix(Tameable __instance, ref string __result)
        {
            try
            {
                if (!OwnerNames.Enabled || __instance == null) return;
                __result = OwnerNames.Append(__result, __instance.GetComponent<ZNetView>());
            }
            catch { }
        }
    }
}
