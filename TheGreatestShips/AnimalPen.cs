using Jotunn.Managers;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// Builds a solid-walled pen on the Stable Ship's deck out of real vanilla wall/gate
    /// pieces, instantiated as plain child objects (not through the build system) so they move
    /// with the ship and collide like the real thing.
    ///
    /// First pass: the deck height, pen size and wall count below are estimates, not measured
    /// in-game.  Expect to retune DeckHeight (and possibly the pen's position along the hull)
    /// once this is actually sailed and looked at.
    /// </summary>
    internal static class AnimalPen
    {
        private const string WallPrefabName = "wood_wall_half"; // solid, 2m wide x 1m tall x 0.3m thick
        private const string GatePrefabName = "wood_gate";

        private const int   WallRows    = 3;    // stacked height: 3m, comfortably above player head height
        private const float PenLength   = 4f;   // along the hull (bow-to-stern)
        private const float PenWidth    = 3f;   // across the beam
        private const float DeckHeight  = 1.2f; // rough guess for deck height above the hull's local origin

        internal static void Build(GameObject clone)
        {
            var wallPrefab = PrefabManager.Instance.GetPrefab(WallPrefabName);
            var gatePrefab = PrefabManager.Instance.GetPrefab(GatePrefabName);
            if (wallPrefab == null || gatePrefab == null)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] Animal pen: '{WallPrefabName}' or '{GatePrefabName}' not found; no pen built.");
                return;
            }

            float halfLen = PenLength / 2f;
            float halfWid = PenWidth / 2f;

            // Bow, port and starboard sides are solid walls; the stern side is a gate, for
            // loading animals aboard.
            PlaceWallRun(clone, wallPrefab, new Vector3(0f, DeckHeight, halfLen), 0f, PenWidth);
            PlaceWallRun(clone, wallPrefab, new Vector3(halfWid, DeckHeight, 0f), 90f, PenLength);
            PlaceWallRun(clone, wallPrefab, new Vector3(-halfWid, DeckHeight, 0f), 90f, PenLength);

            var gate = Object.Instantiate(gatePrefab, clone.transform);
            gate.transform.localPosition = new Vector3(0f, DeckHeight, -halfLen);
            gate.transform.localRotation = Quaternion.identity;

            Jotunn.Logger.LogInfo("[TheGreatestShips] Built animal pen (first pass; position and size are estimates).");
        }

        // Places a run of 2m-wide wall segments, stacked WallRows high, centered on `center` and
        // rotated `yaw` degrees around Y so the run spans `length` along its own local X axis.
        private static void PlaceWallRun(GameObject parent, GameObject wallPrefab, Vector3 center, float yaw, float length)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            int segments = Mathf.Max(1, Mathf.RoundToInt(length / 2f)); // each segment is 2m wide

            for (int i = 0; i < segments; i++)
            {
                float alongOffset = (i - (segments - 1) / 2f) * 2f;
                var offset = rotation * new Vector3(alongOffset, 0f, 0f);

                for (int row = 0; row < WallRows; row++)
                {
                    var wall = Object.Instantiate(wallPrefab, parent.transform);
                    wall.transform.localPosition = center + offset + new Vector3(0f, row * 1f, 0f);
                    wall.transform.localRotation = rotation;
                }
            }
        }
    }
}
