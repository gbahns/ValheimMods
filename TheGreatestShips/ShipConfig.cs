using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// Per-ship configuration, one section per ship named after it.  Call Bind() once from
    /// TheGreatestShipsMod.Awake.
    /// </summary>
    internal static class ShipConfig
    {
        internal sealed class Entries
        {
            public ConfigEntry<string> Recipe;
            public ConfigEntry<float>  Speed;
            public ConfigEntry<float>  Health;
            public ConfigEntry<float>  RudderSpeed;
            public ConfigEntry<Color>  SailColor;
            public ConfigEntry<Color>  HullColor;
            public ConfigEntry<int>    HullStripes;
            public ConfigEntry<float>  Width;
            public ConfigEntry<float>  Length;
            public ConfigEntry<float>  Scale;
        }

        private static readonly Dictionary<ShipDefinition, Entries> _entries =
            new Dictionary<ShipDefinition, Entries>();

        internal static Entries For(ShipDefinition def) => _entries[def];

        internal static ConfigEntry<bool> NameVanillaShips;

        // Raised when a release changes defaults that saved configs should follow.
        //   2 (0.9.1): speeds rebased on logged top speeds, Cargo Longship recipe.
        //   3 (0.9.1): Cargo Longship renamed Knarr, its config section moved to match.
        //   4 (0.9.1): Fast Longship 1.28 -> 1.342, matching the Fast Ship Skuldelev's sail force.
        //   5 (0.9.1): Fast Longship recipe: Troll Hide, Guck and 120 Iron Nails; no Linen or Resin.
        //              Knarr recipe: 180 Iron Nails and 2 Chain (the 0.9.0 recipe had 150 and none).
        //   6 (0.9.1): the Byrdings 10/15/20% slower than a longship (were 1.0 / 0.97 / 0.95).
        //   7 (0.9.1): recipes trimmed to weigh at most 450 (600 for the Big Busse and Greater Byrding).
        //   8 (0.9.1): the cargo ships no longer take Chain.
        //   9 (0.9.1): the cargo ships take Troll Hide instead of Deer Hide; the Greater Byrding drops Wolf Pelt.
        //  10 (0.9.1): the Byrdings' Wood is exactly their pen's pieces (24 / 36 / 52).
        //  11 (0.9.1): the cargo ships take Deer Hide as well as Troll Hide; a little less Ancient Bark.
        private const int CurrentConfigVersion = 11;

        internal static void Bind(TheGreatestShipsMod mod)
        {
            // Not synced: it records what this machine's file has been migrated to.  0.9.0 did not
            // write it, so a file without it is from 0.9.0 (or new, where migration is a no-op).
            var configVersion = mod.Config.Bind("General", "Config Version", 1,
                "Used by the mod to update old defaults. Do not change.");

            // Cosmetic and applied at the main menu, so not synced.
            NameVanillaShips = mod.Config.Bind("General", "Name Vanilla Ships", true,
                "Label the vanilla ships' rudders and holds with the ship's name (\"Use rudder (Karve)\", \"Longship Storage\"), " +
                "as this mod's own ships are, so you can tell which ship you are looking at. Requires a game restart.");

            foreach (var def in ShipDefinitions.All)
            {
                string section = def.DisplayName;
                var e = new Entries();

                e.Recipe = mod.BindSynced(section, "Recipe", Migrated(mod, def, "Recipe", def.DefaultRecipe),
                    "Comma-separated ItemName:Amount pairs (prefab names). Everything is returned when " +
                    "the ship is deconstructed, as with the vanilla ships.");

                // Top speed goes with the square root of the sail force (see ShipPrefabs.ApplyHandling),
                // so the multiplier is squared before it is applied.
                float about = def.BaseTopSpeed * def.DefaultSpeed;
                e.Speed = mod.BindSynced(section, "Top Speed Multiplier", Migrated(mod, def, "Top Speed Multiplier", def.DefaultSpeed),
                    $"Top speed relative to the vanilla {def.BaseName} ({def.BaseTopSpeed} in full wind). " +
                    $"{def.DefaultSpeed} makes it about {about:0.#}. Acceleration under sail changes with it.");

                e.Health = mod.BindSynced(section, "Health", Migrated(mod, def, "Health", def.DefaultHealth),
                    $"Hull health (the vanilla {def.BaseName} has {(def.BasePrefab == "Karve" ? 500 : 1000)}). " +
                    "Ships already built keep their damage; this is the maximum.");

                e.RudderSpeed = mod.BindSynced(section, "Rudder Speed", Migrated(mod, def, "Rudder Speed", def.DefaultRudderSpeed),
                    $"How quickly the rudder swings (the vanilla {def.BaseName} has 1). Lower turns more slowly.");

                // Cosmetic and read once when the ship is created, so not synced.
                e.SailColor = mod.Config.Bind(section, "Sail Color", Migrated(mod, def, "Sail Color", def.DefaultSailColor),
                    $"Tint multiplied into the sail so the {def.DisplayName} can be told apart from a {def.BaseName}. " +
                    "White leaves the sail as it is. Requires a game restart.");

                e.HullColor = mod.Config.Bind(section, "Hull Color", Migrated(mod, def, "Hull Color", def.DefaultHullColor),
                    "Paint multiplied into the hull planks (not the mast or rudder). " +
                    "White leaves the wood as it is. Requires a game restart.");

                e.HullStripes = mod.Config.Bind(section, "Hull Stripes", Migrated(mod, def, "Hull Stripes", def.DefaultHullStripes),
                    "Paint the hull color in this many bands with bare wood between them. 0 paints the hull " +
                    "solid. The bands follow the hull texture's layout. Requires a game restart.");

                // Not synced: the shape is fixed when the ship is created at the main menu, before
                // the server's values arrive.  Players should keep the same value.
                e.Width = mod.Config.Bind(section, "Hull Width", Migrated(mod, def, "Hull Width", def.DefaultWidth),
                    $"Width relative to the vanilla {def.BaseName}; below 1 is narrower, above 1 wider. " +
                    "Everyone on a server should use the same value. Requires a game restart.");

                e.Length = mod.Config.Bind(section, "Hull Length", Migrated(mod, def, "Hull Length", def.DefaultLength),
                    $"Bow-to-stern length relative to the vanilla {def.BaseName}; below 1 is shorter, above 1 longer. " +
                    "Everyone on a server should use the same value. Requires a game restart.");

                e.Scale = mod.Config.Bind(section, "Hull Scale", Migrated(mod, def, "Hull Scale", def.DefaultScale),
                    $"Size of the whole ship, all three dimensions, relative to the vanilla {def.BaseName}. Width and Length " +
                    "multiply on top of it. Everyone on a server should use the same value. Requires a game restart.");

                var captured = def;
                e.Recipe.SettingChanged += (_, __) => ShipPrefabs.ApplyRecipe(captured);
                e.Speed.SettingChanged  += (_, __) => ShipPrefabs.ApplyHandling(captured);
                e.Health.SettingChanged += (_, __) => ShipPrefabs.ApplyHandling(captured);
                e.RudderSpeed.SettingChanged += (_, __) => ShipPrefabs.ApplyHandling(captured);

                // Migrate() only moves a value still sitting at a documented old default, so
                // re-running it for every version step is safe: a hand-set value never matches.
                if (configVersion.Value < CurrentConfigVersion)
                    Migrate(def, e);

                _entries[def] = e;
            }

            if (configVersion.Value < CurrentConfigVersion)
                configVersion.Value = CurrentConfigVersion;
        }

        // A renamed ship's saved config would otherwise be orphaned: nothing binds its old
        // section name again, so a hand-tuned value would be silently lost the moment the
        // section header no longer matches any ship. If this ship was renamed (OldDisplayName
        // set), recover the value saved under the old section -- if there isn't one, this is
        // just `fallback` again, so an un-renamed or never-customized ship is unaffected -- then
        // remove the old entry so it does not also linger in the file under its old name.
        private static T Migrated<T>(TheGreatestShipsMod mod, ShipDefinition def, string key, T fallback)
        {
            if (def.OldDisplayNames == null) return fallback;
            // Bind() hands back `fallback` whether or not the file had the entry, so a saved value
            // is told from an absent one by comparing serialized text against the fallback's; a
            // saved value that happens to equal the fallback is indistinguishable, and harmless
            // to skip.  Newest old name first, so a ship renamed twice takes the latest section.
            string fallbackText = TomlTypeConverter.ConvertToString(fallback, typeof(T));
            T result = fallback;
            bool found = false;
            foreach (var oldName in def.OldDisplayNames)
            {
                var oldDef = new ConfigDefinition(oldName, key);
                var entry = mod.Config.Bind(oldDef, fallback);
                if (!found && entry.GetSerializedValue() != fallbackText)
                {
                    result = entry.Value;
                    found = true;
                }
                mod.Config.Remove(oldDef);
            }
            return result;
        }

        // Moves values still at an old release's default to the current default.  A value that
        // differs from the old default was set by hand and is left alone.
        private static void Migrate(ShipDefinition def, Entries e)
        {
            if (!float.IsNaN(def.OldDefaultSpeed) && Mathf.Approximately(e.Speed.Value, def.OldDefaultSpeed))
            {
                e.Speed.Value = def.DefaultSpeed;
                Jotunn.Logger.LogInfo($"[TheGreatestShips] {def.DisplayName}: Top Speed Multiplier {def.OldDefaultSpeed} -> {def.DefaultSpeed} (new default).");
            }
            if (def.OldDefaultRecipes != null && System.Array.IndexOf(def.OldDefaultRecipes, e.Recipe.Value) >= 0)
            {
                e.Recipe.Value = def.DefaultRecipe;
                Jotunn.Logger.LogInfo($"[TheGreatestShips] {def.DisplayName}: recipe updated to the new default.");
            }
        }

        /// <summary>
        /// Resolves a recipe string into Piece.Requirements.  Needs ObjectDB.
        /// </summary>
        internal static Piece.Requirement[] ParseRecipe(string recipe, string owner)
        {
            var result = new List<Piece.Requirement>();
            foreach (var token in recipe.Split(','))
            {
                var parts = token.Trim().Split(':');
                if (parts.Length < 2) continue;

                string itemName = parts[0].Trim();
                if (!int.TryParse(parts[1].Trim(), out int amount) || amount <= 0) continue;

                var drop = ObjectDB.instance?.GetItemPrefab(itemName)?.GetComponent<ItemDrop>();
                if (drop == null)
                {
                    Jotunn.Logger.LogWarning($"[TheGreatestShips] {owner}: '{itemName}' not found in ObjectDB, skipped.");
                    continue;
                }
                result.Add(new Piece.Requirement { m_resItem = drop, m_amount = amount, m_recover = true });
            }
            return result.ToArray();
        }
    }
}
