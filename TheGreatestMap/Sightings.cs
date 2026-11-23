using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// What you might have seen: the pictures offered as rows under Fish and Big Game, whether
    /// one has been written down yet or not.
    ///
    /// <para>Every other kind's type rows are read off the markers on the map, and that is
    /// deliberate -- a thing you have met has a row, a thing you have not does not, and
    /// <see cref="KindMenu.Quiet"/> says at length why offering rows for the unmet was the wrong
    /// answer when it was tried for ore. These two kinds are where that rule runs out rather
    /// than where it is broken: nothing records a sighting, so no picture ever arrives by
    /// itself, so a row that is not offered can never come to exist. Reading the map answers
    /// "which have you written down", which is a different question from "which could you write
    /// down".</para>
    ///
    /// <para>The fish are asked of the game rather than listed here, because the game already
    /// knows which items are fish and a list of ids written here would go stale at the next
    /// update that adds one. The creatures have to be named: "worth marking where you saw one"
    /// is not a property of a prefab, and offering every trophy in the game would be fifty-odd
    /// rows to find nine in.</para>
    ///
    /// <para>A creature's row wears its trophy's picture and carries the creature's own name,
    /// which are two separate things to ask the game for. The trophy is the only picture of a
    /// creature the game has, but the trophy's name is "Serpent trophy", and a row saying that
    /// over a row of serpent sightings is naming the item rather than the thing seen.</para>
    /// </summary>
    internal static class Sightings
    {
        /// <summary>A picture offered as a row, and what that row is called.</summary>
        internal struct Offer
        {
            internal string Icon;
            internal string Label;
        }

        /// <summary>A creature worth writing down where you saw one.</summary>
        private sealed class Quarry
        {
            /// <summary>The creature in the world, which is where the row's name comes from.</summary>
            internal readonly string Creature;

            /// <summary>Its trophy, whose inventory picture the row wears.</summary>
            internal readonly string Trophy;

            /// <summary>What to call it when the game cannot be asked. See <see cref="Named"/>.</summary>
            internal readonly string Called;

            internal Quarry(string creature, string trophy, string called)
            {
                Creature = creature;
                Trophy = trophy;
                Called = called;
            }
        }

        /// <summary>
        /// The creatures offered under Big Game.
        ///
        /// <para>Written as prefab ids on purpose, both halves of each. A creature is frequently
        /// not named after the creature -- the bear is Bjorn, the vile is Unbjorn whose trophy is
        /// TrophyBjornUndead, and the seeker soldier is SeekerBrute -- so no search for the
        /// English word finds them, and a search for "bear" finds PulledBear, which is a meal.
        /// One the game does not have is left out rather than drawn as a blank, so this may name
        /// more than a given version of Valheim carries.</para>
        /// </summary>
        private static readonly Quarry[] BigGame =
        {
            new Quarry("Serpent", "TrophySerpent", "Serpent"),
            new Quarry("BonemawSerpent", "TrophyBonemawSerpent", "Bonemaw serpent"),
            new Quarry("Bjorn", "TrophyBjorn", "Bear"),
            new Quarry("Unbjorn", "TrophyBjornUndead", "Vile"),
            new Quarry("Gjall", "TrophyGjall", "Gjall"),
            new Quarry("Deer", "TrophyDeer", "Deer"),
            new Quarry("Troll", "TrophyForestTroll", "Troll"),
            new Quarry("FallenValkyrie", "TrophyFallenValkyrie", "Fallen valkyrie"),
            new Quarry("SeekerBrute", "TrophySeekerBrute", "Seeker soldier"),
            new Quarry("Lox", "TrophyLox", "Lox"),
            new Quarry("StoneGolem", "TrophySGolem", "Stone golem"),
            new Quarry("Abomination", "TrophyAbomination", "Abomination"),
            new Quarry("Asksvin", "TrophyAsksvin", "Asksvin"),
            new Quarry("Morgen", "TrophyMorgen", "Morgen"),
        };

        private static readonly List<Offer> None = new List<Offer>();

        /// <summary>
        /// Settled once a session, one list per kind. They depend on ObjectDB, on ZNetScene and
        /// on the localization, none of which changes while a world is open, and they are asked
        /// every time the marker list is built -- which is every refresh of a pinned list.
        /// </summary>
        private static List<Offer> _fish;

        private static List<Offer> _big;

        internal static void Invalidate()
        {
            _fish = null;
            _big = null;
        }

        /// <summary>The pictures this kind offers, in the order they are shown.</summary>
        internal static List<Offer> Offered(Category kind)
        {
            if (kind == Category.Fish)
            {
                if (_fish == null) _fish = Settle(Swimming());
                return _fish ?? None;
            }
            if (kind == Category.BigGame)
            {
                if (_big == null) _big = Settle(Roaming());
                return _big ?? None;
            }
            return None;
        }

        /// <summary>
        /// Keep an answer only once there is something in it. Asked before the item database is
        /// up -- which the marker list is, on the frame a world opens -- this would otherwise
        /// remember an empty answer for the rest of the session.
        /// </summary>
        private static List<Offer> Settle(List<Offer> made) => made.Count > 0 ? made : null;

        /// <summary>
        /// Which of the two a marker written as a Sighting belongs to, Sightings having been one
        /// kind before 4.14.0.
        ///
        /// <para>The picture decides, which is the rule for everything placed by hand: it is what
        /// the marker list files one under, and it is what the player chose when they placed it.
        /// So nothing is rewritten and no marker is shipped anywhere wearing a new identity --
        /// the stored word stays "Sighting" and is read as one of these every time, which is a
        /// migration that cannot go wrong halfway.</para>
        ///
        /// <para>Null means not yet rather than neither: the item database is not up, and an
        /// answer given now would be remembered. See SharedPin.KindCategory, which asks
        /// again.</para>
        /// </summary>
        internal static Category? Refile(string icon)
        {
            var fish = Offered(Category.Fish);
            if (fish.Count == 0) return null;
            foreach (var one in fish) if (IconRegistry.SameKey(one.Icon, icon)) return Category.Fish;
            return Category.BigGame;
        }

        private static List<Offer> Roaming()
        {
            var made = new List<Offer>();
            foreach (var one in BigGame)
            {
                string icon = IconRegistry.PickItem(one.Trophy);
                if (icon == null) continue;
                made.Add(new Offer { Icon = icon, Label = Named(one) });
            }
            Alphabetically(made);
            return made;
        }

        /// <summary>
        /// The creature's own name, in the player's language -- or the name written down here,
        /// if this version of the game has the trophy but no creature by that id.
        /// </summary>
        private static string Named(Quarry one)
        {
            try
            {
                var scene = ZNetScene.instance;
                var prefab = scene != null ? scene.GetPrefab(one.Creature) : null;
                var character = prefab != null ? prefab.GetComponent<Character>() : null;
                string key = character != null ? character.m_name : null;
                if (!string.IsNullOrEmpty(key) && Localization.instance != null)
                {
                    string said = Localization.instance.Localize(key);
                    if (!string.IsNullOrEmpty(said) && !said.StartsWith("[")) return said;
                }
            }
            catch (Exception) { }
            return one.Called;
        }

        /// <summary>
        /// Every fish the game has, by its own reckoning of what a fish is, named as the player
        /// would name it.
        ///
        /// <para>The cooked ones are left out by name: the item type is the game's own answer and
        /// it is the right question to ask, but a plate of cooked perch is not a sighting of one,
        /// and nothing in the type says which is which.</para>
        /// </summary>
        private static List<Offer> Swimming()
        {
            var made = new List<Offer>();
            var db = ObjectDB.instance;
            if (db == null || db.m_items == null) return made;
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
                made.Add(new Offer { Icon = key, Label = IconRegistry.DisplayName(key) ?? prefab.name });
            }
            Alphabetically(made);
            return made;
        }

        /// <summary>
        /// By the name shown rather than by the prefab, which for the fish are Fish1 through
        /// Fish12 -- a list in that order is alphabetical in nothing anybody reads.
        /// </summary>
        private static void Alphabetically(List<Offer> made) =>
            made.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
    }
}
