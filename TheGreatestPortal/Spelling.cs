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
        internal const float Radius = 50f;

        /// <summary>
        /// Called once a portal has been named. Only the letters count, in any case, so dressing
        /// the word up with spaces, digits or punctuation ("Alter!", "a l t e r", "Alter 2") does
        /// not save anyone, while a longer word that merely starts the same way ("Alternate") is
        /// fine. Any other name, or a portal elsewhere, passes.
        /// </summary>
        internal static void Judge(string name, Vector3 portalPos)
        {
            if (TgpConfig.AltarSpellingIsFatal == null || !TgpConfig.AltarSpellingIsFatal.Value) return;
            if (!string.Equals(LettersOnly(name), Misspelling, StringComparison.OrdinalIgnoreCase)) return;
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
