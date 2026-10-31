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
        internal static List<string> Adopt(float reach, bool apply, bool plants)
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

            var meanings = PinMeanings(map, out var pictures);
            var hints = Hints();
            var places = LocationNames.Snapshot();
            var sofar = new List<Written>();
            var byThing = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byPicture = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var kindsOff = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var puzzling = new List<string>();
            var disagreed = new List<string>();
            var byRule = new List<string>();
            var plantOnly = new List<string>();
            var renamed = new List<string>();
            int written = 0, already = 0, erased = 0, off = 0, refused = 0;

            foreach (var pinData in hand)
            {
                var meant = Meaning(meanings, (int)pinData.m_type);
                Count(byPicture, Picture(pictures, (int)pinData.m_type) + ", taken to mean " + Says(meant));

                string typed = (pinData.m_name ?? "").Trim();
                string hint = Hint(hints, typed);
                if (IsNever(hint)) { byRule.Add("  " + Where(pinData)); continue; }

                // Every answer the world has, ranked by what this pin was for and only then by
                // which is nearest. Nearest alone was the first rule here and it was wrong: a
                // hammer dropped on a troll cave has a mushroom eight meters from it and the cave
                // fifteen, so the mushroom answered, and the cave was never marked at all.
                var order = Order[meant];
                Found best = null, nearest = null;
                string nearestPrefab = null;
                Found plantPick = null;
                float bestDistance = 0f, nearestDistance = 0f, plantDistance = 0f;
                int bestRank = int.MaxValue;
                foreach (var hit in LocationNames.WhatStandsAt(pinData.m_pos, reach, places))
                {
                    if (!Build(hit, out var candidate)) continue;
                    float d = Geo.FlatDistance(hit.Pos, pinData.m_pos);
                    if (nearest == null || d < nearestDistance)
                    {
                        nearest = candidate;
                        nearestPrefab = hit.Prefab;
                        nearestDistance = d;
                    }

                    // What the player wrote on the pin outranks everything: it is the only record
                    // of what was meant, and an answer that disagrees with it is an answer about
                    // something else standing nearby.
                    if (hint != null) { if (!Matches(hint, hit.Prefab, candidate)) continue; }
                    else if (typed.Length > 0 && !Agrees(typed, hit.Prefab, candidate)) continue;

                    // Nobody marks plants, and a plant grows within twenty meters of everything,
                    // so left in the running it answers for whatever the pin really meant. Kept
                    // aside rather than dropped, so the listing can say that a plant is all there
                    // was -- and tested after the name, so a marker you did call "dandelion" is
                    // reported as a plant you declined rather than as a name nothing matched.
                    var sort = ClassOf(candidate.Cat);
                    if (sort == Sort.Plant && !plants)
                    {
                        if (plantPick == null || d < plantDistance) { plantPick = candidate; plantDistance = d; }
                        continue;
                    }

                    int rank = Array.IndexOf(order, sort);
                    if (best != null && rank > bestRank) continue;
                    if (best != null && rank == bestRank && d >= bestDistance) continue;
                    best = candidate;
                    bestRank = rank;
                    bestDistance = d;
                }

                if (best == null)
                {
                    // Four ways to find nothing, and they want four different answers -- these are
                    // the markers that stay, and what to do about each is a different decision.
                    if (plantPick != null)
                        plantOnly.Add($"  {Where(pinData)} -- nothing but {plantPick.Name} within {reach:0} m");
                    else if (hint != null)
                        disagreed.Add($"  {Where(pinData)} -- nothing matching \"{hint}\" within {reach:0} m");
                    else if (typed.Length > 0 && nearest != null)
                        disagreed.Add($"  {Where(pinData)} -- nearest is {nearest.Name} ({nearestPrefab}) " +
                                      $"at {nearestDistance:0} m, which does not match the name");
                    else if (typed.Length > 0)
                        disagreed.Add($"  {Where(pinData)} -- nothing at all within {reach:0} m");
                    else
                        puzzling.Add("  " + Where(pinData));
                    continue;
                }

                Count(byThing, Catalog.Prettify(best.Source) + " (" + best.Source + ")");
                var verdict = Consider(best, apply, sofar);
                switch (verdict)
                {
                    case Verdict.Written:  written++; break;
                    case Verdict.Marked:   already++; break;
                    case Verdict.Erased:   erased++;  break;
                    case Verdict.KindOff:  off++; kindsOff.Add(Categories.Label(best.Cat)); break;
                    case Verdict.Refused:  refused++; break;
                }
                // The pin goes when there is a marker standing for the same thing, whether this
                // run wrote it or found it already there. It stays where a marker was refused, so
                // that nothing is lost quietly: an erased spot and a switched-off kind are both
                // reasons to look rather than reasons to tidy up.
                if (verdict != Verdict.Written && verdict != Verdict.Marked) continue;
                if (typed.Length > 0)
                    renamed.Add($"  \"{pinData.m_name}\" -> {best.Name} ({best.Source}) at {bestDistance:0} m");
                if (apply) map.RemovePin(pinData);
            }

            lines.Add(apply
                ? $"{hand.Count} marker(s) placed by hand, looking {reach:0} m around each one."
                : $"{hand.Count} marker(s) placed by hand, looking {reach:0} m around each one. Nothing written yet.");
            if (byPicture.Count > 0)
            {
                lines.Add("The pictures on them, and what each one was read as:");
                foreach (var kv in byPicture) lines.Add($"  {kv.Value} x {kv.Key}");
            }
            lines.Add($"  {written} became recorded markers" + (apply ? ", and their hand-placed pins are gone." : "."));
            if (already > 0) lines.Add($"  {already} stood for something already marked" + (apply ? ", so the pin went and nothing was written." : "."));
            if (erased > 0) lines.Add($"  {erased} stand where a marker was erased, so none was written and the pin stays.");
            if (refused > 0) lines.Add($"  {refused} could not be written at all; the log says why. Their pins stay.");
            if (off > 0) lines.Add($"  {off} are of a kind that is switched off ({string.Join(", ", New(kindsOff))}), so the pin stays.");
            Section(lines, byRule, "your own hints say never to convert these, so nothing happens to them:", "the ones your hints keep");
            Section(lines, disagreed, $"these carry a name that nothing within {reach:0} m matches, so they stay as they are:", "the ones whose name disagreed");
            Section(lines, plantOnly, "these have nothing but plants near them. Nobody marks plants, so they stay; " +
                                      "tgm_adopt plants takes them anyway:", "the ones with only plants nearby");
            Section(lines, puzzling, $"these have nothing the catalog knows within {reach:0} m, so they stay exactly as they are:", "the ones with nothing nearby");
            if (byThing.Count > 0)
            {
                lines.Add("What the world said they were:");
                foreach (var kv in byThing) lines.Add($"  {kv.Key}: {kv.Value}");
            }
            Section(lines, renamed, "names you had typed, and what the marker says now:", "the names you had typed");
            if (apply && written > 0) ClientPins.Restyle();
            return lines;
        }

        /// <summary>
        /// How many of a listing's own lines reach the console. The whole list goes to the log
        /// whenever it is longer than that, because these lists are the point of a dry run -- they
        /// are the markers the tool will not touch, and a report showing twelve of forty-eight
        /// cannot be acted on. The console is spared six hundred lines; the log is not.
        /// </summary>
        private const int Shown = 40;

        private static void Section(List<string> lines, List<string> detail, string header, string what)
        {
            if (detail.Count == 0) return;
            lines.Add($"  {detail.Count}: " + header);
            for (int i = 0; i < detail.Count && i < Shown; i++) lines.Add(detail[i]);
            if (detail.Count <= Shown) return;
            lines.Add($"  ... and {detail.Count - Shown} more; all of them are in the log.");
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] tgm_adopt: {what}, in full:");
            foreach (var line in detail) TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_adopt:" + line);
        }

        // ── what a marker placed by hand was for ────────────────────────────────────

        /// <summary>
        /// What a hand-placed marker was for, as far as the picture on it says. Somebody who uses
        /// hammers for dungeons and dots for ore has already written down what each pin means, and
        /// reading the picture is reading that.
        /// </summary>
        private enum Meant { Unknown, Place, Resource, Fire }

        /// <summary>The kinds of answer the world can give, in the order they are worth ranking.</summary>
        private enum Sort { Place, Weak, Resource, Fire, Plant }

        /// <summary>
        /// Which answers come first for a pin that meant what. Distance decides only between
        /// answers of the same sort, which is the whole correction: the nearest thing to a hammer
        /// on a troll cave is a mushroom, and the nearest thing to a dot on a copper vein really
        /// is the copper.
        /// </summary>
        private static readonly Dictionary<Meant, Sort[]> Order = new Dictionary<Meant, Sort[]>
        {
            { Meant.Place,    new[] { Sort.Place, Sort.Weak, Sort.Resource, Sort.Fire, Sort.Plant } },
            { Meant.Resource, new[] { Sort.Resource, Sort.Place, Sort.Weak, Sort.Fire, Sort.Plant } },
            { Meant.Fire,     new[] { Sort.Fire, Sort.Place, Sort.Weak, Sort.Resource, Sort.Plant } },
            { Meant.Unknown,  new[] { Sort.Place, Sort.Resource, Sort.Weak, Sort.Fire, Sort.Plant } },
        };

        /// <summary>
        /// Which sort of answer a kind is. A structure is a weaker place than a dungeon or a camp
        /// on purpose: half of them are the scenery the generator scatters -- a dolmen, a ruin, a
        /// ship setting -- and a marker is not what anybody was pointing at when a real place is
        /// also in reach.
        /// </summary>
        private static Sort ClassOf(Category c)
        {
            switch (c)
            {
                case Category.Dungeon:
                case Category.Camp:
                case Category.BossAltar:
                case Category.Spawner:
                case Category.Runestone:
                case Category.Trader:    return Sort.Place;
                case Category.Structure: return Sort.Weak;
                case Category.Ore:       return Sort.Resource;
                case Category.Campfire:  return Sort.Fire;
                default:                 return Categories.IsGrown(c) ? Sort.Plant : Sort.Weak;
            }
        }

        /// <summary>
        /// What each of the game's placeable pins is taken to mean, and the picture it wears.
        ///
        /// <para>Read from the name of the sprite the game draws, because that is the only place
        /// the meaning lives: the five types are Icon0 to Icon4 and nothing in the game's own code
        /// says which of them is the hammer. Reading somebody else's asset names is a guess, so
        /// the config overrides it and the listing prints what it decided for every pin -- a wrong
        /// guess should be something you can see rather than something that happens to your map.
        /// </para>
        /// </summary>
        private static Dictionary<int, Meant> PinMeanings(Minimap map, out Dictionary<int, string> pictures)
        {
            var meanings = new Dictionary<int, Meant>();
            pictures = new Dictionary<int, string>();
            if (map != null && map.m_icons != null)
                foreach (var data in map.m_icons)
                {
                    int type = (int)data.m_name;
                    if (!PinTypes.IsPlayerPlaceable(type) || PinTypes.IsCustom(type)) continue;
                    string sprite = data.m_icon != null ? data.m_icon.name : "";
                    pictures[type] = (Minimap.PinType)type + " (" +
                                     (string.IsNullOrEmpty(sprite) ? "no picture" : sprite) + ")";
                    meanings[type] = FromPicture(sprite);
                }
            foreach (var pair in Split(TgmConfig.AdoptPinMeanings != null ? TgmConfig.AdoptPinMeanings.Value : ""))
            {
                if (!Enum.TryParse(pair.Key, true, out Minimap.PinType type)) continue;
                if (!Enum.TryParse(pair.Value, true, out Meant said)) continue;
                meanings[(int)type] = said;
            }
            return meanings;
        }

        /// <summary>
        /// What each of the game's own pictures is a picture of, by the name the game gives it.
        /// Read off a real map rather than guessed at: Icon0 is mapicon_fire, Icon1 mapicon_house,
        /// Icon2 mapicon_hammer and Icon3 mapicon_pin -- the plain dot, which is what a player
        /// reaches for when the thing is a vein of copper rather than a building. The guessing
        /// below could not name that one, and it is the commonest pin on a map by far: 246 of 609
        /// on the first map this was tried against, all of them read as meaning nothing in
        /// particular.
        /// </summary>
        private static readonly Dictionary<string, Meant> Pictures =
            new Dictionary<string, Meant>(StringComparer.OrdinalIgnoreCase)
            {
                { "mapicon_fire",   Meant.Fire },
                { "mapicon_house",  Meant.Place },
                { "mapicon_hammer", Meant.Place },
                { "mapicon_pin",    Meant.Resource },
            };

        /// <summary>
        /// A picture's name, read for what it is a picture of: the four known by name first, then
        /// the words in the name for a picture some other mod or some later version supplies. The
        /// listing prints whatever this decided, and the config overrides it, because a name is
        /// somebody else's and can change.
        /// </summary>
        private static Meant FromPicture(string sprite)
        {
            string name = (sprite ?? "").ToLowerInvariant();
            if (name.Length == 0) return Meant.Unknown;
            if (Pictures.TryGetValue(name, out var known)) return known;
            if (name.Contains("fire") || name.Contains("camp") || name.Contains("hearth")) return Meant.Fire;
            if (name.Contains("hammer") || name.Contains("house") || name.Contains("hut")
                || name.Contains("cave") || name.Contains("castle") || name.Contains("village")
                || name.Contains("mine")) return Meant.Place;
            if (name.Contains("pin") || name.Contains("dot") || name.Contains("circle")
                || name.Contains("point") || name.Contains("ore") || name.Contains("rock")
                || name.Contains("pick")) return Meant.Resource;
            return Meant.Unknown;
        }

        private static Meant Meaning(Dictionary<int, Meant> meanings, int type) =>
            meanings.TryGetValue(type, out var meant) ? meant : Meant.Unknown;

        private static string Picture(Dictionary<int, string> pictures, int type) =>
            pictures.TryGetValue(type, out var picture) ? picture : ((Minimap.PinType)type).ToString();

        private static string Says(Meant meant)
        {
            switch (meant)
            {
                case Meant.Place:    return "a place";
                case Meant.Resource: return "a resource";
                case Meant.Fire:     return "a campfire";
                default:             return "anything (no picture this knows)";
            }
        }

        // ── what the player wrote on the pin ────────────────────────────────────────

        /// <summary>The name hints from the config, keyed by the name as typed, in lower case.</summary>
        private static Dictionary<string, string> Hints()
        {
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Split(TgmConfig.AdoptNameHints != null ? TgmConfig.AdoptNameHints.Value : ""))
                hints[pair.Key] = pair.Value;
            return hints;
        }

        /// <summary>
        /// The hint for what somebody typed on a pin, or null. A leading star level is ignored, so
        /// one hint covers T, 1*T and 2*T: the star says how good the thing is rather than what it
        /// is.
        /// </summary>
        private static string Hint(Dictionary<string, string> hints, string typed)
        {
            if (hints.Count == 0 || string.IsNullOrEmpty(typed)) return null;
            if (hints.TryGetValue(typed, out var hit)) return hit;
            return hints.TryGetValue(Bare(typed), out hit) ? hit : null;
        }

        /// <summary>A hint that says this marker is never to be converted at all.</summary>
        private static bool IsNever(string hint) =>
            hint != null && (hint.Equals("none", StringComparison.OrdinalIgnoreCase)
                          || hint.Equals("never", StringComparison.OrdinalIgnoreCase)
                          || hint.Equals("leave", StringComparison.OrdinalIgnoreCase)
                          || hint == "-");

        /// <summary>Does this answer satisfy the hint? The hint names part of a prefab or of a name.</summary>
        private static bool Matches(string hint, string prefab, Found candidate) =>
            Has(prefab, hint) || Has(candidate.Name, hint);

        /// <summary>
        /// Does an answer agree with what the player wrote on the pin?
        ///
        /// <para>A name typed by hand is the only record of what was meant, and what was meant is
        /// often not the nearest thing: "draugr" on a swamp hut agrees, "2*boar" on a dandelion
        /// does not. Two letters cannot be tested this way at all -- "t" is inside half the words
        /// in the game -- so a short name agrees with nothing and waits for a hint. That is the
        /// honest answer rather than a cautious one: converting it would be a guess that destroys
        /// the only evidence of what the guess was about.</para>
        /// </summary>
        private static bool Agrees(string typed, string prefab, Found candidate)
        {
            foreach (var word in Bare(typed).Split(' ', ',', '-', '_', '/', '+'))
            {
                if (word.Length < 3) continue;
                if (Has(candidate.Name, word) || Has(prefab, word)
                    || Has(Categories.Label(candidate.Cat), word)) return true;
            }
            return false;
        }

        /// <summary>"2*boar" becomes "boar". Anything without a star level comes back as it went in.</summary>
        private static string Bare(string name)
        {
            int star = name.IndexOf('*');
            if (star <= 0 || star + 1 >= name.Length) return name;
            for (int i = 0; i < star; i++) if (!char.IsDigit(name[i])) return name;
            return name.Substring(star + 1).Trim();
        }

        private static bool Has(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && !string.IsNullOrEmpty(needle)
            && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>A "key=value, key=value" setting as pairs, both sides trimmed.</summary>
        private static List<KeyValuePair<string, string>> Split(string text)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(text)) return pairs;
            foreach (var part in text.Split(','))
            {
                int at = part.IndexOf('=');
                if (at <= 0) continue;
                string key = part.Substring(0, at).Trim();
                string value = part.Substring(at + 1).Trim();
                if (key.Length > 0 && value.Length > 0)
                    pairs.Add(new KeyValuePair<string, string>(key, value));
            }
            return pairs;
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
            int written = 0, already = 0, erased = 0, off = 0, unexplored = 0, capped = 0, refused = 0, interior = 0;
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
                        // A dungeon's contents are not on the map. Valheim builds the inside above
                        // y = 3000, so a marker written for one lands on the ground over its
                        // entrance: eighty yellow mushrooms on one hillside, which is what the
                        // burial chambers under it really hold. Recording skips them the same way,
                        // by refusing to record at all while the player is inside one.
                        if (Character.InInterior(pos)) { interior++; continue; }
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
            if (interior > 0) lines.Add($"  {interior} are inside dungeons, where nothing on the map can stand for them.");
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
            var prefab = scene != null ? scene.GetPrefab(hit.Prefab) : null;
            // A campfire first, where somebody built one. The catalog's prefab road refuses these
            // because a prefab cannot say who lit it, and the world lights its own in every camp
            // and village -- but the record carries a creator, which is the same question the
            // piece road asks the piece. Without this a pin dropped on a fire in the Ashlands
            // answers "charred spawner": the thing the fire was lit to suppress.
            if (hit.Built && Catalog.TryClassifyBuilt(prefab, hit.Prefab, hit.Pos, out found)) return true;
            return Catalog.TryClassifyPrefab(prefab, hit.Prefab, hit.Pos, out found);
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
