using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>All configuration entries. Bind() once from TheGreatestMapMod.Awake.</summary>
    internal static class TgmConfig
    {
        // ── Keys ────────────────────────────────────────────────────────────────────
        internal static ConfigEntry<KeyboardShortcut> TakeOutMapKey;
        internal static ConfigEntry<KeyboardShortcut> SyncTableKey;
        internal static ConfigEntry<KeyboardShortcut> LegendKey;
        internal static ConfigEntry<KeyboardShortcut> HighlightPlayersKey;
        internal static ConfigEntry<bool> ShowMessages;
        internal static ConfigEntry<bool> EchoLogToConsole;

        // ── Sharing rules (server-synced) ───────────────────────────────────────────
        internal static ConfigEntry<bool> LockConfiguration;
        internal static ConfigEntry<SharingMode> SharingMode;
        internal static ConfigEntry<bool> AllowErasingMarkers;
        internal static ConfigEntry<bool> RequireMapOutToEdit;
        internal static ConfigEntry<bool> RequireMapOutToRecord;
        internal static ConfigEntry<int> CrossOffMinedDownTo;
        internal static ConfigEntry<bool> TableCarriesPins;
        internal static ConfigEntry<float> ExchangeRadius;
        internal static ConfigEntry<float> ExchangeCooldown;
        internal static ConfigEntry<bool> ExchangeExploration;

        // ── what to draw ────────────────────────────────────────────────────────────
        internal static readonly Dictionary<Category, ConfigEntry<bool>> ShowKind = new Dictionary<Category, ConfigEntry<bool>>();
        internal static ConfigEntry<string> HiddenIcons;
        internal static ConfigEntry<string> IconSizes;
        internal static ConfigEntry<TypeOrder> TypeRowOrder;
        internal static ConfigEntry<bool> ShowCounts;
        internal static ConfigEntry<bool> SplitPlantsByFarming;
        internal static ConfigEntry<string> VanillaPinNames;
        internal static ConfigEntry<bool> HideVanillaControls;
        internal static ConfigEntry<bool> PingRow;
        internal static ConfigEntry<string> HiddenLandmarks;
        internal static ConfigEntry<bool> PauseWhileMapOpen;
        internal static ConfigEntry<bool> ShowPauseButton;
        internal static ConfigEntry<bool> MarkerButton;
        internal static ConfigEntry<float> MarkerButtonOffsetX;
        internal static ConfigEntry<float> MarkerButtonOffsetY;
        internal static ConfigEntry<bool> MarkerButtonAtBottom;
        internal static ConfigEntry<float> PauseButtonOffsetX;
        internal static ConfigEntry<float> PauseButtonOffsetY;
        internal static ConfigEntry<bool> ClipMinimapMarkers;
        internal static ConfigEntry<bool> MarkerTooltips;
        internal static ConfigEntry<bool> MapOutStatusIcon;
        internal static ConfigEntry<bool> HighlightPlayersHidesOthers;
        internal static ConfigEntry<Color> HighlightPlayersColor;
        internal static ConfigEntry<int> HighlightPlayersSize;
        internal static ConfigEntry<bool> CustomBossMarkers;
        internal static ConfigEntry<bool> CustomHildirMarkers;
        internal static ConfigEntry<string> MapExtraSize;
        internal static ConfigEntry<bool> MapResizeShowsMore;
        internal static ConfigEntry<string> MapPosition;
        internal static ConfigEntry<string> LegendSize;
        internal static ConfigEntry<string> LegendPosition;
        internal static ConfigEntry<bool> ShowAllMarkers;
        internal static ConfigEntry<int> CrossedOffFade;
        internal static ConfigEntry<bool> ShowClearedDeposits;
        internal static ConfigEntry<string> HiddenCleared;
        internal static ConfigEntry<string> HiddenSearched;
        internal static ConfigEntry<float> RevealNewMarkers;
        internal static ConfigEntry<float> ShowHiddenWithin;
        internal static ConfigEntry<bool> ThinMarkers;
        internal static ConfigEntry<float> ThinSpacing;
        internal static ConfigEntry<int> MostMarkersDrawn;
        internal static ConfigEntry<Color> PortalColor;
        internal static ConfigEntry<Color> PortalPulseColor;
        internal static ConfigEntry<float> PortalPulseSeconds;
        internal static ConfigEntry<string> HiddenPlaces;
        internal static ConfigEntry<string> HiddenOwnIcons;
        internal static ConfigEntry<string> HiddenBiomes;
        internal static ConfigEntry<bool> ShowPrefabNames;
        private static HashSet<string> _hiddenIconKeys;
        private static HashSet<string> _hiddenPlaceKeys;
        private static HashSet<string> _hiddenOwnIconKeys;
        private static HashSet<string> _hiddenBiomeKeys;

        private static HashSet<string> HiddenIconKeys()
        {
            if (_hiddenIconKeys == null)
            {
                _hiddenIconKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var raw in (HiddenIcons.Value ?? "").Split(','))
                {
                    string key = IconRegistry.Normalize(raw);
                    if (key != null) _hiddenIconKeys.Add(key);
                }
            }
            return _hiddenIconKeys;
        }

        /// <summary>True if this marker icon is on the player's hidden list (item names or icon keys).</summary>
        internal static bool IsIconHidden(string icon)
        {
            string k = IconRegistry.Normalize(icon);
            return k != null && HiddenIconKeys().Contains(k);
        }

        internal static int HiddenIconCount() => HiddenIconKeys().Count;

        /// <summary>Put an icon on the hidden list (saved to the config file at once).</summary>
        internal static void AddHiddenIcon(string icon)
        {
            string key = IconRegistry.Normalize(icon);
            if (key == null || IsIconHidden(key)) return;
            string entry = key.StartsWith("item:") ? key.Substring(5) : key; // the documented forms: Dandelion, pin:Icon1
            string current = (HiddenIcons.Value ?? "").Trim();
            HiddenIcons.Value = current.Length == 0 ? entry : current + "," + entry;
        }

        /// <summary>
        /// Take an icon back off the hidden list. The list is rewritten from what is left rather
        /// than cut out of the text, because it is text a player can edit: somebody who typed
        /// "Dandelion, dandelion" has one icon hidden and two words saying so, and removing one
        /// word would leave the icon hidden by the other.
        /// </summary>
        internal static void RemoveHiddenIcon(string icon)
        {
            string key = IconRegistry.Normalize(icon);
            if (key == null || !IsIconHidden(key)) return;
            var keep = new List<string>();
            foreach (var k in HiddenIconKeys())
            {
                if (IconRegistry.SameKey(k, key)) continue;
                keep.Add(k.StartsWith("item:") ? k.Substring(5) : k);   // the documented forms again
            }
            HiddenIcons.Value = string.Join(",", keep.ToArray());
        }

        private static HashSet<string> HiddenBiomeKeys()
        {
            if (_hiddenBiomeKeys == null)
            {
                _hiddenBiomeKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var raw in (HiddenBiomes == null ? "" : HiddenBiomes.Value ?? "").Split(','))
                {
                    string key = raw.Trim();
                    if (key.Length > 0) _hiddenBiomeKeys.Add(key);
                }
            }
            return _hiddenBiomeKeys;
        }

        /// <summary>Is this kind switched off in this biome? Written by the marker list's rows.</summary>
        internal static bool IsBiomeHidden(Category kind, Heightmap.Biome biome) =>
            HiddenBiomeKeys().Count > 0 && HiddenBiomeKeys().Contains(Biomes.Key(kind, biome));

        internal static int HiddenBiomeCount() => HiddenBiomeKeys().Count;

        internal static void SetBiomeHidden(Category kind, Heightmap.Biome biome, bool hidden)
        {
            if (HiddenBiomes == null || IsBiomeHidden(kind, biome) == hidden) return;
            string key = Biomes.Key(kind, biome);
            var keep = new List<string>();
            foreach (var k in HiddenBiomeKeys())
                if (!string.Equals(k, key, System.StringComparison.OrdinalIgnoreCase)) keep.Add(k);
            if (hidden) keep.Add(key);
            HiddenBiomes.Value = string.Join(",", keep.ToArray());
        }

        private static HashSet<string> HiddenOwnIconKeys()
        {
            if (_hiddenOwnIconKeys == null)
            {
                _hiddenOwnIconKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var raw in (HiddenOwnIcons == null ? "" : HiddenOwnIcons.Value ?? "").Split(','))
                {
                    string key = IconRegistry.Normalize(raw);
                    if (key != null) _hiddenOwnIconKeys.Add(key);
                }
            }
            return _hiddenOwnIconKeys;
        }

        /// <summary>
        /// Is this picture switched off on markers nobody recorded -- the ones a player placed?
        ///
        /// <para>Apart from <see cref="IsIconHidden"/> for the same reason places are: a picture
        /// is not one thing. Structures wear vanilla's house pin because the game draws no
        /// picture of an abandoned farm, so one list covering both made the marker list's House
        /// row -- counted and labelled for the handful of house pins a player had placed -- take
        /// 218 recorded structures down with it, and the kind switch for Structures take the
        /// player's own house pins.</para>
        /// </summary>
        internal static bool IsOwnIconHidden(string icon)
        {
            string k = IconRegistry.Normalize(icon);
            return k != null && HiddenOwnIconKeys().Contains(k);
        }

        internal static int HiddenOwnIconCount() => HiddenOwnIconKeys().Count;

        internal static void SetOwnIconHidden(string icon, bool hidden)
        {
            string key = IconRegistry.Normalize(icon);
            if (key == null || HiddenOwnIcons == null || IsOwnIconHidden(key) == hidden) return;
            var keep = new List<string>();
            foreach (var k in HiddenOwnIconKeys())
                if (!IconRegistry.SameKey(k, key)) keep.Add(k.StartsWith("item:") ? k.Substring(5) : k);
            if (hidden) keep.Add(key.StartsWith("item:") ? key.Substring(5) : key);
            HiddenOwnIcons.Value = string.Join(",", keep.ToArray());
        }

        private static HashSet<string> HiddenPlaceKeys()
        {
            if (_hiddenPlaceKeys == null)
            {
                _hiddenPlaceKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var raw in ((HiddenPlaces != null ? HiddenPlaces.Value : null) ?? "").Split(','))
                {
                    string key = IconRegistry.Normalize(raw);
                    if (key != null) _hiddenPlaceKeys.Add(key);
                }
            }
            return _hiddenPlaceKeys;
        }

        /// <summary>
        /// True if this is one of the places the game marks itself that the player has switched
        /// off: a boss altar, one of Hildir's errands.
        ///
        /// <para>Kept apart from the hidden icons on purpose. A place is not a kind of thing. The
        /// picture one wears is borrowed from a trophy, and a player hiding that trophy is saying
        /// something about trophies -- taking the game's own pin off an altar because of it is a
        /// surprise, and it is one a player reported within a minute of meeting it: the altar's
        /// own pin went with the marker and there was no obvious way back.</para>
        /// </summary>
        internal static bool IsPlaceHidden(string icon)
        {
            string k = IconRegistry.Normalize(icon);
            return k != null && HiddenPlaceKeys().Contains(k);
        }

        internal static int HiddenPlaceCount() => HiddenPlaceKeys().Count;

        internal static void SetPlaceHidden(string icon, bool hidden)
        {
            string key = IconRegistry.Normalize(icon);
            if (key == null || HiddenPlaces == null || IsPlaceHidden(key) == hidden) return;
            var keep = new List<string>();
            foreach (var k in HiddenPlaceKeys())
                if (!IconRegistry.SameKey(k, key)) keep.Add(k.StartsWith("item:") ? k.Substring(5) : k);
            if (hidden) keep.Add(key.StartsWith("item:") ? key.Substring(5) : key);
            HiddenPlaces.Value = string.Join(",", keep.ToArray());
        }

        internal static int HiddenKindCount()
        {
            int n = 0;
            foreach (var entry in ShowKind.Values) if (!entry.Value) n++;
            return n;
        }

        /// <summary>Clear the hidden icons, switch every kind back on and undo the master hide.</summary>
        /// <summary>
        /// One-time: take the five placeable pictures out of Hidden Icons and put them on the
        /// list that is about markers a player placed. Runs on every load and finds nothing after
        /// the first, which is cheaper than remembering that it ran.
        /// </summary>
        private static void MoveOwnPicturesOut()
        {
            if (HiddenIcons == null || HiddenOwnIcons == null) return;
            var moved = new List<string>();
            for (int type = (int)Minimap.PinType.Icon0; type <= (int)Minimap.PinType.Icon4; type++)
            {
                string key = IconRegistry.KeyForVanilla(type);
                if (IsIconHidden(key)) moved.Add(key);
            }
            if (moved.Count == 0) return;
            foreach (var key in moved)
            {
                RemoveHiddenIcon(key);
                _hiddenIconKeys = null;
                SetOwnIconHidden(key, true);
                _hiddenOwnIconKeys = null;
            }
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Moved {moved.Count} picture(s) from Hidden Icons to the list about markers you " +
                "placed: " + string.Join(", ", moved.ToArray()) + ". The two used to be one list, so switching off " +
                "the House row under Yours also hid every recorded structure.");
        }

        internal static void ShowEverything()
        {
            Focus.Clear();
            if (!string.IsNullOrEmpty(HiddenIcons.Value)) HiddenIcons.Value = "";
            if (HiddenPlaces != null && !string.IsNullOrEmpty(HiddenPlaces.Value)) HiddenPlaces.Value = "";
            if (HiddenOwnIcons != null && !string.IsNullOrEmpty(HiddenOwnIcons.Value)) HiddenOwnIcons.Value = "";
            if (HiddenBiomes != null && !string.IsNullOrEmpty(HiddenBiomes.Value)) HiddenBiomes.Value = "";
            foreach (var entry in ShowKind.Values) if (!entry.Value) entry.Value = true;
            if (ShowAllMarkers != null && !ShowAllMarkers.Value) ShowAllMarkers.Value = true;
            // Worked-out deposits too. This was the one switch that hid markers and was not put
            // back here, although AnythingHidden below has always counted it -- so the mod would
            // say something was hidden, be asked to show everything, and go on hiding it.
            if (ShowClearedDeposits != null && !ShowClearedDeposits.Value) ShowClearedDeposits.Value = true;
            // And the world's landmarks, which are hidden by a list of their own.
            WorldPins.ShowAll();
            CrossedOff.ShowAll();
        }

        /// <summary>
        /// Put each saved Catalog line into the order this version ships it in, once.
        ///
        /// <para>The order of those lines meant nothing until the marker list learned to read it
        /// for its progression order, so a line saved by an older version carries whatever order
        /// that version happened to ship -- thistles before dandelions, the mountain's silver
        /// before the swamp's iron -- and a saved line always beats a new default, so nobody
        /// would ever see the order come right. Nobody chose the old one, so nothing is lost by
        /// putting it straight.</para>
        ///
        /// <para>Once, and stamped with the version that did it: afterwards the order is the
        /// player's, and reordering a line reorders the list, which is the whole point of reading
        /// the order from here rather than from a table of our own. Every entry survives exactly
        /// as written -- renamed, given an icon, or one the player added themselves.</para>
        /// </summary>
        private static void OrderTheCatalogOnce()
        {
            string stamp = Progress.CatalogOrdered;
            try
            {
                if (!string.IsNullOrEmpty(stamp)
                    && new System.Version(stamp) >= new System.Version(TheGreatestMapMod.ModVersion)) return;
            }
            catch (Exception) { /* unreadable stamp: do it again, which costs nothing */ }

            var moved = new List<string>();
            foreach (var kv in CategoryPrefabs)
            {
                string was = kv.Value.Value;
                string now = Catalog.InDefaultOrder(was, Categories.DefaultPrefabs(kv.Key));
                if (string.Equals(was, now, StringComparison.Ordinal)) continue;
                kv.Value.Value = now;
                moved.Add(Categories.Label(kv.Key));
            }
            Progress.CatalogOrdered = TheGreatestMapMod.ModVersion;
            if (moved.Count > 0)
                TheGreatestMapMod.Log.LogInfo(
                    "[TheGreatestMap] Put these Catalog lines back into the order this version ships them in, so the " +
                    "marker list can read a progression order off them: " + string.Join(", ", moved.ToArray()) +
                    ". Every entry is still there, exactly as it was written; only the order moved, and only this once.");
        }

        /// <summary>
        /// Icons this mod shipped for a thing and then changed its mind about: the prefab, what
        /// was on it before, and what it should be. A saved Catalog line always beats a new
        /// default -- which is right, since the line is where a player says what they want -- so
        /// without this a correction reaches nobody who had already run the mod.
        ///
        /// <para>Only a line that still says character for character what this mod wrote is
        /// touched, which is the one case where nobody has chosen anything, and only once: the
        /// correction is written down afterwards, so a player who sets the old picture back keeps
        /// it. An entry can be dropped from here once no map in the wild can still be carrying
        /// it, like the passes in <see cref="Migrations"/>.</para>
        /// </summary>
        private static readonly string[][] ShippedIcons =
        {
            // The skeleton trophy is what a burial chamber wears. An evil bone pile wearing it
            // too meant one picture for two different things, which is the map answering the
            // question wrong rather than not answering it.
            new[] { "Spawners", "BonePileSpawner", "TrophySkeleton", "BoneFragments" },
            // The draugr trophy was on the sunken crypts and on the draugr piles, which made one
            // picture mean a dungeon in the swamp or a nest in it. Iron scrap is what a sunken
            // crypt is full of.
            new[] { "Dungeons", "SunkenCrypt4", "TrophyDraugr", "IronScrap" },
            // The three places Hildir sends you wore their own locations' map pictures, and the
            // game has one for the cavern and none for the other two -- so two of the three asked
            // for a sprite that does not exist and said so at every login. The trophy of whoever
            // waits inside says what the place is for and tells the three apart.
            new[] { "Hildir Quests", "Hildir_cave", "loc:Hildir_cave", "TrophyCultist_Hildir" },
            new[] { "Hildir Quests", "Hildir_crypt", "loc:Hildir_crypt", "TrophySkeletonHildir" },
            new[] { "Hildir Quests", "Hildir_plainsfortress", "loc:Hildir_plainsfortress", "TrophyGoblinBruteBrosBrute" },
            // And from her camp's picture, which an unreleased build put in those two lines for a
            // day. Published nowhere, so this is only for the maps it was tested on -- and it is
            // the reason the record below is kept per correction rather than per icon.
            new[] { "Hildir Quests", "Hildir_crypt", "loc:Hildir_camp", "TrophySkeletonHildir" },
            new[] { "Hildir Quests", "Hildir_plainsfortress", "loc:Hildir_camp", "TrophyGoblinBruteBrosBrute" },
            // Iron scrap was on the sunken crypts and on the muddy scrap piles, which made one
            // picture mean a dungeon in the swamp or a buried iron deposit in it -- the same
            // collision the draugr trophy made above, one correction later. The scrap has the
            // better claim: a scrap pile is iron and nothing else, while a crypt has a key that
            // nothing else in the game is about.
            new[] { "Dungeons", "SunkenCrypt4", "IronScrap", "CryptKey" },
            // The dvergr trophy was on all four kinds of dvergr base and on the fallen guard
            // towers beside them, so a garrison and a ruin read alike. It is a braid that reads
            // as nothing at pin size besides; their lantern is what one of these places looks
            // like from across a bog.
            new[] { "Enemy Bases", "Mistlands_GuardTower*", "TrophyDvergr", "piece:piece_dvergr_lantern" },
            new[] { "Enemy Bases", "Mistlands_Excavation*", "TrophyDvergr", "piece:piece_dvergr_lantern" },
            new[] { "Enemy Bases", "Mistlands_Harbour*", "TrophyDvergr", "piece:piece_dvergr_lantern" },
            new[] { "Enemy Bases", "Mistlands_Lighthouse*", "TrophyDvergr", "piece:piece_dvergr_lantern" },
        };

        /// <summary>
        /// Types this mod recorded and should not have: the prefab, and the kind whose line it was
        /// on. Each is dropped from that line and added to the structure exclusions, so it cannot
        /// come back through the unlisted sweep.
        ///
        /// <para>A shipped list is corrected rather than simply changed because a saved
        /// <c>[Catalog]</c> line always beats a new default -- that is what makes the catalog the
        /// player's own -- so a type dropped from what the mod ships stays on every config file
        /// that already had it, which is every config file.</para>
        /// </summary>
        private static readonly string[][] ShippedCuts =
        {
            // A swamp tree with guck sacks on it. The sacks are recorded as guck, which is what
            // anybody goes there for, and there are 700 of these trees in a world.
            new[] { "InfestedTree", "Structures" },
            // The swamp, which is marked by what is worth going to rather than by its buildings:
            // crypts are dungeons, the bone and body piles in the graveyards are spawners, the
            // bog witch is a trader. Grave1 is the graveyard, FireHole the surtling geyser and
            // ShipWreck the wrecks; none of the three was ever listed, so they were recorded by
            // the unlisted sweep and only the exclusion stops them.
            new[] { "SwampHut", "Structures" },
            new[] { "SwampRuin", "Structures" },
            new[] { "SwampWell", "Structures" },
            new[] { "Grave1", "Structures" },
            new[] { "FireHole", "Structures" },
            new[] { "ShipWreck", "Structures" },
            // Stones laid out in the shape of a ship. There is nothing in it and nothing to do
            // with it.
            new[] { "ShipSetting", "Structures" },
            // The Ashlands are laid out of these, and what is in one does not pay for the second
            // walk a marker invites. The fortresses are still marked, and the charred spawners
            // in them are their own kind.
            new[] { "CharredRuins", "Structures" },
            // The rubble, of which there are a hundred in a world -- not the fortress, which is
            // recorded under CharredFortress and wears the trophy these used to.
            new[] { "FortressRuins", "Structures" },
            // Volture nests. Nothing was ever listed for them; the sweep recorded 350 a world.
            new[] { "VoltureNest", "Structures" },
            // The Black Forest's and the Mountain's answer to the swamp hut: a building with
            // nothing in it to search, 400 and 450 of them in a world. The swamp set the
            // precedent and a biome is no reason to decide it differently.
            new[] { "StoneHouse", "Structures" },
            new[] { "StoneTowerRuins", "Structures" },
            // Ruin1, Ruin2 and Ruin3: 450 heaps of stonework a world, with nothing to search.
            // The prefix is "Ruin" exactly, which is what the catalog line matched; the other
            // ruins in the game are named for what they are the ruins of (SwampRuin, CharredRuins,
            // FortressRuins, StoneTowerRuins, AshlandRuins) and none of them starts with it.
            new[] { "Ruin", "Structures" },
        };

        /// <summary>
        /// Types that have changed kind: the prefab, and the label of the kind whose line it used
        /// to be written in. Moving one in the shipped catalog is not enough on its own -- a
        /// player's own saved line is read before the one this version ships, so an entry still
        /// sitting in their Structures line goes on classifying as a structure however the
        /// defaults are rearranged. Dropping it there lets the lookup fall through to the kind
        /// that claims it now.
        ///
        /// <para>Deliberately not a cut: nothing is added to Structures Exclude Prefixes, because
        /// the type is still recorded. Its new kind's line names it, and a listed location is
        /// recorded because it is listed whatever the exclusion list says.</para>
        ///
        /// <para>Both shapes of a prefix are listed where a line could hold either: a config
        /// written before the Plains towers were named exactly says StoneTower*, and one written
        /// after says StoneTower1 and StoneTower3. Each is matched on its own, and an id that
        /// matches nothing is written down as done all the same.</para>
        /// </summary>
        private static readonly string[][] ShippedMoves =
        {
            // Somewhere something lives, which is one idea and is now one kind.
            new[] { "StoneTower", "Structures" },
            new[] { "StoneTower1", "Structures" },
            new[] { "StoneTower3", "Structures" },
            new[] { "Greydwarf_camp", "Structures" },
            new[] { "Mistlands_GuardTower", "Structures" },
            new[] { "Mistlands_Excavation", "Structures" },
            new[] { "Mistlands_Harbour", "Structures" },
            new[] { "Mistlands_Lighthouse", "Structures" },
            new[] { "CharredFortress", "Structures" },
            // Its own kind, under Points of Interest: marked for the eggs, not the stonework.
            new[] { "DrakeNest", "Structures" },
        };

        private static void MoveShippedLinesOnce()
        {
            string done = Progress.CatalogMovesMade;
            var made = new List<string>();
            foreach (var move in ShippedMoves)
            {
                if (Listed(done, move[0])) continue;
                made.Add(move[0]);
                foreach (var kv in CategoryPrefabs)
                {
                    if (!string.Equals(Categories.Label(kv.Key), move[1], StringComparison.Ordinal)) continue;
                    string now = Catalog.Without(kv.Value.Value, move[0]);
                    if (!string.Equals(kv.Value.Value, now, StringComparison.Ordinal))
                    {
                        kv.Value.Value = now;
                        TheGreatestMapMod.Log.LogInfo(
                            $"[TheGreatestMap] {move[0]} belongs to another kind now, so its line has been taken out " +
                            $"of your {move[1]} catalog once. It is still recorded, under its new kind.");
                    }
                    break;
                }
            }
            if (made.Count == 0) return;
            foreach (var id in made) done = done.Length == 0 ? id : done + "," + id;
            Progress.CatalogMovesMade = done;
        }

        /// <summary>
        /// Entries this mod shipped and then corrected: the kind's line, what it used to say word
        /// for word, and what it says now. Only a line that still says the old thing exactly is
        /// changed, so a name you have written yourself is yours.
        /// </summary>
        private static readonly string[][] ShippedEntries =
        {
            // The names the game itself uses, which are what a player reading the map would call
            // them. "Draugr Pile" and "Charred Spawner" were this mod's own inventions.
            new[] { "Spawners", "Spawner_GreydwarfNest=Greydwarf Nest|TrophyGreydwarfBrute",
                                "Spawner_GreydwarfNest=Greydwarf nest|TrophyGreydwarfBrute" },
            new[] { "Spawners", "Spawner_DraugrPile=Draugr Pile|TrophyDraugr",
                                "Spawner_DraugrPile=Body pile|TrophyDraugr" },
            // And a prefix with it, so the swamp's own bone pile prefab is caught at last.
            new[] { "Spawners", "BonePileSpawner=Evil Bone Pile|BoneFragments",
                                "BonePileSpawner*=Evil bone pile|BoneFragments" },
            new[] { "Spawners", "CharredStone_Spawner*=Charred Spawner|TrophyCharredMelee",
                                "CharredStone_Spawner=Monument of Torment|TrophyCharredMelee" },
        };

        private static void FixShippedEntriesOnce()
        {
            string done = Progress.CatalogEntriesFixed;
            var made = new List<string>();
            foreach (var fix in ShippedEntries)
            {
                string id = fix[2];
                if (Listed(done, id)) continue;
                made.Add(id);
                foreach (var kv in CategoryPrefabs)
                {
                    if (!string.Equals(Categories.Label(kv.Key), fix[0], StringComparison.Ordinal)) continue;
                    string now = Catalog.Swap(kv.Value.Value, fix[1], fix[2]);
                    if (!string.Equals(kv.Value.Value, now, StringComparison.Ordinal))
                    {
                        kv.Value.Value = now;
                        TheGreatestMapMod.Log.LogInfo(
                            $"[TheGreatestMap] Your {fix[0]} catalog said \"{fix[1]}\", which is what this mod used " +
                            $"to ship; it is \"{fix[2]}\" now. Change it to anything you like and it stays changed.");
                    }
                    break;
                }
            }
            if (made.Count == 0) return;
            // Separated by commas, which a catalog entry can never contain -- commas are what
            // separates the entries themselves. A bar would have been wrong: every entry with an
            // icon has one.
            foreach (var id in made) done = done.Length == 0 ? id : done + "," + id;
            Progress.CatalogEntriesFixed = done;
        }

        private static void CutShippedLinesOnce()
        {
            string done = Progress.CatalogCutsMade;
            var made = new List<string>();
            foreach (var cut in ShippedCuts)
            {
                if (Listed(done, cut[0])) continue;
                // Written down whether or not there was anything to drop, like the icon
                // corrections: a fresh config never had it, and that is the same job finished.
                made.Add(cut[0]);
                foreach (var kv in CategoryPrefabs)
                {
                    if (!string.Equals(Categories.Label(kv.Key), cut[1], StringComparison.Ordinal)) continue;
                    string now = Catalog.Without(kv.Value.Value, cut[0]);
                    if (!string.Equals(kv.Value.Value, now, StringComparison.Ordinal))
                    {
                        kv.Value.Value = now;
                        TheGreatestMapMod.Log.LogInfo(
                            $"[TheGreatestMap] {cut[0]} is no longer recorded at all, so its line has been taken out " +
                            $"of your {cut[1]} catalog once. Put it back by hand if you want it.");
                    }
                    break;
                }
                // And the prefix, so the unlisted sweep cannot record it under a tidied-up prefab
                // name now that nothing names it.
                if (StructuresExcludePrefixes != null && !Listed(StructuresExcludePrefixes.Value, cut[0]))
                    StructuresExcludePrefixes.Value = string.IsNullOrEmpty(StructuresExcludePrefixes.Value)
                        ? cut[0] : StructuresExcludePrefixes.Value + "," + cut[0];
            }
            if (made.Count == 0) return;
            foreach (var id in made) done = done.Length == 0 ? id : done + "," + id;
            Progress.CatalogCutsMade = done;
        }

        private static void FixShippedIconsOnce()
        {
            string done = Progress.CatalogIconsFixed;
            var made = new List<string>();
            foreach (var fix in ShippedIcons)
            {
                // Which correction, rather than only what it corrected to: an icon can be
                // thought better of twice, and then two corrections for one prefab end at the
                // same picture by different routes. Written down by route, so the second is not
                // taken for the first and skipped -- which is exactly what happened when
                // Hildir's two went from a missing location picture to her camp's and then to
                // the trophies.
                string id = fix[1] + "=" + fix[2] + ">" + fix[3];
                if (Listed(done, id)) continue;
                // Written down whether or not there was anything to correct: a fresh config has
                // the new icon already, and this is a correction that has been dealt with either
                // way. Otherwise every start would look at it again forever.
                made.Add(id);
                foreach (var kv in CategoryPrefabs)
                {
                    if (!string.Equals(Categories.Label(kv.Key), fix[0], StringComparison.Ordinal)) continue;
                    string now = Catalog.WithIcon(kv.Value.Value, fix[1], fix[2], fix[3]);
                    if (string.Equals(kv.Value.Value, now, StringComparison.Ordinal)) break;
                    kv.Value.Value = now;
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] {fix[1]} wore {fix[2]} because that is what this mod used to ship for it; " +
                        $"it is {fix[3]} now. Your Catalog line said the old one word for word, so it has been " +
                        "corrected once. Change it to anything you like and it stays changed.");
                    break;
                }
            }
            if (made.Count == 0) return;
            foreach (var id in made) done = done.Length == 0 ? id : done + "," + id;
            Progress.CatalogIconsFixed = done;
        }

        private static bool Listed(string all, string one)
        {
            if (string.IsNullOrEmpty(all)) return false;
            foreach (var word in all.Split(','))
                if (string.Equals(word.Trim(), one, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>True when anything at all is being hidden, by any of the switches.</summary>
        internal static bool AnythingHidden()
        {
            if (ShowAllMarkers != null && !ShowAllMarkers.Value) return true;
            if (ShowClearedDeposits != null && !ShowClearedDeposits.Value) return true;
            if (Focus.On) return true;
            if (CrossedOff.HiddenGroupCount() > 0) return true;
            if (CrossedOff.HiddenClearedCount() > 0) return true;
            if (WorldPins.HiddenCount() > 0) return true;
            // Markers hidden one at a time count too. They did not, and the question this answers
            // is asked to decide whether to offer a way back -- so a player who had hidden five
            // markers by hand and nothing else was told nothing was hidden and offered nothing.
            if (ViewPrefs.Count > 0) return true;
            return HiddenIconCount() > 0 || HiddenOwnIconCount() > 0 || HiddenBiomeCount() > 0 || HiddenKindCount() > 0;
        }

        // ── Cartography table ───────────────────────────────────────────────────────
        internal static ConfigEntry<bool> AutoSyncTable;
        internal static ConfigEntry<float> TableSyncRadius;
        internal static ConfigEntry<float> TableSyncCooldown;

        // ── Recording ───────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> RecordEnabled;
        internal static ConfigEntry<float> RecordRadius;
        internal static ConfigEntry<float> RecordDwell;
        internal static ConfigEntry<bool> BlockWhileAttacked;
        internal static ConfigEntry<float> AttackedSeconds;
        internal static ConfigEntry<Movement> BlockWhileMoving;
        internal static ConfigEntry<float> FoundMemoryMinutes;
        internal static ConfigEntry<float> LookDwell;
        internal static readonly Dictionary<Category, ConfigEntry<float>> LookDistance = new Dictionary<Category, ConfigEntry<float>>();

        internal static float LookDistanceFor(Category cat)
        {
            return LookDistance.TryGetValue(cat, out var entry) ? Mathf.Max(0f, entry.Value) : 30f;
        }

        /// <summary>
        /// The same, for a thing that has been identified: its kind's distance, unless it is one
        /// of the handful the catalog says is seen from further off. The larger of the two, so
        /// raising the kind's setting is never undone by the exception.
        /// </summary>
        internal static float LookDistanceFor(Found found)
        {
            if (found == null) return 30f;
            return Mathf.Max(LookDistanceFor(found.Cat), Categories.FarSight(found.Source));
        }

        /// <summary>The longest sighting distance of any kind: how far the look ray is cast.</summary>
        internal static float MaxLookDistance()
        {
            float max = 5f;
            foreach (var entry in LookDistance.Values) if (entry.Value > max) max = entry.Value;
            return Mathf.Max(max, Categories.FarthestSight());
        }
        internal static readonly Dictionary<Category, ConfigEntry<bool>> CategoryEnabled = new Dictionary<Category, ConfigEntry<bool>>();
        internal static readonly Dictionary<Category, ConfigEntry<float>> MarkerSpacing = new Dictionary<Category, ConfigEntry<float>>();
        internal static readonly Dictionary<Category, ConfigEntry<float>> LabelSpacing = new Dictionary<Category, ConfigEntry<float>>();
        internal static readonly Dictionary<Category, ConfigEntry<int>> MarkerSize = new Dictionary<Category, ConfigEntry<int>>();
        internal static readonly Dictionary<Category, ConfigEntry<bool>> ShowOnMinimap = new Dictionary<Category, ConfigEntry<bool>>();
        internal static readonly Dictionary<Category, ConfigEntry<string>> CategoryPrefabs = new Dictionary<Category, ConfigEntry<string>>();
        internal static readonly Dictionary<Category, ConfigEntry<string>> CategoryIcon = new Dictionary<Category, ConfigEntry<string>>();
        /// <summary>The second portal picture. Not a category: stone portals are portals.</summary>
        internal static ConfigEntry<string> StonePortalIcon;
        internal static ConfigEntry<bool> StructuresIncludeUnlisted;
        internal static ConfigEntry<string> RecordingExceptions;
        internal static ConfigEntry<bool> ShowNotRecording;
        internal static ConfigEntry<bool> RecordWishbone;
        internal static ConfigEntry<string> StructuresExcludePrefixes;
        internal static ConfigEntry<bool> RecordInBase;
        internal static ConfigEntry<bool> ForgetTilledCrops;
        internal static ConfigEntry<bool> CrossOffStructuresOnChest;
        internal static ConfigEntry<bool> CrossOffMinedCopper;
        internal static ConfigEntry<bool> CrossOffMinedSilver;
        internal static ConfigEntry<bool> ApplyLabelRulesOnSync;
        internal static ConfigEntry<bool> FoldDuplicates;
        internal static ConfigEntry<bool> RepairDungeonIcons;
        internal static ConfigEntry<float> ServerAutosaveMinutes;
        internal static ConfigEntry<float> TransportSweepSeconds;
        internal static ConfigEntry<float> TransportLiveSeconds;
        internal static ConfigEntry<bool> PaintEnabled;
        internal static ConfigEntry<bool> PaintBuildings;
        internal static ConfigEntry<bool> PaintRoads;
        internal static ConfigEntry<bool> PaintDirtPaths;
        internal static ConfigEntry<bool> PaintFarmland;
        internal static ConfigEntry<float> PaintDetailMeters;
        internal static ConfigEntry<float> BuildSweepSeconds;
        internal static ConfigEntry<int> PaintLeastPieces;
        internal static ConfigEntry<bool> PaintRaisedGround;
        internal static ConfigEntry<bool> PaintDugGround;
        internal static ConfigEntry<float> PaintShapedLeastMeters;
        internal static ConfigEntry<bool> ShowMapPaint;
        internal static ConfigEntry<Color> WoodColor;
        internal static ConfigEntry<Color> StoneColor;
        internal static ConfigEntry<Color> IronColor;
        internal static ConfigEntry<Color> MarbleColor;
        internal static ConfigEntry<bool> PaintOutlines;
        internal static ConfigEntry<float> PaintGroundVariation;
        internal static ConfigEntry<bool> PaintSharpMinimap;
        internal static ConfigEntry<Color> PavedColor;
        internal static ConfigEntry<Color> DirtColor;
        internal static ConfigEntry<Color> FarmColor;
        internal static ConfigEntry<int> PaintSpread;
        internal static ConfigEntry<bool> PaintOverWater;
        internal static ConfigEntry<int> StopWaitingAfter;
        internal static ConfigEntry<bool> ShowTransportLabels;
        internal static ConfigEntry<bool> ShowMarkerLabels;
        internal static ConfigEntry<bool> ShowBossLabels;
        internal static ConfigEntry<bool> ShowPortalLabels;
        internal static ConfigEntry<float> LocateSeconds;
        internal static ConfigEntry<int> PlayerMarkerSize;
        internal static ConfigEntry<int> PlayerArrowSize;
        internal static ConfigEntry<bool> PinMarkerMenu;
        internal static ConfigEntry<string> AdoptNameHints;
        internal static ConfigEntry<string> AdoptPinMeanings;
        internal static ConfigEntry<Vector2> PinnedMenuSpot;

        /// <summary>
        /// Settings for markers the server finds and the client draws, rather than markers written
        /// on the map. They behave differently enough to be worth keeping apart: nothing here is
        /// recorded, shared or saved.
        /// </summary>
        private const string Dynamic = "Dynamic Markers";

        /// <summary>
        /// Settings for what is colored into the map picture rather than pinned on top of it.
        /// Kept apart from Display for the same reason Dynamic Markers is: nothing here is a
        /// marker, nothing is recorded, and the questions it answers are not the same ones.
        /// </summary>
        private const string Painting = "Map Painting";

        /// <summary>
        /// Every color, in one place.
        ///
        /// <para>They were filed with the thing each one colors, which reads well in a config
        /// file and badly in a config manager: the colors are the settings somebody actually
        /// sits and fiddles with, and they were scattered down two sections among switches and
        /// thresholds. A color is also the one kind of setting you choose by comparing it with
        /// the others, which you cannot do when they are a screen apart.</para>
        /// </summary>
        private const string Palette = "Colors";

        private static string LiveShowNote(string label)
        {
            string one = label.ToLowerInvariant().TrimEnd('s');
            return "Draw every " + one + " in the world on your map, where it is now. The server finds them and tells " +
                   "everyone, so this covers the whole world rather than what is loaded around you. These pins are drawn " +
                   "only: never written to your map, never shared, and gone the moment this is off.";
        }

        // ── Pocket map visuals ──────────────────────────────────────────────────────
        internal static ConfigEntry<bool> ShowMapInHands;
        internal static ConfigEntry<int> MapPose;
        internal static ConfigEntry<float> MapViewFraction;
        internal static ConfigEntry<string> MapOffset, MapRotation, MapScale;
        internal static ConfigEntry<string> PencilItem, PencilSize, PencilPositionTweak, PencilRotationTweak;

        internal static void Bind(TheGreatestMapMod mod)
        {
            TakeOutMapKey = mod.BindLocal("Keys", "Take Out Map", new KeyboardShortcut(KeyCode.Y),
                "Takes the map out of your pocket (both hands: map left, pencil right) or puts it away. " +
                "Use a key no other mod acts on: a mod that equips something on the same key folds the map straight back up.");
            SyncTableKey = mod.BindLocal("Keys", "Sync Cartography Table", new KeyboardShortcut(KeyCode.U),
                "Reads and writes the nearest cartography table within reach right now, ignoring the auto-sync cooldown.");
            LegendKey = mod.BindLocal("Keys", "Legend", new KeyboardShortcut(KeyCode.L),
                "Opens the legend: every marker this mod can draw and the thing it stands for, with the icon resolved " +
                "the way recording resolves it. The console command tgm_legend does the same.");
            HighlightPlayersKey = mod.BindLocal("Keys", "Highlight Players", new KeyboardShortcut(KeyCode.T),
                "On the large map, light up the other players and put every other marker out, so you can find each " +
                "other on a map carrying hundreds of markers. Press again, or close the map, to bring it all back. " +
                "Only players who are sharing their position can be shown, which is all the game tells your client.");

            EchoLogToConsole = mod.BindLocal("General", "Echo Log To Console", false,
                "Repeat this mod's own log lines in the dev console (F5) as they happen. Off by default; it is for " +
                "watching a sync, a repair or a conversion without alt-tabbing to the log file.");

            ShowMessages = mod.BindLocal("General", "Show Messages", true,
                "Show small top-left messages when the map is taken out, a marker is recorded, and so on.");

            LockConfiguration = mod.BindSynced("General", "Lock Configuration", true,
                "Only server admins may change the settings marked [Synced with Server]. Those are the rules of the " +
                "shared map rather than personal taste: who may erase a marker, how near and how often two players' maps " +
                "merge, what the cartography table carries and how readily you touch one, and which finds are crossed " +
                "off for everyone, so one player's config should not decide them for the rest. The settings marked " +
                "\"Server only\" are in here as well: nothing on your machine reads them, and the mark says so. " +
                "Everything else stays yours. Off lets any player set them for the whole server, which is the " +
                "behavior before 1.2.0. No effect in single player or when you host the game yourself.");
            mod.LockConfig(LockConfiguration);

            SharingMode = mod.BindSynced("Sharing", "Sharing Mode", TheGreatestMap.SharingMode.Table,
                "Table: markers travel like exploration. What you record or place stays on your own map until you merge " +
                "it with the shared map at a cartography table, or with another player's map when you both have your maps " +
                "out standing together. Instant: every change goes to the shared map at once and out to everyone.");
            AllowErasingMarkers = mod.BindSynced("Sharing", "Allow Erasing Markers", false,
                "Let players erase recorded markers, the ones this mod wrote for something really in the world. Off by " +
                "default, because the world does not forget: erase the marker and the next player to walk past records it " +
                "again, so removing one is a repair for a marker that is wrong rather than an everyday action. Hiding is " +
                "what you want for markers you simply do not care to see. A marker somebody placed by hand is not covered: " +
                "whoever placed it can always take it back, and this switch decides whether anyone else may.");
            ExchangeRadius = mod.BindSynced("Sharing", "Exchange Radius", 5f,
                "Distance in meters within which two players who both have their maps out compare and merge their maps. " +
                "The server's, because only one of the two decides: whoever reaches out uses their own radius and the " +
                "other player's machine does half the work, so a wide setting starts an exchange on somebody from across " +
                "the camp, for a meeting they would never have called a meeting.");
            ExchangeCooldown = mod.BindSynced("Sharing", "Exchange Cooldown", 60f,
                "Seconds before the same two players compare maps again. The server's for the same reason as the radius: " +
                "the player reaching out sets it, and comparing explored ground costs a few hundred milliseconds on the " +
                "main thread of both machines. A short cooldown spends that over and over on somebody who did not choose " +
                "it, and on a moving ship a stutter that long is enough to put them through the deck.");
            ExchangeExploration = mod.BindSynced("Sharing", "Exchange Exploration", true,
                "When comparing maps with another player, also share explored areas (the fog of war), as a cartography " +
                "table does. The server's, because it only governs what you send: taking the other player's ground in is " +
                "not gated on it at all, so switching it off leaves you receiving everything and giving nothing back. " +
                "That is a rule about what sharing means on this server rather than a preference, and it is not one " +
                "player's to make.");
            CrossOffMinedDownTo = mod.BindLocalRange("Recording", "Cross Off Deposits Mined Down To", 25, 0, 100,
                "Write a deposit's marker crossed off when this much or less of it is left, as a percentage of the " +
                "pieces it is made of. The game keeps a health for every piece of a vein, so a deposit somebody has " +
                "mined down to a stub can be told from a fresh one without standing on it -- and until this the map " +
                "said \"copper here\" with the same confidence either way. A deposit mined to nothing is not covered, " +
                "because the game destroys that one outright and there is nothing left to mark. 0 switches it off and " +
                "every deposit is written the same.");

            ShowCounts = mod.BindLocal("Display", "Show Counts", true,
                "Write how many markers each heading, kind and type has after its name, and how many of those are " +
                "being drawn where the two differ. Off leaves the names alone, which is quieter to read down and is " +
                "worth having once you know your own map: the numbers answer \"how much is there\" rather than " +
                "\"what is there\". The count of what you are finished with keeps its button either way, that one " +
                "being the switch for them as well as the number.");
            ShowCounts.SettingChanged += (_, __) => KindMenu.Relayout();

            VanillaPinNames = mod.BindLocal("Display", "Vanilla Pin Names", VanillaPins.DefaultNames,
                "What the marker list calls the map's own pins, as PinType=Name pairs. The game gives its five " +
                "placeable icons no names at all -- they are five pictures, and what each one means is whatever you " +
                "decided it means -- so they ship named after what they look like and this is where you write down " +
                "what yours are for: Icon2=Places, Icon3=Resources. Anything you leave out keeps its shipped name. " +
                "Icon0 is the fire, Icon1 the house, Icon2 the hammer, Icon3 the plain dot and Icon4 the portal. " +
                "The game's landmarks are not here: they are told apart by what they stand on and named on their own " +
                "rows under Places, with Hidden Landmarks for the switches.");
            VanillaPinNames.SettingChanged += (_, __) => KindMenu.Relayout();

            HiddenLandmarks = mod.BindLocal("Display", "Hidden Landmarks", "",
                "Comma-separated kinds of the game's own landmark pins not drawn on your map: Center (the middle of " +
                "the world), Boss (the altars), Trader (Haldor, Hildir, the Bog Witch), Hildir (the three places she " +
                "sends you), Other (any landmark this mod cannot name). The game draws all of these as one kind of " +
                "pin and gives no button for any of them, so these are hidden one pin at a time; the marker list's " +
                "rows under Places are what write this.");
            HiddenLandmarks.SettingChanged += (_, __) => { WorldPins.ForgetHidden(); WorldPins.Apply(); };

            HideVanillaControls = mod.BindLocal("Display", "Hide Vanilla Controls", false,
                "Take the game's own row of pin buttons off the large map, the marker list having grown to cover " +
                "every one of them: a row for each icon, which presses the same switch that double-clicking its " +
                "button does, a place-by-hand button on the five you can drop, and a line at the foot of the list " +
                "for pinging out loud. Nothing else of the map's is touched -- the public-position box stays where " +
                "it is -- and nothing is hidden while the marker button is off, since that is the way to the list. " +
                "Off leaves the buttons where the game put them; both work, and the only reason to take them away " +
                "is that two sets of controls for one map is one set too many. Off to begin with: it takes part of " +
                "the game's own screen away, which is a thing to be asked for rather than arrive with an update.");
            PingRow = mod.BindLocal("Display", "Ping Row", true,
                "Keep the line at the foot of the marker list that arms the game's ping, which stands in for the " +
                "ping button taken off the map by the setting above. Turn it off if you ping by middle-clicking the " +
                "map: that gesture is the game's and this mod never touched it, so the row is a second way to a " +
                "thing you already have. It is only ever drawn while the game's own buttons are hidden anyway.");
            HideVanillaControls.SettingChanged += (_, __) => KindMenu.Relayout();
            // A row's worth of the foot, so the list is rebuilt rather than repainted:
            // what the foot does not take is what the scrolling part gets.
            PingRow.SettingChanged += (_, __) => KindMenu.Relayout();

            SplitPlantsByFarming = mod.BindLocal("Display", "Split Plants By Farming", true,
                "In the marker list, stand two rows in place of the five plant kinds -- Farm and Gather, instead " +
                "of berries, mushrooms, herbs, seeds and wild crops -- each listing its types directly. What you " +
                "can plant you only have to find once, so those markers are worth folding away and forgetting; what " +
                "you cannot plant you have to go out and gather again every time you run low, which is what a map " +
                "is for. The line falls between types rather than between kinds because the game puts it there: " +
                "magecap and jotun puffs can be planted and the other four mushrooms cannot, flax and barley can " +
                "and fiddlehead cannot. Off lists the five kinds as before. Ore is a row of its own either way, no " +
                "vein being farmable.");
            SplitPlantsByFarming.SettingChanged += (_, __) => KindMenu.Relayout();

            TypeRowOrder = mod.BindLocal("Display", "Type Order", TypeOrder.Progression,
                "How the types under a kind are listed in the marker list. Progression is the order the catalog " +
                "lines are written in, which is the order you meet the things in them -- a dandelion before a " +
                "thistle, copper before silver -- and reordering a Catalog line reorders these to match. Most First " +
                "puts the commonest at the top, which is what this did before there was a choice. A To Z is by name.");
            TypeRowOrder.SettingChanged += (_, __) => KindMenu.Relayout();

            IconSizes = mod.BindLocal("Display", "Icon Size Tweaks", IconRegistry.DefaultSizes,
                "Icons drawn at a percentage of normal, as Icon=percent pairs: TrophyDraugr=80. Every marker is " +
                "drawn into a box of one size, so how big an icon looks is how much of its own frame the drawing " +
                "fills -- the draugr trophy fills its frame to the edges where the skeleton trophy sits inside a " +
                "margin, and two markers of the same size then look like two sizes. This is the list of the ones " +
                "somebody has judged by eye; nothing measures it. Empty the line to draw them all as the game does.");
            IconSizes.SettingChanged += (_, __) => ClientPins.Restyle();

            HiddenIcons = mod.BindLocal("Display", "Hidden Icons", "",
                "Comma-separated marker icons never drawn on your map, as item prefab names or icon keys, e.g. Dandelion,Thistle,pin:Icon1. " +
                "Hidden markers stay on your map and keep syncing; they are just not shown. Applies at once.");
            HiddenIcons.SettingChanged += (_, __) => { _hiddenIconKeys = null; ClientPins.Restyle(); };

            HiddenOwnIcons = mod.BindLocal("Display", "Hidden Icons On Markers You Placed", "",
                "The pictures you have switched off for markers you placed yourself, under Yours in the marker list. " +
                "The list writes this; there is nothing here to edit by hand. It is kept apart from Hidden Icons " +
                "because a picture is not one thing: a recorded farmstead wears the same house as a house pin you " +
                "dropped, and switching off one should not take the other with it.");
            HiddenOwnIcons.SettingChanged += (_, __) => { _hiddenOwnIconKeys = null; ClientPins.Restyle(); };

            ShowPrefabNames = mod.BindLocal("Display", "Prefab Names In Tooltips", false,
                "Add a line to a marker's tooltip with the game's own name for the thing it stands for and the icon " +
                "key it is wearing -- \"WoodFarm1 - pin:Icon1\". It is what an argument about the catalog is " +
                "actually about, and it is noise the rest of the time, so it is off until you want it.");

            HiddenBiomes = mod.BindLocal("Display", "Hidden Biomes", "",
                "The kinds you have switched off in one biome -- Structure/Mountain switches off mountain ruins and " +
                "leaves the meadows farms alone. The marker list writes this; there is nothing here to edit by hand.");
            HiddenBiomes.SettingChanged += (_, __) => { _hiddenBiomeKeys = null; ClientPins.Restyle(); };

            // Entries written before the two lists were told apart. Anything in Hidden Icons
            // naming one of the five pictures a player can place was put there by the Yours rows,
            // which is the half that moves: left where it was it would now switch off recorded
            // markers instead, which is the opposite of what the player asked for.
            MoveOwnPicturesOut();

            HiddenPlaces = mod.BindLocal("Display", "Hidden Places", "",
                "The places the game marks itself that you have switched off in the marker list -- a boss altar, one " +
                "of Hildir's errands -- by the picture each one wears. The list writes this; there is nothing here to " +
                "edit by hand. It is kept apart from Hidden Icons because a place is not a kind of thing: hiding the " +
                "Bonemass trophy is about trophies, and it should not take the game's own pin off the altar.");
            HiddenPlaces.SettingChanged += (_, __) =>
            {
                _hiddenPlaceKeys = null;
                // The markers the swap draws are not records and are not restyled: they are put
                // up and taken down, so the thing that draws them has to look again. At once,
                // rather than at its next look: a switch that takes a second to answer reads as
                // a switch that did not work.
                OwnMarks.Forget();
                WorldPins.Apply();
            };
            PauseWhileMapOpen = mod.BindLocal("Display", "Pause While Map Open", false,
                "Pause the game while the large map screen is open, the way the ESC menu does. Works by itself when " +
                "playing solo or hosting alone; on a dedicated server it takes the Pause My Server mod, which then " +
                "pauses only while you are the only player online. Closing the map resumes. Applies at once.");
            PauseWhileMapOpen.SettingChanged += (_, __) => MapPause.Refresh();
            ShowPauseButton = mod.BindLocal("Display", "Pause Button On Map", true,
                "Show a pause toggle in the top-right corner of the large map, the same mark GrabMaterials puts on its " +
                "inventory panel. Gray when pausing is switched off, orange while the game really is paused, and red " +
                "with a slash through it when the pause was asked for but refused, which happens on a server with other " +
                "players online or one without the Pause My Server mod.");
            MarkerButton = mod.BindLocal("Display", "Marker Button On Map", true,
                "Add a map-pin button to the row in the top-right corner of the large map, beside the maximize square. " +
                "Right-clicking it hides or shows every marker from this mod at once, as right-clicking a vanilla icon " +
                "does for that icon; left-clicking it opens the list of kinds, for hiding them one at a time. The pin is " +
                "drawn gold while markers are shown and gray while they are hidden.");
            MarkerButtonOffsetX = mod.BindLocal("Display", "Marker Button Offset X", 0f,
                "Move the map-pin button sideways from its place in the row, in pixels: positive right, negative left. " +
                "0 leaves it beside the maximize square. Changes show at once with the map open.");
            MarkerButtonOffsetY = mod.BindLocal("Display", "Marker Button Offset Y", 0f,
                "Move the map-pin button up or down from its place in the row, in pixels: positive up, negative down. " +
                "Measured from whichever corner the setting below names, which is the only way a large offset can hold " +
                "its place: the map grows about its middle, so the two corners travel in opposite directions as it is " +
                "resized.");
            MarkerButtonAtBottom = mod.BindLocal("Display", "Marker Button At Map Bottom", false,
                "Put the map-pin button in the bottom-right corner of the large map rather than the top-right, next to " +
                "the game's own column of icon buttons, and measure the two offsets above from there. Worth having if " +
                "you keep the game's buttons (see Hide Vanilla Controls) and would rather all the pin controls sat " +
                "together. Moving the button down with a large negative Offset Y instead does not survive resizing the " +
                "map, because that offset is measured from the top edge and the icons are measured from the bottom.");
            PauseButtonOffsetX = mod.BindLocal("Display", "Pause Button Offset X", 0f,
                "Move the whole row of map buttons sideways from the map's top-right corner, in pixels: positive " +
                "right, negative left. The pause mark, the maximize square and the map pin move together.");
            PauseButtonOffsetY = mod.BindLocal("Display", "Pause Button Offset Y", 0f,
                "Move the whole row of map buttons up or down, in pixels: negative down, positive up. The row sits in " +
                "the map's top-right corner and the biome name is moved down to sit under it, so it follows the row.");

            ShowAllMarkers = mod.BindLocal("Display", "Show Markers", true,
                "Draw this mod's markers on your map at all. Off hides every one of them, whatever the per-kind switches " +
                "say, including markers that have no kind; they stay on your map and keep syncing. This is what the " +
                "map-pin button on the map screen toggles.");
            ShowAllMarkers.SettingChanged += (_, __) => ClientPins.Restyle();
            ClipMinimapMarkers = mod.BindLocal("Display", "Clip Minimap Markers", true,
                "Hide any marker that falls outside the minimap's circle, whichever mod drew it. Only applies when " +
                "something has made the minimap round, such as the Round Minimap mod: the game still decides what to " +
                "draw by the square the minimap used to be, so markers in the corners stand on the HUD outside the " +
                "circle with nothing under them. The game's own square minimap is left alone, corners and all. This " +
                "only ever hides, and only ever shows again what it hid itself, so nothing another mod wanted hidden " +
                "is disturbed.");

            MarkerTooltips = mod.BindLocal("Display", "Marker Tooltips", true,
                "Hovering a marker on the large map names it: what it is, its icon, whether it has been cleared or " +
                "searched, and who recorded it and when. Most markers carry no label on purpose, which is what keeps a " +
                "crowded map readable, so this is where that information lives.");
            MapOutStatusIcon = mod.BindLocal("Display", "Map Out Status Icon", true,
                "Show a folded-map icon among your status effects, beside Rested and Wet, while your pocket map is out, " +
                "so a glance tells you whether what you find is being written down. Applies at once.");
            HighlightPlayersHidesOthers = mod.BindLocal("Display", "Highlight Players Hides Others", true,
                "While the players are lit up, draw nothing else at all. Off keeps the rest of the map and only grows " +
                "and colors the player markers, which is gentler but harder to read on a crowded map.");
            HighlightPlayersColor = mod.BindMoved("Display", Palette, "Highlight Players Color", Colors.FromHex("#66E0FF"),
                "Color of the player markers while they are lit up.");
            HighlightPlayersSize = mod.BindLocalRange("Display", "Highlight Players Size", 170, 100, 400,
                "How large the player markers are drawn while lit up, as a percentage of their normal size. They also " +
                "breathe slightly, so they read as alive rather than as another pin.");

            MapExtraSize = mod.BindLocal("Display", "Map Extra Size", "0,0",
                "How much larger than normal the large map is drawn, as width,height in UI pixels. The game leaves a " +
                "margin of wasted screen around the map; 0,0 keeps it and the largest value fills the screen. Negative " +
                "values make it smaller than the game draws it, down to a map still worth reading. Drag the gold grip " +
                "in the map's lower-right corner rather than typing here.");
            MapExtraSize.SettingChanged += (_, __) => MapFrame.Reload();
            MapPosition = mod.BindLocal("Display", "Map Position", "0,0",
                "Where the large map sits, measured from the middle of the screen in UI pixels. Drag the strip along " +
                "the very top edge of the map. It cannot be dragged off the screen, and there is nowhere to move it " +
                "once it fills the screen.");
            MapPosition.SettingChanged += (_, __) => MapFrame.Reload();
            MapResizeShowsMore = mod.BindLocal("Display", "Map Resize Shows More", true,
                "Growing the map shows more of the world instead of drawing the same world larger. The map's scale " +
                "comes from its height, so a taller map magnifies unless the zoom is let out to match; this lets it " +
                "out. Off keeps the zoom where you set it and a taller map simply draws bigger.");

            LegendSize = mod.BindLocal("Display", "Legend Size", "560,620",
                "Width and height of the legend panel, in pixels. Dragging its corner grip changes this.");
            LegendPosition = mod.BindLocal("Display", "Legend Position", "0,0",
                "Where the legend sits, measured from the middle of the screen. Dragging its title bar changes this.");
            CrossedOffFade = mod.BindLocalRange("Display", "Crossed Off Fade", 70, 0, 100,
                "How far a crossed-off marker fades, as a percentage: it washes toward gray and goes part " +
                "transparent, so what is done stops competing with what is not. The small cross the game draws " +
                "over it is easy to lose on a dark or busy picture, and a neighboring marker's label can cover it " +
                "outright, which is why the marker itself changes too. 0 leaves crossed-off markers as bright as " +
                "the rest. Applies at once.");
            CrossedOffFade.SettingChanged += (_, __) => ClientPins.Restyle();

            ShowClearedDeposits = mod.BindLocal("Display", "Show Cleared Deposits", true,
                "Keep drawing berry bushes, mushrooms, herbs and ore deposits after they have been used up, crossed " +
                "off, so you can see which ground has already been worked. Off hides every one of them whatever " +
                "Hidden Cleared Types says, that being the blanket switch for the lot; on, each type is drawn unless " +
                "that setting names it. The record is kept either way, and this does not touch places you have " +
                "searched, which Hidden Searched Places covers.");
            ShowClearedDeposits.SettingChanged += (_, __) => ClientPins.Restyle();
            HiddenCleared = mod.BindLocal("Display", "Hidden Cleared Types", "",
                "Comma-separated types of deposit and plant whose markers are not drawn once used up, as item prefab " +
                "names or icon keys, e.g. Copper,Dandelion -- the same spelling the Catalog lines use. One type at a " +
                "time, which is what the \"done\" button on a row in the marker list writes: crossing off your copper " +
                "has nothing to do with whether you still want to see which raspberry bushes you have picked. Show " +
                "Cleared Deposits above is the blanket switch, and while it is off this one has nothing left to say.");
            HiddenCleared.SettingChanged += (_, __) => { CrossedOff.ForgetCleared(); ClientPins.Restyle(); };
            HiddenSearched = mod.BindLocal("Display", "Hidden Searched Places", "",
                "Comma-separated kinds of place whose markers are not drawn once crossed off. Dungeons go by type " +
                "(Burial Chambers, Sunken Crypts, Frost Caves, Troll Caves, Infested Mines, Bear Caves, Other Dungeons, " +
                "named as in the Dungeons catalog with an s added); everything else by kind (Structures, Camps, Runestones, " +
                "Traders, Boss Altars, Portals), plus Placed Markers for the ones you put down yourself. The marker button's " +
                "list on the map edits this. The record is kept either way; used-up deposits have their own switch above.");
            HiddenSearched.SettingChanged += (_, __) => { CrossedOff.ForgetHidden(); ClientPins.Restyle(); };
            // "Map All Portals" was retired in 4.1.0. It asked whether to draw the portals
            // nobody had recorded, which was a real question while portals were recorded and is
            // not one now that none of them are: every portal is drawn from the server's list or
            // none is. "Show Portals", under Dynamic Markers with the ships and carts, is that
            // switch. Leaving both would be two settings for one thing, and the one that sounds
            // like it only adds extras would be the one that turns them all off.
            PortalColor = mod.BindMoved("Display", Palette, "Portal Marker Color", Colors.FromHex("#FF6600"),
                "Color of portal markers, which are worth picking out of the crowd because they are how you travel. " +
                "The default pair is two shades of orange; #A05BDD with #B07CFF as the pulse is a good violet " +
                "alternative.");
            PortalPulseColor = mod.BindMoved("Display", Palette, "Portal Pulse Color", Colors.FromHex("#FF8800"),
                "Portal markers fade back and forth between their color and this one, so they catch the eye on a busy " +
                "map. Two shades of the same color read as a gentle pulse; two different colors are far louder. Pull " +
                "the alpha slider to zero for a steady color instead.");
            PortalPulseSeconds = mod.BindLocal("Display", "Portal Pulse Seconds", 2f,
                "How long one fade from the portal color to the pulse color and back takes. Lower is faster; " +
                "0 stops the fade.");
            RevealNewMarkers = mod.BindLocal("Display", "Reveal New Markers Seconds", 30f,
                "How long a marker you have just recorded is drawn on both maps even though its kind or its icon is " +
                "hidden, so writing something down always shows you what you wrote. 0 turns it off. It does not " +
                "override the map-pin button's master switch or a marker you hid by hand, and it cannot show kinds " +
                "drawn on a vanilla icon (structures, portals, camps, boss altars) while vanilla's own icon button " +
                "has that icon switched off.");
            // Three settings and not one for each kind, because the whole point of thinning by
            // cell is that it needs no per-kind tuning: the cell follows the zoom and the counts
            // follow the ground. The mod has 121 settings already.
            ThinMarkers = mod.BindLocal("Display", "Thin Overlapping Markers", true,
                "Zoomed out, markers of the same kind pile on top of each other -- the same berry bush forty times " +
                "over one meadow -- and drawing them all is what makes the full map slow. With this on, one marker " +
                "of each kind is drawn per patch of ground, so you still see that there are berries and tin here, " +
                "without forty of each. Zoom in and the rest come back. Markers near you are never thinned.");
            ThinSpacing = mod.BindLocal("Display", "Marker Spacing On Screen", 1f,
                "How close two markers of the same kind have to be before one of them is held back, measured in " +
                "that kind's own marker widths on screen. 1 means their pictures are touching, which is what being " +
                "in the way means; below 1 they have to overlap outright, and above 1 they are thinned while a gap " +
                "still shows -- 2 holds one back with a clear marker's width between them. Every marker drawn costs " +
                "frames on a crowded map, so this is the dial between a readable map and a fast one. Only matters " +
                "with Thin Overlapping Markers on.");
            MostMarkersDrawn = mod.BindLocal("Display", "Most Markers Drawn", 800,
                "A limit on how many markers are drawn at once, whatever the spacing allows. Each patch of ground " +
                "keeps one marker of every kind, which is the point of it, so enough different kinds standing " +
                "together can still add up; this is the backstop. Markers are given up from whichever kind has the " +
                "most of them drawn. 0 turns the limit off.");
            ThinMarkers.SettingChanged += (_, __) => ClientPins.Restyle();
            ThinSpacing.SettingChanged += (_, __) => ClientPins.Restyle();
            MostMarkersDrawn.SettingChanged += (_, __) => ClientPins.Restyle();
            ShowHiddenWithin = mod.BindLocal("Display", "Show Hidden Markers Within", 50f,
                "Markers this close to you are drawn even when their kind or their icon is switched off, so the map " +
                "near you always tells the truth: nothing there means nothing recorded there, rather than something " +
                "recorded and not shown. It is what makes switching off the common plants bearable -- you can hide " +
                "every raspberry on the map and still see the one you are standing next to. 0 turns it off. Same " +
                "limits as the reveal above: not against the master switch, not against a marker you hid by hand, " +
                "not over a crossed-off kind you have put away, and not for kinds drawn on one of vanilla's own " +
                "icons while vanilla has that icon switched off.");
            RequireMapOutToEdit = mod.BindSynced("Sharing", "Require Map Out To Edit", false,
                "You must have the pocket map out to place a marker, erase one (yours, someone else's or a recorded one) " +
                "or cross one off on the map screen. Pings are always allowed. Off by default: the map screen edits like vanilla.");
            RequireMapOutToRecord = mod.BindSynced("Sharing", "Require Map Out To Record", true,
                "Automatic recording of found things only happens while the pocket map is out. Turn off to record " +
                "found things whenever you are within the record radius, map or no map.");
            TableCarriesPins = mod.BindSynced("Sharing", "Table Carries Player Markers", false,
                "Vanilla's own pin sharing, off by default. Your maps still merge at a cartography table either " +
                "way -- this is only about the five standard pins you place by hand, which vanilla copies in and out " +
                "of tables and which otherwise pile up a fresh copy on every visit. Off, they stay on your own map " +
                "and stale ones imported from a table before are swept away. Boss, Hildir and memorial pins share " +
                "through the table as in vanilla regardless. Turning it on also sends the markers this mod recorded " +
                "with one of those standard icons, so expect a plain copy of some of them beside the real one.");

            // All three are the server's. A table visit is not a local act: its last step is a
            // merge of your map with the shared map on the server, so how readily you touch a
            // table is how readily you ask the server to do that work. Five players with a wide
            // radius and a short cooldown standing in a base full of tables is a load nobody
            // chose. The sync key is deliberately left out of it -- asking for a sync by hand is
            // always allowed, whatever these say.
            AutoSyncTable = mod.BindSynced("Cartography Table", "Auto Sync", true,
                "Automatically read and write the cartography table when you are right at it. Silent unless something " +
                "is actually exchanged: new areas from the table are read, and the table is written (vanilla's 'map saved') " +
                "only when it lacks areas you have explored. Off, the sync key still works, so this is about what " +
                "happens by itself rather than about whether you may sync at all.");
            TableSyncRadius = mod.BindSynced("Cartography Table", "Sync Radius", 1f,
                "How close to a cartography table you must stand (meters from you to its edge) for auto-sync and the " +
                "sync key. The server's, because the far end of every sync is the server merging your map into the " +
                "shared one.");
            TableSyncCooldown = mod.BindSynced("Cartography Table", "Auto Sync Cooldown", 60f,
                "Seconds between automatic syncs of the same table while you stay in reach. The sync key ignores this, " +
                "so a player who wants their map up to date right now never has to wait on it.");

            RecordEnabled = mod.BindLocal("Recording", "Enabled", true,
                "While the pocket map is out, automatically record things you have found. Nothing is ever recorded " +
                "that you did not look at or interact with yourself.");
            RecordRadius = mod.BindLocal("Recording", "Record Range", 0f,
                "0 (default): taking the map out writes down everything you have found recently, wherever you are now. " +
                "Otherwise only finds within this many meters of you are written down.");
            RecordDwell = mod.BindLocal("Recording", "Record Dwell", 0f,
                "Seconds the map must be out before it starts recording. Zero writes as soon as it is out, which is " +
                "the deliberate act already. A delay here reads as nothing happening, so set it only if you want the " +
                "map to feel slow to write.");
            BlockWhileAttacked = mod.BindLocal("Recording", "Cannot Write Under Attack", true,
                "You cannot write on the map while something is hitting you. Enemies merely being nearby do not stop " +
                "you, unlike resting: you can stand your ground and write if you choose, but not while taking hits.");
            AttackedSeconds = mod.BindLocal("Recording", "Under Attack Seconds", 5f,
                "How long after being hit you still count as under attack.");
            BlockWhileMoving = mod.BindLocal("Recording", "Cannot Write While", Movement.Never,
                "Writing needs you to hold still: Never means movement is no obstacle, Running blocks writing only " +
                "while you run, and Moving means you must stop for a moment, walking included.");
            FoundMemoryMinutes = mod.BindLocal("Recording", "Found Memory Minutes", 30f,
                "How long your character remembers something found but not yet written down. Seeing it again restarts the " +
                "clock. The memory is saved with the character, so a relog inside the window does not lose it. 0 = never forget.");
            LookDwell = mod.BindLocal("Recording", "Look Dwell", 0.75f,
                "Seconds you must keep looking at something for it to count as found. Things under the crosshair within " +
                "interaction range, and anything you interact with, count immediately.");

            RecordWishbone = mod.BindLocal("Recording", "Record What The Wishbone Finds", true,
                "While a wishbone is worn, write down whatever it is pinging about -- buried treasure, a silver vein " +
                "under the snow -- at the spot itself, without digging it up. Nothing is invented: the game tells the " +
                "wishbone exactly which thing it has found, and this asks it the same question. Kinds you have switched " +
                "off stay off, and a thing already marked is still only marked once.");

            foreach (var cat in Categories.All)
            {
                string label = Categories.Label(cat);
                // A kind only ever placed by hand, or drawn live from the server, is never
                // recorded, so none of the recording settings would mean anything for it.
                if (!Categories.IsPlacedOnly(cat) && !Categories.IsLive(cat) && !Categories.VanillaMarks(cat))
                {
                CategoryEnabled[cat] = mod.BindLocal("Recording", "Record " + label, Categories.DefaultEnabled(cat),
                    "Record " + label.ToLowerInvariant() + " you have found." +
                    (cat == Category.Structure ? " Off by default: useful if you like to track which ruins and abandoned houses you have already searched (click a marker on the map to cross it off)." : ""));
                MarkerSpacing[cat] = mod.BindLocal("Recording", label + " Marker Spacing", Categories.DefaultSpacing(cat),
                    "Do not record " + label.ToLowerInvariant() + " if a marker with the same icon already exists within this many " +
                    "meters. Small values give one icon per plant so a clump shows how many there are. The find stays pending and " +
                    "is written once that marker is gone.");
                LabelSpacing[cat] = mod.BindLocal("Recording", label + " Label Spacing", Categories.DefaultLabelSpacing(cat),
                    "Text labels on recorded " + label.ToLowerInvariant() + " markers: -1 = never (the icon says it all), " +
                    "0 = always, or a distance in meters so a clump gets one label (a new marker is icon-only when a marker " +
                    "with the same icon and name that already has a label lies within that distance). Whether a marker " +
                    "carries a label is settled when it is written, so this decides the " + label.ToLowerInvariant() +
                    " you find from now on. Asking for fewer labels thins the markers you already have, at the next " +
                    "sync; asking for more does not write a label onto a marker that has none, because a marker whose " +
                    "label you rubbed out yourself looks exactly like one that never had one. Hovering a marker names " +
                    "it whatever this says.");
                // Nests labeled every single marker, because the kind reached the enum after the
                // switch that decides this and fell through to a catch-all that labels. A fixed
                // default cannot reach anybody who has already run a version with the bug: their
                // config holds the 0 it wrote for them. This can.
                if (cat == Category.DrakeNest)
                    LabelSpacing[cat] = mod.Retune(LabelSpacing[cat], 0f, -1f);
                LookDistance[cat] = mod.BindLocal("Recording", label + " Look Distance", Categories.DefaultLookDistance(cat),
                    "How far away " + label.ToLowerInvariant() + " can be and still count as seen when you look straight at them " +
                    "with clear line of sight. Interacting, or having them under the crosshair within reach, always counts.");
                }
                // Size and minimap are about markers this mod drew into your map, which a live
                // kind has none of: its pins are the server's answer, drawn and thrown away.
                if (!Categories.IsLive(cat))
                {
                MarkerSize[cat] = mod.BindLocalRange("Recording", label + " Marker Size", Categories.DefaultSize(cat), 20, 100,
                    "Size of recorded " + label.ToLowerInvariant() + " markers on the map, as a percentage of the normal marker size.");
                ShowOnMinimap[cat] = mod.BindLocal("Recording", label + " On Minimap", true,
                    "Show recorded " + label.ToLowerInvariant() + " markers on the small minimap (when they are shown at all).");
                ShowOnMinimap[cat].SettingChanged += (_, __) => ClientPins.Restyle();
                MarkerSize[cat].SettingChanged += (_, __) => ClientPins.Restyle();
                }
                // A live kind's switch belongs with the other dynamic-marker settings rather than
                // among the display ones, which are all about markers written on the map.
                ShowKind[cat] = Categories.IsLive(cat)
                    ? mod.BindMoved("Display", Dynamic, "Show " + label, true, LiveShowNote(label))
                    : mod.BindLocal("Display", "Show " + label, true,
                      "Draw recorded " + label.ToLowerInvariant() + " markers on your map. Off hides them on both the large map and the " +
                    "minimap; they stay on your map and keep syncing. To hide single icons instead (say only dandelions) use Hidden Icons.");
                ShowKind[cat].SettingChanged += (_, __) =>
                {
                    ClientPins.Restyle();
                    // The two kinds the game marks itself can be drawn by this mod instead, and
                    // those markers are put up rather than restyled. Told here so the switch
                    // answers in the same frame instead of at the drawer's next look, which is
                    // every two seconds and reads as a switch that did not work.
                    OwnMarks.Forget();
                };
                if (Categories.UsesPrefabList(cat))
                {
                    CategoryPrefabs[cat] = mod.BindLocal("Catalog", label, Categories.DefaultPrefabs(cat),
                        "Comma-separated prefab names that count as " + label.ToLowerInvariant() +
                        ". Prefab=Display Name overrides the marker text; add |Icon (an item prefab name, or pin:<VanillaPinType>) " +
                        "to override the icon. Without |Icon, plants use the icon of the item they give and deposits the icon " +
                        "of what they drop. " + Categories.CatalogNote(cat));
                }
                CategoryIcon[cat] = mod.BindLocal("Icons", label, Categories.DefaultIcon(cat),
                    "Fallback icon for " + label.ToLowerInvariant() + " markers when the thing's own item icon cannot be worked out: " +
                    "an item prefab name (its inventory icon is used), piece:<prefab> (a building piece's build-menu picture) " +
                    "or pin:<VanillaPinType> such as pin:Icon0, pin:Memorial, pin:Boss.");
            }
            // Beside the kinds in the Icons section, though it is not a kind of its own. A
            // stone portal is a portal -- it belongs to the Portal row, the Portal color and the
            // Portal switch, and only the picture differs -- so making it a category to give it
            // a picture would have split one idea in two everywhere else to settle one line.
            StonePortalIcon = mod.BindLocal("Icons", "Stone Portals", Portals.StoneIconDefault,
                "The picture for portals that will take ore and metal through: the stone portal, and any modded " +
                "portal that allows everything. Drawn apart from ordinary portals so you can see at a glance which " +
                "one carries the copper home. Set it to the same value as Portals to stop telling them apart. " +
                "Takes the same forms as the other icons, plus drawn:stoneportal for the one this mod draws itself.");
            StonePortalIcon.SettingChanged += (_, __) => Portals.Redraw();

            OrderTheCatalogOnce();
            FixShippedIconsOnce();

            // Written by the marker list, not by hand. It holds what the player has changed
            // rather than what is on, so that a group added by a later version starts at the tier
            // that version chose for it -- a saved list of what was on would have said nothing
            // about a group that did not exist yet, and silence would have read as off.
            RecordingExceptions = mod.BindLocal("Recording", "Recording Exceptions", "",
                "Groups of things you have switched recording on or off for, as Group=on or Group=off separated by " +
                "the | character. The marker list writes this: tick 'Show what you are not recording' in its gear " +
                "menu to see every group and switch any of them. Anything not named here is recorded or not " +
                "according to what this version ships, so leaving it empty is the ordinary state.");
            ShowNotRecording = mod.BindLocal("Display", "Show What You Are Not Recording", false,
                "Add a row to the marker list for each group of things this mod knows about and is not writing " +
                "markers for, so you can see what they are and ask for any of them. There is no other honest way " +
                "to offer the choice: a group nobody is recording has no markers, so nothing in a list built from " +
                "the map could ever mention it, and a config file full of prefab names is not a question anyone " +
                "can answer. Also in the marker list's gear menu, which is where it is meant to be used.");
            ShowNotRecording.SettingChanged += (_, __) => KindMenu.Relayout();

            RecordingExceptions.SettingChanged += (_, __) =>
            {
                Tiers.Forget();
                ClientPins.Restyle();
            };

            StructuresIncludeUnlisted = mod.BindLocal("Catalog", "Structures Include Unlisted", true,
                "Treat any outdoor location that is not listed under another kind as a structure (the location's prefab name, " +
                "tidied up, becomes the marker text). Turn off to record only the listed structure prefixes.");
            StructuresExcludePrefixes = mod.BindLocal("Catalog", "Structures Exclude Prefixes", "Vegvisir_,Runestone_,Meteorite,TarPit,Rock,Hugin,StartTemple,Pickable,Vegetation,Tree,Bush,MountainGrave,MountainWell," +
                "InfestedTree,SwampHut,SwampRuin,SwampWell,Grave1,FireHole,ShipWreck,ShipSetting,CharredRuins," +
                "FortressRuins,VoltureNest,Ruin,StoneHouse,StoneTowerRuins,Mistlands_Statue,Mistlands_Viaduct",
                "Comma-separated prefab name prefixes never swept in as structures. This is about the places the " +
                "catalog does not name: a location listed under Structures above is recorded because it is listed, " +
                "whatever this says, so leaving one out means taking its line out as well.");
            CutShippedLinesOnce();
            // And the ones that changed kind rather than went away. After the cuts, so a type
            // that was cut and then moved is not written to the exclusion list by one pass and
            // recorded by the other.
            MoveShippedLinesOnce();
            // After the icon corrections above, not before: an entry correction matches the whole
            // entry, icon and all, so it has to see the icon the correction leaves behind.
            FixShippedEntriesOnce();
            FoldDuplicates = mod.BindSynced("Recording", "Fold Markers Standing On One Another", true,
                "After markers arrive from somebody else, fold any that stand on one another into one. Recording " +
                "already refuses to write a marker where one of the same icon is in the way, but it can only check " +
                "this map -- another player who found the same cave first wrote theirs just as honestly, and a merge " +
                "keeps both, so the pair sits one on top of the other and crossing one off leaves a bright marker " +
                "wearing a red cross. What the folded ones knew is kept: crossed off if any of them was, and a name " +
                "if the survivor had none. Two markers that carry different names are left alone, since two names " +
                "side by side are two things somebody meant to tell apart. Every map folds a pair the same way " +
                "round, so they agree rather than " +
                "erasing each other's. The console command tgm_dedupe does the same on demand, and lists first.");

            ApplyLabelRulesOnSync = mod.BindSynced("Recording", "Apply Label Rules To Existing Markers", true,
                "After each sync, apply the label rules above to recorded markers that already exist: within each kind, the " +
                "oldest marker of a same-named cluster keeps its label and the others lose theirs, for everyone. Labels are only " +
                "ever removed, never added back. The console command tgm_relabel does the same on demand.");
            RepairDungeonIcons = mod.BindSynced("Recording", "Repair Dungeon Icons", true,
                "Correct dungeon markers wearing the swamp crypt key because their cave was not in the catalog when they " +
                "were written, such as bear caves. Only markers with that icon whose name says otherwise are touched, so a " +
                "real sunken crypt keeps it. Runs when your map loads and after each merge, which also catches markers " +
                "arriving from players still on an older version. The console command tgm_reicon does the same on demand.");
            ForgetTilledCrops = mod.BindSynced("Recording", "Forget Crops On Tilled Ground", true,
                "Erase markers for plants growing on ground somebody tilled, as you come across them. Maps written " +
                "before this mod knew the difference carry a marker for every crop in a farm -- dozens of them on one " +
                "field -- and they are not discoveries. There is no sweep of the whole world because whether ground " +
                "is tilled can only be read where terrain is loaded, so a farm clears itself the next time anyone " +
                "walks across it. The server's, because the erasing is for everyone and its three neighbors here -- " +
                "folding duplicates, the label rules and the dungeon icon repair -- are already the server's for that same " +
                "reason. Your own \"Record Plants In Your Base\" still holds the sweep off your client, for anyone who " +
                "wants their garden on the map.");

            RecordInBase = mod.BindLocal("Recording", "Record Plants In Your Base", false,
                "Write down berries, mushrooms, herbs, seeds and plants growing on ground you have tilled, or standing " +
                "inside one of your own bases. Off by " +
                "default: the carrots and turnips in your own garden are not a discovery, and a farm fills the map " +
                "with markers for crops you planted and already know about. A base is the game's own idea of one, the " +
                "ground a workbench or fire keeps monsters out of, so it covers the yard as well as the house. Ore " +
                "deposits are not covered: one standing in your yard is still worth knowing about.");

            CrossOffStructuresOnChest = mod.BindSynced("Recording", "Cross Off Structures When Searched", true,
                "Opening a chest inside a structure or an enemy base crosses its marker off for everyone. If it has no " +
                "marker yet, the place is remembered as searched and its marker starts crossed off when it is recorded. " +
                "The key says structures because that is all it covered when it was written; the dvergr excavations and " +
                "the charred fortresses it has always applied to are enemy bases now, and the fuling villages joined them.");
            CrossOffMinedCopper = mod.BindSynced("Recording", "Cross Off Mined Copper", false,
                "Cross a copper deposit's marker off for everyone once it has been mined to nothing. Off by default, because " +
                "a copper deposit runs on underground and a player cannot always tell whether they have got all of it, so " +
                "crossing it off for them would tell them something they could not know. Tin and plants are always crossed " +
                "off, since there is no doubt when those are done. You can still cross one off by hand.");
            CrossOffMinedSilver = mod.BindSynced("Recording", "Cross Off Mined Silver", false,
                "Cross a silver vein's marker off for everyone once it has been mined to nothing. Off by default for the " +
                "same reason as copper: part of a vein can be out of sight, and the map should not know more than the " +
                "player does. You can still cross one off by hand.");
            foreach (var entry in CategoryPrefabs.Values)
                entry.SettingChanged += (_, __) => { Catalog.Invalidate(); KindInference.Invalidate(); };
            StructuresExcludePrefixes.SettingChanged += (_, __) => Catalog.Invalidate();
            // A kind's fallback icon is the last step of working out what a marker wears, so a
            // change to one has to throw the worked-out answers away and redraw.
            foreach (var entry in CategoryIcon.Values)
                entry.SettingChanged += (_, __) => { Catalog.Invalidate(); ClientPins.Restyle(); };

            PinMarkerMenu = mod.BindLocal("Display", "Pin Marker List Open", false,
                "Keep the marker button's list open: it stops closing when you click elsewhere on the map, comes back " +
                "by itself each time you open the map, and can be dragged about the map by its title. Escape still " +
                "closes it. The stick pin in the list's top corner sets this, so it can be set without leaving the map.");
            PinnedMenuSpot = mod.BindLocal("Display", "Pinned Marker List Spot", Vector2.zero,
                "Where the marker list sits, as a fraction of the screen. Dragging the list by its title is what sets " +
                "this; zero means it has never been moved, and then the list opens where you clicked the button. It is " +
                "honored whether the list is pinned or not, so a list you have carried to a corner of the map stays in " +
                "that corner. Pinning does not write it, so a value typed in here is not overwritten.");

            AdoptNameHints = mod.BindLocal("Catalog", "Adopt Name Hints", "",
                "What your own shorthand on a hand-placed marker means, for tgm_adopt, as " +
                "name=answer pairs: \"T=TrollCave, S=Greydwarf_camp, BARNACLE=none, PLAINS=none\". The answer is " +
                "part of a prefab name or of the name this mod would give the thing, and the marker is only " +
                "converted if something matching it is within reach; \"none\" means never convert that marker at " +
                "all, which is the answer for a note about a biome or a two-star boar. A leading star level is " +
                "ignored, so T covers 1*T and 2*T. Without a hint, a marker you named is converted only if the " +
                "answer agrees with the name, so nothing you wrote is lost quietly -- the listing names every " +
                "marker it left alone and what the world offered for it, which is how you know what to put here.");
            AdoptPinMeanings = mod.BindLocal("Catalog", "Adopt Pin Meanings", "",
                "Which of the game's five placeable pins mean what, for tgm_adopt, as type=meaning pairs: " +
                "\"Icon3=place, Icon0=fire, Icon1=resource\". The meanings are place, resource, fire and unknown. " +
                "Left empty it reads the name of the picture the game draws for each pin -- a hammer is a place, a " +
                "dot is a resource -- and the listing prints what it decided for each of your pins, so a wrong " +
                "guess is something you can see rather than something that happens to your map.");

            PlayerArrowSize = mod.BindLocalRange("Display", "Player Arrow Size", 100, 50, 400,
                "Size of your own arrow on the large map, as a percentage of the normal size. Pan and zoom around a " +
                "few hundred markers and a small arrow is easy to lose; this is the one thing on the map that had no " +
                "size of its own, because the game draws it as part of the map rather than as a marker. It does not " +
                "apply while you have a ship's helm: there is next to nothing on the water to lose an arrow among, so " +
                "the size buys nothing there and costs the precision you want while steering.");
            PlayerArrowSize.SettingChanged += (_, __) => { if (PlayerArrowSize.Value == 100) PlayerArrow.Reset(); };

            PlayerMarkerSize = mod.BindLocalRange("Display", "Player Marker Size", 100, 50, 400,
                "Size of the other players' markers on the map, as a percentage of the normal size. These are the " +
                "game's own markers rather than this mod's, and on a map carrying hundreds of markers three moving " +
                "dots are easy to lose; making them larger is the quietest way to keep them findable.");
            PlayerMarkerSize.SettingChanged += (_, __) => ClientPins.Restyle();

            LocateSeconds = mod.BindLocal("Display", "Locate Seconds", 6f,
                "How long markers pulse after you press \"locate\" on their row in the marker list. They swell and " +
                "brighten twice a second and fade back over the last second; nothing is hidden and nothing is changed.");

            ShowMarkerLabels = mod.BindLocal("Display", "Show Marker Labels", true,
                "Write marker names on the large map. Off draws the icons alone, which is worth having on a map with " +
                "thousands of them: the label rules already thin a cluster down to one name, and past a certain " +
                "density even that is a wall of words over the terrain. The names are not lost -- hovering a marker " +
                "still names it, the marker list still finds it by name, and switching this back on writes them all " +
                "again. There is a row in the marker list that does the same thing without leaving the map.");
            ShowMarkerLabels.SettingChanged += (_, __) => ClientPins.RedrawLabels();

            ShowBossLabels = mod.BindLocal("Display", "Show Boss Labels", true,
                "Write the names of the boss altars on the large map -- the ones the game writes itself when you find " +
                "a summoning stone. Kept apart from the marker labels for the same reason the portal names are: there " +
                "are five of them on a whole world, each one is worth reading, and they have nothing in common with " +
                "the thousands of names a recorded map carries. Turning the marker labels off used to take these with " +
                "them, which is a lot to lose for a change made about berry bushes.");
            ShowBossLabels.SettingChanged += (_, __) => ClientPins.RedrawLabels();

            CustomBossMarkers = mod.BindLocal("Display", "Show Custom Boss Markers", false,
                "Draw this mod's own marker for a boss altar instead of the game's pin: the boss's own trophy, named " +
                "for him, where the game draws one picture for all of them. The game marks every altar on every map, " +
                "so a marker of this mod's beside one is the same stones twice -- on, ours is drawn and the game's " +
                "pins are hidden; off, the game's stand as they always did. One marker per altar either way, whether " +
                "or not this mod has ever recorded that one: these are drawn from what the game has marked rather " +
                "than from what has been written down.");
            CustomBossMarkers.SettingChanged += (_, __) =>
            {
                OwnMarks.Forget();
                // Put the game's pins back on the way out. The pass that hides them only runs
                // while this is on, so nothing else would.
                if (!CustomBossMarkers.Value)
                    VanillaPins.SetDrawn(Minimap.instance, Minimap.PinType.Boss, true);
                WorldPins.Redraw();
                ClientPins.Restyle();
            };

            CustomHildirMarkers = mod.BindLocal("Display", "Show Custom Hildir Markers", false,
                "Draw this mod's own markers for the three places Hildir sends you instead of the game's. Nothing is " +
                "recorded for her errands -- the game marks all three itself once she has asked -- so this is a swap " +
                "and not an addition: on, ours are drawn and the game's are hidden; off, the game's stand as they " +
                "always did. Ours are drawn from the game's own marks, so they appear and go with her quests.");
            CustomHildirMarkers.SettingChanged += (_, __) =>
            {
                OwnMarks.Forget();
                WorldPins.Redraw();
                ClientPins.Restyle();
            };

            ShowPortalLabels = mod.BindLocal("Display", "Show Portal Labels", true,
                "Write portal names on the large map. Kept apart from the marker labels because the two are not the " +
                "same complaint: a portal name is worth reading and there are a dozen of them, while marker names run " +
                "to thousands. A base with six portals writes six words across itself, which is reason enough to want " +
                "these gone while keeping the rest.");
            ShowPortalLabels.SettingChanged += (_, __) => Portals.Redraw();

            ShowTransportLabels = mod.BindMoved("Display", Dynamic, "Show Transport Labels", false,
                "Write the name of each ship and cart beside its pin on the large map. Off by default: a harbor of six " +
                "ships writes six names across the coast, the icon already says what is there, and hovering a pin names it.");
            ShowTransportLabels.SettingChanged += (_, __) => Transports.Redraw();

            TransportSweepSeconds = mod.BindMoved("Server", Dynamic, "Transport Sweep Seconds", 5f,
                "Server only, and marked as the server's so it is plain your copy does nothing. How often the server " +
                "looks for every ship and cart in the world and tells the clients where " +
                "they are. These pins answer \"where did I leave it\", not \"where is it this second\": a longship under sail " +
                "covers 20 m in two seconds, so a tighter interval buys little and costs a walk of every object in the world. " +
                "The walk is spread over several frames either way. A vessel that is actually moving does not wait " +
                "on this: see Transport Movement Seconds.", synced: true);

            TransportLiveSeconds = mod.BindMoved("Server", Dynamic, "Transport Movement Seconds", 0.25f,
                "Server only, and marked as the server's so it is plain your copy does nothing. How often a vessel " +
                "that is on the move has its new position passed on. The sweep above answers \"what is out there\", " +
                "which is slow because it means walking the world; this answers \"where is it now\" for the vessels " +
                "already found, which is a lookup each and costs nothing. A ship under sail therefore slides along " +
                "the map instead of jumping every few seconds, while a harbor of moored ships sends nothing at all, " +
                "because nothing has moved. Raise it if the server is short of bandwidth.", synced: true);

            StopWaitingAfter = mod.BindMoved("Sharing", "Server", "Stop Waiting For A Player After", 5000,
                "Server only, and marked as the server's so it is plain your copy does nothing. How far behind a player " +
                "may fall, counted in changes to the shared map, before the server " +
                "stops holding erased markers open on their behalf. An erasure exists only to stop an older copy of the map " +
                "putting a deleted marker back, so once everyone has taken it in it is dead weight -- but one person who " +
                "stops playing would otherwise pin every erasure made afterwards forever. Counted in changes rather than " +
                "days because what puts a returning player out of step is how much the map moved while they were away, not " +
                "how long they were gone: a month away from a sleeping server costs nothing. On a map of five people " +
                "exploring, 5000 changes is roughly two months of ordinary play or a fortnight of hard exploring.",
                synced: true);

            ServerAutosaveMinutes = mod.BindSynced("Server", "Server Autosave Minutes", 0f,
                "Server only, and marked as the server's so it is plain your copy does nothing. Extra world save every " +
                "this many minutes on top of vanilla's own autosave (0 = vanilla only). " +
                "Worth setting on hosts whose panel stop kills the server without saving. A file named save-now in " +
                "BepInEx/config/TheGreatestMap/ also makes the server save at once (used by deploy scripts).");

            // ── what players have made, colored into the map ───────────────────────
            //
            // The switches here are the server's because they decide whether the server sweeps
            // the world and whether every client samples the ground it walks over. The colors and
            // how boldly it is drawn are nobody's business but the player looking at the map.
            PaintEnabled = mod.BindRenamed(Painting, "Player Structures Enabled", "Server Shares Player Developments", false,
                "EXPERIMENTAL, off by default. The master switch. On, the server works out where players have " +
                "built, paved, hoed and farmed across the whole world and sends it to everybody, to be colored " +
                "into their maps. Off, none of it runs and any map already painted goes back to the way the game " +
                "drew it. Players each need Show Player Developments as well, which is also off by default, so " +
                "turning this on does not redraw anybody's map until they ask for it. The settings below only " +
                "matter once this is on.", true);
            PaintBuildings = mod.BindSynced(Painting, "Paint Player Buildings", true,
                "Color the ground players have built on into the map itself, wood in one color and stone in another, " +
                "rather than putting a marker on it. A base is not a point and a marker cannot say how big it is. " +
                "The server sweeps the world for pieces a player raised -- the ruins and camps the world generated " +
                "are not touched, since only a player's piece carries a builder -- and tells everyone, so this is " +
                "the server's to switch on: off, it does not do the work and nobody sees it.");
            PaintRoads = mod.BindSynced(Painting, "Paint Paved Roads", true,
                "Color paved ground into the map, so the roads players have laid show up as roads. The server reads " +
                "this out of the world's own record of every patch of ground anybody has ever shaped, so a road is " +
                "on the map whether or not anyone has walked it since -- including the ones laid long before this " +
                "mod was installed. The server's.");
            PaintDirtPaths = mod.BindSynced(Painting, "Paint Dirt Paths", true,
                "Color the ground players have flattened with a hoe, which is how most trails between places get " +
                "made. Read the same way as paved road, from the world's record rather than from anyone walking it. " +
                "This covers a leveled yard as well as a path: both are ground somebody deliberately made flat. " +
                "Ground that went somewhere instead -- up into a wall, down into a pit -- is the other two " +
                "settings.");
            PaintFarmland = mod.BindSynced(Painting, "Paint Farmland", true,
                "Color cultivated ground into the map, so fields read as fields rather than as whatever grows on " +
                "them. This is the ground the cultivator turned over, not the crops standing in it -- the mod does " +
                "not mark planted crops at all.");
            PaintSharpMinimap = mod.BindLocal(Painting, "Sharp Minimap", true,
                "Draw the painting on the minimap at the resolution of what you are looking at, the way the " +
                "large map does, instead of at the map's own twelve meters to a pixel. The minimap shows a " +
                "small enough patch of world that twelve meters a pixel is a blur, so this is most of the " +
                "difference between a path and a smear. It is drawn to a window wider than the minimap shows " +
                "and panned across, so walking does not redraw it. Turn it off if you would rather spend " +
                "nothing at all on the minimap.");
            PaintOutlines = mod.BindLocal(Painting, "Draw Outlines", true,
                "Draw a hard line around the edge of each building, road and field instead of leaving it as a " +
                "block of color. Straightens the steps the grid leaves behind, so a wall looks like a wall. " +
                "Turn it off to see the plain coloring underneath.");
            PaintGroundVariation = mod.BindLocal(Painting, "Varied Ground Shades", 0.2f,
                "How much the shade of paths, roads and fields wanders from place to place, so that ground " +
                "reads as ground rather than as a sticker laid over the map. The game's own map does this -- " +
                "meadows are a dozen greens and not one -- and a single flat brown is most of what makes a " +
                "painted yard look pasted on. The pattern is fixed to the world, so it does not crawl when you " +
                "pan or zoom, and it is the same for everybody. 0 is one flat color, 0.35 is a gentle mottling, " +
                "and 1 is as varied as it goes. Buildings are left alone: a hall is made of something, and " +
                "dappling it only looks like dirt. The default is measured rather than chosen: real " +
                "ground in the game wanders about a seventh either side of its own brightness.");
            PaintDetailMeters = mod.BindSynced(Painting, "Paint Detail Meters", 1f,
                "Server only. How fine the painting is recorded, in meters. Smaller draws a truer shape -- a two " +
                "meter path comes out two meters wide rather than filling whatever square it touched -- and costs " +
                "more to send: the picture goes to every player whenever it changes, and there is a ceiling on how " +
                "large one message may be. Past that ceiling the server drops farmland, then dirt paths, and says " +
                "so in its log. Raise this if your world is large enough to hit it. One meter is the size of the " +
                "smallest floor piece, and the map is drawn from the shape of each piece rather than from a point " +
                "in it, so going finer than that sharpens where a wall sits rather than what is there. Half a " +
                "meter is as fine as it goes, and costs four times as many cells as one.");
            BuildSweepSeconds = mod.BindSynced(Painting, "Build Sweep Seconds", 120f,
                "Server only. How often the server walks the world for player-built pieces. This is the heaviest " +
                "sweep the mod does -- four hundred prefabs across every sector -- and a base does not move, so it is " +
                "spread over many frames and run rarely. Paths and paving are read in the same pass, so this is also " +
                "how long it can take for ground you have just hoed to reach the map.");
            PaintRaisedGround = mod.BindSynced(Painting, "Paint Raised Ground", false,
                "Whether ground you built up -- a terrace, an earth wall, a causeway across a marsh -- is drawn " +
                "as well as ground you deliberately made a path of. These are often the landmarks of a base, " +
                "which is why they get their own setting rather than sharing one with the holes. The server's, " +
                "since the server is the one looking.");
            PaintDugGround = mod.BindSynced(Painting, "Paint Dug Ground", false,
                "Whether ground you cut away -- a pit, a moat, a mining scrape -- is drawn. Mostly this is not " +
                "something anybody meant to draw: the game paints dirt under its own earthworks, so with this " +
                "on every swing that clipped the ground beside a copper vein comes out on the map as though it " +
                "were a road. A moat around a base is the exception. The server's, since the server is the one " +
                "looking.");
            PaintShapedLeastMeters = mod.BindSynced(Painting, "Shaped Ground Least Meters", 1f,
                "How far the ground has to have moved before it counts as an earthwork rather than as ground " +
                "somebody leveled. Below this it is drawn as a dirt path; above it, it is a wall or a hole and " +
                "only shows if Paint Raised Ground or Paint Dug Ground says so. Raise it if mining scrapes are " +
                "coming out as yards, lower it if a shallow terrace is.");
            PaintLeastPieces = mod.BindSynced(Painting, "Least Pieces To Draw", 6,
                "How much building has to stand in one place before the map draws it. Everything a player raises is " +
                "a piece, so without this the campfire somebody lit while exploring, the workbench put down to " +
                "repair a hammer and the lone torch on a hilltop all came out as buildings in the middle of " +
                "nowhere. A cell is drawn when this many pieces stand within twelve meters of it, so a hut counts " +
                "and a torch does not, and a wall running off the side of a base is not chopped off where the " +
                "pieces thin out. Counted over a distance rather than over cells, so Paint Detail Meters can change " +
                "without retuning it. 1 draws everything. The server's, since the server is the one looking.");
            ShowMapPaint = mod.BindRenamed(Painting, "Show Map Painting", "Show Player Developments", false,
                "EXPERIMENTAL, off by default. Color what the server shares into your own map -- large map, " +
                "minimap and the parchment alike, with fog and zoom handled for free. Off leaves the map exactly " +
                "as the game drew it. It is drawn at the map's own resolution of about twelve meters to a pixel, " +
                "so a path comes out wider and softer than it is; sharpening that is the part still being worked " +
                "on.");
            // Dark again, and this is the third value rather than a return to the first. The
            // original #704F2E was abandoned for reading as swamp, and that is a warning about
            // muted browns rather than about dark ones: the map's terrain is all low saturation,
            // so a color is mistaken for ground when it is washed out, not when it is deep. This
            // one is darker than the tan it replaces and far more saturated than anything the
            // terrain is painted in. Two retunes because there are two old defaults to carry.
            WoodColor = mod.Retune(mod.Retune(
                mod.BindMoved(Painting, Palette, "Wood Color", Colors.FromHex("#8A5524"),
                "The color wooden building is drawn in. " +
                "Hardwood and the rest of the timbers count as wood. A deep saturated brown: it reads as something " +
                "somebody built rather than as ground, which a paler or greyer brown does not."),
                Colors.FromHex("#704F2E"), Colors.FromHex("#8A5524")),
                Colors.FromHex("#C49A6C"), Colors.FromHex("#8A5524"));
            StoneColor = mod.BindMoved(Painting, Palette, "Stone Color", Colors.FromHex("#808085"),
                "The color stone building is drawn in. Marble, ashstone and ancient stone count as stone.");
            IronColor = mod.BindMoved(Painting, Palette, "Iron Color", Colors.FromHex("#555A66"),
                "The color iron building is drawn in -- the reinforced pieces, which usually trace the frame of " +
                "something large.");
            MarbleColor = mod.BindLocal(Palette, "Black Marble Color", Colors.FromHex("#121218"),
                "The color black marble building is drawn in. Not quite true black, so that a hall still reads as a " +
                "hall against dark water or a night map rather than as a hole in it; set it blacker if you would " +
                "rather it did.");
            PavedColor = mod.BindMoved(Painting, Palette, "Paved Color", Colors.FromHex("#9E998C"),
                "The color paved road is drawn in. Building wins where a road runs under it, so a courtyard reads as " +
                "the building it belongs to.");
            // Darker and less saturated than the ground it stands for, deliberately, and this
            // is the third value. #78603F gave way to #AB8042 because that one was measured off
            // real dirt -- and measuring was the mistake. Sampled from a screenshot, a wooden
            // floor is #B2878E and the dirt beside it #C9A471: the dirt is the brighter of the
            // two, so a palette faithful to the materials draws somebody's hall darker than the
            // yard around it. A map is a diagram and not a photograph. The ground belongs under
            // the things standing on it, which puts dirt below wood whatever the real thing does.
            DirtColor = mod.Retune(mod.Retune(
                mod.BindMoved(Painting, Palette, "Dirt Path Color", Colors.FromHex("#5E4A30"),
                "The color hoed dirt path and leveled ground are drawn in. Darker and less saturated than real " +
                "dirt, on purpose: ground on a map wants to sit under the buildings standing on it, and the real " +
                "thing is lighter than a wooden floor rather than darker. Paving wins where a path runs into a " +
                "road."),
                Colors.FromHex("#78603F"), Colors.FromHex("#5E4A30")),
                Colors.FromHex("#AB8042"), Colors.FromHex("#5E4A30"));
            FarmColor = mod.Retune(mod.BindMoved(Painting, Palette, "Farmland Color", Colors.FromHex("#4A3520"),
                "The color cultivated ground is drawn in. A dark brown, the color of turned earth. Both paving and " +
                "dirt path win over it, so a track across a field reads as a track."),
                Colors.FromHex("#615430"), Colors.FromHex("#4A3520"));
            PaintSpread = mod.BindRetuned(Painting, "Paint Spread", 1, 0,
                "How many map pixels around each painted one are painted too. A map pixel is about ten meters, which " +
                "is already wider than most of what is drawn on it, so each step up this multiplies what you see: 1 " +
                "makes every painted pixel a three by three block, about thirty meters across, and a four meter road " +
                "comes out as a thirty meter band with soft edges. 0 draws what is really there and nothing more, " +
                "which is the default; raise it only if you are reading the map at arm's length and want small " +
                "things to shout.");
            PaintOverWater = mod.BindLocal(Painting, "Paint Over Water", true,
                "Draw building and road that stand over water -- docks, bridges, a longhouse on stilts. The game " +
                "decides where the sea is from the map's heights rather than its colors, so these have to be lifted " +
                "a meter above the water line to be seen at all; that is a liberty with the map, and off leaves the " +
                "water alone and loses them.");

            ShowMapInHands = mod.BindLocal("Visuals", "Show Map In Hands", true,
                "Show a parchment map in the left hand and a pencil in the right while the pocket map is out (visible to other players too).");
            MapPose = mod.BindLocal("Visuals", "Map Pose", 0,
                "Animator 'crafting' state to play while the map is out (0 = none). 1 is the hand-crafting pose; experimental, may look odd while moving.");
            MapViewFraction = mod.BindLocal("Visuals", "Map View Fraction", 0.12f,
                "How much of the world map the parchment shows around you (fraction of the full map per side).");
            MapOffset = mod.BindLocal("Visuals", "Map Offset", "0,0.08,0.02", "Local position (x,y,z) of the parchment relative to the left hand bone.");
            MapRotation = mod.BindLocal("Visuals", "Map Rotation", "0,90,0", "Local rotation (euler x,y,z) of the parchment relative to the left hand bone.");
            MapScale = mod.BindLocal("Visuals", "Map Scale", "0.28,0.2,0.004", "Local scale (x,y,z) of the parchment.");
            PencilItem = mod.BindLocal("Visuals", "Pencil Item", "Club",
                "Vanilla item whose held model is shown, scaled down, as the pencil in the right hand. It is placed exactly the way " +
                "the game places that item when held, so it sits in the grip correctly. Any held item prefab works, e.g. Club, KnifeFlint, Torch.");
            PencilSize = mod.BindLocal("Visuals", "Pencil Size", "0.25,0.25,0.25", "Scale (x,y,z) applied to the pencil model.");
            PencilPositionTweak = mod.BindLocal("Visuals", "Pencil Position Tweak", "0,0,0", "Extra local offset (x,y,z) for the pencil on top of the vanilla hand placement.");
            PencilRotationTweak = mod.BindLocal("Visuals", "Pencil Rotation Tweak", "0,0,0", "Extra local rotation (euler x,y,z) for the pencil on top of the vanilla hand placement.");
        }

        internal static Vector3 ParseVector(string s, Vector3 fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            var parts = s.Split(',');
            // Two is as valid as three: a panel's size and place are written as "width,height",
            // and insisting on three quietly threw them away and handed back the fallback, so the
            // legend forgot where it was every time it opened.
            if (parts.Length != 2 && parts.Length != 3) return fallback;
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return fallback;
            if (parts.Length == 2) return new Vector3(x, y, 0f);
            if (float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                return new Vector3(x, y, z);
            return fallback;
        }
    }
}
