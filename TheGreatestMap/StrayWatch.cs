using System;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Markers of this mod's standing on the map with no record behind them, counted every few
    /// seconds and said in the log whenever the number moves.
    ///
    /// <para>This is the one fault the player cannot see. Such a pin carries a type number this
    /// mod made up and no record, so every switch in the panel asks the record it has not got and
    /// leaves it alone, the counts do not include it, and the picture it wears is whatever its
    /// stale number happens to mean this session. A map can be carpeted with thousands of them
    /// while the panel honestly reports the few hundred it knows about, which is exactly what
    /// happened to one player: 4,984 of them over 5,148 real markers, found only because he was
    /// asked to type a console command.</para>
    ///
    /// <para>Saying it in the log is not a fix and is not meant as one. It is the measurement the
    /// whole thing was missing: a line with a wall clock on it says whether they came out of the
    /// character file at login or appeared at some later moment, and the second of those is a
    /// different fault from the first. Two rounds of reasoning went the wrong way for want of
    /// this line.</para>
    ///
    /// <para>The cost is one pass over the pin list every five seconds -- a dictionary lookup per
    /// pin, a fraction of a millisecond even on the largest map seen -- and nothing at all while
    /// the number is steady, which is every session where nothing is wrong.</para>
    /// </summary>
    internal static class StrayWatch
    {
        private const float Every = 5f;

        private static float _next;
        private static int _said;

        internal static void Reset()
        {
            _next = 0f;
            _said = 0;
        }

        private static string Now => DateTime.Now.ToString("HH:mm:ss");

        internal static void Update()
        {
            if (Minimap.instance == null) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Every;

            ClientPins.StrayPins(out int orphans, out _);
            if (orphans == _said) return;
            int was = _said;
            _said = orphans;

            if (orphans > was)
            {
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] {Now} {orphans - was} marker(s) of this mod's lost the record behind them " +
                    $"({orphans} on the map now with none). They are drawn but cannot be hidden, counted or " +
                    "identified, and they wear whatever picture their number means this session. They are kept " +
                    "out of the character file, and tgm_clearlocal takes them off the map at once.");
                return;
            }

            TheGreatestMapMod.Log.LogInfo(
                orphans == 0
                    ? $"[TheGreatestMap] {Now} the {was} marker(s) with no record behind them are off the map."
                    : $"[TheGreatestMap] {Now} {was - orphans} marker(s) with no record behind them went; {orphans} left.");
        }
    }
}
