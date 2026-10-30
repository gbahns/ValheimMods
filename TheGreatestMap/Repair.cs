using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Working out what the markers that never knew are, by asking the world and believing it
    /// only when it agrees with what the marker already looks like.
    ///
    /// Markers written before a marker remembered its own identity carry nothing but an icon, and
    /// for most kinds the icon settles it -- one icon, one catalog line, done, which is what the
    /// catalog pass in <see cref="Migrations"/> does. What it cannot settle is everything that
    /// shares a picture with something else: a dozen kinds of building all wear the house pin.
    ///
    /// The world knows. The server holds every location the generator placed and every ZDO in the
    /// world, standing or not, so it can say what is at a place without anybody going there.
    ///
    /// <para><b>Why this was dangerous, and is not now.</b> Asking what is at a place and writing
    /// the answer down is how a patch of carrots inside a ruin became the ruin and drew as a
    /// house. The answer was true -- that *is* the ruin's ground -- and it was not the answer to
    /// the question. The repair is to make the world's answer agree with the marker before it is
    /// believed: a carrot patch wears the carrot icon and a ruin wears the house pin, so the two
    /// disagree and nothing is written. A fact that has to be confirmed by a second, independent
    /// fact is no longer a guess, which is what makes this safe to run on its own.</para>
    ///
    /// The server answers nearest first, so a greydwarf nest at arm's length is offered before the
    /// camp fifty meters around it, and both wear the same icon -- distance is what tells them
    /// apart, and the nearer thing is the one a marker at that spot was written for.
    ///
    /// <para><b>The markers that say nothing at all.</b> A blank has no icon for the world's
    /// answer to agree with, so the rule above cannot be applied to it -- and it is the marker
    /// that needs this most, since where it stands is the only thing left on it. The rule for
    /// those is the weakest this will go: the nearest thing standing within six meters that the
    /// catalog has a line for. The marker was written by the recorder, so whatever it was written
    /// for is something the catalog knows, and the recorder marks the thing it walked up to. What
    /// that cannot tell apart is two catalogued things within six meters of each other where the
    /// lost one was the further -- a berry bush beside a beehive. A wrong answer there is a
    /// marker for something that really is standing there, which is a far smaller wrong than the
    /// dot it replaces, and a player can erase it; leaving every blank standing to avoid it is
    /// not something a player can undo.</para>
    /// </summary>
    internal static class Repair
    {
        /// <summary>At most this many places in one question, so the message stays small.</summary>
        private const int PerAsk = 400;

        private static bool _asked;

        internal static void Reset() => _asked = false;

        /// <summary>
        /// Recorded markers that do not know what they are, and could.
        ///
        /// Blank is the obvious case. The other is a source naming something no catalog line
        /// covers, which is the same predicament wearing a name: nothing can read it, so nothing
        /// can draw from it or file it. Those come about honestly -- an outdoor location nobody
        /// listed is still recorded as a structure, a location with an interior is still a
        /// dungeon -- and the world may well have a better answer standing at the same spot. The
        /// ten markers reading Pickable_Tar on the live map are all inside tar pits the catalog
        /// does know about.
        /// </summary>
        internal static List<SharedPin> Unknown()
        {
            var list = new List<SharedPin>();
            foreach (var pin in ClientPins.All)
            {
                if (pin == null || !pin.Auto) continue;
                if (!string.IsNullOrEmpty(pin.Source) && !string.IsNullOrEmpty(Catalog.IconFor(pin.Source))) continue;
                // Nothing on it at all. The kind test below cannot speak for these, because the
                // kind is one of the things they are missing -- which is how the case this was
                // written for ended up being the one case it never asked about.
                if (pin.SaysNothing) { list.Add(pin); continue; }
                var kind = ClientPins.KindOf(pin);
                // A kind with no prefab list has nothing to look up: the kind is the whole answer,
                // and those are given one of their own rather than left looking unidentified.
                if (!kind.HasValue || !Categories.UsesPrefabList(kind.Value)) continue;
                list.Add(pin);
            }
            return list;
        }

        /// <summary>
        /// Ask the world about everything that does not know what it is, once per session. False
        /// when there is nothing to ask or nobody to ask; the answer arrives later.
        /// </summary>
        internal static bool Run(bool force = false)
        {
            if (_asked && !force)
            {
                TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] The world has already been asked this session.");
                return false;
            }
            var unknown = Unknown();
            if (unknown.Count == 0)
            {
                _asked = true;
                TheGreatestMapMod.Log.LogInfo(
                    "[TheGreatestMap] Nothing to ask the world about: every marker knows what it is.");
                return false;
            }
            _asked = true;
            if (unknown.Count > PerAsk) unknown.RemoveRange(PerAsk, unknown.Count - PerAsk);

            var places = new List<Vector3>(unknown.Count);
            foreach (var pin in unknown) places.Add(pin.Pos);

            // Said before the question goes out, not after the answer comes back. Twice in a row
            // this produced no line at all and there was no way to tell which of three silent
            // exits had been taken -- asked already, nothing to ask, or nobody to ask.
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Asking the world about {places.Count} marker(s) that do not know what they are.");

            bool sent = LocationNames.AskStanding(places, answers =>
            {
                int settled = 0, refused = 0, blanks = 0;
                var byName = new Dictionary<string, int>();
                for (int i = 0; i < unknown.Count && i < answers.Count; i++)
                {
                    bool blank = unknown[i].SaysNothing;
                    string prefab = Pick(unknown[i], answers[i]);
                    if (string.IsNullOrEmpty(prefab)) { refused++; continue; }
                    ClientPins.Reidentify(unknown[i], prefab);
                    settled++;
                    if (blank) blanks++;
                    byName.TryGetValue(prefab, out int n);
                    byName[prefab] = n + 1;
                }
                if (settled > 0) ClientPins.Restyle();
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Asked the world about {unknown.Count} marker(s) that did not know what they were: " +
                    $"settled {settled}, left {refused} alone because nothing there matched what the marker looks like." +
                    (blanks > 0 ? $" {blanks} of those had nothing on them at all and were named by what stands there." : ""));
                foreach (var kv in byName)
                    TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap]   {kv.Key}: {kv.Value}");
            });
            if (!sent)
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] Could not ask the world: there is nobody to ask yet. " +
                    "The markers keep their icons and are asked about again at the next login.");
            return sent;
        }

        /// <summary>
        /// The first thing standing at that place whose icon is the icon this marker already
        /// wears. Nothing, if none of them agrees -- which is the whole safety of this: the
        /// world's answer has to be confirmed by the marker before it is written down.
        ///
        /// <para>A marker that says nothing has nothing to confirm with, and is answered by
        /// <see cref="Nearest"/> instead. That is a weaker rule and is applied only there, where
        /// the alternative is a dot that can never become anything else; see the note on the
        /// class. Every marker that still wears an icon is held to the icon.</para>
        /// </summary>
        private static string Pick(SharedPin pin, List<string> standing)
        {
            if (pin == null || standing == null) return null;
            string wears = IconRegistry.Normalize(pin.Icon);
            if (string.IsNullOrEmpty(wears)) return pin.SaysNothing ? Nearest(standing) : null;
            foreach (string prefab in standing)
            {
                if (string.IsNullOrEmpty(prefab)) continue;
                string would = Catalog.IconFor(prefab);
                if (string.IsNullOrEmpty(would)) continue;   // the catalog has no line for it
                if (IconRegistry.SameKey(would, wears)) return prefab;
            }
            return null;
        }

        /// <summary>
        /// The nearest thing standing there that the catalog has a line for. The answers come
        /// nearest first, so this is the first one the catalog can draw: a thing it has no line
        /// for would leave the marker as unreadable as it is now, which is no answer at all.
        /// </summary>
        private static string Nearest(List<string> standing)
        {
            foreach (string prefab in standing)
            {
                if (string.IsNullOrEmpty(prefab)) continue;
                if (!string.IsNullOrEmpty(Catalog.IconFor(prefab))) return prefab;
            }
            return null;
        }
    }
}
