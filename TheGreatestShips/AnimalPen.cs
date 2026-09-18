using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// Builds a solid-walled pen on the Stable Ship's deck out of vanilla wall pieces reduced to
    /// bare geometry: renderers and colliders, no scripts.
    ///
    /// Two lessons from the first attempt.  The pieces must not keep their ZNetView / WearNTear /
    /// Piece components: as live build pieces they had no support on a ship and collapsed a few
    /// seconds after spawning, dropping their wood.  And their colliders must not belong to the
    /// ship's rigidbody: Unity works a body's center of mass out from its colliders, so a wall of
    /// them above deck made the ship top-heavy and it rolled over.  The pen therefore sits under
    /// its own kinematic rigidbody, which follows the ship's transform but keeps its colliders
    /// (and mass) to itself.
    ///
    /// Still a first pass on placement: DeckHeight and the pen's size are estimates.
    /// </summary>
    internal static class AnimalPen
    {
        private const string WallPrefabName = "wood_wall_half"; // solid, 2m wide x 1m tall x 0.3m thick

        private const int   WallRows   = 2;    // stacked height: 2m, about head height (3 looked too tall in-game)
        private const float PenLength  = 4f;   // along the hull (bow-to-stern)
        private const float PenWidth   = 3f;   // across the beam
        private const float DeckHeight = 1.2f; // rough guess for deck height above the hull's local origin

        internal static void Build(GameObject clone)
        {
            var wallPrefab = PrefabManager.Instance.GetPrefab(WallPrefabName);
            if (wallPrefab == null)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] Animal pen: '{WallPrefabName}' not found; no pen built.");
                return;
            }

            // Everything hangs off this.  Kinematic: driven by the ship's transform, never by
            // forces, and its colliders are its own rather than compounded into the hull's.
            var pen = new GameObject("animal_pen");
            pen.transform.SetParent(clone.transform, false);
            var body = pen.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity  = false;

            float halfLen = PenLength / 2f;
            float halfWid = PenWidth / 2f;

            // Bow, port and starboard are walled; the stern is left open for loading animals.
            // (A gate needs its Door script, and that needs the networking these pieces lose.)
            int walls = 0;
            walls += PlaceWallRun(pen.transform, wallPrefab, new Vector3(0f, DeckHeight, halfLen), 0f, PenWidth);
            walls += PlaceWallRun(pen.transform, wallPrefab, new Vector3(halfWid, DeckHeight, 0f), 90f, PenLength);
            walls += PlaceWallRun(pen.transform, wallPrefab, new Vector3(-halfWid, DeckHeight, 0f), 90f, PenLength);

            Jotunn.Logger.LogInfo($"[TheGreatestShips] Built animal pen: {walls} wall panels (first pass; position and size are estimates).");
        }

        private static int PlaceWallRun(Transform parent, GameObject wallPrefab, Vector3 center, float yaw, float length)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            int segments = Mathf.Max(1, Mathf.RoundToInt(length / 2f)); // each panel is 2m wide
            int placed = 0;

            for (int i = 0; i < segments; i++)
            {
                float alongOffset = (i - (segments - 1) / 2f) * 2f;
                var offset = rotation * new Vector3(alongOffset, 0f, 0f);

                for (int row = 0; row < WallRows; row++)
                {
                    var wall = Object.Instantiate(wallPrefab, parent);
                    wall.transform.localPosition = center + offset + new Vector3(0f, row * 1f, 0f);
                    wall.transform.localRotation = rotation;
                    StripToGeometry(wall);
                    placed++;
                }
            }
            return placed;
        }

        // Removes every script from the piece and its children, leaving renderers, mesh filters,
        // LOD groups and colliders.  ZNetView goes last: other components RequireComponent it.
        private static void StripToGeometry(GameObject piece)
        {
            var scripts = new List<MonoBehaviour>(piece.GetComponentsInChildren<MonoBehaviour>(true));
            scripts.Sort((a, b) => (a is ZNetView ? 1 : 0) - (b is ZNetView ? 1 : 0));
            foreach (var script in scripts)
                if (script != null) Object.DestroyImmediate(script);
        }
    }
}
