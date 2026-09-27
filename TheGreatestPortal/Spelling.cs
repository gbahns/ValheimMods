using System;
using System.Text;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>
    /// A portal at the sacrificial stones in the middle of the world, named "alter". There is a
    /// word for that place and the word is altar.
    ///
    /// Off unless a server turns it on. The judgment runs on the naming player's own client: it
    /// is their own game that strikes them down, so no other machine decides how they die, and a
    /// player who has not installed the mod is never caught by it.
    /// </summary>
    internal static class Spelling
    {
        private const string Misspelling = "alter";
        private const string WithThe = "thealter";   // the way the place is usually referred to
        internal const float Radius = 50f;

        /// <summary>
        /// Called once a portal has been named. Any other name, or a portal elsewhere, passes.
        /// </summary>
        internal static void Judge(string name, Vector3 portalPos)
        {
            if (TgpConfig.AltarSpellingIsFatal == null || !TgpConfig.AltarSpellingIsFatal.Value) return;
            if (!Counts(name)) return;
            var player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;
            if (Utils.DistanceXZ(Stones(), portalPos) > Radius) return;

            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] A portal within {Radius:0} m of the sacrificial stones was named '{Misspelling}'. It is spelled altar.");
            player.Message(MessageHud.MessageType.Center, "It is spelled <color=orange>ALTAR</color>");

            var hit = new HitData();
            hit.m_damage.m_damage = 1E10f;   // armor is not the point
            hit.m_blockable = false;
            hit.m_dodgeable = false;
            hit.m_point = player.transform.position + Vector3.up;
            hit.m_dir = Vector3.down;
            player.Damage(hit);
        }

        /// <summary>
        /// Whether a name misspells the place. The word counts wherever it appears as a word of
        /// its own, so "The Alter", "Alter Portal" and "the alter 2" are all caught, and so does
        /// the whole name with everything but its letters taken out, which catches "Alter!",
        /// "a l t e r" and "TheAlter". A longer word that merely contains the letters is not a
        /// misspelling of anything and passes: "Altered", "Alternate", "Walter's place".
        /// </summary>
        private static bool Counts(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (IsMisspelling(LettersOnly(name))) return true;
            var word = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetter(c)) { word.Append(c); continue; }
                if (IsMisspelling(word.ToString())) return true;
                word.Length = 0;
            }
            return IsMisspelling(word.ToString());
        }

        private static bool IsMisspelling(string word)
        {
            return string.Equals(word, Misspelling, StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, WithThe, StringComparison.OrdinalIgnoreCase);
        }

        private static string LettersOnly(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var letters = new StringBuilder(text.Length);
            foreach (char c in text)
                if (char.IsLetter(c)) letters.Append(c);
            return letters.ToString();
        }

        /// <summary>
        /// Where the sacrificial stones stand. The game locates its own start temple exactly this
        /// way, and the answer travels to clients with the map icons the server sends, so it needs
        /// nothing loaded: renaming that portal from the other side of the world is judged too. If
        /// the lookup ever fails, the middle of the world is where the stones are anyway.
        /// </summary>
        private static Vector3 Stones()
        {
            var zones = ZoneSystem.instance;
            string start = Game.instance != null ? Game.instance.m_StartLocation : "StartTemple";
            if (zones != null && zones.GetLocationIcon(start, out Vector3 pos)) return pos;
            return Vector3.zero;
        }
    }
}
