using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Two tools for a test world, and nothing a player needs.
    ///
    /// <para>The first turns markers placed by hand into recorded ones. A hammer dropped on a
    /// burial chamber and a dot dropped on a copper vein say what they are only to the person who
    /// put them there -- but the world standing underneath them knows exactly what is being
    /// pointed at, and the server holds that knowledge for every place at once whether anybody is
    /// there or not. So it asks, and writes the marker the recorder would have written.</para>
    ///
    /// <para>The second fills in what was never looked at closely enough to be recorded: every
    /// berry bush, mushroom, thistle and deposit in the ground where you have already been.
    /// Recording wants you to stand near a thing with the map out, which is the right rule for
    /// playing and a slow one for testing -- a test map wants a thousand markers before lunch,
    /// and those are different wants.</para>
    ///
    /// <para>Both write markers for everybody, so both list what they would do and do nothing at
    /// all until told to apply. And both go through the same spacing, the same respect for an
    /// erasure and the same one-label-per-so-many-meters as ordinary recording, so a map made
    /// this way is made of the same markers an honestly played one is.</para>
    ///
    /// <para>Both need the world itself rather than a copy of its map: the locations come from the
    /// generator's own table and the things standing about come from the server's own records.
    /// Single player, a world you are hosting, or the server's own console.</para>
    /// </summary>
    internal static class Admin
    {
        /// <summary>
        /// How far from a marker placed by hand to look for the thing it was pointing at. A pin is
        /// dropped by eye on a map a thousand meters to the inch, so it lands near the thing
        /// rather than on it; twenty meters is about as wrong as a careful hand gets, and it is
        /// short enough that the answer is still about the thing that was meant.
        /// </summary>
        internal const float DefaultReach = 20f;

        /// <summary>
        /// How many markers one survey will write. A developed world holds hundreds of thousands
        /// of things worth a picture, and a map carrying all of them is not a map. It is also the
        /// one guard against a command typed in the wrong window.
        /// </summary>
        internal const int DefaultCap = 1000;

        // ── markers placed by hand become recorded ones ──────────────────────────────

        /// <summary>
        /// Every marker on this map that a player put there by hand, asked about and rewritten as
        /// the thing it was pointing at. The vanilla pin is then removed, so one thing ends up
        /// with one marker.
        ///
        /// <para>A pin with nothing identifiable near it is left exactly where it is. It is the
        /// only outcome here that loses something a player meant, and the player is the only one
        /// who knows what they meant.</para>
        /// </summary>
        internal static List<string> Adopt(float reach, bool apply)
        {
            var lines = new List<string>();
            var map = Minimap.instance;
            if (map == null)
            {
                lines.Add("The map is not up yet.");
                return lines;
            }

            var hand = new List<Minimap.PinData>();
            foreach (var pin in Access.Pins(map))
            {
                if (pin == null || !pin.m_save) continue;
                if (ClientPins.IsOurs(pin)) continue;
                // A custom type is one of this mod's own pictures. Anything wearing one that this
                // mod does not own is a marker in the middle of being placed, which is not a
                // marker yet.
                if (PinTypes.IsCustom((int)pin.m_type)) continue;
                if (!PinTypes.IsPlayerPlaceable((int)pin.m_type)) continue;
                hand.Add(pin);
            }
            if (hand.Count == 0)
            {
                lines.Add("No markers placed by hand on this map. The ones this mod drew are not touched.");
                return lines;
            }
            if (!LocationNames.HaveWorld)
            {
                lines.Add($"{hand.Count} marker(s) placed by hand, and no way to look any of them up: this " +
                          "client is not where the world is. Run it in single player, on a world you are " +
                          "hosting, or from the server's own console.");
                return lines;
            }

            var places = LocationNames.Snapshot();
            var sofar = new List<Written>();
            var byThing = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var kindsOff = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var puzzling = new List<string>();
            var renamed = new List<string>();
            int written = 0, already = 0, erased = 0, off = 0, nothing = 0, refused = 0;

            foreach (var pinData in hand)
            {
                Found found = null;
                // Nearest first, and the first answer the catalog can make a marker of wins. A
                // thing at arm's length is what a pin was pointing at more often than the ground
                // it stands on, which is why the place the generator put there is offered last.
                foreach (var hit in LocationNames.WhatStandsAt(pinData.m_pos, reach, places))
                    if (Build(hit, out found)) break;
                if (found == null)
                {
                    nothing++;
                    if (puzzling.Count < 12) puzzling.Add("  " + Where(pinData));
                    continue;
                }

                Count(byThing, Catalog.Prettify(found.Source) + " (" + found.Source + ")");
                var verdict = Consider(found, apply, sofar);
                switch (verdict)
                {
                    case Verdict.Written:  written++; break;
                    case Verdict.Marked:   already++; break;
                    case Verdict.Erased:   erased++;  break;
                    case Verdict.KindOff:  off++; kindsOff.Add(Categories.Label(found.Cat)); break;
                    case Verdict.Refused:  refused++; break;
                }
                // The pin goes when there is a marker standing for the same thing, whether this
                // run wrote it or found it already there. It stays where a marker was refused, so
                // that nothing is lost quietly: an erased spot and a switched-off kind are both
                // reasons to look rather than reasons to tidy up.
                if (verdict != Verdict.Written && verdict != Verdict.Marked) continue;
                if (!string.IsNullOrEmpty(pinData.m_name) && renamed.Count < 12)
                    renamed.Add($"  \"{pinData.m_name}\" -> {found.Name}");
                if (apply) map.RemovePin(pinData);
            }

            lines.Add(apply
                ? $"{hand.Count} marker(s) placed by hand, looking {reach:0} m around each one."
                : $"{hand.Count} marker(s) placed by hand, looking {reach:0} m around each one. Nothing written yet.");
            lines.Add($"  {written} became recorded markers" + (apply ? ", and their hand-placed pins are gone." : "."));
            if (already > 0) lines.Add($"  {already} stood for something already marked" + (apply ? ", so the pin went and nothing was written." : "."));
            if (erased > 0) lines.Add($"  {erased} stand where a marker was erased, so none was written and the pin stays.");
            if (refused > 0) lines.Add($"  {refused} could not be written at all; the log says why. Their pins stay.");
            if (off > 0) lines.Add($"  {off} are of a kind that is switched off ({string.Join(", ", New(kindsOff))}), so the pin stays.");
            if (nothing > 0)
            {
                lines.Add($"  {nothing} have nothing the catalog knows within {reach:0} m, so they stay exactly as they are:");
                lines.AddRange(puzzling);
                if (nothing > puzzling.Count) lines.Add($"  ... and {nothing - puzzling.Count} more.");
            }
            if (byThing.Count > 0)
            {
                lines.Add("What the world said they were:");
                foreach (var kv in byThing) lines.Add($"  {kv.Key}: {kv.Value}");
            }
            if (renamed.Count > 0)
            {
                lines.Add("Names you had typed, and what the marker says now:");
                lines.AddRange(renamed);
            }
            if (apply && written > 0) ClientPins.Restyle();
            return lines;
        }

        /// <summary>Where a pin is, in the terms a player can go and look at.</summary>
        private static string Where(Minimap.PinData pin)
        {
            string name = string.IsNullOrEmpty(pin.m_name) ? "(no name)" : "\"" + pin.m_name + "\"";
            return $"{name} at {pin.m_pos.x:0}, {pin.m_pos.z:0}";
        }

        // ── filling in what was never looked at ──────────────────────────────────────

        /// <summary>
        /// Every location the generator placed and every thing standing about in the world, turned
        /// into the markers recording would have written for them -- in the part of the map you
        /// have uncovered, or everywhere when asked.
        ///
        /// <para>The locations come from the generator's own table, which covers the whole world
        /// at once. The loose things come from the server's own records, one pass over every object
        /// it holds, which in a developed world is about a million of them: a visible pause, once,
        /// on purpose.</para>
        /// </summary>
        internal static List<string> Survey(bool everywhere, int cap, bool apply)
        {
            var lines = new List<string>();
            if (!LocationNames.HaveWorld)
            {
                lines.Add("This client is not where the world is, so there is nothing to survey. Run it in " +
                          "single player, on a world you are hosting, or from the server's own console.");
                return lines;
            }
            var map = Minimap.instance;
            if (map == null && !everywhere)
            {
                lines.Add("The map is not up yet, so what you have explored cannot be read. " +
                          "tgm_survey all does not need it.");
                return lines;
            }

            var sofar = new List<Written>();
            var byKind = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var kindsOff = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int written = 0, already = 0, erased = 0, off = 0, unexplored = 0, capped = 0, refused = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            void Judge(Found found)
            {
                if (written + capped >= cap) { capped++; return; }
                switch (Consider(found, apply, sofar))
                {
                    case Verdict.Written: written++; Count(byKind, Categories.Label(found.Cat)); break;
                    case Verdict.Marked:  already++; break;
                    case Verdict.Erased:  erased++;  break;
                    case Verdict.KindOff: off++; kindsOff.Add(Categories.Label(found.Cat)); break;
                    case Verdict.Refused: refused++; break;
                }
            }

            // The places first, because the generator's table is small and says the most per line:
            // a dungeon, a village, a tar pit.
            int locations = 0;
            foreach (var placed in LocationNames.Snapshot())
            {
                if (!everywhere && !Explored(map, placed.Pos)) { unexplored++; continue; }
                if (!Catalog.TryClassifyLocation(Entry(placed.Prefab, placed.Pos, placed.Radius), out var found)) continue;
                locations++;
                Judge(found);
            }

            // Then the things standing about in it. Every object the server holds is asked what it
            // is by the hash it carries, which is one dictionary lookup -- the alternative is to
            // ask the world for each of a hundred prefabs in turn, which is a hundred passes over
            // a million records for the same answer.
            var templates = Templates();
            int objects = 0;
            var sectors = LocationNames.AllSectors();
            if (sectors != null)
            {
                for (int i = 0; i < sectors.Length; i++)
                {
                    var list = sectors[i];
                    if (list == null || list.Count == 0) continue;
                    for (int j = 0; j < list.Count; j++)
                    {
                        var zdo = list[j];
                        if (zdo == null || !zdo.IsValid()) continue;
                        if (!templates.TryGetValue(zdo.GetPrefab(), out var template)) continue;
                        // Picked, and there is nothing to go there for until it grows back. The
                        // seeds that never grow back are not in the catalog at all.
                        if (zdo.GetBool(ZDOVars.s_picked, false)) continue;
                        var pos = zdo.GetPosition();
                        if (!everywhere && !Explored(map, pos)) { unexplored++; continue; }
                        objects++;
                        Judge(template.At(pos));
                    }
                }
            }
            clock.Stop();

            lines.Add(apply
                ? $"Surveyed {(everywhere ? "the whole world" : "what you have explored")}: " +
                  $"{locations} location(s) and {objects} thing(s) standing about, in {clock.ElapsedMilliseconds} ms."
                : $"Surveying {(everywhere ? "the whole world" : "what you have explored")} would look at " +
                  $"{locations} location(s) and {objects} thing(s) standing about ({clock.ElapsedMilliseconds} ms). " +
                  "Nothing written yet.");
            lines.Add($"  {written} marker(s) {(apply ? "written" : "to write")}" + (written > 0 ? ":" : "."));
            foreach (var kv in byKind) lines.Add($"    {kv.Key}: {kv.Value}");
            if (already > 0) lines.Add($"  {already} are already marked, within the marker spacing of their kind.");
            if (erased > 0) lines.Add($"  {erased} stand where a marker was erased, so none is written.");
            if (off > 0) lines.Add($"  {off} are of a kind that is switched off ({string.Join(", ", New(kindsOff))}).");
            if (refused > 0) lines.Add($"  {refused} could not be written at all; the log says why.");
            if (!everywhere && unexplored > 0) lines.Add($"  {unexplored} are outside what you have explored. tgm_survey all takes those too.");
            if (capped > 0)
                lines.Add($"  {capped} more were left alone: this stops at {cap}. " +
                          $"Run it again for the next {cap}, or name a bigger number.");
            if (apply && written > 0) ClientPins.Restyle();
            return lines;
        }

        /// <summary>
        /// What one prefab would become, worked out once rather than per object. A hundred thousand
        /// raspberry bushes are one question about raspberries and a hundred thousand positions.
        /// </summary>
        private sealed class Template
        {
            public Category Cat;
            public string Icon;
            public string Name;
            public string Source;

            public Found At(Vector3 pos) =>
                new Found { Cat = Cat, Icon = Icon, Name = Name, Source = Source, Pos = pos, Key = "" };
        }

        private static readonly System.Reflection.FieldInfo _namedPrefabsField =
            HarmonyLib.AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        private static Dictionary<int, Template> Templates()
        {
            var found = new Dictionary<int, Template>();
            var scene = ZNetScene.instance;
            if (scene == null) return found;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Consider(GameObject prefab)
            {
                if (prefab == null || string.IsNullOrEmpty(prefab.name)) return;
                if (!seen.Add(prefab.name)) return;
                if (!Catalog.TryClassifyPrefab(prefab, prefab.name, Vector3.zero, out var one)) return;
                found[prefab.name.GetStableHashCode()] = new Template
                {
                    Cat = one.Cat, Icon = one.Icon, Name = one.Name, Source = one.Source,
                };
            }

            // Both indexes, the way the build sweep reads them: a prefab another mod registered
            // may be in the dictionary and not in the array, and a ZDO carrying it would otherwise
            // be a thing the catalog knows and the survey cannot see.
            var named = _namedPrefabsField != null
                ? _namedPrefabsField.GetValue(scene) as Dictionary<int, GameObject>
                : null;
            if (named != null) foreach (var kv in named) Consider(kv.Value);
            if (scene.m_prefabs != null) foreach (var prefab in scene.m_prefabs) Consider(prefab);
            return found;
        }

        // ── one rule for writing, shared by both ────────────────────────────────────

        private enum Verdict { Written, Marked, Erased, KindOff, Refused }

        /// <summary>
        /// A marker this run has decided on. Kept so that a listing deduplicates against itself
        /// exactly as writing would: two bushes a meter apart are one marker either way, and a
        /// dry run that says otherwise is not a dry run.
        /// </summary>
        private sealed class Written
        {
            public string Icon;
            public string Name;
            public Vector3 At;
            public float Radius;
        }

        /// <summary>
        /// Write one find the way the recorder would, or say why it would not: the same spacing,
        /// the same respect for a spot where a marker was erased, the same one label per so many
        /// meters. Both of these tools come through here.
        /// </summary>
        private static Verdict Consider(Found found, bool apply, List<Written> sofar)
        {
            if (!TgmConfig.CategoryEnabled.TryGetValue(found.Cat, out var enabled) || !enabled.Value)
                return Verdict.KindOff;

            float spacing = TgmConfig.MarkerSpacing.TryGetValue(found.Cat, out var s) ? s.Value : 1f;
            float radius = Mathf.Max(spacing, found.Radius);
            var at = found.DedupeCenter;
            if (ClientPins.HasPinNear(found.Icon, at, radius) || Near(sofar, found.Icon, at, radius))
                return Verdict.Marked;
            if (ClientPins.IsSuppressed(found.Icon, at, radius))
                return Verdict.Erased;

            float labelSpacing = TgmConfig.LabelSpacing.TryGetValue(found.Cat, out var l) ? l.Value : 0f;
            float labelRadius = labelSpacing > 0f ? Mathf.Max(labelSpacing, found.Radius) : labelSpacing;
            bool label = labelSpacing >= 0f
                && !ClientPins.HasLabeledPinNear(found.Icon, found.Name, at, labelRadius)
                && !NearLabeled(sofar, found.Icon, found.Name, at, labelRadius);

            // A refusal has to be reported as one. The store turns a marker down when there is
            // nobody to own it -- spawning, logging out -- and counting that as written would have
            // a test session built on a report of markers that are not there.
            if (apply && ClientPins.CreateShared(label ? found.Name : "", found.Pos, found.Icon,
                    found.Cat.ToString(), auto: true, source: found.Source) == null)
                return Verdict.Refused;
            sofar.Add(new Written
            {
                Icon = found.Icon, Name = label ? found.Name : "", At = at, Radius = found.Radius,
            });
            return Verdict.Written;
        }

        private static bool Near(List<Written> sofar, string icon, Vector3 pos, float radius)
        {
            for (int i = 0; i < sofar.Count; i++)
            {
                var had = sofar[i];
                if (!IconRegistry.SameKey(had.Icon, icon)) continue;
                if (Geo.FlatDistance(had.At, pos) <= Mathf.Max(radius, had.Radius)) return true;
            }
            return false;
        }

        private static bool NearLabeled(List<Written> sofar, string icon, string name, Vector3 pos, float radius)
        {
            for (int i = 0; i < sofar.Count; i++)
            {
                var had = sofar[i];
                if (string.IsNullOrEmpty(had.Name)) continue;
                if (!IconRegistry.SameKey(had.Icon, icon)) continue;
                if (!string.Equals(had.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (Geo.FlatDistance(had.At, pos) <= Mathf.Max(radius, had.Radius)) return true;
            }
            return false;
        }

        // ── odds and ends ───────────────────────────────────────────────────────────

        /// <summary>What the catalog makes of one thing standing at one place.</summary>
        private static bool Build(LocationNames.Standing hit, out Found found)
        {
            found = null;
            if (string.IsNullOrEmpty(hit.Prefab)) return false;
            // A radius means the generator placed it, and the catalog has its own road for those:
            // the marker belongs on the location's origin with the location's reach, so that two
            // people finding one tar pit write one marker.
            if (hit.Radius > 0f)
                return Catalog.TryClassifyLocation(Entry(hit.Prefab, hit.Pos, hit.Radius), out found);
            var scene = ZNetScene.instance;
            return Catalog.TryClassifyPrefab(scene != null ? scene.GetPrefab(hit.Prefab) : null,
                hit.Prefab, hit.Pos, out found);
        }

        private static LocationIndex.Entry Entry(string prefab, Vector3 pos, float radius) =>
            new LocationIndex.Entry
            {
                Prefab = prefab,
                Pos = pos,
                Radius = Mathf.Max(radius, 4f),
                HasInterior = Catalog.LocationHasInterior(prefab),
            };

        /// <summary>
        /// Is this place one you have uncovered? Your own exploring and everyone else's, because
        /// the map you are looking at is the sum of both and "where I have explored" is a question
        /// about that map.
        /// </summary>
        private static bool Explored(Minimap map, Vector3 pos)
        {
            if (map == null) return false;
            int size = map.m_textureSize;
            float pixel = map.m_pixelSize;
            if (size <= 0 || pixel <= 0f) return false;
            float half = size * 0.5f;
            int px = Mathf.RoundToInt(pos.x / pixel + half);
            int py = Mathf.RoundToInt(pos.z / pixel + half);
            if (px < 0 || py < 0 || px >= size || py >= size) return false;
            int index = py * size + px;
            var mine = Access.Explored(map);
            if (mine != null && index < mine.Length && mine[index]) return true;
            var theirs = Access.ExploredOthers(map);
            return theirs != null && index < theirs.Length && theirs[index];
        }

        private static void Count(SortedDictionary<string, int> tally, string key)
        {
            tally.TryGetValue(key, out int n);
            tally[key] = n + 1;
        }

        /// <summary>A sorted set as a list, because string.Join wants one and this targets an old framework.</summary>
        private static List<string> New(SortedSet<string> set) => new List<string>(set);
    }
}
