using System;
using System.Globalization;
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
            Access.ClaimObituary("{v} was smote for being an idiot.");
            player.Damage(hit);
        }

        /// <summary>
        /// Whether a name misspells the place. The word counts wherever it stands on its own, in
        /// any case: "The Alter", "Alter Portal", "the alter 2". It also counts when it has been
        /// dressed up, because every reading is tried with the dressing taken off -- punctuation
        /// and spaces removed ("Alt-er Portal", "a l t e r", "A-L-T-E-R"), accents flattened
        /// ("áltér"), letters that merely look Latin put back ("аltеr" in Cyrillic), and digits and
        /// symbols read as the letters they stand in for ("a1ter", "4lt3r", "@lter").
        ///
        /// A longer word that merely contains the letters is not a misspelling of anything and
        /// passes: "Altered", "Alternate", "Walter's place". That is the line, and it is why this
        /// works word by word rather than looking for the letters anywhere in the name.
        /// </summary>
        private static bool Counts(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (Hits(name)) return true;   // the whole name, spaces and all: "a l t e r"
            foreach (var word in name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                if (Hits(word)) return true;   // or any one word of it: "Alt-er Portal"
            return false;
        }

        /// <summary>
        /// Does this read as the word once it is stripped back? Tried both ways round, because the
        /// two readings disagree: "Alter 4" is the word with a number after it, while "4lter" is
        /// the word with a number inside it, and no single pass can take both.
        /// </summary>
        private static bool Hits(string text)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                string reduced = Reduce(text, pass == 1);
                if (IsMisspelling(reduced)) return true;
                if (IsMisspelling(Squeeze(reduced))) return true;   // a letter leant on: "allterr"
            }
            return false;
        }

        /// <summary>A run of the same letter read as one of it, so a stutter is no escape.</summary>
        private static string Squeeze(string word)
        {
            var sb = new StringBuilder(word.Length);
            foreach (char c in word)
                if (sb.Length == 0 || sb[sb.Length - 1] != c) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>The word itself, or its plural: "Alters" misspells "Altars" just as surely.</summary>
        private static bool IsMisspelling(string word)
        {
            if (Same(word)) return true;
            int last = word.Length - 1;
            return last > 0 && (word[last] == 's' || word[last] == 'S') && Same(word.Substring(0, last));
        }

        private static bool Same(string word)
        {
            return string.Equals(word, Misspelling, StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, WithThe, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The letters of a piece of text and nothing else, lowercased, with accents flattened and
        /// look-alike letters from other alphabets put back. With <paramref name="leet"/>, digits
        /// and symbols that stand in for letters are read as those letters first.
        /// </summary>
        private static string Reduce(string text, bool leet)
        {
            string decomposed = text;
            try { decomposed = text.Normalize(NormalizationForm.FormD); }
            catch (ArgumentException) { }   // a lone surrogate; the raw text will do
            var sb = new StringBuilder(decomposed.Length);
            foreach (char raw in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;   // the accent itself
                char c = Latin(raw);
                if (leet) c = Spelt(c);
                if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Letters from other alphabets, and full-width ones, that are Latin to the eye.</summary>
        private static char Latin(char c)
        {
            if (c >= '\uFF21' && c <= '\uFF3A') return (char)(c - 0xFF21 + 'A');   // full-width capitals
            if (c >= '\uFF41' && c <= '\uFF5A') return (char)(c - 0xFF41 + 'a');   // and smalls
            switch (c)
            {
                case '\u0430': return 'a';   // Cyrillic a
                case '\u0435': return 'e';   // Cyrillic e
                case '\u043E': return 'o';   // Cyrillic o
                case '\u0441': return 'c';   // Cyrillic s, which looks like c
                case '\u0440': return 'p';   // Cyrillic r, which looks like p
                case '\u0445': return 'x';   // Cyrillic h, which looks like x
                case '\u0442': return 't';   // Cyrillic t
                case '\u0456': return 'i';   // Ukrainian i
                case '\u04CF': return 'l';   // Cyrillic palochka
                case '\u03B1': return 'a';   // Greek alpha
                case '\u03B5': return 'e';   // Greek epsilon
                case '\u03BF': return 'o';   // Greek omicron
                case '\u03C4': return 't';   // Greek tau
                case '\u03B9': return 'i';   // Greek iota
                default: return c;
            }
        }

        /// <summary>Digits and symbols read as the letters they are standing in for.</summary>
        private static char Spelt(char c)
        {
            switch (c)
            {
                case '0': return 'o';
                case '1': case '|': return 'l';
                case '3': return 'e';
                case '4': return 'a';
                case '5': case '$': return 's';
                case '7': return 't';
                case '@': return 'a';
                case '!': return 'i';
                default: return c;
            }
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
