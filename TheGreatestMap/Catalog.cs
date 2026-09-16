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
    }

    /// <summary>Static facts about each category: defaults, labels.</summary>
    internal static class Categories
    {
        internal static readonly Category[] All = (Category[])Enum.GetValues(typeof(Category));

        internal static bool UsesPrefabList(Category c) => c != Category.Runestone && c != Category.Portal;

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
                default:                 return c.ToString();
            }
        }

        /// <summary>
        /// Kinds that are a supply of something rather than a place: they run out. A crossed-off
        /// marker means "cleared" for these, and "already searched" for a structure, which is why
        /// the two are shown and hidden separately.
        /// </summary>
        internal static bool IsResource(Category c)
        {
            return c == Category.Berries || c == Category.Mushrooms || c == Category.Herbs
                || c == Category.Ore || c == Category.Seeds || c == Category.Plants;
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
            { "SunkenCrypt4", "TrophyDraugr" }, { "MountainCave02", "TrophyCultist" }, { "TrollCave02", "TrophyFrostTroll" },
            { "BearCave", "TrophyBjorn" }, { "Mistlands_DvergrTownEntrance1", "TrophySeeker" }, { "Mistlands_DvergrTownEntrance2", "TrophySeeker" },
            { "Vendor_BlackForest", "Coins" }, { "Hildir_camp", "Coins" }, { "BogWitch_Camp", "Coins" },
            { "GoblinCamp2", "TrophyGoblin" }, { "Spawner_GreydwarfNest", "TrophyGreydwarfBrute" }, { "WoodVillage1", "TrophyDraugr" }, // WoodFarm1 is the abandoned farm: a structure, not a camp
            { "Eikthyrnir", "TrophyEikthyr" }, { "GDKing", "TrophyTheElder" }, { "Bonemass", "TrophyBonemass" },
            { "Dragonqueen", "TrophyDragonQueen" }, { "GoblinKing", "TrophyGoblinKing" },
            { "Mistlands_DvergrBossEntrance1", "TrophySeekerQueen" }, { "FaderLocation", "TrophyFader" },
        };

        internal static string KnownIcon(string prefab)
        {
            return !string.IsNullOrEmpty(prefab) && KnownIcons.TryGetValue(prefab, out var icon) ? icon : null;
        }

        /// <summary>Icon used when a thing's own item icon cannot be worked out. Item prefab name or pin:&lt;PinType&gt;.</summary>
        internal static string DefaultIcon(Category c)
        {
            switch (c)
            {
                case Category.Berries:   return "Raspberry";
                case Category.Mushrooms: return "Mushroom";
                case Category.Herbs:     return "Dandelion";
                case Category.Seeds:     return "CarrotSeeds";
                case Category.Plants:    return "Flax";
                case Category.Ore:       return "CopperOre";
                case Category.Dungeon:   return "pin:Icon3"; // nothing is "a crypt in general": a wrong picture is worse than a plain one
                case Category.Runestone: return "pin:Memorial";
                case Category.Trader:    return "Coins";
                case Category.Camp:      return "pin:Icon0";
                case Category.BossAltar: return "pin:Boss";
                case Category.Portal:    return "pin:Icon4";
                case Category.Structure: return "pin:Icon1";
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
                case Category.Ore:       return 80;
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
                case Category.Structure: return -1f; // the house icon says it all
                default:                 return 0f;  // traders (three share the coin icon), portals (the tag)
            }
        }

        internal static string DefaultPrefabs(Category c)
        {
            switch (c)
            {
                case Category.Berries:
                    return "RaspberryBush=Raspberries,BlueberryBush=Blueberries,CloudberryBush=Cloudberries";
                case Category.Mushrooms:
                    return "Pickable_Mushroom=Mushrooms,Pickable_Mushroom_yellow=Yellow Mushrooms,Pickable_Mushroom_blue=Blue Mushrooms," +
                           "Pickable_Mushroom_Magecap=Magecap,Pickable_Mushroom_JotunPuffs=Jotun Puffs,Pickable_SmokePuff=Smoke Puffs";
                case Category.Herbs:
                    return "Pickable_Thistle=Thistle,Pickable_Dandelion=Dandelion";
                case Category.Seeds:
                    return "Pickable_SeedCarrot=Carrot Seeds,Pickable_SeedTurnip=Turnip Seeds,Pickable_SeedOnion=Onion Seeds";
                case Category.Plants:
                    return "Pickable_Flax_Wild=Wild Flax,Pickable_Barley_Wild=Wild Barley,Pickable_Fiddlehead=Fiddlehead";
                case Category.Ore:
                    return "rock4_copper=Copper,MineRock_Tin=Tin,silvervein=Silver|SilverOre,MineRock_Obsidian=Obsidian,MineRock_Meteorite=Meteorite," +
                           "mudpile_beacon=Scrap Pile|IronScrap,mudpile2=Scrap Pile|IronScrap,mudpile=Scrap Pile|IronScrap,Pickable_Tar=Tar Pit," +
                           "giant_brain=Petrified Bone,giant_helmet1=Petrified Bone,giant_helmet2=Petrified Bone,giant_ribs=Petrified Bone," +
                           "giant_skull=Petrified Bone,giant_sword1=Petrified Bone,giant_sword2=Petrified Bone";
                case Category.Dungeon:
                    return "Crypt2=Burial Chambers|TrophySkeleton,Crypt3=Burial Chambers|TrophySkeleton,Crypt4=Burial Chambers|TrophySkeleton," +
                           "SunkenCrypt4=Sunken Crypt|TrophyDraugr,MountainCave02=Frost Cave|TrophyCultist,TrollCave02=Troll Cave|TrophyFrostTroll," +
                           "Mistlands_DvergrTownEntrance1=Infested Mine|TrophySeeker,Mistlands_DvergrTownEntrance2=Infested Mine|TrophySeeker," +
                           "BearCave=Bear Cave|TrophyBjorn";
                case Category.Trader:
                    return "Vendor_BlackForest=Haldor|Coins,Hildir_camp=Hildir|Coins,BogWitch_Camp=Bog Witch|Coins";
                case Category.Camp:
                    return "GoblinCamp2=Fuling Village|TrophyGoblin,WoodVillage1=Draugr Village|TrophyDraugr," +
                           "Spawner_GreydwarfNest=Greydwarf Nest|TrophyGreydwarfBrute";
                case Category.BossAltar:
                    return "Eikthyrnir=Eikthyr|TrophyEikthyr,GDKing=The Elder|TrophyTheElder,Bonemass=Bonemass|TrophyBonemass," +
                           "Dragonqueen=Moder|TrophyDragonQueen,GoblinKing=Yagluth|TrophyGoblinKing," +
                           "Mistlands_DvergrBossEntrance1=The Queen|TrophySeekerQueen,FaderLocation=Fader|TrophyFader";
                case Category.Structure:
                    return "WoodFarm1=Abandoned Farm,WoodHouse*=Abandoned House,AbandonedLogCabin*=Log Cabin,StoneTowerRuins*=Stone Tower Ruins,StoneHouse*=Stone House," +
                           "Ruin*=Ruins,SwampHut*=Swamp Hut,SwampRuin*=Swamp Ruins,SwampWell*=Swamp Well,StoneHenge*=Stonehenge,StoneTower*=Stone Tower," +
                           "StoneCircle*=Stone Circle,Dolmen*=Dolmen,MountainGrave*=Mountain Grave,MountainWell*=Mountain Well,DrakeNest*=Drake Nest," +
                           "Greydwarf_camp*=Greydwarf Nest,ShipSetting*=Ship Setting,Waymarker*=Waymarker,InfestedTree*=Infested Tree," +
                           "Mistlands_GuardTower*=Dvergr Guard Tower,Mistlands_Excavation*=Dvergr Excavation,Mistlands_Harbour*=Dvergr Harbour," +
                           "Mistlands_Lighthouse*=Dvergr Lighthouse,Mistlands_Giant*=Giant Remains,Mistlands_Swords*=Petrified Swords," +
                           "Mistlands_Statue*=Dvergr Statue,Mistlands_Viaduct*=Viaduct,CharredRuins*=Charred Ruins,AshlandRuins*=Ashlands Ruins," +
                           "FortressRuins*=Fortress Ruins,PlaceofMystery*=Place of Mystery";
                default:
                    return "";
            }
        }

        internal static string CatalogNote(Category c)
        {
            switch (c)
            {
                case Category.Dungeon:   return "Any location with an interior also counts as a dungeon even if it is not listed.";
                case Category.Ore:       return "Matches MineRock5, MineRock, Destructible and Pickable objects by prefab name.";
                case Category.Structure: return "Entries ending in * match by prefix. Unlisted outdoor locations also count when 'Structures Include Unlisted' is on.";
                default:                 return "";
            }
        }
    }

    /// <summary>Something the player has found: where it is and what marker it would produce.</summary>
    internal sealed class Found
    {
        public string Key;
        public Category Cat;
        public string Icon;
        public string Name;
        public Vector3 Pos;      // where the marker goes
        public Vector3 Center;   // for locations: the location origin (dedupe center)
        public float Radius;     // for locations: exterior radius (dedupe radius); 0 otherwise
        public long FoundAt;     // DateTime.UtcNow.Ticks when first (or last) seen

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
            CrossedOff.Forget();
        }

        private static void Rebuild()
        {
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
        internal sealed class LegendRow
        {
            public Category Cat;
            public string Prefab;
            public string Name;
            public string Icon;
            public string Source;
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
                    Missing = icon != null && icon.StartsWith("item:") && !ItemExists(icon.Substring(5)),
                    // A catalog line for something the game does not have is dead weight, and is
                    // invisible otherwise: onion seeds, for instance, come out of chests rather
                    // than the ground, so there may be nothing in the world to ever mark.
                    NoPrefab = !prefab.EndsWith("*") && ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) == null,
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
        internal static List<KeyValuePair<string, string>> DungeonTypes()
        {
            if (!_built) Rebuild();
            var list = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string prefab, Entry e)
            {
                if (e.Cat != Category.Dungeon || string.IsNullOrEmpty(prefab)) return;
                string icon = IconRegistry.Normalize(e.Icon ?? Categories.KnownIcon(prefab) ?? IconFromDungeonName(prefab));
                if (icon == null || !seen.Add(icon)) return;
                list.Add(new KeyValuePair<string, string>(icon, e.Name ?? Prettify(prefab)));
            }
            foreach (var kv in _byPrefab) Add(kv.Key, kv.Value);
            foreach (var kv in _byPrefix) Add(kv.Key, kv.Value);
            foreach (var kv in _builtInByPrefab) Add(kv.Key, kv.Value);
            foreach (var kv in _builtInByPrefix) Add(kv.Key, kv.Value);
            return list;
        }

        private static bool ItemExists(string name)
        {
            try { return ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(name) != null; }
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
        private static bool ClassifyDeposit(GameObject owner, string displayName, DropTable drops, out Found found)
        {
            found = null;
            string prefab = PrefabName(owner);
            string drop = FirstDrop(drops);
            if (Lookup(prefab, out var e))
            {
                string icon = e.Icon ?? Categories.KnownIcon(prefab) ?? drop;
                found = Make(e.Cat, e.Name ?? Loc(displayName, Prettify(prefab)), icon, owner.transform.position, KeyOf(owner, prefab));
                return true;
            }
            if (!WorthMining(drop)) return false;
            if (_unlistedDeposits.Add(prefab ?? ""))
                TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Deposit '{prefab}' is not in the catalog; marking it anyway because it yields {drop}.");
            found = Make(Category.Ore, Loc(displayName, Prettify(prefab)), drop, owner.transform.position, KeyOf(owner, prefab));
            return true;
        }

        /// <summary>True when a deposit gives something you would go there for, rather than rubble.</summary>
        private static bool WorthMining(string dropKey)
        {
            if (string.IsNullOrEmpty(dropKey)) return false;
            string name = dropKey.StartsWith("item:") ? dropKey.Substring(5) : dropKey;
            return !Rubble.Contains(name);
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
                    found = Make(e.Cat, e.Name ?? Loc(pickable.GetHoverName(), Prettify(prefab)), icon, pickable.transform.position, KeyOf(pickable.gameObject, prefab));
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
                    found = Make(e.Cat, e.Name ?? Prettify(prefab), icon, destructible.transform.position, KeyOf(destructible.gameObject, prefab));
                    return true;
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
        /// A location's marker: for structures it goes on the piece that was seen (the location
        /// origin of a farm is the middle of the yard, not the house); everything else sits on
        /// the origin. The location origin and radius are kept for deduplication either way.
        /// </summary>
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
            found = Make(cat, name, icon, cat == Category.Structure ? hitPos : center, key);
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

        private static Found Make(Category cat, string name, string icon, Vector3 pos, string key)
        {
            string resolved = IconRegistry.Normalize(icon) ?? FallbackIcon(cat);
            return new Found { Key = key, Cat = cat, Icon = resolved, Name = name ?? "", Pos = pos };
        }

        // What a deposit yields besides the thing you went there for. A copper vein drops stone as
        // well as ore, and the stone tends to come first in its table, so taking the first drop
        // labelled a copper deposit with a lump of stone.
        private static readonly HashSet<string> Rubble = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Stone", "Wood", "RoundLog", "FineWood", "Resin", "Flint",
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
