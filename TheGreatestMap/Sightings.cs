using System;
using System.Collections.Generic;

namespace TheGreatestMap
{
    /// <summary>
    /// What a sighting can be: the pictures offered as rows under Sightings in the marker list.
    ///
    /// <para>Every other kind's type rows are read off the markers on the map, and that is
    /// deliberate -- a thing you have met has a row, a thing you have not does not, and
    /// <see cref="KindMenu.Quiet"/> says at length why offering rows for the unmet was the wrong
    /// answer when it was tried for ore. A placed-only kind is where that rule runs out rather
    /// than where it is broken: nothing records a sighting, so no picture ever arrives by itself,
    /// so a row that is not offered can never come to exist. Reading the map for sightings
    /// answers "which have you written down", which is a different question from "which could
    /// you write down" -- and until this, the answer to the second was one picture, the serpent
    /// the kind wears, however many things a player might want to mark having seen.</para>
    ///
    /// <para>The fish are asked of the game rather than listed here, because the game already
    /// knows which items are fish and a list of ids written here would go stale at the next
    /// update that adds one. The creatures have to be named: "worth marking where you saw one"
    /// is not a property of a prefab, and offering every trophy in the game would be fifty-seven
    /// rows to find five in.</para>
    /// </summary>
    internal static class Sightings
    {
        /// <summary>
        /// The creatures of the water, ahead of the fish, and the ones that roam, after them.
        ///
        /// <para>Written as prefab ids on purpose. A creature is frequently not named after the
        /// creature -- the bear is Bjorn, and the vile is Unbjorn, whose trophy is
        /// TrophyBjornUndead -- so no search for the English word finds either of them, and a
        /// search for "bear" finds PulledBear, which is a meal. One that the game does not have
        /// is left out rather than drawn as a blank, so this list may name more than a given
        /// version of Valheim carries.</para>
        /// </summary>
        private static readonly string[] OfTheWater = { "TrophySerpent", "TrophyBonemawSerpent" };

        private static readonly string[] Roaming = { "TrophyBjorn", "TrophyBjornUndead", "TrophyGjall" };

        private static readonly List<string> None = new List<string>();

        /// <summary>
        /// Settled once a session. It depends on ObjectDB and on the localization, neither of
        /// which changes while a world is open, and it is asked every time the marker list is
        /// built -- which is every refresh of a pinned list.
        /// </summary>
        private static List<string> _offered;

        internal static void Invalidate() => _offered = null;

        /// <summary>The pictures a sighting can wear, in the order they are offered.</summary>
        internal static List<string> Offered()
        {
            if (_offered != null) return _offered;
            var db = ObjectDB.instance;
            if (db == null || db.m_items == null) return None;
            var list = new List<string>();
            foreach (var name in OfTheWater) Offer(list, name);
            foreach (var key in Swimming(db)) list.Add(key);
            foreach (var name in Roaming) Offer(list, name);
            // Only once there is something in it. Asked before the item database is up, this
            // would otherwise remember an empty answer for the rest of the session.
            if (list.Count > 0) _offered = list;
            return list;
        }

        private static void Offer(List<string> list, string item)
        {
            string key = IconRegistry.PickItem(item);
            if (key != null) list.Add(key);
        }

        /// <summary>
        /// Every fish the game has, by its own reckoning of what a fish is, named as the player
        /// would name it and sorted that way.
        ///
        /// <para>Sorted by the shown name rather than by the prefab, because the prefabs are
        /// Fish1 through Fish12 and a list in that order is alphabetical in nothing anybody
        /// reads. The cooked ones are left out by name: the item type is the game's own answer
        /// and it is the right question to ask, but a plate of cooked perch is not a sighting of
        /// one, and nothing in the type says which is which.</para>
        /// </summary>
        private static List<string> Swimming(ObjectDB db)
        {
            var found = new List<KeyValuePair<string, string>>();
            foreach (var prefab in db.m_items)
            {
                if (prefab == null) continue;
                if (prefab.name.IndexOf("cooked", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                ItemDrop drop;
                try { drop = prefab.GetComponent<ItemDrop>(); }
                catch (Exception) { continue; }
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                if (drop.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Fish) continue;
                string key = "item:" + prefab.name;
                found.Add(new KeyValuePair<string, string>(IconRegistry.DisplayName(key) ?? prefab.name, key));
            }
            found.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.CurrentCultureIgnoreCase));
            var keys = new List<string>();
            foreach (var one in found) keys.Add(one.Value);
            return keys;
        }
    }
}
