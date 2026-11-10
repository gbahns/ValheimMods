using System;
using System.Collections.Generic;

namespace TheGreatestMap
{
    /// <summary>
    /// Taking back the word "Manual" from markers nobody placed.
    ///
    /// <para>A pass in <see cref="Migrations"/> used to write "Manual" into every marker that had
    /// a blank source and a picture, on the reasoning that the mod always writes down what it
    /// found, so a blank must mean a person put it there. Format 4 and earlier had no source
    /// field, so markers recorded before it are in exactly that shape -- thousands of them. On
    /// the map that showed this up the guess was wrong 376 times out of 376, and the pass made it
    /// permanent: the source is part of a marker's key, so rewriting it gave each marker a new
    /// identity, which then travelled to everyone.</para>
    ///
    /// <para>What that costs a player is not cosmetic. A marker claiming to be hand-placed is out
    /// of reach of everything that works on recorded markers -- tgm_forgotten passes over it,
    /// tgm_identify refuses to touch it on purpose, no group switch covers it, and the marker menu
    /// offers it a label rather than a kind. And it cannot be told apart from the markers the
    /// player really did place, which is the part that cannot be shrugged off: those are the ones
    /// nothing else in the world remembers.</para>
    ///
    /// <para><b>The height says which is which, and the game is what makes it say so.</b> A player
    /// places a pin by clicking the map, and a click on a map carries no height: vanilla's
    /// <c>MapPointToWorld</c> ends <c>return new Vector3(mx, 0f, my)</c>, so every pin anyone has
    /// ever placed by hand stands at y exactly zero. A marker the recorder wrote down stands where
    /// the thing stands, and nothing in a Valheim world sits at zero -- the sea alone is at
    /// thirty. So a marker claiming to be hand-placed while standing at a real height is a marker
    /// this mod put there.</para>
    ///
    /// <para>Measured before it was believed: on the map this was built for, one copy still in
    /// format 6 -- the last format to write the recorded-or-placed flag down -- holds 4,703
    /// markers, and the height agrees with the flag <b>4,703 times out of 4,703</b>. Every one of
    /// the 62 it calls hand-placed is at zero; not one of the 4,641 it calls recorded is. Naming
    /// such a copy still asks it, not because the height needs the help but because two tests
    /// that disagree mean something is wrong with one of them, and the right answer then is to
    /// touch nothing and say so.</para>
    /// </summary>
    internal static class Unmanual
    {
        internal sealed class Plan
        {
            /// <summary>Standing at a real height, so the recorder is what put them there.</summary>
            internal readonly List<SharedPin> Recorded = new List<SharedPin>();
            /// <summary>
            /// Recorded too, but the marker this one would turn back into is already on the map.
            /// That happens where the recorder wrote the same find down twice, once on either
            /// side of the pass, so the copy carrying the word is a second copy of something
            /// already correct. There is no identity free to give it back, and leaving it would
            /// leave two markers on one spot, so it is erased instead of renamed.
            /// </summary>
            internal readonly List<SharedPin> Duplicate = new List<SharedPin>();
            /// <summary>At zero: somebody clicked the map. Left exactly as they are.</summary>
            internal int Placed;
            /// <summary>
            /// The one sort of marker with a real height that no player placed either: a vanilla
            /// death or bed pin, which <see cref="ClientPins.ImportLocalPins"/> takes on as
            /// hand-placed because it belongs to the player rather than to the world. Nobody
            /// chose to place it, the game did, and it stands where the player died or will wake,
            /// so it carries a real height and the test above would wrongly reach it.
            /// </summary>
            internal int Guarded;
            /// <summary>An older copy of the map contradicts the height. Left alone and reported.</summary>
            internal readonly List<SharedPin> Disputed = new List<SharedPin>();
            /// <summary>How many an older copy was asked about and agreed on.</summary>
            internal int Agreed;
            /// <summary>The copy that was asked, where one was named.</summary>
            internal string Voucher;
            internal int Count => Recorded.Count + Duplicate.Count;
            internal bool Nothing => Count == 0;
        }

