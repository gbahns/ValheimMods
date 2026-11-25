using System.Collections.Generic;

namespace TheGreatestMap
{
    /// <summary>
    /// What "show all" showed, remembered so it can be taken back.
    ///
    /// <para><b>Why.</b> Showing everything hidden used to be a one-way door. It does not suspend
    /// the hiding, it empties it: the hidden pictures, the hidden places, the hidden biomes, every
    /// per-kind switch, the master switch, the worked-out deposits, the hidden landmarks, both
    /// lists of crossed-off markers, and the markers put away one at a time. All of it gone, with
    /// no way back -- and the button is one press in the corner of the marker list, with the same
    /// thing again in every marker's right-click menu. The more use a player gets out of hiding,
    /// the more that one press costs them, which is the wrong way round.</para>
    ///
    /// <para><b>What it does not keep.</b> The find box, which "show all" also clears: a search
    /// is a question being asked rather than a setting, and nobody wants yesterday's question
    /// back. And "Hide your markers" behind the gear, which "show all" has never touched, being
    /// an override of these switches rather than one of them.</para>
    ///
    /// <para><b>How long it lasts.</b> Until the next "show all", or until the world changes. Not
    /// written to the character: a stash that outlived the session would offer to undo something
    /// the player had long since settled into. It needs no scope of its own beyond that, because
    /// the offer disappears by itself -- the corner shows "put back" only while nothing is
    /// hidden, so the first thing a player hides again replaces it with "show all".</para>
    /// </summary>
    internal static class PutBack
    {
        private sealed class Stash
        {
            internal string Icons, OwnIcons, Places, Biomes, Landmarks, Cleared, Searched;
            internal bool AllMarkers, ClearedDeposits;
            internal readonly Dictionary<Category, bool> Kinds = new Dictionary<Category, bool>();
            internal readonly List<PinKey> Markers = new List<PinKey>();
        }

        private static Stash _stash;

        /// <summary>Is there a hiding to put back?</summary>
        internal static bool Any => _stash != null;

        internal static void Forget() => _stash = null;

        /// <summary>
        /// Remember what is hidden now. Called by <see cref="TgmConfig.ShowEverything"/> itself
        /// rather than by its callers, so that no way of showing everything can forget to.
        /// </summary>
        internal static void Take()
        {
            var was = new Stash
            {
                Icons = Text(TgmConfig.HiddenIcons),
                OwnIcons = Text(TgmConfig.HiddenOwnIcons),
                Places = Text(TgmConfig.HiddenPlaces),
                Biomes = Text(TgmConfig.HiddenBiomes),
                Landmarks = Text(TgmConfig.HiddenLandmarks),
                Cleared = Text(TgmConfig.HiddenCleared),
                Searched = Text(TgmConfig.HiddenSearched),
                AllMarkers = TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value,
                ClearedDeposits = TgmConfig.ShowClearedDeposits == null || TgmConfig.ShowClearedDeposits.Value,
            };
            foreach (var kv in TgmConfig.ShowKind) was.Kinds[kv.Key] = kv.Value.Value;
            ViewPrefs.CopyInto(was.Markers);
            _stash = was;
        }

