using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>How much movement stops you writing on the map.</summary>
    internal enum Movement
    {
        Never,
        Running,
        Moving,
    }

    internal enum Category
    {
        Berries,
        Mushrooms,
        Herbs,
        Ore,
        Dungeon,
        Runestone,
        Trader,
        Camp,
        BossAltar,
        Portal,
        Structure,
        Seeds,
        Plants,
        Campfire,
        Sighting,
        Player,
        Ship,
        Cart,
        Spawner,
        Workbench,
    }

    /// <summary>
    /// What a kind is, one level up: the heading it sits under in the marker list.
    ///
    /// <para>Nineteen kinds in one flat list put a raspberry bush and a ship at the same level,
    /// and they are not remotely the same sort of thing -- one is a place something grows that
    /// was written down weeks ago, the other is where a boat is floating this second. Nothing in
    /// the mod disagreed about that; the distinctions were all there in the predicates below,
    /// used by the recorder and never shown to the reader.</para>
    ///
    /// <para>These are groupings for reading, not for storing: nothing is saved per group and no
    /// setting mentions one. Hiding a group is hiding each of its kinds, which is the setting
    /// that already existed.</para>
    /// </summary>
    internal enum Group
    {
        /// <summary>Things you go and harvest, which run out: berries, mushrooms, herbs, seeds, plants, ore.</summary>
        Resources,
        /// <summary>
        /// What you found: dungeons, camps, ruins, runestones, spawners. Places too, and they stay
        /// where they are, but there are hundreds of them and nothing marks one until you walk
        /// past it -- which is a different thing to read than the four landmarks the world is hung
        /// on, and was the reason one heading held both and answered neither.
        /// </summary>
        Points,
        /// <summary>
        /// The world's own landmarks, which the game marks for you as you find them: the boss
        /// altars, the traders, Hildir's quests, the center of the world. A handful of them in a
        /// whole world, each one somewhere you will go back to for a reason, and every one of them
        /// drawn whether this mod is here or not.
        /// </summary>
        Places,
        /// <summary>What you put on the map by hand, about nothing the catalog will ever know.</summary>
        Yours,
        /// <summary>
        /// What is standing now rather than what was found: a ship under way, a cart somewhere, a
        /// portal, another player, and the fires and benches a base is made of. Some of these are
        /// never recorded at all -- the server simply says where they are -- and the rest are
        /// recorded and taken off again when the piece comes down. Either way none of them is a
        /// discovery, and none can be trusted a week from now.
        /// </summary>
        Dynamic,
    }

    /// <summary>How the types under a kind are listed.</summary>
    internal enum TypeOrder
    {
        /// <summary>The catalog's own order, which is the order a player meets the things in it.</summary>
        Progression,
        /// <summary>Commonest first, which is what the list did before there was a choice.</summary>
        MostFirst,
        /// <summary>By name.</summary>
        AToZ,
    }

    /// <summary>
    /// Which half of the plants a type belongs to, when the marker list splits them by whether
    /// they can be farmed rather than by what sort of plant they are.
    ///
    /// <para>The reason to write a plant down is that you will have to come back for it. Once a
    /// thing can be planted that stops being true: you find one patch, you take it home, and the
    /// marker is a note about a trip you will never make again. What cannot be planted you have
    /// to go out and gather every time you run low, which is exactly what a map is for -- so the
    /// two halves are read completely differently, and the useful one is the smaller one.</para>
    ///
    /// <para><b>The line falls between types, not between kinds</b>, because the game puts it
    /// there: magecap and jotun puffs have saplings and the other four mushrooms do not, flax and
    /// barley have them and fiddlehead does not. That is why this is not another Category -- no
    /// re-sorting of the catalog could express it, since two members of one kind land on opposite
    /// sides. Like <see cref="Group"/> it is a grouping for reading only: nothing is stored per
    /// half, and hiding one is hiding each of the types in it.</para>
    /// </summary>
    internal enum Harvest
    {
        /// <summary>Has a sapling: find it once and grow the rest.</summary>
        Farm,
        /// <summary>Has none: every one you want is another trip.</summary>
        Gather,
    }

    /// <summary>Static facts about each category: defaults, labels.</summary>
    internal static class Categories
    {
        internal static readonly Group[] AllGroups = (Group[])Enum.GetValues(typeof(Group));

        /// <summary>
        /// Which heading a kind belongs under. Everything not named here is a place, which is the
        /// honest default: a kind nobody has sorted yet is something you go to.
        /// </summary>
        internal static Group GroupOf(Category c)
        {
            switch (c)
            {
                case Category.Berries:
                case Category.Mushrooms:
                case Category.Herbs:
                case Category.Seeds:
                case Category.Plants:
                case Category.Ore:       return Group.Resources;
                case Category.Sighting:  return Group.Yours;
                case Category.Ship:
                case Category.Cart:
                case Category.Portal:
                case Category.Player:
                // A fire and a bench do not move the way a cart does, but neither does a portal:
                // all four are put up, torn down and put up elsewhere as a base changes shape,
                // which is the thing this heading is about. None of them is a discovery.
                case Category.Campfire:
                case Category.Workbench: return Group.Dynamic;
                // The two the game marks by itself the moment you find one, which is what makes
                // them landmarks rather than discoveries: this mod records neither by default,
                // because there is already a pin there.
                case Category.Trader:
                case Category.BossAltar: return Group.Places;
                // Dungeons, camps, structures, runestones, spawners -- and anything nobody has
                // sorted yet, which is the honest default: a kind this switch has not heard of is
                // something you came across rather than something the world is hung on.
                default:                 return Group.Points;
            }
        }

        internal static string GroupLabel(Group g)
        {
            switch (g)
            {
                case Group.Resources: return "Resources";
                case Group.Points:    return "Points of Interest";
                case Group.Places:    return "Places";
                case Group.Yours:     return "Placed Markers";
                case Group.Dynamic:   return "Dynamic Markers";
                default:              return g.ToString();
            }
        }

        /// <summary>
        /// What the row says, which is what you do about it rather than what it is: a thing you
        /// farm, or a thing you go and gather. Both are a word long, and either way the row is
        /// an instruction rather than a classification -- the point of splitting them was to say
        /// which of the two a marker is for.
        /// </summary>
        internal static string HarvestLabel(Harvest h)
        {
            return h == Harvest.Farm ? "Farm" : "Gather";
        }

        /// <summary>
        /// The picture for a half, held to the same rule as a kind's: something that stands for
        /// the whole of it and is nothing in it. The cultivator is what you plant any of them
        /// with, and the sprig is wild greens rather than the dandelion or the thistle. Both are
        /// already a kind's picture -- Plants and Herbs -- and both of those kinds are gone from
        /// the list while it is split this way, so neither can appear twice.
        /// </summary>
        internal static string HarvestIcon(Harvest h)
        {
            return h == Harvest.Farm ? "Cultivator" : "drawn:herbs";
        }

        internal static string HarvestTip(Harvest h)
        {
            return h == Harvest.Farm
                ? "What you can plant. Find one patch and you need never find another, which is what makes these " +
                  "worth folding away: magecap and jotun puffs are planted as they come, carrot, turnip and onion " +
                  "seeds grow more of themselves, and flax and barley take anywhere in the Plains."
                : "What you cannot plant, and so have to go out and gather again every time you run low -- which is " +
                  "what a map is for. Berries, the common mushrooms, smoke puffs, dandelion, thistle and fiddlehead.";
        }

        /// <summary>What the heading means, for the row's tooltip.</summary>
        internal static string GroupTip(Group g)
        {
            switch (g)
            {
                case Group.Resources:
                    return "What you go and harvest, and what runs out. Crossing one of these off means it is gone.";
                case Group.Points:
                    return "The places you found: dungeons, camps, ruins, runestones, spawners. They stay where they " +
                           "are, like the landmarks below, but there are hundreds of them and nothing marks one until " +
                           "you walk past it. Crossing one off means you are done with it.";
                case Group.Places:
                    return "The landmarks the world is hung on, which the game marks for you as you find them: the " +
                           "boss altars, the traders, Hildir's quests, the center of the world. A handful in a whole " +
                           "world, and each one somewhere you go back to for a reason.";
                case Group.Yours:
                    return "What you noted by hand, about something the catalog will never know: a serpent surfacing, " +
                           "a bay that fishes well, where the trolls walk.";
                case Group.Dynamic:
                    return "What is standing now rather than what was found. A ship moves, a cart is somewhere else, " +
                           "a portal is rebuilt and renamed, a fire or a bench goes up and comes down as a base " +
                           "changes shape -- so none of these can be trusted a week from now, and the mod either " +
                           "asks the server where they are or takes the marker off when the piece goes.";
                default:
                    return null;
            }
        }

        internal static readonly Category[] All = (Category[])Enum.GetValues(typeof(Category));

        internal static bool UsesPrefabList(Category c) =>
            c != Category.Runestone && c != Category.Portal && !IsPlacedOnly(c) && !IsLive(c);

        /// <summary>
        /// A kind that is never recorded and never stored: the server says where these are now,
        /// and the client draws that. A ship moves, so a marker written where one was found is
        /// wrong by the next tide -- which is why none is written. Everything about recording,
        /// spacing, labels and crossing off is therefore beside the point for these.
        ///
        /// Portals joined them in 4.1.0. They do not move, but they are torn down, rebuilt and
        /// renamed at will, which is the same problem wearing a different hat: a marker written
        /// once goes wrong within days. They were recorded and then continuously corrected --
        /// renamed to follow the portal, erased when it went -- and that correction was the only
        /// thing left in the mod that changed a stored marker in ordinary play. The game keeps an
        /// authoritative list of every portal and the server already sends it, so the correction
        /// was keeping a copy in step with something we were being told anyway.
        /// </summary>
        /// <summary>
        /// A kind that exists because a player built one. The world is full of fires and benches
        /// standing in camps and villages and those belong to the place, so only the ones with a
        /// creator on them are recorded -- and when the piece comes down, its marker goes.
        ///
        /// <para>A workbench is here for the same reason the campfire is: both hold the spawning
        /// off around them, so where they stand is worth remembering, and both are torn down and
        /// moved as a base changes shape.</para>
        /// </summary>
        internal static bool IsBuilt(Category c) =>
            c == Category.Campfire || c == Category.Workbench;

        internal static bool IsLive(Category c) =>
            c == Category.Ship || c == Category.Cart || c == Category.Portal;

        /// <summary>
        /// A kind the game puts on the map by itself, the moment you find one. Recording our own
        /// on top is a second pin saying the same thing in a different picture, so nothing here
        /// records these -- but they stay in the marker list, because "where is the nearest
        /// trader" is the same question the list answers for everything else, and the catalog
        /// line is still what says which of the world's locations are traders.
        /// </summary>
        internal static bool VanillaMarks(Category c) => c == Category.Trader;

        /// <summary>
        /// A kind the game draws its own pins for, whether or not this mod records one as well.
        /// The altars and the traders: both are marked the moment you find one.
        ///
        /// <para>Kept apart from <see cref="VanillaMarks"/>, which is the stronger statement --
        /// nothing records these at all, so they have no recording settings. Boss altars do
        /// record, off by default, because this mod's marker says which boss it is in the trophy
        /// it wears and the game's pin does not. So they are listed and pinged like a trader, and
        /// still recordable like anything else.</para>
        /// </summary>
        internal static bool MarkedByGame(Category c) => c == Category.Trader || c == Category.BossAltar;

        /// <summary>
        /// A kind nothing in the world can be classified as: it exists so a player can mark
        /// something the catalog will never know about -- a serpent surfacing, a bay that fishes
        /// well, where the trolls walk. Nothing records one, so the settings about recording would
        /// all be answers to a question never asked, and it is not listed in any prefab catalog.
        /// </summary>
        internal static bool IsPlacedOnly(Category c) =>
            c == Category.Sighting || c == Category.Player;

        internal static string Label(Category c)
        {
            switch (c)
            {
                case Category.BossAltar: return "Boss Altars";
                case Category.Dungeon:   return "Dungeons";
                case Category.Runestone: return "Runestones";
                case Category.Trader:    return "Traders";
                case Category.Camp:      return "Camps";
                case Category.Portal:    return "Portals";
                case Category.Structure: return "Structures";
                case Category.Campfire:  return "Campfires";
                case Category.Workbench: return "Workbenches";
                case Category.Sighting:  return "Sightings";
                case Category.Player:    return "Players";
                case Category.Ship:      return "Ships";
                case Category.Cart:      return "Carts";
                case Category.Spawner:   return "Spawners";
                default:                 return c.ToString();
            }
        }

        /// <summary>
        /// Kinds that are a supply of something rather than a place: they run out. A crossed-off
        /// marker means "cleared" for these, and "already searched" for a structure, which is why
        /// the two are shown and hidden separately.
        /// </summary>
        /// <summary>
        /// Things that grow, as opposed to ore in the ground. These are what a player plants, so
        /// these are the ones worth leaving unrecorded inside a base: a deposit that happens to
        /// stand in your yard is still a deposit, but your own carrots are not a discovery.
        /// </summary>
        internal static bool IsGrown(Category c)
        {
            return c == Category.Berries || c == Category.Mushrooms || c == Category.Herbs
                || c == Category.Seeds || c == Category.Plants;
        }

        internal static bool IsResource(Category c)
        {
            return c == Category.Berries || c == Category.Mushrooms || c == Category.Herbs
                || c == Category.Ore || c == Category.Seeds || c == Category.Plants
                || c == Category.Spawner;
        }

        internal static bool DefaultEnabled(Category c)
        {
            // Structures are a personal habit. Traders and boss altars the game marks by itself the
            // moment you find them, so recording our own on top would be a second pin saying the
            // same thing.
            return c != Category.Structure && c != Category.Trader && c != Category.BossAltar;
        }

        // Icons for well-known locations, kept in code so that a catalog line saved by an older
        // version (without |Icon overrides) still gets the right picture.
        private static readonly Dictionary<string, string> KnownIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Crypt2", "TrophySkeleton" }, { "Crypt3", "TrophySkeleton" }, { "Crypt4", "TrophySkeleton" },
            { "SunkenCrypt4", "IronScrap" }, { "MountainCave02", "TrophyCultist" }, { "TrollCave02", "TrophyFrostTroll" },
            { "BearCave", "TrophyBjorn" }, { "Mistlands_DvergrTownEntrance1", "TrophySeeker" }, { "Mistlands_DvergrTownEntrance2", "TrophySeeker" },
            { "Vendor_BlackForest", "Coins" }, { "Hildir_camp", "Coins" }, { "BogWitch_Camp", "Coins" },
            // Deposits whose own entry may predate its |Icon: without one the icon falls back to
            // what the deposit drops, and then to the kind's own fallback, which is copper ore --
            // so a scrap pile and a giant's bones both came out looking like copper.
            { "mudpile", "IronScrap" }, { "mudpile2", "IronScrap" }, { "mudpile_beacon", "IronScrap" },
            { "silvervein", "SilverOre" },
            // A giant's remains are not one thing: the swords and armor give copper scrap, the
            // skull and ribs black marble, so each wears what it actually yields.
            { "giant_brain", "SoftTissue" },
            { "giant_helmet1", "CopperScrap" }, { "giant_helmet2", "CopperScrap" },
            { "giant_sword1", "CopperScrap" }, { "giant_sword2", "CopperScrap" },
            { "giant_ribs", "BlackMarble" }, { "giant_skull", "BlackMarble" },
            { "GoblinCamp2", "TrophyGoblin" }, { "Spawner_GreydwarfNest", "TrophyGreydwarfBrute" }, { "WoodVillage1", "TrophyDraugr" }, // WoodFarm1 is the abandoned farm: a structure, not a camp
            { "Eikthyrnir", "TrophyEikthyr" }, { "GDKing", "TrophyTheElder" }, { "Bonemass", "TrophyBonemass" },
            { "Dragonqueen", "TrophyDragonQueen" }, { "GoblinKing", "TrophyGoblinKing" },
            { "Mistlands_DvergrBossEntrance1", "TrophySeekerQueen" }, { "FaderLocation", "TrophyFader" },
        };

        internal static string KnownIcon(string prefab)
        {
            return !string.IsNullOrEmpty(prefab) && KnownIcons.TryGetValue(prefab, out var icon) ? icon : null;
        }

        /// <summary>
        /// Icon used when a thing's own item icon cannot be worked out, and the picture the kind
        /// itself wears in the marker list. Item prefab name or pin:&lt;PinType&gt;.
        ///
        /// <para><b>Never one of the kind's own members.</b> These used to be: berries wore a
        /// raspberry, ore wore copper ore, herbs wore a dandelion. Unfold such a kind in the
        /// marker list and the heading and one of the rows under it are the same picture, which
        /// says the two are the same thing. Worse on the map: a hand-placed "there is ore here"
        /// marker drew as copper, claiming to know a metal nobody had looked at.</para>
        ///
        /// <para>So each of these is something that stands for the whole kind and is nothing in
        /// it: the jam you make of any berries, an omelette of any mushrooms, the pickaxe you mine
        /// any vein with, a bowl of greens, a handful of tree seeds, the cultivator you plant a
        /// wild crop with. Every one was checked against the game's own item list rather than
        /// guessed -- a name the game does not have draws a plain dot. Each is a setting in the
        /// Icons section, and `tgm_items &lt;word&gt;` lists what else there is to choose from.</para>
        ///
        /// <para>Structures are the exception, and cannot be helped: every structure wears
        /// vanilla's house pin because the game draws no picture of an abandoned farm, so the kind
        /// and its types look alike for want of anything else to look like.</para>
        /// </summary>
        internal static string DefaultIcon(Category c)
        {
            switch (c)
            {
                // The one picture here the game does not have: there is no item meaning "berries,
                // any of them", and the jam that stood in for it read as a jar rather than as
                // fruit. Drawn in the mod, which is also why it is the only key in this switch
                // that is not something of Valheim's own.
                case Category.Berries:   return "drawn:berries";
                case Category.Mushrooms: return "drawn:mushrooms";    // two of them, not one of the six
                case Category.Herbs:     return "drawn:herbs";        // a sprig, rather than the thistle or the dandelion
                case Category.Seeds:     return "BeechSeeds";         // seeds, and not any of the three crops' seeds
                case Category.Plants:    return "Cultivator";         // what you plant a wild crop with
                case Category.Ore:       return "PickaxeIron";        // mining, rather than one metal
                // Was a plain dot, on the grounds that nothing is "a crypt in general" and a
                // wrong picture is worse than a plain one. Both halves still hold -- the keys the
                // game has are each for one dungeon -- so this is drawn rather than borrowed: a
                // hole in the ground you walk into, which is what the six of them have in common.
                case Category.Dungeon:   return "drawn:dungeons";
                case Category.Runestone: return "pin:Memorial";
                case Category.Trader:    return "Coins";
                case Category.Camp:      return "pin:Icon0";
                case Category.BossAltar: return "pin:Boss";
                case Category.Portal:    return "pin:Icon4";
                case Category.Structure: return "pin:Icon1";
                case Category.Campfire:  return "piece:fire_pit"; // the campfire's own build-menu picture
                case Category.Workbench: return "piece:piece_workbench";
                case Category.Sighting:  return "TrophySerpent";
                // Not "pin:Player": that is the vanilla type the game draws other players with,
                // and the game refuses to let a player place one by hand, so a marker armed with
                // it is never taken onto the shared map. Same picture, our own pin.
                case Category.Player:    return "like:Player";
                // A vessel is built with the hammer, so it has a build-menu picture of itself.
                case Category.Ship:      return "piece:Karve";
                case Category.Cart:      return "piece:Cart";
                // Only the fallback: every spawner in the list wears the trophy of whatever
                // comes out of it, which is the thing worth knowing before you break one open.
                // A skull for the kind itself, rather than the skeleton's trophy, which is what
                // the evil bone pile wears -- the kind and one of its members should not be one
                // picture.
                case Category.Spawner:   return "drawn:spawners";
                default:                 return "pin:Icon3";
            }
        }

        /// <summary>Minimum distance between two markers with the same icon. Plants get one icon each so a clump reads as a clump.</summary>
        internal static float DefaultSpacing(Category c)
        {
            switch (c)
            {
                case Category.Berries:
                case Category.Mushrooms:
                case Category.Seeds:
                case Category.Plants:
                case Category.Herbs:     return 1f;
                case Category.Ore:       return 5f;
                case Category.Portal:    return 5f;
                case Category.Campfire:  return 3f;
                case Category.Workbench: return 3f;
                case Category.Structure: return 6f;  // buildings in a farm can stand close together
                default:                 return 20f;
            }
        }

        /// <summary>How far away a thing of this kind can be and still count as seen when looked straight at.</summary>
        internal static float DefaultLookDistance(Category c)
        {
            switch (c)
            {
                case Category.Berries:
                case Category.Mushrooms:
                case Category.Seeds:
                case Category.Plants:
                case Category.Herbs:     return 20f;
                case Category.Ore:       return 40f;
                case Category.Runestone: return 30f;
                case Category.Portal:    return 40f;
                case Category.Campfire:  return 40f;
                case Category.Workbench: return 40f;
                default:                 return 80f; // dungeons, structures, camps, altars, traders: big things
            }
        }

        /// <summary>Marker size on the map as a percentage; small for the kinds that come in clumps.</summary>
        internal static int DefaultSize(Category c)
        {
            switch (c)
            {
                case Category.Berries:
                case Category.Mushrooms:
                case Category.Seeds:
                case Category.Plants:
                case Category.Herbs:     return 60;
                case Category.Ore:
                case Category.Campfire:
                case Category.Workbench: return 80;
                default:                 return 100;
            }
        }

        /// <summary>Label rule: below zero never label, zero always, otherwise one label per that many meters.</summary>
        internal static float DefaultLabelSpacing(Category c)
        {
            switch (c)
            {
                case Category.Berries:
                case Category.Mushrooms:
                case Category.Herbs:
                case Category.Seeds:
                case Category.Plants:
                case Category.Ore:
                case Category.Runestone:
                case Category.Dungeon:
                case Category.Camp:
                case Category.BossAltar:
                case Category.Campfire:
                case Category.Workbench:
                // A greydwarf trophy means a greydwarf spawner: the icon has always been the
                // whole answer here, and each kind of spawner wears its own trophy. This kind
                // was added to the enum after this switch was written and fell through to the
                // catch-all, which labels, so every nest and bone pile came out with its own
                // name drawn beside it.
                case Category.Spawner:
                case Category.Structure: return -1f; // the house icon says it all
                default:                 return 0f;  // traders (three share the coin icon), portals (the tag)
            }
        }

        internal static string DefaultPrefabs(Category c)
        {
            switch (c)
            {
                case Category.Berries:
                    // VineAsh is the Ashlands vine that bears vineberry clusters, growing up the
                    // walls of the ruins out there. It is a berry you can farm -- picking a
                    // cluster can give seeds, and the seeds plant a vine anywhere -- so it is the
                    // one berry that lands in the farmable half, and the only reason to mark one
                    // is to find your first. It carries its icon because the vine and the berry
                    // are not called the same thing.
                    return "RaspberryBush=Raspberries,BlueberryBush=Blueberries,CloudberryBush=Cloudberries," +
                           "VineAsh=Vineberries|Vineberry";
                case Category.Mushrooms:
                    return "Pickable_Mushroom=Mushrooms,Pickable_Mushroom_yellow=Yellow Mushrooms,Pickable_Mushroom_blue=Blue Mushrooms," +
                           "Pickable_Mushroom_Magecap=Magecap,Pickable_Mushroom_JotunPuffs=Jotun Puffs,Pickable_SmokePuff=Smoke Puffs";
                case Category.Herbs:
                    // In the order a player meets them, which is what the marker list reads to
                    // put its rows in progression order: a dandelion in the meadows long before
                    // a thistle in the black forest.
                    return "Pickable_Dandelion=Dandelion,Pickable_Thistle=Thistle";
                case Category.Seeds:
                    return "Pickable_SeedCarrot=Carrot Seeds,Pickable_SeedTurnip=Turnip Seeds,Pickable_SeedOnion=Onion Seeds";
                case Category.Plants:
                    return "Pickable_Flax_Wild=Wild Flax,Pickable_Barley_Wild=Wild Barley,Pickable_Fiddlehead=Fiddlehead";
                case Category.Ore:
                    // rock4_copper carries its icon rather than deriving one: it is the only ore
                    // entry the loaded world cannot answer for, so it fell through to the kind's
                    // own picture -- and since MineRock_Copper is also called Copper, the marker
                    // list showed Copper twice, once as a pickaxe and once as copper ore. Two
                    // entries of one name have to resolve to one icon or they are two rows.
                    // In the order a player meets them, which is what the marker list reads for
                    // its progression order: the swamp's iron before the mountain's silver.
                    return "rock4_copper=Copper|CopperOre,MineRock_Tin=Tin," +
                           "mudpile_beacon=Scrap Pile|IronScrap,mudpile2=Scrap Pile|IronScrap,mudpile=Scrap Pile|IronScrap," +
                           "silvervein=Silver|SilverOre,MineRock_Obsidian=Obsidian,MineRock_Meteorite=Meteorite,TarPit*=Tar Pit|Tar," +
                           "giant_brain=Giant Brain|SoftTissue,giant_helmet1=Giant Helmet|CopperScrap,giant_helmet2=Giant Helmet|CopperScrap,giant_ribs=Giant Ribs|BlackMarble," +
                           "giant_skull=Giant Skull|BlackMarble,giant_sword1=Giant Sword|CopperScrap,giant_sword2=Giant Sword|CopperScrap," +
                           "YggdrasilRoot*=Yggdrasil Root|Sap,GuckSack=Guck|Guck,GuckSack_small=Guck|Guck," +
                           // Deposits the catalog had no line for, found by asking the game what
                           // every mineable prefab yields (tgm_deposits). Each of these is a thing
                           // somebody would go to: the fractured twins come with them, since a
                           // name ending _frac falls back to the whole one.
                           "rock3_silver=Silver|SilverOre,MineRock_Copper=Copper|CopperOre," +
                           "goldvein=Gold|GoldOre,TrollFrost*=Frozen Troll|GoldOre";
                case Category.Dungeon:
                    return "Crypt2=Burial Chambers|TrophySkeleton,Crypt3=Burial Chambers|TrophySkeleton,Crypt4=Burial Chambers|TrophySkeleton," +
                           // Iron scrap rather than the draugr's trophy, which was also on the
                           // draugr piles. This is the picture of the thing a sunken crypt is
                           // full of, and the swamp's scrap piles wear it too: the same metal in
                           // the same biome, which is the one collision anybody asked for.
                           "SunkenCrypt4=Sunken Crypt|IronScrap,MountainCave02=Frost Cave|TrophyCultist,TrollCave02=Troll Cave|TrophyFrostTroll," +
                           "Mistlands_DvergrTownEntrance1=Infested Mine|TrophySeeker,Mistlands_DvergrTownEntrance2=Infested Mine|TrophySeeker," +
                           "BearCave=Bear Cave|TrophyBjorn,MorgenHole*=Putrid Hole|TrophyMorgen";
                case Category.Trader:
                    return "Vendor_BlackForest=Haldor|Coins,Hildir_camp=Hildir|Coins,BogWitch_Camp=Bog Witch|Coins";
                case Category.Camp:
                    return "GoblinCamp2=Fuling Village|TrophyGoblin,WoodVillage1=Draugr Village|TrophyDraugr";
                case Category.Spawner:
                    // Vanilla names these three inconsistently -- two Spawner_ and one not -- so
                    // they are listed rather than matched by name. Listing is also the only safe
                    // way: the world is full of invisible Spawner_<creature> points, thousands of
                    // them inside dungeons and camps, and nothing on the map should mark those.
                    return "Spawner_GreydwarfNest=Greydwarf Nest|TrophyGreydwarfBrute," +
                           "Spawner_DraugrPile=Draugr Pile|TrophyDraugr," +
                           // Bones rather than a skeleton trophy: the trophy is what a burial
                           // chamber wears, and two different things wearing one picture is the
                           // map lying about one of them. A pile of bones is what this is.
                           "BonePileSpawner=Evil Bone Pile|BoneFragments," +
                           "CharredStone_Spawner*=Charred Spawner|TrophyCharredMelee";
                case Category.BossAltar:
                    return "Eikthyrnir=Eikthyr|TrophyEikthyr,GDKing=The Elder|TrophyTheElder,Bonemass=Bonemass|TrophyBonemass," +
                           "Dragonqueen=Moder|TrophyDragonQueen,GoblinKing=Yagluth|TrophyGoblinKing," +
                           "Mistlands_DvergrBossEntrance1=The Queen|TrophySeekerQueen,FaderLocation=Fader|TrophyFader";
                case Category.Structure:
                    return "WoodFarm1=Abandoned Farm,WoodHouse*=Abandoned House,AbandonedLogCabin*=Log Cabin,StoneTowerRuins*=Stone Tower Ruins,StoneHouse*=Stone House," +
                           "Ruin*=Ruins,SwampHut*=Swamp Hut,SwampRuin*=Swamp Ruins,SwampWell*=Swamp Well,StoneHenge*=Stonehenge|pin:Memorial,StoneTower*=Stone Tower," +
                           "StoneCircle*=Stone Circle|pin:Memorial,Dolmen*=Dolmen|pin:Memorial,DrakeNest*=Drake Nest|DragonEgg," +
                           "Greydwarf_camp*=Greydwarf Nest|TrophyGreydwarfBrute,ShipSetting*=Ship Setting|pin:Memorial,Waymarker*=Waymarker|pin:Memorial,InfestedTree*=Infested Tree|TrophySeeker," +
                           "Mistlands_GuardTower*=Dvergr Guard Tower|TrophyDvergr,Mistlands_Excavation*=Dvergr Excavation|TrophyDvergr,Mistlands_Harbour*=Dvergr Harbour|TrophyDvergr," +
                           "Mistlands_Lighthouse*=Dvergr Lighthouse|TrophyDvergr," +
                           "Mistlands_Statue*=Dvergr Statue|TrophyDvergr,Mistlands_Viaduct*=Viaduct|TrophyDvergr,CharredRuins*=Charred Ruins,AshlandRuins*=Ashlands Ruins," +
                           "FortressRuins*=Fortress Ruins|TrophyCharredMage,PlaceofMystery*=Place of Mystery";
                case Category.Campfire:
                    return "fire_pit=Campfire|piece:fire_pit";
                case Category.Workbench:
                    return "piece_workbench=Workbench|piece:piece_workbench";
                default:
                    return "";
            }
        }

        internal static string CatalogNote(Category c)
        {
            switch (c)
            {
                case Category.Dungeon:   return "Any location with an interior also counts as a dungeon even if it is not listed.";
                case Category.Ore:       return "Matches MineRock5, MineRock, Destructible and Pickable objects by prefab name, and a plain networked object such as a Yggdrasil root.";
                case Category.Structure: return "Entries ending in * match by prefix. Unlisted outdoor locations also count when 'Structures Include Unlisted' is on.";
                case Category.Campfire:  return "Only fires a player built count, not the ones the world puts in camps and villages. The icon can be piece:<prefab>, a building piece's build-menu picture.";
                case Category.Workbench: return "Only benches a player built count, not the ones the world puts in camps and villages. Any crafting station can be listed here -- a forge, a stonecutter -- and the workbench is listed because it holds the spawning off around it.";
                case Category.Spawner:   return "The nests and piles that creatures come out of, which you can walk up to and break. Valheim also has thousands of invisible spawn points named Spawner_<creature> inside dungeons and camps; do not add those.";
                default:                 return "";
            }
        }
    }

    /// <summary>Something the player has found: where it is and what marker it would produce.</summary>
    internal sealed class Found
    {
        public string Key;
        public Category Cat;
        /// <summary>The prefab the catalog matched, or empty when the kind alone says what this is.</summary>
        public string Source = "";
        public string Icon;
        public string Name;
        public Vector3 Pos;      // where the marker goes
        public Vector3 Center;   // for locations: the location origin (dedupe center)
        public float Radius;     // for locations: exterior radius (dedupe radius); 0 otherwise
        public long FoundAt;     // DateTime.UtcNow.Ticks when first (or last) seen
        public bool Cleared;     // used up before it was written down, so it is written crossed off

        public Vector3 DedupeCenter => Radius > 0f ? Center : Pos;
    }

    /// <summary>
    /// Turns an arbitrary hit or interacted GameObject into a Found record, using the configured
    /// prefab lists plus a few component rules (portals, runestones, interior locations). The
    /// icon is the thing's own item where it has one (what a pickable gives, what a deposit
    /// drops), else the catalog entry's icon, else the category fallback.
    /// </summary>
    internal static class Catalog
    {
        private sealed class Entry
        {
            public Category Cat;
            public string Name;
            public string Icon;
        }

        // Case-insensitive, like the prefix match below and the icon table above. The game's own
        // naming is inconsistent (rock4_copper beside Rock4_cell), and a catalog line that differs
        // only in case looked exactly like no catalog line at all.
        private static readonly Dictionary<string, Entry> _byPrefab = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<KeyValuePair<string, Entry>> _byPrefix = new List<KeyValuePair<string, Entry>>();
        private static readonly Dictionary<string, Entry> _builtInByPrefab = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<KeyValuePair<string, Entry>> _builtInByPrefix = new List<KeyValuePair<string, Entry>>();
        private static string[] _structureExclusions = new string[0];
        private static bool _built;

        internal static void Invalidate()
        {
            _built = false;
            _iconBySource.Clear();
            CrossedOff.Forget();
            // Which of the world's locations are altars and which are traders comes out of the
            // catalog, so the landmarks have to be classified again when it changes.
            WorldPins.Forget();
        }

        private static readonly Dictionary<string, string> _iconBySource =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// What a thing wears now, worked out from what it is rather than read off the marker.
        /// This is the whole point of a marker remembering its prefab: decide tomorrow that a
        /// sunken crypt suits a draugr trophy better than a swamp key, and every sunken crypt
        /// already on the map changes with it, including the ones other people wrote. Resolved
        /// exactly the way recording and the legend resolve it, so all three agree.
        ///
        /// Null when the prefab is not in the catalog at all, which leaves the marker wearing
        /// what it was drawn with -- a thing somebody has since removed from their catalog should
        /// not lose its picture.
        /// </summary>
        internal static string IconFor(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (!_built) Rebuild();
            if (_iconBySource.TryGetValue(source, out var cached)) return cached;
            // "kind:Runestone" names a kind rather than a prefab, and a kind draws with its own
            // icon -- the one the legend shows and the Icons section sets. Without this the
            // writer finds nothing to derive and keeps a copy of an icon the kind already says.
            string icon = SharedPin.IsKindOnly(source)
                ? IconForKind(source.Substring(5))
                : (Lookup(source, out var e) ? ResolveLegendIcon(source, e, out _) : null);
            _iconBySource[source] = icon;
            return icon;
        }

        /// <summary>The icon a kind draws with, as configured or as it comes.</summary>
        private static string IconForKind(string kind)
        {
            if (!System.Enum.TryParse(kind, true, out Category cat)) return null;
            string configured = TgmConfig.CategoryIcon.TryGetValue(cat, out var cfg) ? cfg.Value : null;
            return IconRegistry.Normalize(string.IsNullOrEmpty(configured) ? Categories.DefaultIcon(cat) : configured);
        }

        private static void Rebuild()
        {
            _orderByPrefab.Clear();
            _orderByIcon = null;
            _farmableByIcon = null;
            _plantRankByIcon = null;
            _ordered = 0;
            _iconBySource.Clear();
            _byPrefab.Clear();
            _byPrefix.Clear();
            _builtInByPrefab.Clear();
            _builtInByPrefix.Clear();
            foreach (var cat in Categories.All)
            {
                if (TgmConfig.CategoryPrefabs.TryGetValue(cat, out var entry))
                    Parse(cat, entry.Value, _byPrefab, _byPrefix);
                // The same list as this version ships it. A config file written by an older build
                // keeps its old line, which is how a newly catalogued location (bear caves, and
                // burial chambers before them) stayed unknown to everyone who had played before.
                Parse(cat, Categories.DefaultPrefabs(cat), _builtInByPrefab, _builtInByPrefix);
            }
            // Longest prefix wins when several match.
            _byPrefix.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            _builtInByPrefix.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            var exclusions = new List<string>();
            foreach (var raw in (TgmConfig.StructuresExcludePrefixes.Value ?? "").Split(','))
            {
                string s = raw.Trim();
                if (s.Length > 0) exclusions.Add(s);
            }
            _structureExclusions = exclusions.ToArray();
            _built = true;
        }

        /// <summary>
        /// Where each prefab stands in its kind's catalog line, and from that where each icon
        /// stands. The lines are written in the order a player meets the things in them --
        /// dandelions before thistles, raspberries before cloudberries, copper before silver --
        /// so the order is already there to be read, and a player who disagrees can reorder the
        /// line and the map's lists follow.
        /// </summary>
        private static readonly Dictionary<string, int> _orderByPrefab = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int> _orderByIcon;
        private static int _ordered;

        /// <summary>
        /// Where this icon's type stands in its kind, or a number past the end for one the
        /// catalog does not list. Built the first time it is wanted, from the catalog as parsed.
        /// </summary>
        internal static int OrderOf(string icon)
        {
            if (!_built) Rebuild();
            if (_orderByIcon == null)
            {
                _orderByIcon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _orderByPrefab)
                {
                    string key = IconFor(kv.Key);
                    if (key == null) continue;
                    // The first line to claim a picture owns its place: two prefabs drawn alike
                    // are one row in the list, and it stands where the earlier of them does.
                    if (!_orderByIcon.ContainsKey(key) || _orderByIcon[key] > kv.Value) _orderByIcon[key] = kv.Value;
                }
            }
            string norm = IconRegistry.Normalize(icon);
            return norm != null && _orderByIcon.TryGetValue(norm, out int at) ? at : int.MaxValue;
        }

        /// <summary>
        /// Everything in the game that can be planted and later picked again, as the prefab names
        /// the catalog lines use.
        ///
        /// <para>Read out of the game's own asset names rather than remembered: a thing is
        /// plantable exactly when it has a <c>sapling_</c> prefab, and the full list of those is
        /// <c>sapling_barley</c>, <c>_carrot</c>, <c>_flax</c>, <c>_jotunpuffs</c>,
        /// <c>_magecap</c>, <c>_onion</c>, <c>_turnip</c> and the three <c>_seed*</c> ones, beside
        /// the five tree saplings and two vines. So the three crop seeds are farmable and close
        /// their own loop -- a carrot grows seeds and the seeds grow carrots -- while the berries,
        /// the three common mushrooms, smoke puffs, dandelion, thistle and fiddlehead have no
        /// sapling at all and never will have one by accident.</para>
        ///
        /// <para>Ore is not here and needs no entry: no vein can be farmed, which is why it stays
        /// a kind of its own however the plants are grouped.</para>
        /// </summary>
        private static readonly HashSet<string> Plantable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pickable_SeedCarrot", "Pickable_SeedTurnip", "Pickable_SeedOnion",
            "Pickable_Mushroom_Magecap", "Pickable_Mushroom_JotunPuffs",
            "Pickable_Flax_Wild", "Pickable_Barley_Wild",
            "VineAsh",
        };

        /// <summary>
        /// The order the plants are met in, across every kind they are spread over, for the two
        /// halves to list their types in.
        ///
        /// <para>Everything else in this mod reads its progression order out of the catalog lines,
        /// which are written in the order a player meets the things in them -- and a player who
        /// disagrees reorders the line and the lists follow. That cannot order a half: a line puts
        /// its own entries in order and says nothing about where they stand against another kind's,
        /// and a half is a handful of types out of four kinds. Taken from the lines alone, magecap
        /// came out above carrot seeds -- a Mistlands mushroom before a Black Forest seed -- for no
        /// reason but Mushrooms standing above Seeds in the enum. Biome is a fact about the type,
        /// not about its kind, so for these it is written down.</para>
        ///
        /// <para>Farmable first, in the order you can first plant one: the Black Forest's carrots,
        /// the swamp's turnips, the mountain's onions, the Plains' barley and flax, the Mistlands'
        /// jotun puffs and magecap, the Ashlands' vineberries. Then wild, in the order you first
        /// walk past one: the meadows' raspberries, mushrooms and dandelions, the Black Forest's
        /// blueberries and yellow mushrooms, thistle, the Plains' cloudberries, the Mistlands'
        /// blue mushrooms and fiddlehead, the Ashlands' smoke puffs. Anything not named here -- a
        /// player's own catalog entry -- sorts after
        /// all of it by the catalog's order, the same way the ordinary type rows treat a type the
        /// catalog has never heard of.</para>
        /// </summary>
        private static readonly string[] PlantProgression =
        {
            "Pickable_SeedCarrot", "Pickable_SeedTurnip", "Pickable_SeedOnion",
            "Pickable_Barley_Wild", "Pickable_Flax_Wild",
            "Pickable_Mushroom_JotunPuffs", "Pickable_Mushroom_Magecap",
            "VineAsh",

            "RaspberryBush", "Pickable_Mushroom", "Pickable_Dandelion",
            "BlueberryBush", "Pickable_Mushroom_yellow", "Pickable_Thistle",
            "CloudberryBush", "Pickable_Mushroom_blue", "Pickable_Fiddlehead",
            "Pickable_SmokePuff",
        };

        private static Dictionary<string, int> _plantRankByIcon;

        /// <summary>
        /// Where the type drawn with this icon stands among the plants, or a number past the end
        /// for one this does not name. Asked of the icon rather than the prefab for the same
        /// reason <see cref="CanBeFarmed"/> is: a row in the list is a picture.
        /// </summary>
        internal static int PlantOrderOf(string icon)
        {
            if (!_built) Rebuild();
            if (_plantRankByIcon == null)
            {
                _plantRankByIcon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < PlantProgression.Length; i++)
                {
                    string key = IconFor(PlantProgression[i]);
                    if (key == null || _plantRankByIcon.ContainsKey(key)) continue;
                    _plantRankByIcon[key] = i;
                }
            }
            string norm = IconRegistry.Normalize(icon);
            return norm != null && _plantRankByIcon.TryGetValue(norm, out int at) ? at : int.MaxValue;
        }

        private static Dictionary<string, bool> _farmableByIcon;

        /// <summary>
        /// Can the type drawn with this icon be planted? Asked of the icon rather than the prefab
        /// because a type in the marker list <i>is</i> an icon -- that is the only thing a marker
        /// carries saying which type it is -- so the answer has to be one per picture.
        ///
        /// <para>Two prefabs drawn alike are one row, and one row cannot be in both halves, so the
        /// earlier of them in the catalog answers for the picture. That is the same rule the
        /// ordering uses, and for the same reason. Nothing in the shipped catalog is in that
        /// position: every farmable entry has a picture of its own.</para>
        /// </summary>
        internal static bool CanBeFarmed(string icon)
        {
            if (!_built) Rebuild();
            if (_farmableByIcon == null)
            {
                _farmableByIcon = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _orderByPrefab)
                {
                    string key = IconFor(kv.Key);
                    if (key == null) continue;
                    if (rank.TryGetValue(key, out int at) && at <= kv.Value) continue;
                    rank[key] = kv.Value;
                    _farmableByIcon[key] = Plantable.Contains(kv.Key);
                }
            }
            string norm = IconRegistry.Normalize(icon);
            return norm != null && _farmableByIcon.TryGetValue(norm, out bool yes) && yes;
        }

        /// <summary>
        /// A saved catalog line put back into the order the built-in one is written in, keeping
        /// every entry it has and every word of each of them.
        ///
        /// <para>The order of these lines meant nothing until the marker list learned to read it,
        /// so a line saved by an older version is in whatever order that version happened to ship
        /// -- and a player who has never touched the setting still gets that order forever, since
        /// a saved line always beats a new default. Nobody chose it, so nothing is lost by
        /// putting it right once.</para>
        ///
        /// <para>Only the order moves. An entry the player renamed, gave an icon to, or added
        /// themselves is carried across exactly as written: the ones the built-in line knows go
        /// in its order, and anything else keeps its place after them, in the order it was in.
        /// So the set is the same set, and a player who reorders the line afterwards keeps their
        /// order -- this is done once, not at every login.</para>
        /// </summary>
        internal static string InDefaultOrder(string saved, string builtIn)
        {
            if (string.IsNullOrEmpty(saved) || string.IsNullOrEmpty(builtIn)) return saved;
            var entries = new List<KeyValuePair<string, string>>();   // prefab, as written
            foreach (var raw in saved.Split(','))
            {
                string one = raw.Trim();
                if (one.Length == 0) continue;
                entries.Add(new KeyValuePair<string, string>(PrefabOf(one), one));
            }
            if (entries.Count == 0) return saved;

            var order = new List<string>();
            foreach (var raw in builtIn.Split(','))
            {
                string one = raw.Trim();
                if (one.Length > 0) order.Add(PrefabOf(one));
            }

            var taken = new bool[entries.Count];
            var result = new List<string>(entries.Count);
            foreach (var want in order)
                for (int i = 0; i < entries.Count; i++)
                    if (!taken[i] && string.Equals(entries[i].Key, want, StringComparison.OrdinalIgnoreCase))
                    {
                        taken[i] = true;
                        result.Add(entries[i].Value);
                    }
            // Whatever the built-in line has never heard of: the player's own, and anything the
            // mod has since dropped. Kept, in the order they were already in.
            for (int i = 0; i < entries.Count; i++) if (!taken[i]) result.Add(entries[i].Value);
            return string.Join(",", result.ToArray());
        }

        /// <summary>
        /// Change one entry's icon in a saved catalog line, and only where it still says exactly
        /// what it is expected to say. Everything else about the line -- the order, the other
        /// entries, this entry's own display name -- is left as written.
        /// </summary>
        internal static string WithIcon(string saved, string prefab, string was, string now)
        {
            if (string.IsNullOrEmpty(saved)) return saved;
            var parts = saved.Split(',');
            bool changed = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string one = parts[i].Trim();
                int bar = one.IndexOf('|');
                if (bar < 0) continue;
                if (!string.Equals(PrefabOf(one), prefab, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(one.Substring(bar + 1).Trim(), was, StringComparison.Ordinal)) continue;
                parts[i] = one.Substring(0, bar + 1) + now;
                changed = true;
            }
            return changed ? string.Join(",", parts) : saved;
        }

        /// <summary>The prefab part of one catalog entry: "name=Label|Icon" is "name".</summary>
        private static string PrefabOf(string entry)
        {
            string item = entry;
            int bar = item.IndexOf('|');
            if (bar >= 0) item = item.Substring(0, bar);
            int eq = item.IndexOf('=');
            if (eq > 0) item = item.Substring(0, eq);
            return item.Trim();
        }

        private static void Parse(Category cat, string list, Dictionary<string, Entry> byPrefab, List<KeyValuePair<string, Entry>> byPrefix)
        {
            foreach (var raw in (list ?? "").Split(','))
            {
                string item = raw.Trim();
                if (item.Length == 0) continue;
                string prefab = item, name = null, icon = null;
                int bar = item.IndexOf('|');
                if (bar >= 0) { icon = item.Substring(bar + 1).Trim(); item = item.Substring(0, bar); prefab = item; }
                int eq = item.IndexOf('=');
                if (eq > 0) { prefab = item.Substring(0, eq).Trim(); name = item.Substring(eq + 1).Trim(); }
                if (prefab.Length == 0) continue;
                var e = new Entry
                {
                    Cat = cat,
                    Name = string.IsNullOrEmpty(name) ? null : name,
                    Icon = string.IsNullOrEmpty(icon) ? null : icon,
                };
                // Counted across every kind rather than within one: the lists these order are
                // always of one kind, and one running number needs no second bookkeeping.
                string bare = prefab.TrimEnd('*');
                if (bare.Length > 0 && !_orderByPrefab.ContainsKey(bare)) _orderByPrefab[bare] = ++_ordered;
                if (prefab.EndsWith("*"))
                {
                    string prefix = prefab.Substring(0, prefab.Length - 1);
                    if (prefix.Length > 0) byPrefix.Add(new KeyValuePair<string, Entry>(prefix, e));
                }
                else
                {
                    byPrefab[prefab] = e;
                }
            }
        }

        /// <summary>One catalog line, with the icon it will actually draw and where that came from.</summary>
        /// <summary>
        /// Write an icon into the player's own catalog line for one prefab, as the legend's editor
        /// does. An entry the player does not have -- most of them, since the shipped list is only
        /// a fallback -- is added rather than silently ignored, keeping the name the legend shows
        /// so the line still reads as a sentence. An empty icon takes the override off again and
        /// lets the icon be worked out as before.
        /// </summary>
        internal static bool SetIcon(Category cat, string prefab, string name, string icon)
        {
            if (string.IsNullOrEmpty(prefab) || !TgmConfig.CategoryPrefabs.TryGetValue(cat, out var setting)) return false;
            icon = (icon ?? "").Trim();
            var parts = new List<string>((setting.Value ?? "").Split(','));
            bool done = false;
            for (int i = 0; i < parts.Count && !done; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;
                int bar = part.IndexOf('|');
                string head = bar >= 0 ? part.Substring(0, bar) : part;
                int eq = head.IndexOf('=');
                string key = (eq >= 0 ? head.Substring(0, eq) : head).Trim();
                if (!string.Equals(key, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                parts[i] = icon.Length == 0 ? head : head + "|" + icon;
                done = true;
            }
            if (!done)
            {
                string head = string.IsNullOrEmpty(name) ? prefab : prefab + "=" + name;
                parts.Add(icon.Length == 0 ? head : head + "|" + icon);
            }
            var kept = new List<string>();
            foreach (var p in parts) if (p.Trim().Length > 0) kept.Add(p.Trim());
            setting.Value = string.Join(",", kept.ToArray());   // SettingChanged rebuilds the catalog
            return true;
        }

        /// <summary>
        /// What one marker of this kind actually is, read from its icon: a frost cave rather than
        /// a dungeon. Dungeons and structures are several things wearing one kind, and the icon is
        /// the only thing on a marker that says which -- the same reading the hide list uses to
        /// group them. Null for a kind that is only ever itself, and null for a marker still
        /// wearing the kind's fallback icon, which says nothing about which of them it is.
        /// </summary>
        /// <summary>
        /// What a thing is called, from what it is. A marker that knows its own prefab can be
        /// named exactly -- a swamp hut rather than "Structures" -- which the icon could never
        /// do, because a dozen buildings share the house pin. Null when the catalog has no line
        /// for it, which leaves the icon to answer as before.
        /// </summary>
        internal static string NameForSource(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (!_built) Rebuild();
            if (!Lookup(source, out var e)) return null;
            return e.Name ?? Prettify(source);
        }

        internal static string TypeNameFor(Category cat, string icon)
        {
            if (!CrossedOff.HasTypes(cat)) return null;
            string key = IconRegistry.Normalize(icon);
            if (key == null) return null;
            // One house pin covers abandoned farms, swamp huts and stone towers alike, so the
            // catalog holds a line mapping that pin to each of them, and the first one found wins
            // a race it should never have been entered in. Naming them all after it -- every
            // structure in the game an "Abandoned Farm" -- is worse than saying nothing. The hide
            // list draws the same line, where these all go to the catch-all group.
            if (string.Equals(key, IconRegistry.Normalize(Categories.DefaultIcon(cat)), StringComparison.OrdinalIgnoreCase))
                return null;
            foreach (var kv in TypesOf(cat))
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        internal sealed class LegendRow
        {
            public Category Cat;
            public string Prefab;
            public string Name;
            public string Icon;
            public string Source;
            public bool Configured;   // the player's own line names this icon, not a fallback
            public bool Missing;    // the icon key names an item this game does not have
            public bool NoPrefab;   // nothing by that name exists in this game at all
        }

        /// <summary>
        /// Every catalog line, resolved the way recording resolves it, so the picture in the legend
        /// is the picture you will get. Most icons are not written in the catalog at all: a plant
        /// borrows the icon of the item it gives and a deposit the icon of what it drops, which is
        /// only knowable by looking the prefab up in the loaded world. That is the point of showing
        /// this in game rather than as a table in the readme.
        /// </summary>
        internal static List<LegendRow> Legend()
        {
            if (!_built) Rebuild();
            var rows = new List<LegendRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string prefab, Entry e)
            {
                if (prefab == null || !seen.Add(prefab + "/" + e.Cat)) return;
                string source;
                string icon = ResolveLegendIcon(prefab, e, out source);
                rows.Add(new LegendRow
                {
                    Cat = e.Cat,
                    Prefab = prefab,
                    Name = e.Name ?? Prettify(prefab.TrimEnd('*')),
                    Icon = icon,
                    Source = source,
                    Configured = source == "catalog",
                    Missing = icon != null && (icon.StartsWith("item:") ? !ItemExists(icon.Substring(5))
                        : icon.StartsWith("piece:") && ZNetScene.instance != null && ZNetScene.instance.GetPrefab(icon.Substring(6)) == null),
                    // A catalog line for something the game does not have is dead weight, and is
                    // invisible otherwise: onion seeds, for instance, come out of chests rather
                    // than the ground, so there may be nothing in the world to ever mark.
                    NoPrefab = !prefab.EndsWith("*") && !ExistsInGame(prefab),
                });
            }
            foreach (var kv in _byPrefab) Add(kv.Key, kv.Value);
            foreach (var kv in _byPrefix) Add(kv.Key + "*", kv.Value);
            foreach (var kv in _builtInByPrefab) Add(kv.Key, kv.Value);
            foreach (var kv in _builtInByPrefix) Add(kv.Key + "*", kv.Value);
            rows.Sort((a, b) => a.Cat != b.Cat ? a.Cat.CompareTo(b.Cat) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return rows;
        }

        /// <summary>
        /// Each dungeon type the catalog knows, as (icon key, name), in catalog order, with the icon
        /// resolved the way recording resolves it. The icon is the only thing on a stored dungeon
        /// marker that says which dungeon it is.
        /// </summary>
        /// <summary>
        /// The kinds of thing a category holds, as (icon, name) pairs: burial chambers and sunken
        /// crypts within dungeons, swamp huts and mountain graves within structures. A marker
        /// stores neither its type nor, usually, a label, so its icon is the only handle on what it
        /// is -- which is also how a player tells them apart on the map.
        /// </summary>
        internal static List<KeyValuePair<string, string>> TypesOf(Category cat)
        {
            if (!_built) Rebuild();
            var list = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string prefab, Entry e)
            {
                if (e.Cat != cat || string.IsNullOrEmpty(prefab)) return;
                string guess = cat == Category.Dungeon ? IconFromDungeonName(prefab) : null;
                string icon = IconRegistry.Normalize(e.Icon ?? Categories.KnownIcon(prefab) ?? guess ?? Categories.DefaultIcon(cat));
                if (icon == null || !seen.Add(icon)) return;
                list.Add(new KeyValuePair<string, string>(icon, e.Name ?? Prettify(prefab)));
            }
            foreach (var kv in _byPrefab) Add(kv.Key, kv.Value);
            foreach (var kv in _byPrefix) Add(kv.Key, kv.Value);
            foreach (var kv in _builtInByPrefab) Add(kv.Key, kv.Value);
            foreach (var kv in _builtInByPrefix) Add(kv.Key, kv.Value);
            return list;
        }

        private static HashSet<string> _locationNames;
        private static int _locationsCounted = -1;

        /// <summary>
        /// Is there anything in this game by that name? Dungeons, camps, traders, boss altars and
        /// most structures are locations, which the world places rather than spawns, and they are
        /// not in ZNetScene at all -- so asking it alone called every one of them missing and put
        /// twenty honest catalog lines in the legend's "not in this game" count.
        /// </summary>
        private static bool ExistsInGame(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            try
            {
                if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) != null) return true;
                var zone = ZoneSystem.instance;
                var locations = zone != null ? zone.m_locations : null;
                if (locations == null) return ZNetScene.instance == null;   // nothing loaded: do not accuse
                if (_locationNames == null || _locationsCounted != locations.Count)
                {
                    _locationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var loc in locations)
                    {
                        if (loc == null) continue;
                        if (!string.IsNullOrEmpty(loc.m_prefabName)) _locationNames.Add(loc.m_prefabName);
                        if (!string.IsNullOrEmpty(loc.m_name)) _locationNames.Add(loc.m_name);
                    }
                    _locationsCounted = locations.Count;
                }
                return _locationNames.Contains(prefab);
            }
            catch (Exception) { return true; }   // cannot tell, so do not cry wolf
        }

        /// <summary>
        /// How far a place of this name reaches, or zero where the game has no location by that
        /// name. Which locations the generator placed, and where, is the server's business; how
        /// big each kind of place is ships with the game, so a client can answer this alone.
        ///
        /// It is how far apart two markers for one place can stand: a marker sits where the
        /// player who found the place was standing, and a tar pit is wide enough that two people
        /// finding the same one write their markers nine meters apart.
        /// </summary>
        internal static float LocationRadius(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return 0f;
            try
            {
                var zone = ZoneSystem.instance;
                var locations = zone != null ? zone.m_locations : null;
                if (locations == null) return 0f;
                foreach (var loc in locations)
                {
                    if (loc == null) continue;
                    if (!string.Equals(loc.m_prefabName, prefab, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(loc.m_name, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                    // Reached by name: the field is not public on the real assembly, and reading
                    // it directly compiles against Libs and throws against the game.
                    return HarmonyLib.Traverse.Create(loc).Field("m_exteriorRadius").GetValue<float>();
                }
            }
            catch (Exception) { /* nothing loaded, or the field moved: no radius to offer */ }
            return 0f;
        }

        private static bool ItemExists(string name)
        {
            try { return IconRegistry.ItemPrefab(name) != null; }
            catch (Exception) { return false; }
        }

        private static string ResolveLegendIcon(string prefab, Entry e, out string source)
        {
            string bare = prefab.TrimEnd('*');
            if (!string.IsNullOrEmpty(e.Icon)) { source = "catalog"; return IconRegistry.Normalize(e.Icon); }
            string known = Categories.KnownIcon(bare);
            if (known != null) { source = "built in"; return IconRegistry.Normalize(known); }

            // The same sources recording uses, read off the prefab in the loaded world.
            var scene = ZNetScene.instance;
            var go = scene != null ? scene.GetPrefab(bare) : null;
            if (go != null)
            {
                var pickable = go.GetComponent<Pickable>();
                if (pickable != null && pickable.m_itemPrefab != null) { source = "what it gives"; return IconRegistry.ItemKey(pickable.m_itemPrefab); }
                var rock5 = go.GetComponent<MineRock5>();
                if (rock5 != null) { string d = FirstDrop(rock5.m_dropItems); if (d != null) { source = "what it drops"; return d; } }
                var rock = go.GetComponent<MineRock>();
                if (rock != null) { string d = FirstDrop(rock.m_dropItems); if (d != null) { source = "what it drops"; return d; } }
                var destructible = go.GetComponent<Destructible>();
                var drops = destructible != null ? go.GetComponent<DropOnDestroyed>() : null;
                if (drops != null) { string d = FirstDrop(drops.m_dropWhenDestroyed); if (d != null) { source = "what it drops"; return d; } }
            }
            if (e.Cat == Category.Dungeon)
            {
                string guess = IconFromDungeonName(bare);
                if (guess != null) { source = "guessed from the name"; return IconRegistry.Normalize(guess); }
            }
            source = "the kind's fallback";
            string fallback = TgmConfig.CategoryIcon.TryGetValue(e.Cat, out var cfg) ? cfg.Value : Categories.DefaultIcon(e.Cat);
            return IconRegistry.Normalize(fallback);
        }

        /// <summary>
        /// Why classification did or did not recognize this object, for the look report. Finding
        /// the component is only half the job: its prefab name still has to match a catalog line,
        /// and a mismatch there looks exactly like finding nothing at all.
        /// </summary>
        internal static string Explain(GameObject go)
        {
            if (go == null) return "no object";
            if (!_built) Rebuild();
            string Check(string what, GameObject owner)
            {
                if (owner == null) return null;
                string prefab = PrefabName(owner);
                bool known = Lookup(prefab, out var e);
                return $"{what} on '{prefab}': " + (known
                    ? $"catalogued as {e.Cat}" + (e.Name != null ? $" '{e.Name}'" : "") + (e.Icon != null ? $", icon {e.Icon}" : "")
                    : "NOT in the catalog, so nothing is recorded for it");
            }
            var rock5 = go.GetComponentInParent<MineRock5>();
            if (rock5 != null) return Check("MineRock5", rock5.gameObject);
            var rock = go.GetComponentInParent<MineRock>();
            if (rock != null) return Check("MineRock", rock.gameObject);
            var pickable = go.GetComponentInParent<Pickable>();
            if (pickable != null) return Check("Pickable", pickable.gameObject);
            var destructible = go.GetComponentInParent<Destructible>();
            if (destructible != null) return Check("Destructible", destructible.gameObject);
            return "no deposit, plant or destructible component on this object or any parent";
        }

        /// <summary>
        /// The prefab an object came from, asked of the network object rather than read off the
        /// GameObject's name. Valheim renames things at runtime: a mined deposit's own object ends
        /// up called "___MineRock5 m_meshFilter", because MineRock5 sets a name on its MeshFilter
        /// component and in Unity that renames the GameObject. Anything trusting the name then sees
        /// a deposit it has never heard of, which is how a copper vein beside a marked one went
        /// unrecorded. The ZDO keeps the prefab it was spawned from, and that cannot drift.
        /// </summary>
        internal static string PrefabName(GameObject go)
        {
            if (go == null) return null;
            try
            {
                var view = go.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && ZNetScene.instance != null)
                {
                    var prefab = ZNetScene.instance.GetPrefab(view.GetZDO().GetPrefab());
                    if (prefab != null) return Utils.GetPrefabName(prefab);
                }
            }
            catch (Exception) { }
            return Utils.GetPrefabName(go);
        }

        /// <summary>
        /// Every deposit prefab the game has, what it yields, and whether the catalog knows it.
        /// This exists to answer one question with facts: if recording a deposit were decided by
        /// what it drops rather than by a list of names, what would start or stop being recorded?
        /// </summary>
        internal static List<string> Deposits()
        {
            var lines = new List<string>();
            var scene = ZNetScene.instance;
            if (scene == null) { lines.Add("No world loaded."); return lines; }
            if (!_built) Rebuild();
            int wouldChange = 0;
            var seen = new List<string>();
            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;
                bool isRock = prefab.GetComponent<MineRock5>() != null || prefab.GetComponent<MineRock>() != null;
                if (!isRock) continue;
                string name = Utils.GetPrefabName(prefab);
                var rock5 = prefab.GetComponent<MineRock5>();
                var rock = prefab.GetComponent<MineRock>();
                string drop = rock5 != null ? FirstDrop(rock5.m_dropItems) : FirstDrop(rock.m_dropItems);
                bool rubbleOnly = drop == null || Rubble.Contains(drop.StartsWith("item:") ? drop.Substring(5) : drop);
                bool known = Lookup(name, out var e);
                bool wouldRecord = !rubbleOnly;
                if (wouldRecord != known) wouldChange++;
                seen.Add($"  {name}: yields {(drop ?? "nothing")}{(rubbleOnly ? " (rubble only)" : "")}, " +
                         $"catalog says {(known ? e.Cat.ToString() : "unknown")}" +
                         (wouldRecord != known ? (wouldRecord ? "  <- WOULD START being recorded" : "  <- WOULD STOP being recorded") : ""));
            }
            seen.Sort(StringComparer.OrdinalIgnoreCase);
            lines.Add($"{seen.Count} deposit prefabs; {wouldChange} would change if what it drops decided rather than the catalog.");
            lines.AddRange(seen);
            return lines;
        }

        /// <summary>
        /// Every spawner prefab the game has, what it sends out, whether it can be broken, and
        /// whether the catalog knows it.
        ///
        /// <para>"Am I missing one?" cannot be answered from a list somebody wrote down, which is
        /// the thing being checked. It is answered by the game: a spawner that stands in the
        /// world carries a <c>SpawnArea</c>, which is the component that sends creatures out at
        /// whoever walks near, so asking every prefab whether it has one is asking the question
        /// itself. The web disagrees with itself about this -- one dump of the vanilla spawners
        /// lists EvilHeart_Forest and EvilHeart_Swamp as well, with nothing said about whether
        /// either is placed in a world -- and a game update can add one at any time.</para>
        ///
        /// <para>Told apart from the thousands of invisible spawn points inside dungeons and
        /// camps, which carry <c>CreatureSpawner</c> instead: those are a trigger in the ground
        /// with no object to break and nothing to walk up to, and nothing on the map should mark
        /// one. They are counted rather than listed, so the distinction is on the screen.</para>
        /// </summary>
        internal static List<string> Spawners()
        {
            var lines = new List<string>();
            var scene = ZNetScene.instance;
            if (scene == null) { lines.Add("No world loaded."); return lines; }
            if (!_built) Rebuild();
            var seen = new List<string>();
            int points = 0, unknown = 0;
            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;
                // On the prefab or on anything under it: a nest keeps its spawner on the root
                // and nothing says the next one has to.
                if (prefab.GetComponentInChildren<CreatureSpawner>(true) != null) { points++; continue; }
                var area = prefab.GetComponentInChildren<SpawnArea>(true);
                if (area == null) continue;
                string name = Utils.GetPrefabName(prefab);
                var what = new List<string>();
                if (area.m_prefabs != null)
                    foreach (var one in area.m_prefabs)
                    {
                        if (one == null || one.m_prefab == null) continue;
                        string sent = Utils.GetPrefabName(one.m_prefab);
                        if (!what.Contains(sent)) what.Add(sent);
                    }
                // Whether there is anything to break, which is what makes a spawner a place a
                // player goes rather than a fact about the ground.
                bool breakable = prefab.GetComponentInChildren<Destructible>(true) != null
                                 || prefab.GetComponentInChildren<MineRock5>(true) != null
                                 || prefab.GetComponentInChildren<MineRock>(true) != null;
                bool known = Lookup(name, out var e);
                if (!known) unknown++;
                seen.Add($"  {name}: sends out {(what.Count > 0 ? string.Join(", ", what.ToArray()) : "nothing")}; " +
                         $"{(breakable ? "can be broken" : "nothing to break")}; " +
                         $"catalog says {(known ? e.Cat.ToString() : "unknown")}" +
                         (!known && breakable ? "  <- NOT in the catalog" : ""));
            }
            seen.Sort(StringComparer.OrdinalIgnoreCase);
            lines.Add($"{seen.Count} spawner prefab(s) that stand in the world; " +
                      $"{points} invisible spawn point prefab(s), which nothing should mark.");
            lines.AddRange(seen);
            if (unknown > 0)
                lines.Add("A spawner the catalog does not know is not recorded. Add it to [Catalog] Spawners as " +
                          "Prefab=Display Name|Icon if it is something you would go to.");
            return lines;
        }

        private static readonly HashSet<string> _unlistedDeposits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A deposit is worth marking because of what it yields, not because its name is on a list.
        /// The object already carries both facts: the display name the HUD shows when you point at
        /// it, and the table of what mining it gives you. Asking those two questions is simpler than
        /// keeping a list of every prefab in the game, and it cannot fall out of step with the game
        /// the way a list does: a renamed object, a fractured twin, a capital letter or an ore added
        /// in a later update all used to read as "not a deposit at all".
        ///
        /// A plain boulder yields only rubble, so it is not marked. The catalog still decides what a
        /// deposit is called and which icon it wears, where we want something better than the
        /// defaults, and it can still put a deposit in some other category.
        /// </summary>
        /// <summary>
        /// Kinds whose catalog lines name ordinary objects standing in the world. The rest name
        /// spawned locations (structures, dungeons, camps, altars, traders), which need their
        /// radius and interior weighed, or campfires, which count only when a player built one.
        /// </summary>
        private static bool IsLooseObjectKind(Category c)
        {
            switch (c)
            {
                case Category.Berries:
                case Category.Mushrooms:
                case Category.Herbs:
                case Category.Seeds:
                case Category.Plants:
                case Category.Ore:
                // A nest, a draugr pile, a charred stone: things standing in the world that you
                // walk up to and break, and the catalog lists the four of them by name rather
                // than by pattern, so the thousands of invisible spawn points inside dungeons and
                // camps cannot come in with them. They were left out of this road when it was
                // written, which left a hammer dropped on a greydwarf nest with nothing to find.
                case Category.Spawner: return true;
                default:            return false;
            }
        }

        private static bool ClassifyDeposit(GameObject owner, string displayName, DropTable drops, out Found found)
        {
            found = null;
            string prefab = PrefabName(owner);
            string drop = FirstDrop(drops);
            // Mined down to a stub: written crossed off, the same as a thing used up in front of
            // you. A vein somebody else emptied last month is no more worth walking to.
            var view = owner.GetComponent<ZNetView>();
            bool mined = MinedDown(Remaining(view != null && view.IsValid() ? view.GetZDO() : null));
            if (Lookup(prefab, out var e))
            {
                string icon = e.Icon ?? Categories.KnownIcon(prefab) ?? drop;
                found = Make(e.Cat, e.Name ?? Loc(displayName, Prettify(prefab)), icon, owner.transform.position, KeyOf(owner, prefab), prefab);
                found.Cleared = mined;
                return true;
            }
            if (!WorthMining(drop)) return false;
            if (_unlistedDeposits.Add(prefab ?? ""))
                TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Deposit '{prefab}' is not in the catalog; marking it anyway because it yields {drop}.");
            found = Make(Category.Ore, Loc(displayName, Prettify(prefab)), drop, owner.transform.position, KeyOf(owner, prefab), prefab);
            found.Cleared = mined;
            return true;
        }

        /// <summary>
        /// How much of a deposit is still there, as a fraction of its pieces: 1 for untouched, 0.2
        /// for a vein mined down to a fifth of itself, and -1 when the question does not apply.
        ///
        /// <para>MineRock5 keeps one health per piece in its own record -- a base64 package of a
        /// count and that many floats -- and a piece at or below zero has been mined away. Nothing
        /// else in the world says this: the object looks much the same from the map whether it is
        /// whole or a stub, and the mod has been telling players "copper here" with equal
        /// confidence either way. A deposit mined to nothing is not the question, because the game
        /// destroys that one outright, record and all.</para>
        ///
        /// <para>Read from the record rather than from the object, so it can be asked of a vein
        /// nobody is standing near -- which is what the admin sweep needs -- and so there is one
        /// answer rather than two.</para>
        /// </summary>
        internal static float Remaining(ZDO zdo)
        {
            if (zdo == null) return -1f;
            // Floats and strings live in separate tables under the same name, so this cannot pick
            // up the plain health a destructible or a piece keeps under the same key.
            string text = zdo.GetString(ZDOVars.s_health, "");
            if (string.IsNullOrEmpty(text)) return -1f;      // never touched: nothing is written yet
            try
            {
                var pkg = new ZPackage(Convert.FromBase64String(text));
                int count = pkg.ReadInt();
                if (count <= 0) return -1f;
                int alive = 0;
                for (int i = 0; i < count; i++) if (pkg.ReadSingle() > 0f) alive++;
                return (float)alive / count;
            }
            catch (Exception)
            {
                // Somebody else's data in a field we only read. Not knowing is the honest answer.
                return -1f;
            }
        }

        /// <summary>
        /// Has this much of a deposit been mined away for the marker to be written crossed off?
        /// The player sets where that line is, because how much of a vein is worth a walk is
        /// theirs to judge; zero turns it off and the marker is written the same either way.
        /// </summary>
        internal static bool MinedDown(float remaining)
        {
            if (remaining < 0f) return false;
            float limit = TgmConfig.CrossOffMinedDownTo != null ? TgmConfig.CrossOffMinedDownTo.Value : 0;
            return limit > 0 && remaining * 100f <= limit;
        }

        /// <summary>True when a deposit gives something you would go there for, rather than rubble.</summary>
        private static bool WorthMining(string dropKey)
        {
            if (string.IsNullOrEmpty(dropKey)) return false;
            string name = dropKey.StartsWith("item:") ? dropKey.Substring(5) : dropKey;
            return !Rubble.Contains(name);
        }

        /// <summary>What kind the catalog files this prefab under, or nothing if it has no line.</summary>
        internal static Category? CategoryOf(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            if (!_built) Rebuild();
            return Lookup(prefab, out var entry) ? entry.Cat : (Category?)null;
        }

        private static bool Lookup(string prefab, out Entry entry)
        {
            if (!_built) Rebuild();
            entry = null;
            if (string.IsNullOrEmpty(prefab)) return false;
            if (_byPrefab.TryGetValue(prefab, out entry)) return true;
            foreach (var kv in _byPrefix)
            {
                if (prefab.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) { entry = kv.Value; return true; }
            }
            // Not in the player's own list: fall back to the one this version ships, so a config
            // file saved by an older build does not hide entries added since. Anything the player
            // has written wins, including a deletion of a line we ship.
            if (_builtInByPrefab.TryGetValue(prefab, out entry)) return true;
            foreach (var kv in _builtInByPrefix)
            {
                if (prefab.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) { entry = kv.Value; return true; }
            }
            // Valheim ships a fractured twin of many breakables, named with a "_frac" suffix, and
            // the world uses one or the other: rock4_copper and rock4_copper_frac are both copper
            // and a player cannot tell them apart. Catalogue the base name and both are covered.
            string bare = StripVariant(prefab);
            if (bare != null) return Lookup(bare, out entry);
            entry = null;
            return false;
        }

        private static readonly string[] Variants = { "_frac", "_broken", "_destroyed" };

        /// <summary>The base name behind a runtime variant, or null when this is already the base.</summary>
        private static string StripVariant(string prefab)
        {
            foreach (var suffix in Variants)
                if (prefab.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return prefab.Substring(0, prefab.Length - suffix.Length);
            return null;
        }

        private static bool IsExcludedStructure(string prefab)
        {
            foreach (var prefix in _structureExclusions)
                if (prefab.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string FallbackIcon(Category cat)
        {
            string configured = TgmConfig.CategoryIcon.TryGetValue(cat, out var entry) ? entry.Value : null;
            return IconRegistry.Normalize(configured) ?? IconRegistry.Normalize(Categories.DefaultIcon(cat));
        }

        internal static bool TryClassify(GameObject go, out Found found)
        {
            found = null;
            if (go == null) return false;
            if (!_built) Rebuild();

            var portal = go.GetComponentInParent<TeleportWorld>();
            if (portal != null)
            {
                string tag = SafeText(portal);
                found = Make(Category.Portal, string.IsNullOrEmpty(tag) ? "Portal" : tag, null, portal.transform.position, KeyOf(portal.gameObject, "portal"));
                return true;
            }

            // A campfire or a workbench someone built: where a group stopped for the night, or
            // where the spawning is being held off. The world puts fires and benches in camps and
            // villages too, and those belong to the place, so only a player's own count.
            //
            // Found by the component that makes the thing what it is rather than by asking every
            // piece: a base holds hundreds of walls and floors, and none of them is a marker.
            var station = go.GetComponentInParent<Fireplace>() != null
                ? (Component)go.GetComponentInParent<Fireplace>()
                : go.GetComponentInParent<CraftingStation>();
            if (station != null)
            {
                var piece = station.GetComponent<Piece>();
                string prefab = PrefabName(station.gameObject);
                if (piece != null && piece.IsPlacedByPlayer() && Lookup(prefab, out var e) && Categories.IsBuilt(e.Cat))
                {
                    found = Make(e.Cat, e.Name ?? Loc(piece.m_name, Categories.Label(e.Cat)), e.Icon,
                                 station.transform.position, KeyOf(station.gameObject, prefab), prefab);
                    return true;
                }
            }

            var rune = go.GetComponentInParent<RuneStone>();
            if (rune != null)
            {
                found = Make(Category.Runestone, Loc(rune.GetHoverName(), "Runestone"), null, rune.transform.position, KeyOf(rune.gameObject, "rune"));
                return true;
            }

            var vegvisir = go.GetComponentInParent<Vegvisir>();
            if (vegvisir != null)
            {
                found = Make(Category.Runestone, Loc(vegvisir.GetHoverName(), "Vegvisir"), null, vegvisir.transform.position, KeyOf(vegvisir.gameObject, "vegvisir"));
                return true;
            }

            var pickable = go.GetComponentInParent<Pickable>();
            if (pickable != null && !HarvestedForGood(pickable))
            {
                string prefab = PrefabName(pickable.gameObject);
                if (Lookup(prefab, out var e))
                {
                    string icon = e.Icon ?? Categories.KnownIcon(prefab) ?? IconRegistry.ItemKey(pickable.m_itemPrefab);
                    found = Make(e.Cat, e.Name ?? Loc(pickable.GetHoverName(), Prettify(prefab)), icon, pickable.transform.position, KeyOf(pickable.gameObject, prefab), prefab);
                    return true;
                }
            }

            var rock5 = go.GetComponentInParent<MineRock5>();
            if (rock5 != null) return ClassifyDeposit(rock5.gameObject, rock5.m_name, rock5.m_dropItems, out found);

            var rock = go.GetComponentInParent<MineRock>();
            if (rock != null) return ClassifyDeposit(rock.gameObject, rock.m_name, rock.m_dropItems, out found);

            var destructible = go.GetComponentInParent<Destructible>();
            if (destructible != null)
            {
                string prefab = PrefabName(destructible.gameObject);
                if (Lookup(prefab, out var e))
                {
                    var drops = destructible.GetComponent<DropOnDestroyed>();
                    string icon = e.Icon ?? Categories.KnownIcon(prefab) ?? (drops != null ? FirstDrop(drops.m_dropWhenDestroyed) : null);
                    found = Make(e.Cat, e.Name ?? Prettify(prefab), icon, destructible.transform.position, KeyOf(destructible.gameObject, prefab), prefab);
                    return true;
                }
            }

            // A thing with no component that says what it is. A Yggdrasil root is not mined, picked
            // or broken -- you build a sap extractor on it -- so it carries nothing but a ZNetView,
            // and the only handle on it is the name of its networked prefab. Pickables are left out:
            // one already went past the branch above, and it was skipped there for a reason (picked
            // for good). So are locations and campfires, which have their own rules.
            if (pickable == null)
            {
                var view = go.GetComponentInParent<ZNetView>();
                if (view != null)
                {
                    string prefab = PrefabName(view.gameObject);
                    if (Lookup(prefab, out var e) && IsLooseObjectKind(e.Cat))
                    {
                        found = Make(e.Cat, e.Name ?? Prettify(prefab), e.Icon ?? Categories.KnownIcon(prefab),
                            view.transform.position, KeyOf(view.gameObject, prefab), prefab);
                        return true;
                    }
                }
            }

            // Non-networked parts of a location (a crypt's rock shell, terrain props) are children
            // of the spawned location root.
            var location = go.GetComponentInParent<Location>();
            if (location != null && ClassifyLocation(PrefabName(location.gameObject), location.m_hasInterior,
                    location.transform.position, location.m_exteriorRadius, go.transform.position, out found))
                return true;

            // Networked pieces (walls, floors, chests, doors) are separate world objects with no
            // Location parent: attribute them to the spawned location whose radius contains them.
            if (IsWorldPiece(go) && LocationIndex.TryFind(go.transform.position, out var near)
                && ClassifyLocation(near.Prefab, near.HasInterior, near.Pos, near.Radius, go.transform.position, out found))
            {
                // A compound (farm, village) holds several buildings, each worth its own marker:
                // identify the building this piece belongs to and mark that instead. A piece
                // that is not part of a building (a fence segment, a lone pole) is no find.
                if (found.Cat == Category.Structure)
                {
                    var building = Buildings.Resolve(go, near.Pos, near.Radius);
                    if (building == null) { found = null; return false; }
                    found.Key += ":" + building.Id;
                    found.Pos = building.Centroid;
                    found.Center = building.Centroid;
                    found.Radius = BuildingRadius;
                }
                return true;
            }

            return false;
        }

        /// <summary>De-duplication radius around a building's center.</summary>
        internal const float BuildingRadius = 5f;

        /// <summary>What marker a spawned location would produce, if any.</summary>
        internal static bool TryClassifyLocation(LocationIndex.Entry entry, out Found found)
        {
            found = null;
            if (entry == null) return false;
            if (!_built) Rebuild();
            return ClassifyLocation(entry.Prefab, entry.HasInterior, entry.Pos, entry.Radius, entry.Pos, out found);
        }

        /// <summary>
        /// What marker a prefab standing at a place would produce, worked out from the prefab and
        /// its name rather than from an object in the world.
        ///
        /// <para>TryClassify above wants components with a transform, which is to say an object
        /// that exists. A sweep of the world's own records has no such thing to hand: a berry bush
        /// in a zone nobody is standing in exists only as a record of a prefab and a position. The
        /// prefab carries everything that says what it is -- and the icon with it, the item a
        /// pickable gives or the ore a deposit drops -- so this takes a prefab and a position and
        /// puts the two together.</para>
        ///
        /// <para>Only the loose things that stand about in the world. A location comes from the
        /// generator's own table and has its own road through here; portals and campfires are about
        /// what a player built, which a prefab cannot say; and a structure is a piece of a location
        /// rather than a thing in its own right.</para>
        /// </summary>
        internal static bool TryClassifyPrefab(GameObject prefab, string name, Vector3 pos, out Found found)
        {
            found = null;
            if (!_built) Rebuild();
            if (string.IsNullOrEmpty(name)) return false;
            if (!Lookup(name, out var e) || !IsLooseObjectKind(e.Cat))
            {
                // Not in the catalog, but a deposit says what it is by what it gives: the live
                // road above has always fallen back to that, and this one never did, so a silver
                // vein the catalog happens not to list was recorded when you walked up to it and
                // invisible to the sweep. The two roads now agree.
                if (prefab == null) return false;
                var rock5 = prefab.GetComponent<MineRock5>();
                var rock = prefab.GetComponent<MineRock>();
                if (rock5 == null && rock == null) return false;
                string yield = rock5 != null ? FirstDrop(rock5.m_dropItems) : FirstDrop(rock.m_dropItems);
                if (!WorthMining(yield)) return false;
                if (_unlistedDeposits.Add(name))
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Deposit '{name}' is not in the catalog; the sweep marks it " +
                        $"anyway because it yields {yield}.");
                found = Make(Category.Ore, Prettify(name), yield, pos, "prefab:" + name, name);
                return true;
            }

            string icon = e.Icon ?? Categories.KnownIcon(name);
            if (icon == null && prefab != null)
            {
                // The same sources recording reads, in the same order, off the prefab instead of
                // off the thing: what a pickable gives, what a deposit drops, what a breakable
                // leaves behind.
                var pickable = prefab.GetComponent<Pickable>();
                if (pickable != null) icon = IconRegistry.ItemKey(pickable.m_itemPrefab);
                if (icon == null)
                {
                    var rock5 = prefab.GetComponent<MineRock5>();
                    if (rock5 != null) icon = FirstDrop(rock5.m_dropItems);
                }
                if (icon == null)
                {
                    var rock = prefab.GetComponent<MineRock>();
                    if (rock != null) icon = FirstDrop(rock.m_dropItems);
                }
                if (icon == null)
                {
                    var drops = prefab.GetComponent<DropOnDestroyed>();
                    if (drops != null) icon = FirstDrop(drops.m_dropWhenDestroyed);
                }
            }
            found = Make(e.Cat, e.Name ?? Prettify(name), icon, pos, "prefab:" + name, name);
            return true;
        }

        /// <summary>
        /// Does this kind of location have an inside? Read off the game's own location list, like
        /// the radius beside it, and for the same reason: which locations the generator placed is
        /// the server's business, but what each kind of place is like ships with the game.
        ///
        /// <para>It is how a cave the catalog has no line for is still filed as a dungeon rather
        /// than as a building, which is the one thing the generator's table cannot say on its own.</para>
        /// </summary>
        internal static bool LocationHasInterior(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            try
            {
                var zone = ZoneSystem.instance;
                var locations = zone != null ? zone.m_locations : null;
                if (locations == null) return false;
                foreach (var loc in locations)
                {
                    if (loc == null) continue;
                    if (!string.Equals(loc.m_prefabName, prefab, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(loc.m_name, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                    // By name, like the radius: not public on the real assembly, so reading it
                    // directly compiles against Libs and throws against the game.
                    return HarmonyLib.Traverse.Create(loc).Field("m_hasInterior").GetValue<bool>();
                }
            }
            catch (Exception) { /* nothing loaded, or the field moved */ }
            return false;
        }

        /// <summary>
        /// A location's marker: for structures it goes on the piece that was seen (the location
        /// origin of a farm is the middle of the yard, not the house); everything else sits on
        /// the origin. The location origin and radius are kept for deduplication either way.
        /// </summary>
        /// <summary>
        /// What a piece somebody built would become, read from the prefab and a position rather
        /// than from the piece.
        ///
        /// <para>Only the kinds that are about building, which today means campfires. A campfire
        /// is one of this mod's markers only because a player lit it -- the world lights its own
        /// in camps and villages and those are not finds -- so the caller has to have established
        /// that from the record's creator before asking. The prefab road above refuses these for
        /// exactly that reason, and without this a pin dropped on a fire in the Ashlands answers
        /// "charred spawner": the thing the fire was lit to suppress.</para>
        /// </summary>
        internal static bool TryClassifyBuilt(GameObject prefab, string name, Vector3 pos, out Found found)
        {
            found = null;
            if (!_built) Rebuild();
            if (string.IsNullOrEmpty(name)) return false;
            if (!Lookup(name, out var e) || !Categories.IsBuilt(e.Cat)) return false;
            found = Make(e.Cat, e.Name ?? Prettify(name), e.Icon, pos, "prefab:" + name, name);
            return true;
        }

        private static bool ClassifyLocation(string prefab, bool hasInterior, Vector3 center, float radius, Vector3 hitPos, out Found found)
        {
            found = null;
            if (string.IsNullOrEmpty(prefab)) return false;
            string key = "loc:" + prefab + ":" + RoundKey(center);
            Category cat;
            string name, icon = null;
            if (Lookup(prefab, out var e))
            {
                cat = e.Cat;
                name = e.Name ?? Prettify(prefab);
                icon = e.Icon ?? Categories.KnownIcon(prefab);
                // Listed, but with no icon of its own: guess from the name rather than let it fall
                // through to the kind's icon, which for dungeons is the swamp crypt key.
                if (icon == null && e.Cat == Category.Dungeon) icon = IconFromDungeonName(prefab);
            }
            else if (hasInterior)
            {
                cat = Category.Dungeon;
                name = Prettify(prefab);
                icon = GuessDungeonIcon(prefab);
            }
            else if (TgmConfig.StructuresIncludeUnlisted.Value && !IsExcludedStructure(prefab))
            {
                cat = Category.Structure;
                name = Prettify(prefab);
            }
            else
            {
                return false;
            }
            found = Make(cat, name, icon, cat == Category.Structure ? hitPos : center, key, prefab);
            found.Center = center;
            found.Radius = Mathf.Max(radius, 4f);
            return true;
        }

        /// <summary>
        /// Picked, and it never grows back: carrot, turnip and onion seeds are like this, and the
        /// game itself treats the case as final, destroying the object once it has been picked.
        /// There is nothing there to find any more, so it must not be recorded, and a marker
        /// already written for it is wrong.
        /// </summary>
        internal static bool HarvestedForGood(Pickable pickable)
        {
            if (pickable == null || pickable.m_respawnTimeMinutes > 0f) return false;
            var view = pickable.GetComponent<ZNetView>();
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.GetBool(ZDOVars.s_picked, false);
        }


        private static readonly HashSet<string> _unlistedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// An icon for a dungeon the catalog does not list. Without this the marker fell back to
        /// the kind's icon, which is the swamp crypt key, so every unlisted cave claimed to be a
        /// burial chamber. Guessing from the name is crude but never says "crypt" about a troll
        /// cave, and anything still unrecognized gets a plain dot rather than a wrong picture.
        /// The prefab is logged once so it can be added to the catalog properly.
        /// </summary>
        /// <summary>The icon a dungeon name suggests, for repairing markers already written. Null when nothing fits.</summary>
        internal static string IconFromDungeonName(string name)
        {
            string p = (name ?? "").ToLowerInvariant();
            // The bear's own name is Bjorn, so no amount of searching for "bear" would ever have
            // found its trophy; the only match was PulledBear, a meal. Creatures are not always
            // named after the English word, which is why a known id beats any search.
            if (p.Contains("bear")) return IconRegistry.PickItem("TrophyBjorn") ?? IconRegistry.PickItemLike("bjorn") ?? IconRegistry.PickItemLike("bear");
            if (p.Contains("troll")) return "TrophyFrostTroll";
            if (p.Contains("sunken")) return "TrophyDraugr";
            if (p.Contains("crypt") || p.Contains("burial")) return "TrophySkeleton";
            if (p.Contains("frost") || p.Contains("mountain")) return "TrophyUlv";
            if (p.Contains("dvergr") || p.Contains("infested")) return "TrophySeeker";
            if (p.Contains("goblin") || p.Contains("fuling")) return "TrophyGoblin";
            if (p.Contains("draugr")) return "TrophyDraugr";
            return null;
        }

        private static string GuessDungeonIcon(string prefab)
        {
            string icon = IconFromDungeonName(prefab);
            if (_unlistedSeen.Add(prefab ?? ""))
            {
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Dungeon '{prefab}' is not in the Dungeons catalog; using " +
                    (icon ?? "a plain dot") + ". Add it to the Catalog section to give it a proper name and icon.");
            }
            return icon ?? "pin:Icon3";
        }

        /// <summary>A building piece the world generated, as opposed to something a player built.</summary>
        internal static bool IsWorldPiece(GameObject go)
        {
            var piece = go.GetComponentInParent<Piece>();
            if (piece != null) return !piece.IsPlacedByPlayer();
            return go.GetComponentInParent<WearNTear>() != null
                || go.GetComponentInParent<Container>() != null
                || go.GetComponentInParent<Door>() != null;
        }

        private static Found Make(Category cat, string name, string icon, Vector3 pos, string key, string source = "")
        {
            string resolved = IconRegistry.Normalize(icon) ?? FallbackIcon(cat);
            return new Found { Key = key, Cat = cat, Icon = resolved, Name = name ?? "", Pos = pos, Source = source ?? "" };
        }

        // What a deposit yields besides the thing you went there for. A copper vein drops stone as
        // well as ore, and the stone tends to come first in its table, so taking the first drop
        // labelled a copper deposit with a lump of stone.
        private static readonly HashSet<string> Rubble = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Stone", "Wood", "RoundLog", "FineWood", "Resin", "Flint",
            // Grausten and ice are the stone of their own biomes: the Ashlands is made of cliffs
            // that yield Grausten and the Deep North of rocks that yield ice, and a marker on each
            // would be a marker on the ground itself. Without these, asking what a thing yields
            // put a deposit marker on every cliff face in the Ashlands.
            "Grausten", "Ice",
        };

        /// <summary>
        /// The drop worth putting on the map: the one you would go there for, which is whatever is
        /// not rubble. Falls back to the first drop when a thing yields nothing else, so a plain
        /// boulder still shows stone.
        /// </summary>
        private static string FirstDrop(DropTable table)
        {
            if (table == null || table.m_drops == null) return null;
            string fallback = null;
            foreach (var drop in table.m_drops)
            {
                if (drop.m_item == null) continue;
                string name = Utils.GetPrefabName(drop.m_item);
                if (fallback == null) fallback = IconRegistry.ItemKey(drop.m_item);
                if (!Rubble.Contains(name)) return IconRegistry.ItemKey(drop.m_item);
            }
            return fallback;
        }

        /// <summary>
        /// The item a deposit is mined for, as a bare prefab name (CopperOre), or null when the
        /// object is not a deposit. The same reading that picks a deposit's icon.
        /// </summary>
        internal static string DepositYield(GameObject go)
        {
            if (go == null) return null;
            string key = null;
            var rock5 = go.GetComponentInParent<MineRock5>();
            if (rock5 != null) key = FirstDrop(rock5.m_dropItems);
            var rock = key == null ? go.GetComponentInParent<MineRock>() : null;
            if (rock != null) key = FirstDrop(rock.m_dropItems);
            var drops = key == null ? go.GetComponentInParent<DropOnDestroyed>() : null;
            if (drops != null) key = FirstDrop(drops.m_dropWhenDestroyed);
            return key != null && key.StartsWith("item:") ? key.Substring(5) : key;
        }

        private static string KeyOf(GameObject go, string prefix)
        {
            var nview = go.GetComponentInParent<ZNetView>();
            if (nview != null && nview.IsValid())
                return prefix + ":" + nview.GetZDO().m_uid;
            return prefix + ":" + RoundKey(go.transform.position);
        }

        private static string RoundKey(Vector3 p)
        {
            return Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + "," + Mathf.RoundToInt(p.z);
        }

        private static string SafeText(TeleportWorld portal)
        {
            try { return portal.GetText(); } catch { return null; }
        }

        private static string Loc(string text, string fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            try
            {
                string s = Localization.instance != null ? Localization.instance.Localize(text) : text;
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch { return fallback; }
        }

        internal static string Prettify(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return "";
            string s = Regex.Replace(prefab, @"[\d_]+", " ");
            s = Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length == 0 ? prefab : s;
        }
    }
}
