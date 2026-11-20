using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Marker icons travel with each shared marker as a key: "item:Dandelion" (that item's
    /// inventory icon), "piece:fire_pit" (a building piece's build-menu picture) or "pin:Boss"
    /// (a vanilla pin sprite). Each client maps a key to a local
    /// pin type on first use, starting at 100, and registers the sprite with the minimap, so
    /// every client draws the same picture even if it met the keys in a different order.
    ///
    /// <para>Every key, "pin:" included: a key borrows the game's picture and never the game's
    /// pin type. See <see cref="TypeFor"/> for what went wrong while it borrowed both.</para>
    /// </summary>
    internal static class IconRegistry
    {
        internal const int Base = 100;

        /// <summary>
        /// Types from here up belong to other mods and are never allocated here. TheGreatestPortal
        /// draws its portal pins at 1000, and a pin type is only an int, so two mods can silently
        /// pick the same one: registering an icon for a type removes any existing entry for it,
        /// and the checks below match pins by type. Counting up from 100, one type per distinct
        /// icon key, this would need a map that had recorded 900 kinds of thing to reach the
        /// floor - so in practice the ceiling costs nothing and the collision cannot happen.
        /// </summary>
        internal const int ReservedFloor = 1000;

        internal const string FallbackKey = "pin:Icon3";

        private static readonly Dictionary<string, int> _typeByKey = new Dictionary<string, int>();
        private static readonly List<string> _keys = new List<string>();
        private static bool _warnedFull;

        internal static int MaxType => Base + _keys.Count - 1;

        /// <summary>"Dandelion" → "item:Dandelion"; "pin:boss" → "pin:boss"; blank → null.</summary>
        internal static string Normalize(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            key = key.Trim();
            if (key.Length == 0) return null;
            if (key.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) return "item:" + key.Substring(5).Trim();
            if (key.StartsWith("pin:", StringComparison.OrdinalIgnoreCase)) return "pin:" + key.Substring(4).Trim();
            if (key.StartsWith("like:", StringComparison.OrdinalIgnoreCase)) return "like:" + key.Substring(5).Trim();
            if (key.StartsWith("piece:", StringComparison.OrdinalIgnoreCase)) return "piece:" + key.Substring(6).Trim();
            if (key.StartsWith("drawn:", StringComparison.OrdinalIgnoreCase)) return "drawn:" + key.Substring(6).Trim();
            // A location's own map picture, which is what the game draws for the landmarks it
            // marks: the one place a row can borrow a picture the player is already reading.
            if (key.StartsWith("loc:", StringComparison.OrdinalIgnoreCase)) return "loc:" + key.Substring(4).Trim();
            return "item:" + key;
        }

        /// <summary>
        /// The sizes as the mod ships them: an icon drawn against the others at a percentage of
        /// normal, where the game's own artwork is out of step with the rest.
        ///
        /// <para>Every marker is drawn into a box of one size, so how big an icon looks is how
        /// much of its own frame the drawing fills -- which is the artist's business and varies.
        /// The draugr trophy fills its frame to the edges where the skeleton trophy sits inside a
        /// margin, so two markers at the same size look like two sizes. Nothing can be measured
        /// here: a sprite's alpha is not readable at runtime, so this is a list of the ones
        /// somebody has looked at and judged.</para>
        /// </summary>
        internal const string DefaultSizes = "TrophyDraugr=80";

        private static Dictionary<string, float> _sizes;
        private static string _sizesFrom;

        /// <summary>
        /// How big this icon is drawn against the others, as a fraction of what its kind's size
        /// setting says. One for everything that has not been singled out.
        /// </summary>
        internal static float SizeOf(string iconKey)
        {
            string line = TgmConfig.IconSizes != null ? TgmConfig.IconSizes.Value : DefaultSizes;
            if (_sizes == null || !string.Equals(line, _sizesFrom, StringComparison.Ordinal)) ParseSizes(line);
            // The usual case, and this is asked per marker on every map move: nothing singled
            // out means nothing to work out.
            if (_sizes.Count == 0) return 1f;
            string key = Normalize(iconKey);
            return key != null && _sizes.TryGetValue(key, out float size) ? size : 1f;
        }

        private static void ParseSizes(string line)
        {
            _sizes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            _sizesFrom = line;
            if (string.IsNullOrEmpty(line)) return;
            foreach (var raw in line.Split(','))
            {
                string one = raw.Trim();
                int eq = one.IndexOf('=');
                if (eq <= 0) continue;
                string key = Normalize(one.Substring(0, eq));
                if (key == null) continue;
                if (!float.TryParse(one.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float percent)) continue;
                _sizes[key] = Mathf.Clamp(percent, 20f, 200f) / 100f;
            }
        }

        internal static bool SameKey(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The game's own pin type named by a "pin:" key, where the key names one at all.
        ///
        /// <para><b>Names only, and only ones that exist.</b> Enum.TryParse takes a bare number as
        /// readily as a name and never checks the result is a value the enum defines, so on its
        /// own it made "pin:" an alias for every integer: "pin:12" parsed to Ping and "pin:137"
        /// to a type that exists nowhere. The game picks a pin's picture from its type number
        /// alone -- once, in AddPin, never revisited -- so a number arriving in this channel does
        /// not draw a broken marker, it draws a perfectly good picture of the wrong thing,
        /// standing exactly where the marker belongs. Ping is 12, in the middle of the range, and
        /// a ping circle is the one wrong answer a player is certain to notice.</para>
        ///
        /// <para>Numbers reach here by being printed out as well as read in: Enum.ToString on an
        /// undefined value gives back the number, so anything that built a key from a type it had
        /// not checked -- see <see cref="KeyForVanilla"/> -- sent a number out that this would
        /// have taken back as a vanilla type.</para>
        /// </summary>
        private static bool TryVanilla(string normalized, out Minimap.PinType type)
        {
            type = Minimap.PinType.Icon3;
            if (normalized == null || !normalized.StartsWith("pin:")) return false;
            string name = normalized.Substring(4);
            if (name.Length == 0 || char.IsDigit(name[0]) || name[0] == '-' || name[0] == '+') return false;
            return Enum.TryParse(name, true, out type) && Enum.IsDefined(typeof(Minimap.PinType), type);
        }

        /// <summary>
        /// The game's own pin type whose picture this key wears, "pin:" and "like:" alike.
        ///
        /// <para>What a key borrows, rather than what it is drawn as -- which are the same thing
        /// for the game's pins and no longer the same thing here. Two callers need it: the sweep
        /// that recognizes the copies older versions left in the character file wearing the
        /// borrowed number, and the boss label switch, which used to read the number off the pin
        /// because an altar of this mod's was a vanilla boss pin.</para>
        /// </summary>
        internal static bool BorrowedVanillaType(string iconKey, out int type)
        {
            type = (int)Minimap.PinType.Icon3;
            string key = Normalize(iconKey);
            if (key == null) return false;
            if (key.StartsWith("like:")) key = "pin:" + key.Substring(5);
            if (!key.StartsWith("pin:") || !TryVanilla(key, out var vanilla)) return false;
            type = (int)vanilla;
            return true;
        }

        private static readonly HashSet<string> _warnedPins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Say so, once per key, when a "pin:" key names nothing. Silence here is what would let a
        /// whole map draw as the wrong picture with nothing anywhere saying why.
        /// </summary>
        private static void WarnBadPin(string key)
        {
            if (!_warnedPins.Add(key)) return;
            TheGreatestMapMod.Log.LogWarning(
                $"[TheGreatestMap] Icon key '{key}' names none of the game's pin types; drawing those markers " +
                "with the default pin. A number here is the likely cause -- pin keys are names, such as pin:Icon3.");
        }

        /// <summary>
        /// The local pin type for an icon key, allocating and registering it on first use.
        ///
        /// <para><b>A "pin:" key borrows the game's picture and not the game's pin type.</b> It
        /// used to borrow both, and a marker drawn that way was not a marker wearing a vanilla
        /// picture -- it was a vanilla pin. Six whole kinds went out as one: runestones as
        /// Memorial, structures as Icon1, camps as Icon0, boss altars as Boss, portals as Icon4,
        /// and anything with no icon of its own as Icon3.</para>
        ///
        /// <para>Nothing could tell such a pin from one the player dropped by hand, and this mod
        /// least of all. Its own safety nets ask the type: the guard that keeps markers out of
        /// the character file, the guard that keeps them off a cartography table, the login sweep
        /// that clears the ones an older version left behind, the status line, the stray watch,
        /// tgm_clearlocal. Every one of them is blind to a vanilla number, so a copy that reached
        /// the character file stayed there, was written again at the next save, and came back
        /// every login wearing this mod's own words -- "Memorial", "House", "Dot" -- as a pin
        /// with no record behind it that no switch of this mod's could hide and no sweep could
        /// find. It travelled, too: the table guard let it through to every other player's map.
        /// One player reached 744 memorials and 312 "placed" pins against under twenty he had
        /// placed, deleted them by hand, and had them all back at the next login.</para>
        ///
        /// <para>A number of this mod's own making costs nothing -- the picture is the same, and
        /// "like:" has done exactly this for the icons a player has to be able to place -- and it
        /// makes a leak visible to every one of those nets instead of none of them.</para>
        /// </summary>
        internal static int TypeFor(string iconKey)
        {
            string key = Normalize(iconKey);
            if (key == null) return TypeFor(FallbackKey);
            if (key.StartsWith("pin:"))
            {
                if (!TryVanilla(key, out _))
                {
                    WarnBadPin(key);
                    key = "like:" + FallbackKey.Substring(4);
                }
                else key = "like:" + key.Substring(4);
            }
            if (_typeByKey.TryGetValue(key, out int type)) return type;
            type = Base + _keys.Count;
            if (type >= ReservedFloor)
            {
                // Nothing sensible is left to allocate, and taking a reserved number would break
                // another mod's pins rather than only this marker. Draw it with the fallback pin.
                if (!_warnedFull)
                {
                    _warnedFull = true;
                    TheGreatestMapMod.Log.LogWarning(
                        $"[TheGreatestMap] {_keys.Count} icon types in use, which reaches the {ReservedFloor} reserved for other mods; " +
                        "further icons will be drawn with the default pin.");
                }
                return (int)Minimap.PinType.Icon3;
            }
            _keys.Add(key);
            _typeByKey[key] = type;
            Register(Minimap.instance, key, type);
            return type;
        }

        /// <summary>
        /// The "pin:" key for one of the game's own pin types. A type the enum does not define
        /// gets the fallback rather than a key: ToString would print the bare number, and that
        /// number reads back as a vanilla type -- which is how a type of ours, or a stale one out
        /// of old stored data, could come back wearing the game's picture for whatever that
        /// number happens to mean.
        /// </summary>
        internal static string KeyForVanilla(int type) =>
            Enum.IsDefined(typeof(Minimap.PinType), type)
                ? "pin:" + ((Minimap.PinType)type).ToString()
                : FallbackKey;

        /// <summary>A readable name for an icon key: the item's translated name, or what the vanilla pin icon shows.</summary>
        internal static string DisplayName(string iconKey)
        {
            string key = Normalize(iconKey);
            if (key == null) return "marker";
            string raw = key.Substring(key.IndexOf(':') + 1);
            if (key.StartsWith("pin:"))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "icon0": return "Fire";
                    case "icon1": return "House";
                    case "icon2": return "Hammer";
                    case "icon3": return "Dot";
                    case "icon4": return "Portal";
                    default: return raw;
                }
            }
            if (key.StartsWith("piece:"))
            {
                var piece = PieceOf(raw);
                if (piece != null && !string.IsNullOrEmpty(piece.m_name) && Localization.instance != null)
                {
                    string localized = Localization.instance.Localize(piece.m_name);
                    if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("[")) return localized;
                }
                return raw;
            }
            try
            {
                var prefab = ItemPrefab(raw);
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string name = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
                if (!string.IsNullOrEmpty(name) && Localization.instance != null)
                {
                    string localized = Localization.instance.Localize(name);
                    if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("[")) return localized;
                }
            }
            catch (Exception) { }
            return raw;
        }

        /// <summary>
        /// The first of these item prefabs the game actually has, as an icon key. Valheim adds
        /// items between updates and not every creature has a trophy, so a guess like the bear's
        /// is written as a preference with a fallback rather than a single name that may resolve
        /// to nothing and leave a plain dot.
        /// </summary>
        internal static string PickItem(params string[] names)
        {
            if (ObjectDB.instance == null || names == null) return null;
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    var prefab = ItemPrefab(name);
                    if (prefab != null && prefab.GetComponent<ItemDrop>() != null) return "item:" + name;
                }
                catch (Exception) { }
            }
            return null;
        }

        private static readonly Dictionary<string, string> _likeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ask the game which item it actually has, instead of guessing its name. Valheim's naming
        /// is not predictable enough to hard-code: the bear has no "TrophyBear", and the strings
        /// that look like drops in the asset files turn out to be sounds and meshes. So the item
        /// database is searched for one whose prefab name contains the word, preferring a trophy
        /// and then the shortest name, which is usually the plain item rather than a variant.
        /// What it settles on is logged once, so the right name can be written into the catalog.
        /// </summary>
        internal static string PickItemLike(string contains)
        {
            if (string.IsNullOrEmpty(contains) || ObjectDB.instance == null) return null;
            if (_likeCache.TryGetValue(contains, out var cached)) return cached;
            string best = null;
            int bestRank = -1;
            try
            {
                foreach (var prefab in ObjectDB.instance.m_items)
                {
                    if (prefab == null) continue;
                    var drop = prefab.GetComponent<ItemDrop>();
                    if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                    // Beards and hair are items too, and a beard is not a bear.
                    if (drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Customization) continue;
                    string name = Utils.GetPrefabName(prefab);
                    if (!NameContainsWord(name, contains)) continue;
                    if (!HasIcon(drop)) continue;   // an item with no usable icon would draw as a dot
                    int rank = Rank(name, contains);
                    if (best == null || rank > bestRank
                        || (rank == bestRank && name.Length < best.Length))
                    {
                        best = name;
                        bestRank = rank;
                    }
                }
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not search the item database for '{contains}': {e.Message}");
            }
            string key = best != null ? "item:" + best : null;
            _likeCache[contains] = key;
            TheGreatestMapMod.Log.LogInfo(best != null
                ? $"[TheGreatestMap] Icon for '{contains}': using the item '{best}'."
                : $"[TheGreatestMap] No item matching '{contains}' in the game; that marker gets a plain dot.");
            return key;
        }

        /// <summary>
        /// A trophy of the creature beats one of its drops, and a drop named after it ("BearHide")
        /// beats something that merely mentions it ("PulledBear", a meal). Ties go to the shorter
        /// name, which is usually the plain item rather than a variant.
        /// </summary>
        private static int Rank(string name, string word)
        {
            if (name.StartsWith("Trophy", StringComparison.OrdinalIgnoreCase)) return 2;
            if (name.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        /// <summary>
        /// "Bear" is in "BearHide" and "TrophyBear" but not in "Beard1". Prefab names are written
        /// in PascalCase, so a match counts only where the word starts one (or starts the name) and
        /// is not run on by more lowercase letters.
        /// </summary>
        private static bool NameContainsWord(string name, string word)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int at = 0;
            while ((at = name.IndexOf(word, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool startsWord = at == 0 || char.IsUpper(name[at]);
                int after = at + word.Length;
                bool endsWord = after >= name.Length || !char.IsLower(name[after]);
                if (startsWord && endsWord) return true;
                at++;
            }
            return false;
        }

        /// <summary>An item whose icon cannot be read is no use: it would draw as a plain dot anyway.</summary>
        private static bool HasIcon(ItemDrop drop)
        {
            try { return drop.m_itemData.GetIcon() != null; }
            catch (Exception) { return false; }
        }

        /// <summary>Every item whose name contains the word, for choosing an icon by eye.</summary>
        /// <summary>
        /// Does this thing answer to that word -- by its prefab id, or by the name a player
        /// knows it as?
        ///
        /// <para>The id was the whole of the search, which asks a player to already know the
        /// answer. Nobody calls the circlet HelmetDverger or the meadows farmstead WoodFarm1, and
        /// searching for "extractor" came back with nothing at all while the thing it names is
        /// standing in the game -- filed under a prefab with a different word in it. The id is
        /// what a setting wants typed, so it is still what gets printed; it is no use as the
        /// thing you have to type to find it.</para>
        /// </summary>
        private static bool Answers(string prefab, string shownName, string contains)
        {
            if (!string.IsNullOrEmpty(prefab) && prefab.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (string.IsNullOrEmpty(shownName)) return false;
            string shown = shownName;
            // A token, not a name, until it is localized -- "$piece_sapcollector".
            if (shown.StartsWith("$") && Localization.instance != null)
            {
                string said = Localization.instance.Localize(shown);
                if (!string.IsNullOrEmpty(said) && !said.StartsWith("[")) shown = said;
            }
            return shown.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static List<string> ItemsLike(string contains)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(contains) || ObjectDB.instance == null) return found;
            try
            {
                foreach (var prefab in ObjectDB.instance.m_items)
                {
                    if (prefab == null) continue;
                    var drop = prefab.GetComponent<ItemDrop>();
                    if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                    string name = Utils.GetPrefabName(prefab);
                    if (!Answers(name, drop.m_itemData.m_shared.m_name, contains)) continue;
                    string note = drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Customization ? " (customization, skipped)"
                        : HasIcon(drop) ? "" : " (no icon, skipped)";
                    found.Add(name + note);
                }
            }
            catch (Exception) { }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        /// <summary>
        /// Every picture the game has whose name contains a word, in all four places a picture
        /// can come from, each written as the icon key that would use it.
        ///
        /// <para>Items were the whole of this, which answered the question "what does this thing
        /// give me" and not the question actually being asked, which is "has the game drawn this
        /// thing". A sunken crypt gives iron scrap and a muddy scrap pile gives iron scrap, and
        /// neither of them is iron scrap -- so the other three places are worth asking: a build
        /// piece carries a rendered picture of itself, the map carries a picture for each of the
        /// locations it marks, and it carries its own pin sprites. A search over all four is how
        /// you find out that the game has no picture of a thing, which is the answer that sends
        /// you to drawn: instead.</para>
        /// </summary>
        internal static List<string> PicturesLike(string contains)
        {
            var found = ItemsLike(contains);
            if (string.IsNullOrEmpty(contains)) return found;

            // Build pieces: the build menu's own picture of the piece, which is a render of the
            // thing itself rather than of what it gives.
            try
            {
                var scene = ZNetScene.instance;
                if (scene != null && scene.m_prefabs != null)
                {
                    var pieces = new List<string>();
                    foreach (var prefab in scene.m_prefabs)
                    {
                        if (prefab == null) continue;
                        string name = Utils.GetPrefabName(prefab);
                        var piece = prefab.GetComponent<Piece>();
                        if (piece == null || piece.m_icon == null) continue;
                        if (!Answers(name, piece.m_name, contains)) continue;
                        pieces.Add("piece:" + name);
                    }
                    pieces.Sort(StringComparer.OrdinalIgnoreCase);
                    found.AddRange(pieces);
                }
            }
            catch (Exception) { }

            var map = Minimap.instance;
            if (map == null) return found;

            // The pictures the map keeps for the world's own landmarks, by location name.
            try
            {
                var spots = new List<string>();
                foreach (var data in map.m_locationIcons)
                {
                    if (data.m_icon == null || data.m_name == null) continue;
                    if (data.m_name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    spots.Add("loc:" + data.m_name);
                }
                spots.Sort(StringComparer.OrdinalIgnoreCase);
                found.AddRange(spots);
            }
            catch (Exception) { }

            // And the map's own pin sprites, which are named for what they are rather than for
            // what they show: Icon3 is the plain dot.
            try
            {
                var pins = new List<string>();
                foreach (var data in map.m_icons)
                {
                    if (data.m_icon == null) continue;
                    string name = data.m_name.ToString();
                    if (name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    pins.Add("pin:" + name);
                }
                pins.Sort(StringComparer.OrdinalIgnoreCase);
                found.AddRange(pins);
            }
            catch (Exception) { }
            return found;
        }

        internal static void ForgetPicks() => _likeCache.Clear();

        internal static string ItemKey(GameObject itemPrefab)
        {
            return itemPrefab == null ? null : "item:" + Utils.GetPrefabName(itemPrefab);
        }

        /// <summary>Icon for markers stored before icons were keyed (format 1).</summary>
        internal static string LegacyKey(int type)
        {
            switch (type)
            {
                case 100: return "item:Raspberry";
                case 101: return "item:Mushroom";
                case 102: return "item:Thistle";
                case 103: return "item:CopperOre";
                case 104: return "item:CryptKey";
                case 105: return "pin:Memorial";
                case 106: return "item:Coins";
                case 107: return "pin:Icon0";
                default:  return type < Base ? KeyForVanilla(type) : FallbackKey;
            }
        }

        /// <summary>
        /// A new Minimap instance (world load): re-register every icon met so far.
        ///
        /// <para><b>This is the one moment every picture on the map can be decided wrongly at
        /// once.</b> The key list is static and outlives a world, so the second world load of a
        /// session -- a relog, or leaving to the menu and rejoining -- re-registers every icon met
        /// in the first. An "item:" key resolves to nothing while ObjectDB is not up yet, and
        /// <see cref="Register"/> caches that miss as the plain dot for the rest of the session,
        /// because nothing ever resolves a key a second time. Every berry, ore and seed marker
        /// would then wear one wrong picture until the game was restarted, with the stored markers
        /// and the settings both perfectly intact -- which is a thing no player could report in a
        /// way anyone could act on. Hence the line below: whether the list was ready is the fact
        /// that tells this apart from a map that simply looks wrong.</para>
        /// </summary>
        internal static void OnMinimapStart(Minimap map)
        {
            // Only worth saying when there is something to lose. On the first load the list is
            // empty, every icon is resolved later by TypeFor as its marker arrives, and by then
            // the game is up -- so the silence here is the healthy case, not a missing check.
            if (_keys.Count > 0)
            {
                int items = 0;
                for (int i = 0; i < _keys.Count; i++)
                    if (_keys[i].StartsWith("item:")) items++;
                bool ready = ObjectDB.instance != null;
                if (!ready && items > 0)
                    TheGreatestMapMod.Log.LogWarning(
                        $"[TheGreatestMap] Drawing a new map: registering {_keys.Count} icon(s) again while the " +
                        $"game's item list is not loaded yet, and {items} of them name an item. Those will fall " +
                        "back to the plain dot and keep it for the rest of this session, so every marker of those " +
                        "kinds will wear the same wrong picture until the game is restarted. Nothing is wrong with " +
                        "the saved markers or the settings.");
                else
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Drawing a new map: registering {_keys.Count} icon(s) again; the game's " +
                        (ready ? "item list is loaded." : "item list is not loaded, but no icon needs it."));
            }
            for (int i = 0; i < _keys.Count; i++) Register(map, _keys[i], Base + i);
            EnsureArrays(map);
        }

        private static void Register(Minimap map, string key, int type)
        {
            if (map == null) return;
            Sprite sprite = Resolve(map, key);
            if (sprite == null)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] No icon for '{key}'; using the plain dot.");
                sprite = Resolve(map, FallbackKey);
            }
            map.m_icons.RemoveAll(x => (int)x.m_name == type);
            map.m_icons.Add(new Minimap.SpriteData { m_name = (Minimap.PinType)type, m_icon = sprite });
            EnsureArrays(map);
        }

        internal static Sprite Resolve(Minimap map, string key)
        {
            key = Normalize(key);
            if (key == null || map == null) return null;
            if (key.StartsWith("pin:") || key.StartsWith("like:"))
            {
                // "like:" wears a vanilla picture on a pin type of our own. "pin:" is the vanilla
                // type itself, which is right for a picture the game already uses for that
                // meaning -- and wrong whenever a player has to be able to place one, because
                // the game only lets its own five numbered pins be placed by hand. A marker
                // armed as a vanilla type is dropped as a plain vanilla pin and never adopted:
                // it draws, it keeps its label, and it is on nobody else's map. The player
                // marker did exactly that until this existed.
                string want = key.StartsWith("like:") ? "pin:" + key.Substring(5) : key;
                if (!TryVanilla(want, out var vanilla)) return null;
                foreach (var data in map.m_icons)
                    if (data.m_name == vanilla) return data.m_icon;
                return null;
            }
            if (key.StartsWith("piece:"))
            {
                var piece = PieceOf(key.Substring(6));
                return piece != null ? piece.m_icon : null;
            }
            // One of this mod's own pictures, for a kind the game has no single picture of.
            if (key.StartsWith("drawn:")) return MenuKit.Drawn(key.Substring(6));
            // The picture the game draws for one of the world's locations. Not every location has
            // one -- only the ones it marks -- and a name it does not know gives null, which the
            // caller takes as "use the default pin".
            if (key.StartsWith("loc:"))
            {
                string where = key.Substring(4);
                foreach (var data in map.m_locationIcons)
                    if (string.Equals(data.m_name, where, StringComparison.OrdinalIgnoreCase)) return data.m_icon;
                return null;
            }
            try
            {
                if (ObjectDB.instance == null) return null;
                var prefab = ItemPrefab(key.Substring(5));
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                return drop != null && drop.m_itemData != null ? drop.m_itemData.GetIcon() : null;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not load icon '{key}': {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// An icon key as a player would type it: a plain item name has no prefix, the other forms
        /// keep theirs. This is what the legend shows and what its editor writes back, so what is
        /// read off the screen can be typed in again.
        /// </summary>
        internal static string Short(string iconKey)
        {
            string key = Normalize(iconKey);
            if (key == null) return "";
            return key.StartsWith("item:") ? key.Substring(5) : key;
        }

        private static Dictionary<string, GameObject> _itemsByName;
        private static int _itemsCounted = -1;

        /// <summary>
        /// An item prefab by name, allowing for the case being wrong. The game hashes the name, so
        /// its own lookup is exact, and an icon typed as "Softtissue" rather than "SoftTissue" then
        /// resolves to nothing and falls back to the kind's own picture -- which looks like the
        /// icon being wrong rather than the spelling. Built once from the item list and rebuilt if
        /// that list changes.
        /// </summary>
        internal static GameObject ItemPrefab(string name)
        {
            if (string.IsNullOrEmpty(name) || ObjectDB.instance == null) return null;
            try
            {
                var exact = ObjectDB.instance.GetItemPrefab(name);
                if (exact != null) return exact;
                var items = ObjectDB.instance.m_items;
                if (items == null) return null;
                if (_itemsByName == null || _itemsCounted != items.Count)
                {
                    _itemsByName = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
                    foreach (var go in items)
                        if (go != null && !_itemsByName.ContainsKey(go.name)) _itemsByName[go.name] = go;
                    _itemsCounted = items.Count;
                }
                return _itemsByName.TryGetValue(name, out var hit) ? hit : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>The Piece on a world prefab, for pieces that draw their build-menu picture.</summary>
        private static Piece PieceOf(string prefab)
        {
            try
            {
                var go = ZNetScene.instance != null && !string.IsNullOrEmpty(prefab) ? ZNetScene.instance.GetPrefab(prefab) : null;
                return go != null ? go.GetComponent<Piece>() : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// The game's own render of a building piece: a picture of the thing, in its own colors
        /// and its own light.
        ///
        /// <para>Worth asking about separately from every other key because painting one ruins
        /// it. A tint is a multiply, so a blue-lit stone render under an orange wash arrives as
        /// an orange smudge with the picture gone -- and the picture was the entire reason for
        /// choosing it. The campfire has kept its colors for this reason since it was given the
        /// fire pit's render; this is that rule with a name.</para>
        /// </summary>
        internal static bool IsBuildPicture(string iconKey)
        {
            string key = Normalize(iconKey);
            return key != null && key.StartsWith("piece:");
        }

        /// <summary>A vanilla pin sprite: white, so it takes whatever color it is given.</summary>
        internal static bool IsVanillaPin(string iconKey)
        {
            string key = Normalize(iconKey);
            // "like:" wears the same sprite and is what every "pin:" key resolves to now.
            return key != null && (key.StartsWith("pin:") || key.StartsWith("like:"));
        }

        /// <summary>Grow Minimap's per-type visibility array so vanilla can index our types.</summary>
        internal static void EnsureArrays(Minimap map)
        {
            if (map == null) return;
            int need = Math.Max(MaxType + 1, Enum.GetValues(typeof(Minimap.PinType)).Length);
            var old = Access.VisibleIconTypes(map);
            if (old != null && old.Length >= need) return;
            var arr = new bool[need];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = old == null || i >= old.Length || old[i];
            Access.VisibleIconTypes(map) = arr;
        }
    }
}
