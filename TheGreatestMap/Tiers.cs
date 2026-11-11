using System;
using System.Collections.Generic;

namespace TheGreatestMap
{
    /// <summary>
    /// How willing this mod is to write a marker for a thing, in three steps.
    ///
    /// <para>There used to be two, and they were the same step: a prefab was in a catalog line or
    /// it was not. That conflated two questions which turn out to have different answers -- is
    /// this worth a marker, and does this mod know what it is -- and every time somebody answered
    /// the first by deleting a line, they answered the second by accident. Hundreds of markers
    /// already on the map lost their kind in that moment, and a marker with no kind has no name,
    /// no picture and no row in the marker list: a gold dot titled "marker" that no switch could
    /// hide and nothing could explain. Three tiers separate the questions. Everything here is
    /// known. What differs is whether a marker gets written.</para>
    ///
    /// <para><see cref="Tier.Never"/> is the answer for things there are thousands of with
    /// nothing in them. Not a setting -- there is no sense in offering to mark every beech tree,
    /// and an option nobody would take is clutter in a config file that has too much of it
    /// already. They stay catalogued so that a marker somebody wrote in an older version still
    /// knows what it is, and can be named, pictured and hidden like anything else.</para>
    ///
    /// <para><see cref="Tier.Off"/> is for the borderline: things a reasonable player might want
    /// and most will not. Off until asked for, and the marker list shows them when you tick
    /// "Show what you are not recording", which is the only honest way to offer a choice about a
    /// prefab nobody can be expected to have heard of.</para>
    ///
    /// <para><see cref="Tier.On"/> is everything else, and it is where most of the catalog lives.
    /// A player who does not want dandelions turns dandelions off in the same list.</para>
    ///
    /// <para>The deciding question is not how many there are. There are 559 Burial Chambers in a
    /// world and every one is worth walking to; there are 650 dvergr statues and viaducts and not
    /// one of them is. It is value per marker, and 400 markers of scenery beside 92 that mean
    /// something is worse than no markers at all, because it is the beside that does the
    /// damage.</para>
    /// </summary>
    internal enum Tier
    {
        /// <summary>Catalogued so it can be named, never recorded, not offered.</summary>
        Never,
        /// <summary>Offered and off until the player asks for it.</summary>
        Off,
        /// <summary>Recorded unless the player says otherwise.</summary>
        On,
    }

    /// <summary>
    /// The groups a player switches recording on and off by, and the tier each one starts at.
    ///
    /// <para>A group is the unit of the choice because it is the unit of the thought. "Black
    /// Forest ruins" is one thing to a player and eleven prefabs to the game -- Ruin1, Ruin2,
    /// Ruin3, four StoneHouses, five StoneTowerRuins -- and no list of eleven cryptic names is a
    /// question anyone can answer. Only the exceptions are written down here: anything in the
    /// catalog that this table does not name is its own group, at <see cref="Tier.On"/>, labelled
    /// with the name it already shows on the map.</para>
    ///
    /// <para>This is in code and not in the config file on purpose. A marker carries only the
    /// prefab it was written for and every client works out the name, the kind and the picture
    /// from its own catalog -- so two players with different catalogs do not merely prefer
    /// different things, they disagree about what the same marker is, and the one with the
    /// shorter catalog sees gold dots. Shared data needs a shared vocabulary, and the way to
    /// guarantee one is to compile it in: everybody on this version agrees by construction. What
    /// stays a per-player choice is what each of them writes down, which is the switch below, and
    /// what each of them draws, which it never was.</para>
    /// </summary>
    internal static class Tiers
    {
        internal sealed class Group
        {
            /// <summary>What the player sees. No comma, no '=' and no '|': the switches are a list.</summary>
            public string Label;
            public Tier Tier;
            public Category Cat;
            /// <summary>One line, shown under the row and in the documented list.</summary>
            public string Why;
            /// <summary>Catalog syntax: prefab[=Name][|Icon], comma separated, * matches by prefix.</summary>
            public string Entries;
        }

        // ── the exceptions ──────────────────────────────────────────────────────────────
        //
        // Counts are from a whole world rather than from one player's exploring: the game places
        // every location when the world is made, so these are what is out there to be walked past.

