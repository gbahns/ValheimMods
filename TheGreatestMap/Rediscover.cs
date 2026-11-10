using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Put back a pin the game had marked for you and something took off your map.
    ///
    /// <para>These are the one kind of mark on a map that cannot be recovered by playing. Where a
    /// boss altar stands is written in the world, and whether <em>you</em> have read a vegvisir
    /// for it is written in your character file -- nowhere else, and nothing rebuilds it. The
    /// world will not tell a client about an altar nobody has been sent to, and vanilla draws no
    /// landmark pin for one, so the pin in the character file is the whole of the record. Lose it
    /// and the only honest way back is to walk out and read another stone, which picks its own
    /// altar and may well pick a different one.</para>
    ///
    /// <para>Written because one was lost: a Bonemass altar at (-4033, -581), discovered, present
    /// in a character file of 2026-11-06 and gone from the same character two days later, while
    /// the other twelve stood. Whichever sweep took it, the map had no way to say so and no way
    /// to put it back. The guards in <see cref="ClientPins.TheGamesOwn"/> stop it happening
    /// again; this is for the one already gone, and for the next time something is.</para>
    ///
    /// <para>Deliberately not automatic. Marking an altar nobody discovered is a cheat, and only
    /// the player knows which they had -- so this asks for the spot, one at a time, and the
    /// listing is there to be read against a backup rather than taken as a to-do list.</para>
    /// </summary>
    internal static class Rediscover
    {
        /// <summary>What a stone says: the pin it puts up, under the name the game gives it.</summary>
        internal struct Mark
        {
            public Minimap.PinType Type;
            public string PinName;
        }

        /// <summary>How near the spot asked about a location has to be to be the one meant.</summary>
        private const float Near = 64f;

        private static Dictionary<string, Mark> _stones;

        /// <summary>
        /// Which pin each location's own stone puts up, by location.
        ///
        /// <para>Read off the stones rather than written down here, because the stones are where
        /// the answer actually lives: a vegvisir carries the location it points at, the pin type
        /// and the pin's name together as prefab data, and so does a rune stone. That makes the
        /// table the game's own -- including the name, which is a key out of its text table
        /// ($enemy_bonemass, $hud_pin_hildir1) and not something this mod should be inventing.
        /// Keyed by location, so unlike <see cref="OwnMarks"/>'s reading of the same stones there
        /// is no trouble with two stones sharing a pin type: here the location is the question.</para>
        /// </summary>
        internal static Dictionary<string, Mark> Stones()
        {
            if (_stones != null) return _stones;
            var found = new Dictionary<string, Mark>(StringComparer.OrdinalIgnoreCase);
            var scene = ZNetScene.instance;
            if (scene == null) return found;
            try
            {
                foreach (var prefab in scene.m_prefabs)
                {
                    if (prefab == null) continue;
                    var veg = prefab.GetComponent<Vegvisir>();
                    if (veg != null && veg.m_locations != null)
                        foreach (var one in veg.m_locations)
                            if (one != null) Note(found, one.m_locationName, one.m_pinType, one.m_pinName);
                    var rune = prefab.GetComponent<RuneStone>();
                    if (rune != null) Note(found, rune.m_locationName, rune.m_pinType, rune.m_pinName);
                }
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Could not read which pin each location's stone puts up: {e.Message}");
                return found;
            }
            _stones = found;
            return found;
        }

        private static void Note(Dictionary<string, Mark> found, string location,
                                 Minimap.PinType type, string pinName)
        {
            if (string.IsNullOrEmpty(location) || type == Minimap.PinType.None) return;
            if (found.ContainsKey(location)) return;
            found[location] = new Mark { Type = type, PinName = pinName ?? "" };
        }

        /// <summary>The catalog changed, or the world did: the stones are read again.</summary>
        internal static void Forget() => _stones = null;

        /// <summary>
        /// Every place the world knows that a stone would mark, and whether a pin of yours stands
        /// on it. Read against a backup of the character file, this says which one went missing.
        /// </summary>
        internal static List<string> Report()
        {
            var said = new List<string>();
            var map = Minimap.instance;
            var stones = Stones();
            if (map == null || stones.Count == 0)
            {
                said.Add("Nothing to say yet: the world is not loaded.");
                return said;
            }
            var rows = new List<string>();
            int marked = 0, bare = 0;
            foreach (var kv in WorldPins.Known())
            {
                if (!stones.TryGetValue(kv.Value, out var stone)) continue;
                bool has = Standing(map, kv.Key, stone.Type);
                if (has) marked++; else bare++;
                rows.Add(string.Format("  {0}  {1,8:0} {2,8:0}  {3}",
                                       has ? "marked " : "bare   ", kv.Key.x, kv.Key.z, kv.Value));
            }
            rows.Sort(StringComparer.Ordinal);
            said.Add($"{marked} of these places are marked on your map and {bare} are not. " +
                     "Bare is the ordinary case -- it means nobody has been sent there -- so read " +
                     "this against what your character file held before, not as a list of things to fix.");
            said.AddRange(rows);
            said.Add("Put one back with: tgm_rediscover <x> <z>");
            return said;
        }

        /// <summary>Is a pin of this type standing on this spot already?</summary>
        private static bool Standing(Minimap map, Vector3 pos, Minimap.PinType type)
        {
            foreach (var pin in Access.Pins(map))
                if (pin != null && pin.m_type == type && Utils.DistanceXZ(pin.m_pos, pos) < 1f) return true;
            return false;
        }

        /// <summary>
        /// Mark the place nearest this spot the way the game would have. The pin is saved with the
        /// character, exactly as vanilla's own is, so it survives the session that puts it back.
        /// </summary>
        internal static string Put(Vector3 asked, string named)
        {
            var map = Minimap.instance;
            if (map == null) return "No map.";
            var stones = Stones();
            // Named outright, and then the coordinates are taken as given. For the case the
            // listing cannot help with: what a client is told about the world's locations comes
            // from the server and need not include an altar, so a spot read out of a backup of
            // the character file may be a place this client cannot look up at all. The stone
            // still says which pin that location wears, which is the part worth being right.
            if (!string.IsNullOrEmpty(named))
            {
                if (!stones.TryGetValue(named, out var byName))
                    return $"No stone in the game marks a location called '{named}'. The ones that do: "
                         + string.Join(", ", Locations()) + ".";
                if (Standing(map, asked, byName.Type))
                    return $"{named} at ({asked.x:0}, {asked.z:0}) is already marked on your map.";
                return Add(map, asked, named, byName);
            }
            string where = null;
            Vector3 at = Vector3.zero;
            float best = Near;
            foreach (var kv in WorldPins.Known())
            {
                if (!stones.ContainsKey(kv.Value)) continue;
                float d = Utils.DistanceXZ(kv.Key, asked);
                if (d >= best) continue;
                best = d;
                where = kv.Value;
                at = kv.Key;
            }
            if (where == null)
                return $"The world knows of no place a stone marks within {Near:0} m of " +
                       $"({asked.x:0}, {asked.z:0}). tgm_rediscover lists the ones it does know, or name " +
                       "the place yourself: tgm_rediscover <x> <z> <location>, one of " +
                       string.Join(", ", Locations()) + ".";
            var stone = stones[where];
            if (Standing(map, at, stone.Type))
                return $"{where} at ({at.x:0}, {at.z:0}) is already marked on your map.";
            return Add(map, at, where, stone);
        }

        /// <summary>The locations the game's own stones can mark, for a usage line to name.</summary>
        private static List<string> Locations()
        {
            var said = new List<string>(Stones().Keys);
            said.Sort(StringComparer.Ordinal);
            return said;
        }

        /// <summary>Put the pin up and write it down, which is the whole of what a stone does.</summary>
        private static string Add(Minimap map, Vector3 at, string where, Mark stone)
        {
            var pin = map.AddPin(at, stone.Type, stone.PinName, true, false, 0L);
            if (pin == null) return $"The map refused a pin for {where} at ({at.x:0}, {at.z:0}).";
            map.SaveMapData();
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Put back the game's own {stone.Type} pin for {where} at " +
                $"({at.x:0.0}, {at.z:0.0}), named {stone.PinName}. Saved with the character, as vanilla saves it.");
            return $"Marked {where} at ({at.x:0}, {at.z:0}) as {stone.Type}, the way a stone would have. " +
                   "It is saved with your character.";
        }
    }
}
