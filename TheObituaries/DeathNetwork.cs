using System;
using HarmonyLib;

namespace TheObituaries
{
    /// <summary>
    /// Carries an obituary from the victim's client to every client, and shows it.
    ///
    /// One routed RPC to Everybody: the sender's own ZRoutedRpc handles it locally as well as
    /// forwarding it, the server relays it to every other peer, and a server or client without
    /// this mod drops it as an unknown method. So the server needs nothing installed, and an
    /// unmodded player simply sees no obituary.
    /// </summary>
    internal static class DeathNetwork
    {
        private const string RpcName = "DM_Obituary";

        // Chat.m_hideTimer is private; zeroing it is what makes the chat window pop up for a
        // line, the same thing Chat does for an incoming message.
        private static readonly AccessTools.FieldRef<Chat, float> HideTimer =
            AccessTools.FieldRefAccess<Chat, float>("m_hideTimer");

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            rpc.Register<ZPackage>(RpcName, RPC_Obituary);
        }

        internal static void Send(Notice notice)
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || notice == null) return;
            rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, notice.Pack());
        }

        private static void RPC_Obituary(long sender, ZPackage pkg)
        {
            try
            {
                var notice = Notice.Unpack(pkg);
                if (notice != null) Show(notice);
            }
            catch (Exception e)
            {
                TheObituariesMod.Log.LogWarning("Could not show an obituary: " + e);
            }
        }

        /// <summary>Shows an obituary here, per the display settings. Also used by the console preview.</summary>
        internal static void Show(Notice notice)
        {
            if (!TheObituariesMod.ModEnabled.Value) return;

            string rich = notice.Rich();

            if (TheObituariesMod.ShowInChat.Value && Chat.instance != null)
            {
                Chat.instance.AddString(rich);
                HideTimer(Chat.instance) = 0f;
            }

            var hud = MessageHud.instance;
            if (hud != null)
            {
                switch (TheObituariesMod.OnScreen.Value)
                {
                    case TheObituariesMod.ScreenSpot.TopLeft: hud.ShowMessage(MessageHud.MessageType.TopLeft, rich); break;
                    case TheObituariesMod.ScreenSpot.Center:  hud.ShowMessage(MessageHud.MessageType.Center,  rich); break;
                }

                if (TheObituariesMod.YouFragged.Value && notice.KillerIsPlayer
                    && Player.m_localPlayer != null && notice.KillerId != ZDOID.None
                    && notice.KillerId == Player.m_localPlayer.GetZDOID())
                {
                    hud.ShowMessage(MessageHud.MessageType.Center,
                        "You fragged " + TheObituariesMod.Colored(notice.Victim, TheObituariesMod.VictimColor));
                }
            }

            if (TheObituariesMod.LogObituaries.Value)
                TheObituariesMod.Log.LogInfo(notice.Plain());
        }
    }
}
