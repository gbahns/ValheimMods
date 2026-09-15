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
        internal static ConfigEntry<bool> ShowMessages;

        // ── Sharing rules (server-synced) ───────────────────────────────────────────
        internal static ConfigEntry<SharingMode> SharingMode;
        internal static ConfigEntry<bool> AllowErasingMarkers;
        internal static ConfigEntry<bool> RequireMapOutToEdit;
        internal static ConfigEntry<bool> RequireMapOutToRecord;
        internal static ConfigEntry<bool> SharePlacedPins;
        internal static ConfigEntry<bool> TableCarriesPins;
        internal static ConfigEntry<float> ExchangeRadius;
        internal static ConfigEntry<float> ExchangeCooldown;
        internal static ConfigEntry<bool> ExchangeExploration;

        // ── what to draw ────────────────────────────────────────────────────────────
        internal static readonly Dictionary<Category, ConfigEntry<bool>> ShowKind = new Dictionary<Category, ConfigEntry<bool>>();
        internal static ConfigEntry<string> HiddenIcons;
        internal static ConfigEntry<bool> PauseWhileMapOpen;
        internal static ConfigEntry<bool> ShowPauseButton;
        internal static ConfigEntry<bool> MarkerButton;
        internal static ConfigEntry<bool> ShowAllMarkers;
        internal static ConfigEntry<bool> ShowClearedDeposits;
        internal static ConfigEntry<float> RevealNewMarkers;
        internal static ConfigEntry<bool> MapAllPortals;
        internal static ConfigEntry<string> PortalColor;
        internal static ConfigEntry<string> PortalPulseColor;
        internal static ConfigEntry<float> PortalPulseSeconds;
        private static HashSet<string> _hiddenIconKeys;

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

        internal static int HiddenKindCount()
        {
            int n = 0;
            foreach (var entry in ShowKind.Values) if (!entry.Value) n++;
            return n;
        }

        /// <summary>Clear the hidden icons, switch every kind back on and undo the master hide.</summary>
        internal static void ShowEverything()
        {
            if (!string.IsNullOrEmpty(HiddenIcons.Value)) HiddenIcons.Value = "";
            foreach (var entry in ShowKind.Values) if (!entry.Value) entry.Value = true;
            if (ShowAllMarkers != null && !ShowAllMarkers.Value) ShowAllMarkers.Value = true;
        }

        /// <summary>True when anything at all is being hidden, by any of the switches.</summary>
        internal static bool AnythingHidden()
        {
            if (ShowAllMarkers != null && !ShowAllMarkers.Value) return true;
            return HiddenIconCount() > 0 || HiddenKindCount() > 0;
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

        /// <summary>The longest sighting distance of any kind: how far the look ray is cast.</summary>
        internal static float MaxLookDistance()
        {
            float max = 5f;
            foreach (var entry in LookDistance.Values) if (entry.Value > max) max = entry.Value;
            return max;
        }
        internal static readonly Dictionary<Category, ConfigEntry<bool>> CategoryEnabled = new Dictionary<Category, ConfigEntry<bool>>();
        internal static readonly Dictionary<Category, ConfigEntry<float>> MarkerSpacing = new Dictionary<Category, ConfigEntry<float>>();
        internal static readonly Dictionary<Category, ConfigEntry<float>> LabelSpacing = new Dictionary<Category, ConfigEntry<float>>();
        internal static readonly Dictionary<Category, ConfigEntry<int>> MarkerSize = new Dictionary<Category, ConfigEntry<int>>();
        internal static readonly Dictionary<Category, ConfigEntry<bool>> ShowOnMinimap = new Dictionary<Category, ConfigEntry<bool>>();
        internal static readonly Dictionary<Category, ConfigEntry<string>> CategoryPrefabs = new Dictionary<Category, ConfigEntry<string>>();
        internal static readonly Dictionary<Category, ConfigEntry<string>> CategoryIcon = new Dictionary<Category, ConfigEntry<string>>();
        internal static ConfigEntry<bool> StructuresIncludeUnlisted;
        internal static ConfigEntry<string> StructuresExcludePrefixes;
        internal static ConfigEntry<bool> CrossOffStructuresOnChest;
        internal static ConfigEntry<bool> ApplyLabelRulesOnSync;
        internal static ConfigEntry<float> ServerAutosaveMinutes;

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
            ShowMessages = mod.BindLocal("General", "Show Messages", true,
                "Show small top-left messages when the map is taken out, a marker is recorded, and so on.");

            SharingMode = mod.BindSynced("Sharing", "Sharing Mode", TheGreatestMap.SharingMode.Table,
                "Table: markers travel like exploration. What you record or place stays on your own map until you merge " +
                "it with the shared map at a cartography table, or with another player's map when you both have your maps " +
                "out standing together. Instant: every change goes to the shared map at once and out to everyone.");
            AllowErasingMarkers = mod.BindSynced("Sharing", "Allow Erasing Markers", false,
                "Let players erase this mod's markers (Shift + right-click on the map screen). Off by default so a marker " +
                "cannot be lost to a stray click; vanilla pins are unaffected. An erasure merges like any other change: " +
                "it wins over older copies of the marker and is itself replaced if someone records the spot again later.");
            ExchangeRadius = mod.BindLocal("Sharing", "Exchange Radius", 5f,
                "Distance in meters within which two players who both have their maps out compare and merge their maps.");
            ExchangeCooldown = mod.BindLocal("Sharing", "Exchange Cooldown", 60f,
                "Seconds before the same two players compare maps again.");
            ExchangeExploration = mod.BindLocal("Sharing", "Exchange Exploration", true,
                "When comparing maps with another player, also share explored areas (the fog of war), as a cartography table does.");
            HiddenIcons = mod.BindLocal("Display", "Hidden Icons", "",
                "Comma-separated marker icons never drawn on your map, as item prefab names or icon keys, e.g. Dandelion,Thistle,pin:Icon1. " +
                "Hidden markers stay on your map and keep syncing; they are just not shown. Applies at once.");
            HiddenIcons.SettingChanged += (_, __) => { _hiddenIconKeys = null; ClientPins.Restyle(); };
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
                "Add a map-pin button above vanilla's icon buttons on the right edge of the large map. Right-clicking it hides " +
                "or shows every marker from this mod at once, as right-clicking a vanilla icon does for that icon; " +
                "left-clicking it opens the list of kinds, for hiding them one at a time. The pin is drawn gold while " +
                "markers are shown and gray while they are hidden.");
            ShowAllMarkers = mod.BindLocal("Display", "Show Markers", true,
                "Draw this mod's markers on your map at all. Off hides every one of them, whatever the per-kind switches " +
                "say, including markers that have no kind; they stay on your map and keep syncing. This is what the " +
                "map-pin button on the map screen toggles.");
            ShowAllMarkers.SettingChanged += (_, __) => ClientPins.Restyle();
            ShowClearedDeposits = mod.BindLocal("Display", "Show Cleared Deposits", true,
                "Keep drawing berry bushes, mushrooms, herbs and ore deposits after they have been used up, crossed " +
                "off, so you can see which ground has already been worked. Off hides them once cleared. The record is " +
                "kept either way, and this does not touch structures, whose cross-off means searched rather than gone.");
            ShowClearedDeposits.SettingChanged += (_, __) => ClientPins.Restyle();
            MapAllPortals = mod.BindLocal("Display", "Map All Portals", true,
                "Draw every portal that currently exists in the world, not only the ones you or someone who shared their " +
                "map with you has seen. Portals are built by the players, so on a server where everyone is in the same " +
                "group this simply keeps the map honest; turn it off on a public server, or if you would rather learn a " +
                "portal the way you learn everything else. The extra portals are drawn only: they are never written to " +
                "your map and never shared, so turning this off leaves nothing behind. Either way the portal markers you " +
                "do have are kept current, renamed when the portal is renamed and erased when it is torn down.");
            MapAllPortals.SettingChanged += (_, __) => Portals.Redraw();
            PortalColor = mod.BindLocal("Display", "Portal Marker Color", "#FFEDB8",
                "Color of portal markers, which are worth picking out of the crowd because they are how you travel. " +
                "A hex color such as #B07CFF, or a name such as violet. The default is the same pale gold as every " +
                "other recorded marker.");
            PortalPulseColor = mod.BindLocal("Display", "Portal Pulse Color", "#B07CFF",
                "Portal markers fade back and forth between their color and this one, so they catch the eye on a busy " +
                "map. Leave it empty for a steady color.");
            PortalPulseSeconds = mod.BindLocal("Display", "Portal Pulse Seconds", 2f,
                "How long one fade from the portal color to the pulse color and back takes. Lower is faster; " +
                "0 stops the fade.");
            RevealNewMarkers = mod.BindLocal("Display", "Reveal New Markers Seconds", 30f,
                "How long a marker you have just recorded is drawn on both maps even though its kind or its icon is " +
                "hidden, so writing something down always shows you what you wrote. 0 turns it off. It does not " +
                "override the map-pin button's master switch or a marker you hid by hand, and it cannot show kinds " +
                "drawn on a vanilla icon (structures, portals, camps, boss altars) while vanilla's own icon button " +
                "has that icon switched off.");
            RequireMapOutToEdit = mod.BindSynced("Sharing", "Require Map Out To Edit", false,
                "You must have the pocket map out to place a marker, erase one (yours, someone else's or a recorded one) " +
                "or cross one off on the map screen. Pings are always allowed. Off by default: the map screen edits like vanilla.");
            RequireMapOutToRecord = mod.BindSynced("Sharing", "Require Map Out To Record", true,
                "Automatic recording of found things only happens while the pocket map is out. Turn off to record " +
                "found things whenever you are within the record radius, map or no map.");
            SharePlacedPins = mod.BindSynced("Sharing", "Share Placed Markers", true,
                "Markers you place on the map screen are shared instantly with everyone on the server. " +
                "When false they stay private, as in vanilla.");
            TableCarriesPins = mod.BindSynced("Sharing", "Table Carries Player Markers", false,
                "When false (recommended) the cartography table only carries map exploration. Player-placed markers " +
                "(the five standard icons and this mod's icons) are neither written to nor read from the table, and " +
                "stale copies previously imported from a table are swept away. Boss, Hildir and memorial pins still " +
                "share through the table as in vanilla. Set true to restore vanilla table behavior for markers.");

            AutoSyncTable = mod.BindLocal("Cartography Table", "Auto Sync", true,
                "Automatically read and write the cartography table when you are right at it. Silent unless something " +
                "is actually exchanged: new areas from the table are read, and the table is written (vanilla's 'map saved') " +
                "only when it lacks areas you have explored.");
            TableSyncRadius = mod.BindLocal("Cartography Table", "Sync Radius", 1f,
                "How close to a cartography table you must stand (meters from you to its edge) for auto-sync and the sync key.");
            TableSyncCooldown = mod.BindLocal("Cartography Table", "Auto Sync Cooldown", 60f,
                "Seconds between automatic syncs of the same table while you stay in reach. The sync key ignores this.");

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

            foreach (var cat in Categories.All)
            {
                string label = Categories.Label(cat);
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
                    "with the same icon and name that already has a label lies within that distance).");
                LookDistance[cat] = mod.BindLocal("Recording", label + " Look Distance", Categories.DefaultLookDistance(cat),
                    "How far away " + label.ToLowerInvariant() + " can be and still count as seen when you look straight at them " +
                    "with clear line of sight. Interacting, or having them under the crosshair within reach, always counts.");
                MarkerSize[cat] = mod.BindLocalRange("Recording", label + " Marker Size", Categories.DefaultSize(cat), 20, 100,
                    "Size of recorded " + label.ToLowerInvariant() + " markers on the map, as a percentage of the normal marker size.");
                ShowOnMinimap[cat] = mod.BindLocal("Recording", label + " On Minimap", true,
                    "Show recorded " + label.ToLowerInvariant() + " markers on the small minimap (when they are shown at all).");
                ShowKind[cat] = mod.BindLocal("Display", "Show " + label, true,
                    "Draw recorded " + label.ToLowerInvariant() + " markers on your map. Off hides them on both the large map and the " +
                    "minimap; they stay on your map and keep syncing. To hide single icons instead (say only dandelions) use Hidden Icons." +
                    (IconRegistry.VanillaFilters(Categories.DefaultIcon(cat))
                        ? " This kind draws on one of vanilla's own map icons, so vanilla's icon button on the map screen already hides and " +
                          "shows it, along with any pin you placed by hand with that icon; this switch covers only the markers this mod recorded."
                        : ""));
                ShowKind[cat].SettingChanged += (_, __) => ClientPins.Restyle();
                ShowOnMinimap[cat].SettingChanged += (_, __) => ClientPins.Restyle();
                MarkerSize[cat].SettingChanged += (_, __) => ClientPins.Restyle();
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
                    "an item prefab name (its inventory icon is used) or pin:<VanillaPinType> such as pin:Icon0, pin:Memorial, pin:Boss.");
            }
            StructuresIncludeUnlisted = mod.BindLocal("Catalog", "Structures Include Unlisted", true,
                "Treat any outdoor location that is not listed under another kind as a structure (the location's prefab name, " +
                "tidied up, becomes the marker text). Turn off to record only the listed structure prefixes.");
            StructuresExcludePrefixes = mod.BindLocal("Catalog", "Structures Exclude Prefixes", "Vegvisir_,Runestone_,Meteorite,TarPit,Rock,Hugin,StartTemple,Pickable,Vegetation,Tree,Bush",
                "Comma-separated prefab name prefixes never recorded as structures.");
            ApplyLabelRulesOnSync = mod.BindLocal("Recording", "Apply Label Rules To Existing Markers", true,
                "After each sync, apply the label rules above to recorded markers that already exist: within each kind, the " +
                "oldest marker of a same-named cluster keeps its label and the others lose theirs, for everyone. Labels are only " +
                "ever removed, never added back. The console command tgm_relabel does the same on demand.");
            CrossOffStructuresOnChest = mod.BindLocal("Recording", "Cross Off Structures When Searched", true,
                "Opening a chest inside a structure crosses its marker off for everyone. If the structure has no marker yet, " +
                "it is remembered as searched and its marker starts crossed off when it is recorded.");
            foreach (var entry in CategoryPrefabs.Values)
                entry.SettingChanged += (_, __) => { Catalog.Invalidate(); KindInference.Invalidate(); };
            StructuresExcludePrefixes.SettingChanged += (_, __) => Catalog.Invalidate();

            ServerAutosaveMinutes = mod.BindLocal("Server", "Server Autosave Minutes", 0f,
                "Server only. Extra world save every this many minutes on top of vanilla's own autosave (0 = vanilla only). " +
                "Worth setting on hosts whose panel stop kills the server without saving. A file named save-now in " +
                "BepInEx/config/TheGreatestMap/ also makes the server save at once (used by deploy scripts).");

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
            if (parts.Length != 3) return fallback;
            if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                return new Vector3(x, y, z);
            return fallback;
        }
    }
}
