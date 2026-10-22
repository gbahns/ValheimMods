using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Forgetting crops that were written down before the map knew better.
    ///
    /// Planting a turnip grows the very same thing the wild one is, so a farm was recorded plant
    /// by plant: twenty-nine markers inside twenty meters on one map. Recording no longer does
    /// that -- ground somebody tilled grows crops, not discoveries -- but every map written
    /// before that still carries them, and asking players to find a console command to undo a
    /// mistake the mod made is not a repair.
    ///
    /// So it cleans up after itself, quietly, as the player goes about their business. There is
    /// no sweep of the whole world because there cannot be an honest one: whether ground is
    /// tilled is read from the terrain, and terrain exists only for the ground loaded around
    /// somebody. Guessing at a distance -- by how close the markers are to each other, or to a
    /// workbench -- is how a dozen different buildings all came to be called an abandoned farm.
    ///
    /// Walking through your own base is enough. Every few seconds a few nearby markers are
    /// asked, and a crop standing on tilled ground is erased for everyone. A farm clears itself
    /// the next time anyone walks across it, and nobody has to be told anything.
    ///
    /// Erased without a suppression, unlike erasing by hand: a suppression exists to stop a spot
    /// being recorded again, and recording already refuses this ground. One fewer thing for every
    /// map to carry forever.
    /// </summary>
    internal static class Tilled
    {
        /// <summary>How far from the player to look. Terrain is loaded well past this.</summary>
        private const float Reach = 64f;

        private const float EverySeconds = 3f;

        /// <summary>Terrain readings per pass, so a crowded base costs nothing noticeable.</summary>
        private const int PerPass = 40;

        private static float _next;
        private static int _forgotten;

        internal static void Reset()
        {
            _next = 0f;
            _forgotten = 0;
        }

        internal static void Update()
        {
            if (TgmConfig.ForgetTilledCrops != null && !TgmConfig.ForgetTilledCrops.Value) return;
            if (TgmConfig.RecordInBase != null && TgmConfig.RecordInBase.Value) return;
            if (Time.time < _next) return;
            _next = Time.time + EverySeconds;

            var player = Player.m_localPlayer;
            if (player == null || !PersonalMap.Loaded) return;
            var here = player.transform.position;

            // Distance first, because it is the cheap test and it is what rules nearly every
            // marker out. Taking a slice of the whole map instead, and asking how far away each
            // one in the slice was, meant standing in a farm did nothing for several minutes
            // while the slice worked its way round to those markers.
            var doomed = new List<SharedPin>();
            int asked = 0;
            foreach (var pin in ClientPins.All)
            {
                if (pin == null || !pin.Auto) continue;
                if (Geo.FlatDistance(here, pin.Pos) > Reach) continue;
                var kind = ClientPins.KindOf(pin);
                if (!kind.HasValue || !Categories.IsGrown(kind.Value)) continue;
                // Reading the terrain is the dear part, so only so many of those per pass; what
                // is left is still standing there next time.
                if (++asked > PerPass) break;
                if (!OnTilledGround(pin.Pos)) continue;
                doomed.Add(pin);
            }

            foreach (var pin in doomed)
            {
                ClientPins.RemoveStale(pin.Key);
                _forgotten++;
            }
            if (doomed.Count > 0)
            {
                ClientPins.Restyle();
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Forgot {doomed.Count} crop marker(s) standing on tilled ground " +
                    $"({_forgotten} so far). Planted ground is not a discovery; the erasure travels like any other.");
            }
        }

        private static bool OnTilledGround(Vector3 pos)
        {
            try
            {
                var ground = Heightmap.FindHeightmap(pos);
                return ground != null && ground.IsCultivated(pos);
            }
            catch (System.Exception) { return false; }
        }
    }
}
