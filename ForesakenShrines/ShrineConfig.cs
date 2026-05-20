using BepInEx.Configuration;
using Jotunn.Configs;
using System.Collections.Generic;
using UnityEngine;

namespace ForesakenShrines
{
    /// <summary>
    /// All server-synced configuration entries for Forsaken Shrines.
    /// Call Bind() once from ForesakenShrinesMod.Awake before event subscriptions.
    /// </summary>
    internal static class ShrineConfig
    {
        // ── Placement rules ─────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> RequireNaturalTerrain;
        internal static ConfigEntry<bool> RequireSkyExposure;

        // ── Per-shrine recipe strings ────────────────────────────────────────────────
        // Format: "ItemName:Amount:Recover,..."  e.g. "Stone:20:true,TrophyDeer:5:false"
        private static readonly Dictionary<string, ConfigEntry<string>> _recipes
            = new Dictionary<string, ConfigEntry<string>>();

        internal static void Bind(ForesakenShrinesMod mod)
        {
            RequireNaturalTerrain = mod.BindSynced("Placement", "RequireNaturalTerrain", true,
                "Shrines must be placed directly on natural terrain — no player-built floor or platform beneath them.");
            RequireSkyExposure = mod.BindSynced("Placement", "RequireSkyExposure", true,
                "Shrines must be placed under open sky — no roof above them.");

            foreach (var def in ShrineDefinitions.All)
            {
                _recipes[def.PieceName] = mod.BindSynced(
                    "Recipe", def.PieceName,
                    BuildDefault(def.Requirements),
                    "Comma-separated recipe: ItemName:Amount:Recover. " +
                    "Recover=true means the item is returned when the shrine is demolished. " +
                    "Set Amount to 0 to skip an ingredient.");
            }
        }

        /// <summary>
        /// Resolves the config string for a shrine into live Piece.Requirements using ObjectDB.
        /// Must be called during or after Phase 2 (ObjectDB available).
        /// </summary>
        internal static Piece.Requirement[] BuildRequirements(string pieceName)
        {
            if (!_recipes.TryGetValue(pieceName, out var entry))
                return new Piece.Requirement[0];

            var result = new List<Piece.Requirement>();
            foreach (var token in entry.Value.Split(','))
            {
                var t = token.Trim();
                if (t.Length == 0) continue;

                var parts = t.Split(':');
                if (parts.Length < 2) continue;

                string itemName = parts[0].Trim();
                if (!int.TryParse(parts[1].Trim(), out int amount) || amount <= 0) continue;
                bool recover = parts.Length >= 3 &&
                    parts[2].Trim().Equals("true", System.StringComparison.OrdinalIgnoreCase);

                var prefab = ObjectDB.instance?.GetItemPrefab(itemName);
                var drop   = prefab?.GetComponent<ItemDrop>();
                if (drop == null)
                {
                    Jotunn.Logger.LogWarning($"[ForesakenShrines] {pieceName}: '{itemName}' not found in ObjectDB — skipped.");
                    continue;
                }
                result.Add(new Piece.Requirement { m_resItem = drop, m_amount = amount, m_recover = recover });
            }
            return result.ToArray();
        }

        private static string BuildDefault(RequirementConfig[] reqs)
        {
            var parts = new List<string>(reqs.Length);
            foreach (var r in reqs)
                parts.Add($"{r.Item}:{r.Amount}:{(r.Recover ? "true" : "false")}");
            return string.Join(",", parts);
        }
    }
}