        internal static readonly Group[] All =
        {
            // ── never: scenery, and the things a marker would be a lie about ───────────

            new Group
            {
                Label = "Standing Stones", Tier = Tier.Never, Cat = Category.Structure,
                Why = "435 in a world between them and nothing in any of them.",
                Entries = "StoneHenge*=Stonehenge|pin:Memorial,StoneCircle*=Stone Circle|pin:Memorial," +
                          "Dolmen*=Dolmen|pin:Memorial,Waymarker*=Waymarker|pin:Memorial",
            },
            new Group
            {
                Label = "Dvergr Ruins", Tier = Tier.Never, Cat = Category.Structure,
                Why = "650 statues and viaducts you walk past, and 150 fallen guard towers; what is " +
                      "worth the trip in the Mistlands is an extractor and a pile of soft tissue, and " +
                      "those are at the towers still standing, the harbors and the excavations, which " +
                      "are Enemy Bases and already marked.",
                // The fallen towers by their own prefixes, which are longer than the Enemy Bases
                // line's "Mistlands_GuardTower*" and so beat it -- that line was taking all three
                // of them with it, 150 of the 350 towers in a world, and giving each the name and
                // the trophy of a tower the dvergr still hold. On the map and in the marker list a
                // ruin and a garrison then read identically, which is the one thing a picture is
                // there to prevent.
                // The stone they are made of, which is the whole of what is there. They wore the
                // dvergr trophy, the same as the bases, so a fallen tower and a garrison were
                // told apart by nothing at all -- and the bases wear the dvergr lantern now, so
                // this is the other half of telling them apart.
                //
                // The block from the build menu, not the item: item:BlackMarble is the giant
                // ribs and skulls in the Ashlands, and there are seventy of those on one map.
                // Two pictures of the same stone, which is fair enough -- one is a lump of it in
                // a hand, the other is a wall built of it.
                Entries = "Mistlands_Statue*=Dvergr Statue|piece:blackmarble_2x2x2," +
                          "Mistlands_Viaduct*=Viaduct|piece:blackmarble_2x2x2," +
                          "Mistlands_GuardTower1_ruined*=Fallen Guard Tower|piece:blackmarble_2x2x2," +
                          "Mistlands_GuardTower2_ruined*=Fallen Guard Tower|piece:blackmarble_2x2x2," +
                          "Mistlands_GuardTower3_ruined*=Fallen Guard Tower|piece:blackmarble_2x2x2",
            },
            new Group
            {
                Label = "Tar Puddles", Tier = Tier.Never, Cat = Category.Ore,
                Why = "Every one of them is inside a tar pit, and the pit is marked as a deposit.",
                Entries = "Pickable_Tar=Tar|Tar",
            },
            new Group
            {
                Label = "Infested Trees", Tier = Tier.Never, Cat = Category.Structure,
                Why = "700 in a world. The guck on them is recorded as guck, which is what anyone goes for.",
                Entries = "InfestedTree*=Infested Tree|Guck",
            },
            new Group
            {
                Label = "Places of Mystery", Tier = Tier.Never, Cat = Category.Structure,
                Why = "Three in a world, and the game pins all three itself -- the same arrangement " +
                      "the boss altars and Hildir's quests have.",
                Entries = "PlaceofMystery*=Place of Mystery",
            },
            // Nothing here for rocks, trees, bushes and the rest of what the ground is made of.
            // They are kept out by prefix in 'Structures Exclude Prefixes', which is the right
            // tool for them: naming them here would put a catalog entry called "Rock" in front of
            // every prefab beginning with those letters, and the whole point of an entry is that
            // it says what a thing is. A tier is for a thing somebody could argue for.

            // ── off: a reasonable player might want these, and most will not ───────────

            new Group
            {
                Label = "Black Forest Ruins", Tier = Tier.Off, Cat = Category.Structure,
                Why = "850 heaps of stonework in a world. A few hold a chest.",
                Entries = "Ruin*=Black Forest Ruin,StoneHouse*=Black Forest Ruin",
            },
            new Group
            {
                Label = "Mountain Ruins", Tier = Tier.Off, Cat = Category.Structure,
                Why = "400 of them, not counting the two kinds with onion seeds in, which are " +
                      "their own group and recorded.",
                Entries = "StoneTowerRuins01*=Mountain Ruin,StoneTowerRuins02*=Mountain Ruin," +
                          "StoneTowerRuins03*=Mountain Ruin,StoneTowerRuins06*=Mountain Ruin," +
                          "StoneTowerRuins07*=Mountain Ruin,StoneTowerRuins08*=Mountain Ruin," +
                          "StoneTowerRuins09*=Mountain Ruin,StoneTowerRuins10*=Mountain Ruin," +
                          "StoneTowerRuins11*=Mountain Ruin",
            },
            new Group
            {
                Label = "Mountain Graves", Tier = Tier.Off, Cat = Category.Structure,
                Why = "The graves and wells on the peaks.",
                Entries = "MountainGrave*=Mountain Grave,MountainWell*=Mountain Well",
            },
            new Group
            {
                Label = "Swamp Ruins", Tier = Tier.Off, Cat = Category.Structure,
                Why = "The swamp is marked by what is worth going to: the crypts are dungeons, the " +
                      "bone and body piles in the graveyards are spawners, the bog witch is a trader.",
                Entries = "SwampHut*=Swamp Hut,SwampRuin*=Swamp Ruin,SwampWell*=Swamp Well,Grave1*=Graveyard",
            },
            new Group
            {
                Label = "Plains Fortress Ruins", Tier = Tier.Off, Cat = Category.Structure,
                Why = "Yagluth's broken fortresses. The Fuling towers and villages near them are " +
                      "Enemy Bases and recorded.",
                Entries = "FortressRuins*=Fortress Ruin",
            },
            new Group
            {
                Label = "Ashlands Ruins", Tier = Tier.Off, Cat = Category.Structure,
                Why = "The charred ruins. The fortresses are Enemy Bases and recorded.",
                Entries = "CharredRuins*=Charred Ruin,AshlandRuins*=Ashlands Ruins",
            },
            new Group
            {
                Label = "Shipwrecks", Tier = Tier.Off, Cat = Category.Structure,
                Why = "Wrecks and ship settings on the shoreline; a few hold a crate.",
                Entries = "ShipWreck*=Shipwreck,ShipSetting*=Ship Setting",
            },
            new Group
            {
                Label = "Volture Nests", Tier = Tier.Off, Cat = Category.DrakeNest,
                Why = "338 in a world, and the reason to go to one is the eggs.",
                Entries = "VoltureNest*=Volture Nest|VoltureEgg",
            },

            // ── on, but worth naming as a group of their own ────────────────────────────

            new Group
            {
                Label = "Surtling Geysers", Tier = Tier.On, Cat = Category.Spawner,
                Why = "The fire holes in the swamp that surtlings climb out of, which is where " +
                      "surtling cores come from -- the same reason the bone and body piles in the " +
                      "graveyards beside them are marked.",
                Entries = "FireHole*=Surtling Geyser|SurtlingCore",
            },

            // The fallen giants come in two heaps that are mined for different things, and the
            // catalog names each piece -- Giant Helmet, Giant Sword, Giant Ribs, Giant Skull. The
            // marker list groups by picture, the gear shares copper scrap and the bones share
            // black marble, so the rows came out as two called "Giant": all the names had in
            // common was the word, and a row named by what its members share is only as good as
            // what they share. These are the names.
            new Group
            {
                Label = "Giant Gear", Tier = Tier.On, Cat = Category.Ore,
                Why = "The helmets and swords of the fallen giants, which are mined for copper scrap.",
                Entries = "giant_helmet1,giant_helmet2,giant_sword1,giant_sword2",
            },

            new Group
            {
                Label = "Giant Skeleton", Tier = Tier.On, Cat = Category.Ore,
                Why = "The ribs and skulls of the fallen giants, which are mined for black marble. " +
                      "The brain is its own thing and gives soft tissue.",
                Entries = "giant_ribs,giant_skull",
            },

            new Group
            {
                Label = "Dvergr Bases", Tier = Tier.On, Cat = Category.Camp,
                Why = "The four kinds of place the dvergr still hold, which are worth the trip for " +
                      "the extractor and what is stacked around it. Named as one group because that " +
                      "is how a player thinks of them, and because they share a picture: a row " +
                      "covering all four cannot be called after whichever one came first.",
                Entries = "Mistlands_GuardTower*=Dvergr Guard Tower,Mistlands_Excavation*=Dvergr Excavation," +
                          "Mistlands_Harbour*=Dvergr Harbor,Mistlands_Lighthouse*=Dvergr Lighthouse",
            },

            new Group
            {
                Label = "Onion Seed Ruins", Tier = Tier.On, Cat = Category.Structure,
                Why = "The two mountain ruins with onion seeds in the chest, which are worth the " +
                      "climb until you have your first.",
                Entries = "StoneTowerRuins04*=Mountain Ruin,StoneTowerRuins05*=Mountain Ruin",
            },
        };

