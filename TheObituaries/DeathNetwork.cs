using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

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

        // Chat.m_hideTimer is private; the window shows while it is below m_hideDelay, so
        // setting it negative keeps the window up for longer than a chat message would.
        private static readonly AccessTools.FieldRef<Chat, float> HideTimer =
            AccessTools.FieldRefAccess<Chat, float>("m_hideTimer");

        private static Coroutine _centerRepeat;

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

            if (TheObituariesMod.ShowInChat.Value && Chat.instance != null)
            {
                Chat.instance.AddString(notice.Rich(sized: true));
                // 0 would give the vanilla stay (m_hideDelay, 10 s); below zero gives more.
                float stay = Mathf.Max(0f, TheObituariesMod.ChatSeconds.Value);
                HideTimer(Chat.instance) = Mathf.Min(0f, Chat.instance.m_hideDelay - stay);
            }

            var hud = MessageHud.instance;
            if (hud != null)
            {
                string rich = notice.Rich(sized: false);
                switch (TheObituariesMod.OnScreen.Value)
                {
                    case TheObituariesMod.ScreenSpot.TopLeft:
                        hud.ShowMessage(MessageHud.MessageType.TopLeft, rich);
                        break;
                    case TheObituariesMod.ScreenSpot.Center:
                        ShowCenter(rich, TheObituariesMod.CenterSeconds.Value);
                        break;
                }

                if (TheObituariesMod.YouFragged.Value && notice.KillerIsPlayer
                    && Player.m_localPlayer != null && notice.KillerId != ZDOID.None
                    && notice.KillerId == Player.m_localPlayer.GetZDOID())
                {
                    ShowCenter("You fragged " + TheObituariesMod.Colored(TheObituariesMod.Bold(notice.Victim), TheObituariesMod.VictimColor),
                        TheObituariesMod.CenterSeconds.Value);
                }
            }

            if (TheObituariesMod.LogObituaries.Value)
                TheObituariesMod.Log.LogInfo(notice.Plain());
        }

        /// <summary>
        /// The center message fades out over four seconds and there is no duration to set,
        /// so a longer stay is a repeat: show it again every few seconds until the time is up.
        /// A newer message cancels the repeat of the one before it.
        /// </summary>
        private static void ShowCenter(string text, float seconds)
        {
            var hud = MessageHud.instance;
            if (hud == null) return;
            hud.ShowMessage(MessageHud.MessageType.Center, text);
            var host = TheObituariesMod.Instance;
            if (host == null || seconds <= 4f) return;
            if (_centerRepeat != null) host.StopCoroutine(_centerRepeat);
            _centerRepeat = host.StartCoroutine(RepeatCenter(text, seconds));
        }

        private static IEnumerator RepeatCenter(string text, float seconds)
        {
            float shown = 0f;
            const float step = 3f;   // re-show a little before the four-second fade finishes
            while (shown + step < seconds)
            {
                yield return new WaitForSecondsRealtime(step);
                shown += step;
                var hud = MessageHud.instance;
                if (hud == null) yield break;
                hud.ShowMessage(MessageHud.MessageType.Center, text, log: false);
            }
            _centerRepeat = null;
        }
    }
}
