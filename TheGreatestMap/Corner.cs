namespace TheGreatestMap
{
    /// <summary>
    /// The two buttons in the marker list's title strip -- "hide all" and "show all" -- and the
    /// one state they share.
    ///
    /// <para><b>Why a state rather than two conditions.</b> Each button used to work out whether
    /// it was lit by asking the map: "hide all" read the master override, "show all" read whether
    /// anything at all was hidden and whether a stash existed. Two derived answers to one
    /// question is how the pair came to disagree -- the override on with nothing else hidden lit
    /// one button while the other claimed everything was shown. There is one state here, three
    /// values wide, and both buttons read it.</para>
    ///
    /// <para><b>The rules.</b> Neither is lit to begin with. Pressing a dark button lights it and
    /// does what it says. Pressing the lit one turns it off and undoes it, leaving the other
    /// dark. Pressing the dark one while the other is lit turns that one off first, undoing it,
    /// and then does the press -- so the two can never be lit at once, which they could not mean
    /// at once either.</para>
    ///
    /// <para><b>What "undoing it" is.</b> For "hide all" the override goes back off, and whatever
    /// the switches in the list were hiding underneath it is still hiding -- it holds markers off
    /// rather than switching any of them off. For "show all" it is <see cref="PutBack"/>: the
    /// hiding that press cleared goes back on.</para>
    /// </summary>
    internal static class Corner
    {
        internal enum Mode
        {
            /// <summary>Neither button lit: the map is however the switches in the list leave it.</summary>
            None,
            /// <summary>"Hide all" lit: the master override is holding every marker of ours off.</summary>
            HideAll,
            /// <summary>"Show all" lit: nothing is hidden, and what was is remembered.</summary>
            ShowAll,
        }

        private static Mode _now;

        internal static Mode Now => _now;

        /// <summary>
        /// A press on either button. <paramref name="which"/> is the button pressed, never
        /// <see cref="Mode.None"/>.
        /// </summary>
        internal static void Press(Mode which)
        {
            bool again = _now == which;
            TurnOff();
            if (!again) TurnOn(which);
            ClientPins.Restyle();
        }

        /// <summary>
        /// What tgm_show does. The console asks for everything shown rather than for a button to
        /// be pressed, so asking twice shows everything twice rather than undoing it.
        /// </summary>
        internal static void ShowAll()
        {
            if (_now != Mode.ShowAll) Press(Mode.ShowAll);
        }

        /// <summary>
        /// What tgm_hideagain does. Also reachable once the light has gone out by itself -- the
        /// stash outlives the lit button, so hiding something by hand and then asking for the
        /// rest back still works.
        /// </summary>
        internal static void HideAgain()
        {
            if (_now == Mode.ShowAll) { Press(Mode.ShowAll); return; }
            PutBack.Apply();
            ClientPins.Restyle();
        }

        /// <summary>
        /// Forget which button was lit without undoing anything. For a new world, and for the
        /// moment the player hides something by hand while "show all" is lit: everything is no
        /// longer shown, so the light would be saying something untrue. The stash stays, so
        /// tgm_hideagain can still reach it.
        /// </summary>
        internal static void Forget() => _now = Mode.None;

        private static void TurnOff()
        {
            if (_now == Mode.HideAll)
            {
                if (TgmConfig.ShowAllMarkers != null) TgmConfig.ShowAllMarkers.Value = true;
            }
            else if (_now == Mode.ShowAll)
            {
                PutBack.Apply();
            }
            _now = Mode.None;
        }

        private static void TurnOn(Mode which)
        {
            if (which == Mode.HideAll)
            {
                if (TgmConfig.ShowAllMarkers != null) TgmConfig.ShowAllMarkers.Value = false;
            }
            else
            {
                // Takes the stash itself, before it writes anything, so that every way of showing
                // everything can be taken back and not only this one.
                TgmConfig.ShowEverything();
            }
            _now = which;
        }
    }
}
