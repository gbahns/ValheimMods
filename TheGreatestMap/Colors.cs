using System;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Colors kept in the config as colors, rather than as text that happens to look like one.
    ///
    /// <para>BepInEx stores any type it has a converter for and ships converters for the
    /// primitives and nothing else, so a mod that wants a color reaches for a string. The config
    /// manager then offers what it offers for every other string: a text box. The player types
    /// six hex digits, alt-tabs to a color wheel to find out what they mean, and comes back to
    /// try again. Registering one converter turns every one of those boxes into a swatch with
    /// sliders behind it, because the config manager draws by type and has known how to draw a
    /// color all along -- the text box was never a decision anybody made, just the shape the
    /// storage left behind.</para>
    ///
    /// <para>The written form does not change. A value already in a config file as #RRGGBB reads
    /// back as exactly the color it always was, which is why this parses the loose forms and not
    /// only the one it writes.</para>
    /// </summary>
    internal static class Colors
    {
        /// <summary>Six hex digits, or eight, with the # still to be put on.</summary>
        private static readonly Regex BareHex = new Regex("^[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$");

        /// <summary>
        /// Teach BepInEx to store a Color, unless somebody already has.
        ///
        /// <para>The converter table is global and shared by every plugin in the game, so the
        /// first mod to register wins and the rest inherit its format. That is left alone
        /// deliberately rather than fought over: a converter that reads a color reads a color,
        /// and replacing another mod's would rewrite its settings into a form it may not read
        /// back. Inheriting a stranger's format is safe in a way that imposing ours is not.</para>
        /// </summary>
        internal static void Register()
        {
            if (TomlTypeConverter.CanConvert(typeof(Color))) return;
            TomlTypeConverter.AddConverter(typeof(Color), new TypeConverter
            {
                ConvertToString = (value, type) => ToHex((Color)value),
                ConvertToObject = (text, type) => FromHex(text),
            });
        }

        /// <summary>Eight digits always, which is the form the config manager shows.</summary>
        internal static string ToHex(Color color) => "#" + ColorUtility.ToHtmlStringRGBA(color);

        /// <summary>
        /// Read a color leniently, and throw rather than guess.
        ///
        /// <para>Empty means transparent. That is what a setting uses to say "nothing", and it is
        /// what Portal Pulse Color meant by an empty string before it was a color; an alpha of
        /// zero says the same thing in a form a color picker can actually set.</para>
        ///
        /// <para>Text that is not a color at all throws, which is the point rather than an
        /// oversight. BepInEx catches it, warns with the offending value, and leaves the setting
        /// at its default. A fallback returned quietly from here would turn a typo into a choice
        /// nobody made and never hear about it.</para>
        /// </summary>
        internal static Color FromHex(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return Color.clear;
            text = text.Trim();
            // Only hex gets a # put on it: ColorUtility reads names like "cyan" as well, and
            // those it only reads without one.
            if (text[0] != '#' && BareHex.IsMatch(text)) text = "#" + text;
            if (ColorUtility.TryParseHtmlString(text, out var parsed)) return parsed;
            throw new FormatException(text + " is not a color this understands; try a hex value such as #B07CFF.");
        }
    }
}
