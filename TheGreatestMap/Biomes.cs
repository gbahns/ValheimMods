using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Which biome a marker stands in.
    ///
    /// <para>Asked of the world generator rather than stored on the marker, because the generator
    /// answers from the seed alone: it needs nobody standing there, no ZDO loaded and no trip to
    /// the server, and it gives the same answer on every machine. A marker does not move, so an
    /// answer is good forever -- which is what makes it safe to cache by position and cheap enough
    /// for the drawing path, where everything is asked about every marker on every relayout.</para>
    ///
    /// <para>The alternative was <c>ZoneLocation.m_biome</c>, which is wrong for this: that says
    /// which biomes a location is <em>allowed</em> to spawn in, as a set of flags, not where the
    /// one in front of you actually is. And it can only answer about markers that know they stand
    /// for a location, which is the smaller half of any map.</para>
    /// </summary>
    internal static class Biomes
    {
        private static readonly Dictionary<long, Heightmap.Biome> _at = new Dictionary<long, Heightmap.Biome>();

        /// <summary>Whether the generator is there to ask. False in the main menu, and briefly on login.</summary>
        internal static bool Known => WorldGenerator.instance != null;

        /// <summary>
        /// The biome at a place, to the meter. Rounded because two markers a hand's breadth apart
        /// are in the same biome and there is no sense asking twice, and because an exact float
        /// key would never hit the cache.
        /// </summary>
        internal static Heightmap.Biome Of(Vector3 pos)
        {
            long key = ((long)Mathf.RoundToInt(pos.x) << 32) ^ (uint)Mathf.RoundToInt(pos.z);
            if (_at.TryGetValue(key, out var known)) return known;
            var world = WorldGenerator.instance;
            if (world == null) return Heightmap.Biome.None;   // not cached: there is a world coming
            var biome = world.GetBiome(pos);
            _at[key] = biome;
            return biome;
        }

        /// <summary>Forget the answers. Only for a change of world, where every one of them is wrong.</summary>
        internal static void Forget() => _at.Clear();

        /// <summary>What a player calls it: "Black Forest", not "BlackForest".</summary>
        internal static string Label(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows:     return "Meadows";
                case Heightmap.Biome.BlackForest: return "Black Forest";
                case Heightmap.Biome.Swamp:       return "Swamp";
                case Heightmap.Biome.Mountain:    return "Mountains";
                case Heightmap.Biome.Plains:      return "Plains";
                case Heightmap.Biome.Mistlands:   return "Mistlands";
                case Heightmap.Biome.AshLands:    return "Ashlands";
                case Heightmap.Biome.DeepNorth:   return "Deep North";
                case Heightmap.Biome.Ocean:       return "Ocean";
                default:                          return "Nowhere";
            }
        }

        /// <summary>
        /// The order a player meets them in, so the rows read as a journey rather than as an
        /// alphabet. Anything unheard-of sorts last rather than first.
        /// </summary>
        internal static int Order(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows:     return 0;
                case Heightmap.Biome.BlackForest: return 1;
                case Heightmap.Biome.Swamp:       return 2;
                case Heightmap.Biome.Mountain:    return 3;
                case Heightmap.Biome.Plains:      return 4;
                case Heightmap.Biome.Mistlands:   return 5;
                case Heightmap.Biome.AshLands:    return 6;
                case Heightmap.Biome.DeepNorth:   return 7;
                case Heightmap.Biome.Ocean:       return 8;
                default:                          return 9;
            }
        }

        /// <summary>The key a hidden biome row is written down under: one kind's biome, not every kind's.</summary>
        internal static string Key(Category kind, Heightmap.Biome biome) => kind.ToString() + "/" + biome.ToString();
    }
}
