using System;
using System.Collections;
using TMPro;
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

        // A routed RPC to Everybody is handed to our own handler synchronously, inside the
        // Invoke call, before it goes out; this flag is how the handler knows the death is ours.
        private static bool _sendingOwn;

        internal static void Send(Notice notice)
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || notice == null) return;
            _sendingOwn = true;
            try { rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, notice.Pack()); }
            finally { _sendingOwn = false; }
        }

        private static void RPC_Obituary(long sender, ZPackage pkg)
        {
            try
            {
                var notice = Notice.Unpack(pkg);
                if (notice != null) Show(notice, own: _sendingOwn);
            }
            catch (Exception e)
            {
                TheObituariesMod.Log.LogWarning("Could not show an obituary: " + e);
            }
        }

        /// <summary>
        /// Shows an obituary here, per the display settings. <paramref name="own"/> is the
        /// local player's own death, which gets the longer center stay: they are lying there
        /// anyway, while everyone else is busy. Also used by the console preview.
        /// </summary>
        internal static void Show(Notice notice, bool own = false)
        {
            float centerStay = own ? TheObituariesMod.CenterSeconds.Value : TheObituariesMod.CenterSecondsOthers.Value;
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
                        ShowCenter(rich, centerStay);
                        break;
                }

                if (TheObituariesMod.YouFragged.Value && notice.KillerIsPlayer
                    && Player.m_localPlayer != null && notice.KillerId != ZDOID.None
                    && notice.KillerId == Player.m_localPlayer.GetZDOID())
                {
                    ShowCenter("You fragged " + TheObituariesMod.Colored(TheObituariesMod.Bold(notice.Victim), TheObituariesMod.VictimColor),
                        TheObituariesMod.CenterSecondsOthers.Value);
                }
            }

            if (TheObituariesMod.LogObituaries.Value)
                TheObituariesMod.Log.LogInfo(notice.Plain());
        }

        /// <summary>
        /// The center message has no duration to set: ShowMessage queues two alpha crossfades on
        /// its text, to 1 at once and then to 0 over four seconds, one applied per frame. So a
        /// longer stay is: let those two land, cancel the fade by crossfading to 1 in no time,
        /// hold, and start the same four-second fade ourselves when the time is up. If another
        /// center message replaces the text meanwhile, it takes over and the hold stops.
        /// </summary>
        private static void ShowCenter(string text, float seconds)
        {
            var hud = MessageHud.instance;
            if (hud == null) return;
            hud.ShowMessage(MessageHud.MessageType.Center, text);
            var host = TheObituariesMod.Instance;
            if (host == null || seconds <= FadeSeconds) return;
            if (_centerRepeat != null) host.StopCoroutine(_centerRepeat);
            _centerRepeat = host.StartCoroutine(HoldCenter(text, seconds));
        }

        private const float FadeSeconds = 4f;   // the game's own fade-out

        private static IEnumerator HoldCenter(string text, float seconds)
        {
            // The two queued crossfades are applied on the next two frames the HUD updates.
            yield return new WaitForSecondsRealtime(0.25f);
            var label = MessageHud.instance != null ? MessageHud.instance.m_messageCenterText : null;
            if (label == null || label.text != text) { _centerRepeat = null; yield break; }
            label.CrossFadeAlpha(1f, 0f, ignoreTimeScale: true);

            float hold = seconds - FadeSeconds - 0.25f;
            float elapsed = 0f;
            while (elapsed < hold)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                if (label.text != text) { _centerRepeat = null; yield break; }
            }
            label.CrossFadeAlpha(0f, FadeSeconds, ignoreTimeScale: true);
            _centerRepeat = null;
        }
    }
}
