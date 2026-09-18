using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// Builds a rail fence on the Stable Ship's deck -- a 1 m post at each corner, 2 m beams along
    /// the sides -- out of vanilla pole and beam pieces reduced to bare geometry: renderers and
    /// colliders, no scripts.
    ///
    /// Two lessons from the first attempt (solid wall panels).  The pieces must not keep their
    /// ZNetView / WearNTear / Piece components: as live build pieces they had no support on a ship
    /// and collapsed a few seconds after spawning, dropping their wood.  And their colliders must
    /// not belong to the ship's rigidbody: Unity works a body's center of mass out from its
    /// colliders, so geometry above deck made the ship top-heavy and it rolled over.  The pen
    /// therefore sits under its own kinematic rigidbody, which follows the ship's transform but
    /// keeps its colliders (and mass) to itself.
    ///
    /// Still a first pass on placement: DeckHeight and the pen's size are estimates.
    /// </summary>
    internal static class AnimalPen
    {
        // All three have their pivot at the piece's center.
        private const string PostPrefabName  = "wood_pole";   // 0.4 x 1.0 x 0.4
        private const string Beam2PrefabName = "wood_beam";   // 2.0 x 0.4 x 0.4, along its local X
        private const string Beam1PrefabName = "wood_beam_1"; // 1.0 x 0.4 x 0.4

        private const float PenLength  = 4f;   // along the hull (bow-to-stern)
        private const float PenWidth   = 3f;   // across the beam
        private const float DeckHeight = 1.2f; // rough guess for deck height above the hull's local origin

        // Where the posts stand, relative to the hull's origin.  The earlier wall panels were
        // centered on DeckHeight, so their bottoms sat at DeckHeight - 0.5, and Greg judged those
        // "on the deck": the posts start there too.
        private const float PostBottom = DeckHeight - 0.5f;
        private const float PostHeight = 1f;
        // Rail centers above PostBottom.  Beams are 0.4 thick, so 0.4 and 0.8 cover 0.2..1.0 with
        // no gap between them, and nothing a boar could get under.
        private static readonly float[] RailHeights = { 0.4f, 0.8f };

        internal static void Build(GameObject clone)
        {
            var post  = PrefabManager.Instance.GetPrefab(PostPrefabName);
            var beam2 = PrefabManager.Instance.GetPrefab(Beam2PrefabName);
            var beam1 = PrefabManager.Instance.GetPrefab(Beam1PrefabName);
            if (post == null || beam2 == null || beam1 == null)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] Animal pen: '{PostPrefabName}', '{Beam2PrefabName}' or '{Beam1PrefabName}' not found; no pen built.");
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
            int pieces = 0;

            foreach (var corner in new[] { new Vector3(halfWid, 0, halfLen), new Vector3(-halfWid, 0, halfLen),
                                           new Vector3(halfWid, 0, -halfLen), new Vector3(-halfWid, 0, -halfLen) })
            {
                Place(post, pen.transform, corner + new Vector3(0f, PostBottom + PostHeight / 2f, 0f), Quaternion.identity);
                pieces++;
            }

            // Four sides, no gate: a gate needs its Door script, and that needs the networking
            // these pieces lose.  Loading is the player's problem for now.
            foreach (float rail in RailHeights)
            {
                float y = PostBottom + rail;
                pieces += PlaceRail(pen.transform, beam2, beam1, new Vector3(0f, y, halfLen), 0f, PenWidth);    // bow
                pieces += PlaceRail(pen.transform, beam2, beam1, new Vector3(0f, y, -halfLen), 0f, PenWidth);   // stern
                pieces += PlaceRail(pen.transform, beam2, beam1, new Vector3(halfWid, y, 0f), 90f, PenLength);  // starboard
                pieces += PlaceRail(pen.transform, beam2, beam1, new Vector3(-halfWid, y, 0f), 90f, PenLength); // port
            }

            Jotunn.Logger.LogInfo($"[TheGreatestShips] Built animal pen: {pieces} posts and rails (first pass; position and size are estimates).");
        }

        // A rail of the given length, centered on `center`, running along the yaw'd X axis: as
        // many 2 m beams as fit, then a 1 m beam for an odd metre, laid end to end from one post
        // to the other.
        private static int PlaceRail(Transform parent, GameObject beam2, GameObject beam1, Vector3 center, float yaw, float length)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            float x = -length / 2f;
            int placed = 0;
            while (length - (x + length / 2f) >= 2f - 0.01f)
            {
                Place(beam2, parent, center + rotation * new Vector3(x + 1f, 0f, 0f), rotation);
                x += 2f; placed++;
            }
            if (length - (x + length / 2f) >= 1f - 0.01f)
            {
                Place(beam1, parent, center + rotation * new Vector3(x + 0.5f, 0f, 0f), rotation);
                placed++;
            }
            return placed;
        }

        private static void Place(GameObject prefab, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var piece = Object.Instantiate(prefab, parent);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = localRotation;
            StripToGeometry(piece);
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
