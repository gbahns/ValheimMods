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

        /// <summary>Structures are a personal habit (tracking which ruins you have searched); off unless asked for.</summary>
        internal static bool DefaultEnabled(Category c) => c != Category.Structure;

        // Icons for well-known locations, kept in code so that a catalog line saved by an older
        // version (without |Icon overrides) still gets the right picture.
        private static readonly Dictionary<string, string> KnownIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Crypt2", "TrophySkeleton" }, { "Crypt3", "TrophySkeleton" }, { "Crypt4", "TrophySkeleton" },
            { "SunkenCrypt4", "CryptKey" }, { "MountainCave02", "TrophyUlv" }, { "TrollCave02", "TrophyFrostTroll" },
            { "Mistlands_DvergrTownEntrance1", "TrophySeeker" }, { "Mistlands_DvergrTownEntrance2", "TrophySeeker" },
            { "Vendor_BlackForest", "Coins" }, { "Hildir_camp", "Coins" }, { "BogWitch_Camp", "Coins" },
            { "GoblinCamp2", "TrophyGoblin" }, { "WoodVillage1", "TrophyDraugr" }, // WoodFarm1 is the abandoned farm: a structure, not a camp
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
                case Category.Ore:       return "CopperOre";
                case Category.Dungeon:   return "CryptKey";
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
                    return "Pickable_Thistle=Thistle,Pickable_Dandelion=Dandelion,Pickable_Flax_Wild=Wild Flax,Pickable_Barley_Wild=Wild Barley," +
                           "Pickable_SeedCarrot=Carrot Seeds,Pickable_SeedTurnip=Turnip Seeds,Pickable_SeedOnion=Onion Seeds,Pickable_Fiddlehead=Fiddlehead";
                case Category.Ore:
                    return "rock4_copper=Copper,MineRock_Tin=Tin,silvervein=Silver,MineRock_Obsidian=Obsidian,MineRock_Meteorite=Meteorite," +
                           "mudpile_beacon=Scrap Pile,mudpile2=Scrap Pile,mudpile=Scrap Pile,Pickable_Tar=Tar Pit," +
                           "giant_brain=Petrified Bone,giant_helmet1=Petrified Bone,giant_helmet2=Petrified Bone,giant_ribs=Petrified Bone," +
                           "giant_skull=Petrified Bone,giant_sword1=Petrified Bone,giant_sword2=Petrified Bone";
                case Category.Dungeon:
                    return "Crypt2=Burial Chambers|TrophySkeleton,Crypt3=Burial Chambers|TrophySkeleton,Crypt4=Burial Chambers|TrophySkeleton," +
                           "SunkenCrypt4=Sunken Crypt|CryptKey,MountainCave02=Frost Cave|TrophyUlv,TrollCave02=Troll Cave|TrophyFrostTroll," +
                           "Mistlands_DvergrTownEntrance1=Infested Mine|TrophySeeker,Mistlands_DvergrTownEntrance2=Infested Mine|TrophySeeker";
                case Category.Trader:
                    return "Vendor_BlackForest=Haldor|Coins,Hildir_camp=Hildir|Coins,BogWitch_Camp=Bog Witch|Coins";
                case Category.Camp:
                    return "GoblinCamp2=Fuling Village|TrophyGoblin,WoodVillage1=Draugr Village|TrophyDraugr";
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

        private static readonly Dictionary<string, Entry> _byPrefab = new Dictionary<string, Entry>();
        private static readonly List<KeyValuePair<string, Entry>> _byPrefix = new List<KeyValuePair<string, Entry>>();
        private static string[] _structureExclusions = new string[0];
        private static bool _built;

        internal static void Invalidate() => _built = false;

        private static void Rebuild()
        {
            _byPrefab.Clear();
            _byPrefix.Clear();
            foreach (var cat in Categories.All)
            {
                if (!TgmConfig.CategoryPrefabs.TryGetValue(cat, out var entry)) continue;
                foreach (var raw in (entry.Value ?? "").Split(','))
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
                        if (prefix.Length > 0) _byPrefix.Add(new KeyValuePair<string, Entry>(prefix, e));
                    }
                    else
                    {
                        _byPrefab[prefab] = e;
                    }
                }
            }
            // Longest prefix wins when several match.
            _byPrefix.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            var exclusions = new List<string>();
            foreach (var raw in (TgmConfig.StructuresExcludePrefixes.Value ?? "").Split(','))
            {
                string s = raw.Trim();
                if (s.Length > 0) exclusions.Add(s);
            }
            _structureExclusions = exclusions.ToArray();
            _built = true;
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
            return false;
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
                string prefab = Utils.GetPrefabName(pickable.gameObject);
                if (Lookup(prefab, out var e))
                {
                    string icon = e.Icon ?? Categories.KnownIcon(prefab) ?? IconRegistry.ItemKey(pickable.m_itemPrefab);
                    found = Make(e.Cat, e.Name ?? Loc(pickable.GetHoverName(), Prettify(prefab)), icon, pickable.transform.position, KeyOf(pickable.gameObject, prefab));
                    return true;
                }
            }

            var rock5 = go.GetComponentInParent<MineRock5>();
            if (rock5 != null)
            {
                string prefab = Utils.GetPrefabName(rock5.gameObject);
                if (Lookup(prefab, out var e))
                {
                    string icon = e.Icon ?? Categories.KnownIcon(prefab) ?? FirstDrop(rock5.m_dropItems);
                    found = Make(e.Cat, e.Name ?? Loc(rock5.m_name, Prettify(prefab)), icon, rock5.transform.position, KeyOf(rock5.gameObject, prefab));
                    return true;
                }
            }

            var rock = go.GetComponentInParent<MineRock>();
            if (rock != null)
            {
                string prefab = Utils.GetPrefabName(rock.gameObject);
                if (Lookup(prefab, out var e))
                {
                    string icon = e.Icon ?? Categories.KnownIcon(prefab) ?? FirstDrop(rock.m_dropItems);
                    found = Make(e.Cat, e.Name ?? Loc(rock.m_name, Prettify(prefab)), icon, rock.transform.position, KeyOf(rock.gameObject, prefab));
                    return true;
                }
            }

            var destructible = go.GetComponentInParent<Destructible>();
            if (destructible != null)
            {
                string prefab = Utils.GetPrefabName(destructible.gameObject);
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
            if (location != null && ClassifyLocation(Utils.GetPrefabName(location.gameObject), location.m_hasInterior,
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
            }
            else if (hasInterior)
            {
                cat = Category.Dungeon;
                name = Prettify(prefab);
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

        private static string FirstDrop(DropTable table)
        {
            if (table == null || table.m_drops == null) return null;
            foreach (var drop in table.m_drops)
                if (drop.m_item != null) return IconRegistry.ItemKey(drop.m_item);
            return null;
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
