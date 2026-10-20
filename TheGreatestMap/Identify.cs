using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Telling markers written before this version what they are.
    ///
    /// A marker now remembers the thing it stands for, so the picture can be decided later and
    /// every marker of that thing changes with it. Markers written before that only carry the
    /// picture, and the picture is a poor way back: "a swamp key" could be a sunken crypt or
    /// anything else somebody pointed that icon at.
    ///
    /// Reading them back is possible while the icons still mean what they meant when the markers
    /// were written, which is why this wants doing before the next icon is changed rather than
    /// after. Each marker is matched against the catalog by its kind and the icon it wears, and
    /// by its name where it still has one -- most do not, because the label rules erase names
    /// rather than hide them.
    ///
    /// Several catalog lines often answer at once: three crypt prefabs are all "Burial Chambers"
    /// wearing one trophy. That is not an ambiguity worth reporting, because every answer draws
    /// the same marker with the same name; the lowest prefab wins so that two players folding the
    /// same map reach the same answer. A marker matching nothing is left exactly as it is,
    /// wearing the icon written on it.
    /// </summary>
    internal static class Identify
    {
        internal sealed class Result
        {
            internal int Known;          // already knows what it is
            internal int Matched;        // one thing it can be, or several that all draw the same
            internal int Ambiguous;      // several, drawing differently: left alone
            internal int Lost;           // nothing in the catalog wears this
            internal int Placed;         // put down by hand: its kind is all it ever claimed
            internal readonly Dictionary<string, int> ByType = new Dictionary<string, int>();
            internal readonly Dictionary<string, int> LostBy = new Dictionary<string, int>();
            /// <summary>The ones the icon could not settle, for the world to be asked about.</summary>
            internal readonly List<SharedPin> Unsettled = new List<SharedPin>();
        }

        /// <summary>
        /// Pictures this mod drew before it drew the present ones. A marker wearing one is not
        /// unidentifiable, it is merely old: nothing in the catalog answers to a swamp crypt key
        /// any more because sunken crypts took the draugr trophy, and frost caves left the ulv
        /// trophy for the cultist's. Each only applies while nothing current wears that icon, so
        /// putting an icon back makes the ordinary match win again.
        /// </summary>
        private sealed class WasDrawnAs
        {
            internal Category Kind;
            internal string Icon;
            internal string Prefab;
        }

        private static readonly WasDrawnAs[] Legacy =
        {
            new WasDrawnAs { Kind = Category.Dungeon, Icon = "CryptKey",  Prefab = "SunkenCrypt4" },
            new WasDrawnAs { Kind = Category.Dungeon, Icon = "TrophyUlv", Prefab = "MountainCave02" },
        };

        private static Catalog.LegendRow Older(SharedPin pin, Category kind, List<Catalog.LegendRow> rows)
        {
            string icon = IconRegistry.Normalize(pin.Icon);
            if (icon == null) return null;
            foreach (var old in Legacy)
            {
                if (old.Kind != kind || !IconRegistry.SameKey(icon, old.Icon)) continue;
                foreach (var row in rows)
                    if (row.Cat == kind && IconRegistry.SameKey(row.Icon, icon)) return null;  // still in use
                foreach (var row in rows)
                    if (row.Cat == kind && string.Equals(row.Prefab, old.Prefab, StringComparison.OrdinalIgnoreCase))
                        return row;
            }
            return null;
        }

        private static void Count(Dictionary<string, int> into, string key)
        {
            into.TryGetValue(key, out int n);
            into[key] = n + 1;
        }

        /// <summary>Every catalog line that could have produced a marker, by kind.</summary>
        private static List<Catalog.LegendRow> Rows()
        {
            var rows = new List<Catalog.LegendRow>(Catalog.Legend());
            rows.Sort((a, b) => string.Compare(a.Prefab, b.Prefab, StringComparison.OrdinalIgnoreCase));
            return rows;
        }

        /// <summary>
        /// What this marker is, or null. Name first where there is one, since a name is what the
        /// catalog called the thing and survives an icon being changed under it -- that is what
        /// rescues the markers still wearing the swamp key. Icon second, which is all most
        /// markers have left.
        /// </summary>
        private static List<Catalog.LegendRow> Candidates(SharedPin pin, Category kind, List<Catalog.LegendRow> rows)
        {
            string icon = IconRegistry.Normalize(pin.Icon);
            var byName = new List<Catalog.LegendRow>();
            var byIcon = new List<Catalog.LegendRow>();
            foreach (var row in rows)
            {
                if (row.Cat != kind) continue;
                if (!string.IsNullOrEmpty(pin.Name) && string.Equals(row.Name, pin.Name, StringComparison.OrdinalIgnoreCase))
                    byName.Add(row);
                if (icon != null && IconRegistry.SameKey(row.Icon, icon)) byIcon.Add(row);
            }
            // Both agreeing is the best answer there is; the name alone beats the icon alone,
            // because the icon is the thing being changed out from under these markers.
            if (byName.Count > 0 && byIcon.Count > 0)
            {
                var both = new List<Catalog.LegendRow>();
                foreach (var row in byName) if (byIcon.Contains(row)) both.Add(row);
                if (both.Count > 0) return both;
            }
            if (byName.Count > 0) return byName;
            return byIcon;
        }

        /// <summary>True when every candidate would draw the same marker, so picking one is free.</summary>
        private static bool AllAlike(List<Catalog.LegendRow> rows)
        {
            for (int i = 1; i < rows.Count; i++)
                if (!IconRegistry.SameKey(rows[i].Icon, rows[0].Icon)
                    || !string.Equals(rows[i].Name, rows[0].Name, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        internal static Result Run(bool apply)
        {
            var result = new Result();
            var rows = Rows();
            foreach (var pin in new List<SharedPin>(ClientPins.All))
            {
                if (!string.IsNullOrEmpty(pin.Source)) { result.Known++; continue; }
                if (!pin.Auto) { result.Placed++; continue; }
                var kind = ClientPins.KindOf(pin);
                if (!kind.HasValue) { result.Lost++; Count(result.LostBy, "no kind"); continue; }
                // Nothing to tell apart within this kind, so its icon already follows the setting
                // and there is nothing to write down.
                if (!Categories.UsesPrefabList(kind.Value)) { result.Placed++; continue; }

                var candidates = Candidates(pin, kind.Value, rows);
                if (candidates.Count == 0)
                {
                    var older = Older(pin, kind.Value, rows);
                    if (older != null) candidates.Add(older);
                }
                if (candidates.Count == 0)
                {
                    result.Lost++;
                    result.Unsettled.Add(pin);
                    Count(result.LostBy, Categories.Label(kind.Value) + " / " + (IconRegistry.Normalize(pin.Icon) ?? "no icon"));
                    continue;
                }
                if (candidates.Count > 1 && !AllAlike(candidates))
                {
                    result.Ambiguous++;
                    result.Unsettled.Add(pin);
                    Count(result.LostBy, Categories.Label(kind.Value) + " / " + (IconRegistry.Normalize(pin.Icon) ?? "no icon") + " (several)");
                    continue;
                }

                var row = candidates[0];
                result.Matched++;
                Count(result.ByType, row.Name + " (" + row.Prefab + ")");
                if (!apply) continue;
                // The trailing star of a prefix line is not part of any prefab's name, and the
                // catalog matches a bare prefix just as well.
                pin.Source = row.Prefab.TrimEnd('*');
                ClientPins.UpdateMarker(pin);
            }
            if (apply && result.Matched > 0) ClientPins.Restyle();
            return result;
        }

        /// <summary>
        /// For the markers the icon could not settle, ask the world what actually stands there.
        /// A dozen catalog lines share the house pin and the names that told them apart were
        /// erased long ago, so there is nothing on the marker left to read -- but the building
        /// itself is still there, and the server knows its name. That is a fact rather than a
        /// guess, which is the whole difference: guessing would file every house pin in the game
        /// under whichever catalog line came first.
        /// </summary>
        internal static bool AskTheWorld(Result result, bool apply, Action<List<string>> report)
        {
            if (result == null || result.Unsettled.Count == 0) return false;
            var pins = new List<SharedPin>(result.Unsettled);
            var places = new List<Vector3>(pins.Count);
            foreach (var pin in pins) places.Add(pin.Pos);
            return LocationNames.Ask(places, names =>
            {
                var lines = new List<string>();
                var byName = new Dictionary<string, int>();
                int named = 0, nothing = 0;
                for (int i = 0; i < pins.Count && i < names.Count; i++)
                {
                    string prefab = names[i];
                    if (string.IsNullOrEmpty(prefab)) { nothing++; continue; }
                    named++;
                    Count(byName, Catalog.Prettify(prefab) + " (" + prefab + ")");
                    if (!apply) continue;
                    pins[i].Source = prefab;
                    ClientPins.UpdateMarker(pins[i]);
                }
                lines.Add($"The world named {named} of {pins.Count} the icons could not settle" +
                          (nothing > 0 ? $"; {nothing} stand where it has no building on record." : "."));
                foreach (var kv in byName) lines.Add($"  {kv.Key}: {kv.Value}");
                if (!apply && named > 0) lines.Add("Run tgm_identify apply to write these too.");
                if (apply && named > 0) ClientPins.Restyle();
                report(lines);
            });
        }

        /// <summary>The report both the command and the automatic pass print.</summary>
        internal static List<string> Describe(Result r, bool applied)
        {
            var lines = new List<string>
            {
                $"{r.Matched} marker(s) {(applied ? "now know" : "could be told")} what they are; " +
                $"{r.Known} already did, {r.Placed} follow their kind and need no telling.",
            };
            if (r.Ambiguous > 0) lines.Add($"{r.Ambiguous} could be several different things and were left alone.");
            if (r.Lost > 0) lines.Add($"{r.Lost} match nothing in the catalog and keep the icon they were drawn with.");
            foreach (var kv in r.ByType) lines.Add($"  {kv.Key}: {kv.Value}");
            foreach (var kv in r.LostBy) lines.Add($"  unmatched, {kv.Key}: {kv.Value}");
            return lines;
        }
    }
}
