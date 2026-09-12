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
        internal static ConfigEntry<bool> RequireMapOutToEdit;
        internal static ConfigEntry<bool> RequireMapOutToRecord;
        internal static ConfigEntry<bool> SharePlacedPins;
        internal static ConfigEntry<bool> TableCarriesPins;

        // ── Cartography table ───────────────────────────────────────────────────────
        internal static ConfigEntry<bool> AutoSyncTable;
        internal static ConfigEntry<float> TableSyncRadius;
        internal static ConfigEntry<float> TableSyncCooldown;

        // ── Recording ───────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> RecordEnabled;
        internal static ConfigEntry<float> RecordRadius;
        internal static ConfigEntry<float> RecordDwell;
        internal static ConfigEntry<float> LookDistance;
        internal static ConfigEntry<float> LookDwell;
        internal static readonly Dictionary<Category, ConfigEntry<bool>> CategoryEnabled = new Dictionary<Category, ConfigEntry<bool>>();
        internal static readonly Dictionary<Category, ConfigEntry<float>> MarkerSpacing = new Dictionary<Category, ConfigEntry<float>>();
        internal static readonly Dictionary<Category, ConfigEntry<float>> LabelSpacing = new Dictionary<Category, ConfigEntry<float>>();
        internal static readonly Dictionary<Category, ConfigEntry<string>> CategoryPrefabs = new Dictionary<Category, ConfigEntry<string>>();
        internal static readonly Dictionary<Category, ConfigEntry<string>> CategoryIcon = new Dictionary<Category, ConfigEntry<string>>();
        internal static ConfigEntry<bool> StructuresIncludeUnlisted;
        internal static ConfigEntry<string> StructuresExcludePrefixes;

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
                "share through the table as in vanilla. Set true to restore vanilla table behaviour for markers.");

            AutoSyncTable = mod.BindLocal("Cartography Table", "Auto Sync", true,
                "Automatically read and write the cartography table when you walk within reach of it.");
            TableSyncRadius = mod.BindLocal("Cartography Table", "Sync Radius", 4f,
                "Distance in metres from a cartography table within which auto-sync and the sync key work.");
            TableSyncCooldown = mod.BindLocal("Cartography Table", "Auto Sync Cooldown", 60f,
                "Seconds between automatic syncs of the same table while you stay in reach. The sync key ignores this.");

            RecordEnabled = mod.BindLocal("Recording", "Enabled", true,
                "While the pocket map is out, automatically record things you have found. Nothing is ever recorded " +
                "that you did not look at or interact with yourself.");
            RecordRadius = mod.BindLocal("Recording", "Record Radius", 10f,
                "Found things within this many metres are written down once the map has been out long enough.");
            RecordDwell = mod.BindLocal("Recording", "Record Dwell", 3f,
                "Seconds the map must be out before it starts recording.");
            LookDistance = mod.BindLocal("Recording", "Look Distance", 30f,
                "How far away something can be and still count as found when you look straight at it with clear line of sight.");
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
                    "metres. Small values give one icon per plant so a clump shows how many there are. The find stays pending and " +
                    "is written once that marker is gone.");
                LabelSpacing[cat] = mod.BindLocal("Recording", label + " Label Spacing", Categories.DefaultLabelSpacing(cat),
                    "Text labels on recorded " + label.ToLowerInvariant() + " markers: -1 = never (the icon says it all), " +
                    "0 = always, or a distance in metres so a clump gets one label (a new marker is icon-only when a marker " +
                    "with the same icon and name that already has a label lies within that distance).");
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
            foreach (var entry in CategoryPrefabs.Values)
                entry.SettingChanged += (_, __) => Catalog.Invalidate();
            StructuresExcludePrefixes.SettingChanged += (_, __) => Catalog.Invalidate();

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