        // ── what the player has changed ─────────────────────────────────────────────────

        private static Dictionary<string, bool> _said;
        private static Dictionary<string, Group> _byLabel;

        internal static void Forget()
        {
            _said = null;
            _byLabel = null;
        }

        private static Dictionary<string, Group> ByLabel()
        {
            if (_byLabel != null) return _byLabel;
            var map = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in All) map[g.Label] = g;
            _byLabel = map;
            return map;
        }

        internal static Group Find(string label) =>
            !string.IsNullOrEmpty(label) && ByLabel().TryGetValue(label, out var g) ? g : null;

        /// <summary>
        /// The tier a group starts at. A label this table has never heard of is a group of one
        /// thing the catalog names, and those are recorded: that is what <see cref="Tier.On"/> is.
        /// </summary>
        internal static Tier TierOf(string label)
        {
            var g = Find(label);
            return g == null ? Tier.On : g.Tier;
        }

        /// <summary>
        /// Written as a list of exceptions rather than a list of what is on, so that a group added
        /// by a later version starts at the tier that version chose for it. A saved list of what
        /// was on would have said nothing about a group that did not exist when it was written,
        /// and silence would have read as off.
        /// </summary>
        private static Dictionary<string, bool> Said()
        {
            if (_said != null) return _said;
            var said = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            string raw = TgmConfig.RecordingExceptions != null ? TgmConfig.RecordingExceptions.Value : null;
            foreach (var part in (raw ?? "").Split('|'))
            {
                string item = part.Trim();
                if (item.Length == 0) continue;
                int eq = item.LastIndexOf('=');
                if (eq <= 0) continue;
                string label = item.Substring(0, eq).Trim();
                string state = item.Substring(eq + 1).Trim();
                if (label.Length == 0) continue;
                if (string.Equals(state, "on", StringComparison.OrdinalIgnoreCase)) said[label] = true;
                else if (string.Equals(state, "off", StringComparison.OrdinalIgnoreCase)) said[label] = false;
            }
            _said = said;
            return said;
        }