        /// <summary>
        /// Work out which markers claiming to be hand-placed were recorded. <paramref name="vouch"/>
        /// is an older copy of this map to cross-check against, or null for the height alone.
        /// </summary>
        internal static Plan Study(MapStore vouch, string voucherName)
        {
            var plan = new Plan();
            // Only worth asking a copy old enough to still carry the flag. A format 7 or later
            // copy was written after it was dropped, so its sources say whatever the pass that
            // caused this left behind -- asking one would confirm the damage rather than correct
            // it, which is the one way this repair could make things worse.
            Dictionary<string, string> said = null;
            if (vouch != null && vouch.ReadVersion <= 6)
            {
                plan.Voucher = voucherName;
                said = new Dictionary<string, string>();
                foreach (var old in vouch.Pins.Values)
                    if (old != null) said[Spot(old)] = old.Source ?? "";
            }

            foreach (var pin in new List<SharedPin>(ClientPins.All))
            {
                if (pin == null || !pin.HandPlaced) continue;
                // An exact comparison, deliberately: the zero is not a measurement that came out
                // near zero, it is a constant in the game's own conversion from a point on the
                // map to a point in the world, and it survives the store unchanged.
                bool mapClick = pin.Pos.y == 0f;
                if (!mapClick && PlacedByTheGameNotAPlayer(pin.Icon)) { plan.Guarded++; continue; }
                if (said != null && said.TryGetValue(Spot(pin), out string was))
                {
                    // Blank is the answer: the reader gives a marker the word "Manual" wherever
                    // the old flag said a person placed it, so a blank source in a copy that
                    // still had the flag is that copy saying "recorded".
                    bool copySaysPlaced = !string.IsNullOrEmpty(was);
                    if (copySaysPlaced != mapClick) { plan.Disputed.Add(pin); continue; }
                    plan.Agreed++;
                }
                if (mapClick) { plan.Placed++; continue; }
                var blank = pin.Clone();
                blank.Source = "";
                if (!blank.Key.IsNone && ClientPins.Holds(blank.Key)) plan.Duplicate.Add(pin);
                else plan.Recorded.Add(pin);
            }
            return plan;
        }

        /// <summary>
        /// Put them back under the identity they had before the word was written in. Fills
        /// <paramref name="record"/> so the whole thing can be taken back.
        /// </summary>
        internal static int Apply(Plan plan, AdminUndo.Entry record)
        {
            int n = 0;
            // The duplicates first, so a fold can never be the thing that blocks a rename.
            foreach (var pin in plan.Duplicate)
            {
                if (!ClientPins.Holds(pin.Key)) continue;
                if (record != null) record.Erased.Add(pin.Clone());
                ClientPins.RemoveStale(pin.Key);
                n++;
            }
            foreach (var pin in plan.Recorded)
            {
                var was = pin.Key;
                var back = pin.Clone();
                back.Source = "";
                if (back.Key.IsNone) continue;
                if (record != null) record.Erased.Add(pin.Clone());
                if (!ClientPins.Rewrite(was, back)) continue;
                if (record != null) record.Wrote.Add(back.Key);
                n++;
            }
            if (n > 0) ClientPins.Restyle();
            return n;
        }

        /// <summary>
        /// Where a marker stands, to a tenth of a meter, with whose it is -- everything a marker's
        /// key is made of except the source, which is the one field in question here.
        /// </summary>
        private static string Spot(SharedPin pin) =>
            pin.OwnerId.ToString() + "@" + pin.Pos.x.ToString("0.0") + "," + pin.Pos.z.ToString("0.0");

        /// <summary>
        /// Whether this picture belongs to a pin the game put on the map by itself and this mod
        /// then took on as the player's: where they died, and where they will wake up. Both stand
        /// at a real height, which is the whole reason they need naming here.
        /// </summary>
        private static bool PlacedByTheGameNotAPlayer(string icon) =>
            IconRegistry.SameKey(icon, IconRegistry.KeyForVanilla((int)Minimap.PinType.Death))
            || IconRegistry.SameKey(icon, IconRegistry.KeyForVanilla((int)Minimap.PinType.Bed));
    }
}
