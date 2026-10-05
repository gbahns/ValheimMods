using System;

namespace TheGreatestMap
{
    /// <summary>
    /// Just the two IconRegistry members the pin store touches, copied verbatim from
    /// TheGreatestMap/IconRegistry.cs. The real file is 438 lines that reach into TgmConfig,
    /// Localization, Minimap, ZNet and Player, none of which exist outside the game.
    ///
    /// Normalize and SameKey are byte-for-byte the originals, because SameKey decides whether two
    /// suppressions are the same during a merge - getting it wrong would silently drop or duplicate
    /// an erased spot.
    ///
    /// LegacyKey is only reached when reading a format older than version 2. These stores are
    /// version 4, so it is never called; it throws rather than returning a plausible wrong answer,
    /// so if a v1 file ever turns up here the merge fails loudly instead of rewriting icons.
    /// </summary>
    internal static class IconRegistry
    {
        internal static string Normalize(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            key = key.Trim();
            if (key.Length == 0) return null;
            if (key.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) return "item:" + key.Substring(5).Trim();
            if (key.StartsWith("pin:", StringComparison.OrdinalIgnoreCase)) return "pin:" + key.Substring(4).Trim();
            if (key.StartsWith("piece:", StringComparison.OrdinalIgnoreCase)) return "piece:" + key.Substring(6).Trim();
            return "item:" + key;
        }

        internal static bool SameKey(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        internal static string LegacyKey(int type)
        {
            throw new NotSupportedException(
                "This store is in a pre-version-2 format, whose icon keys need the full IconRegistry. " +
                "Merge it in game instead.");
        }
    }
}