        /// <summary>Whether markers are being written for this group right now.</summary>
        internal static bool Recording(string label)
        {
            if (string.IsNullOrEmpty(label)) return true;
            if (TierOf(label) == Tier.Never) return false;
            return Said().TryGetValue(label, out bool on) ? on : TierOf(label) == Tier.On;
        }

        /// <summary>Whether the player has said anything about this group, either way.</summary>
        internal static bool Chosen(string label) =>
            !string.IsNullOrEmpty(label) && Said().ContainsKey(label);

        /// <summary>
        /// Switch a group. Agreeing with the tier takes the exception out again rather than
        /// writing it down, so a config file carries only what the player has actually changed.
        /// </summary>
        internal static void SetRecording(string label, bool on)
        {
            if (string.IsNullOrEmpty(label) || TgmConfig.RecordingExceptions == null) return;
            if (TierOf(label) == Tier.Never) return;
            var said = new Dictionary<string, bool>(Said(), StringComparer.OrdinalIgnoreCase);
            if (on == (TierOf(label) == Tier.On)) said.Remove(label);
            else said[label] = on;
            var parts = new List<string>();
            foreach (var kv in said) parts.Add(kv.Key + "=" + (kv.Value ? "on" : "off"));
            parts.Sort(StringComparer.OrdinalIgnoreCase);
            _said = said;
            TgmConfig.RecordingExceptions.Value = string.Join("|", parts.ToArray());
        }
    }
}