        /// <summary>
        /// Hide again everything the last "show all" showed.
        ///
        /// <para>Only the switches that were off go back off. Writing every one of them would
        /// undo whatever the player has changed since as well, and a button offering to put back
        /// what you hid should not also take away what you have unhidden.</para>
        /// </summary>
        internal static void Apply()
        {
            var was = _stash;
            if (was == null) return;
            // Dropped first: this writes a dozen settings, each of which may be watched, and a
            // stash still standing while they are written is one that could be re-applied
            // halfway through by anything those watchers reach.
            _stash = null;

            Put(TgmConfig.HiddenIcons, was.Icons);
            Put(TgmConfig.HiddenOwnIcons, was.OwnIcons);
            Put(TgmConfig.HiddenPlaces, was.Places);
            Put(TgmConfig.HiddenBiomes, was.Biomes);
            Put(TgmConfig.HiddenLandmarks, was.Landmarks);
            Put(TgmConfig.HiddenCleared, was.Cleared);
            Put(TgmConfig.HiddenSearched, was.Searched);
            if (!was.AllMarkers && TgmConfig.ShowAllMarkers != null) TgmConfig.ShowAllMarkers.Value = false;
            if (!was.ClearedDeposits && TgmConfig.ShowClearedDeposits != null)
                TgmConfig.ShowClearedDeposits.Value = false;
            foreach (var kv in was.Kinds)
                if (!kv.Value && TgmConfig.ShowKind.TryGetValue(kv.Key, out var entry) && entry.Value)
                    entry.Value = false;
            foreach (var id in was.Markers) ViewPrefs.Hide(id);

            WorldPins.ForgetHidden();
            CrossedOff.ForgetHidden();
            CrossedOff.ForgetCleared();
            ClientPins.Restyle();
        }

        /// <summary>
        /// What putting it back would hide again, counted and named -- "4 kinds, 7 types" -- or
        /// empty. The sentence around it belongs to whoever is saying it.
        /// </summary>
        internal static string Counts()
        {
            var was = _stash;
            if (was == null) return "";
            var parts = new List<string>(5);
            int kinds = 0;
            foreach (var kv in was.Kinds) if (!kv.Value) kinds++;
            if (kinds > 0) parts.Add(kinds == 1 ? "1 kind" : kinds + " kinds");
            int icons = Items(was.Icons) + Items(was.OwnIcons);
            if (icons > 0) parts.Add(icons == 1 ? "1 type" : icons + " types");
            if (was.Markers.Count > 0)
                parts.Add(was.Markers.Count == 1 ? "1 marker" : was.Markers.Count + " markers");
            int done = Items(was.Searched) + Items(was.Cleared);
            if (done > 0) parts.Add(done == 1 ? "1 crossed off" : done + " crossed off");
            int world = Items(was.Landmarks);
            if (world > 0) parts.Add(world == 1 ? "1 landmark" : world + " landmarks");
            if (!was.ClearedDeposits) parts.Add("cleared deposits");
            return string.Join(", ", parts.ToArray());
        }

        private static string Text(BepInEx.Configuration.ConfigEntry<string> entry) =>
            entry != null ? entry.Value ?? "" : "";

        /// <summary>
        /// The remembered list back into a setting, added to whatever is there rather than
        /// written over it.
        ///
        /// <para>The corner button cannot be pressed while anything is hidden -- it is "show all"
        /// again by then -- so there is usually nothing there to add to. tgm_hideagain can be run
        /// at any time though, and overwriting would quietly throw away whatever the player had
        /// hidden since, which is the same complaint as the one this class exists for.</para>
        /// </summary>
        private static void Put(BepInEx.Configuration.ConfigEntry<string> entry, string was)
        {
            if (entry == null || string.IsNullOrEmpty(was)) return;
            string merged = Both(entry.Value, was);
            if (!string.Equals(entry.Value, merged, System.StringComparison.Ordinal)) entry.Value = merged;
        }

        /// <summary>Both lists, each entry once, whatever is there now keeping its place.</summary>
        private static string Both(string now, string was)
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var kept = new List<string>();
            foreach (var line in new[] { now, was })
                foreach (var raw in (line ?? "").Split(','))
                {
                    string one = raw.Trim();
                    if (one.Length == 0 || !seen.Add(one)) continue;
                    kept.Add(one);
                }
            return string.Join(",", kept.ToArray());
        }

        /// <summary>How many things a comma-separated setting names.</summary>
        private static int Items(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            int n = 0;
            foreach (var part in line.Split(','))
                if (!string.IsNullOrEmpty(part.Trim())) n++;
            return n;
        }
    }
}
