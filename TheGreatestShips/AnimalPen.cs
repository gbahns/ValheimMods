using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// Where a ship's livestock pen goes and how big it is, in the hull's local units: on a ship
    /// with a Hull Scale, everything here scales with it.  Positions are for the VikingShip hull,
    /// whose deck is at y = 0.58, mast at z = 0.28, aft rowing benches from z = -2.78, chest from
    /// z = 3.06.  Rails are whole beams when (Width - 0.4) and (Length - 0.4) split into 2 m and
    /// 1 m pieces.
    /// </summary>
    internal sealed class PenSpec
    {
        public float   Length;       // along the hull (bow-to-stern)
        public float   Width;        // across the beam
        public float   CenterZ;      // pen center, fore (+) or aft (-) of the hull's origin
        public float   PostHeight;   // corner posts; the 1 m pole is scaled to this
        public float[] RailHeights;  // rail centers above the deck; rails are 0.4 thick
    }

    /// <summary>
    /// Builds a rail fence on a ship's deck -- a post at each corner, beams along the sides --
    /// out of vanilla pole and beam pieces reduced to bare geometry: renderers and colliders, no
    /// scripts.  The rails form a solid band, which also blocks the line-of-sight ray monster AI
    /// uses to spot a target: a penned animal can't see threats and they can't see it.
    ///
    /// Three lessons from earlier attempts.  The pieces must not keep their ZNetView / WearNTear /
    /// Piece components: as live build pieces they had no support on a ship and collapsed a few
    /// seconds after spawning, dropping their wood.  Their colliders can't simply be added to the
    /// ship's rigidbody either: Unity works a body's center of mass out from its colliders, so
    /// geometry above deck made the ship top-heavy and it rolled over.  And they can't sit on a
    /// kinematic rigidbody of their own: a kinematic body moved through its transform teleports
    /// each physics step instead of sweeping, so when the hull jolted on a rock the rails jumped
    /// straight through the boars.  So the colliders do belong to the ship's body -- real swept
    /// contact, like the hull -- and PenBallast pins the body's center of mass and inertia to the
    /// hull-only values so the rails add no top-heaviness.
    /// </summary>
    internal static class AnimalPen
    {
        // All three have their pivot at the piece's center.
        private const string PostPrefabName  = "wood_pole";   // 0.4 x 1.0 x 0.4
        private const string Beam2PrefabName = "wood_beam";   // 2.0 x 0.4 x 0.4, along its local X
        private const string Beam1PrefabName = "wood_beam_1"; // 1.0 x 0.4 x 0.4

        // The VikingShip's deck, above the hull's local origin: everything that stands on it
        // (benches, ladders, chest, controls) sits at y = 0.53..0.62 in the prefab.
        private const float DeckHeight = 0.58f;
        private const float PostWidth  = 0.4f;

        internal static void Build(GameObject clone, PenSpec spec)
        {
            var post  = PrefabManager.Instance.GetPrefab(PostPrefabName);
            var beam2 = PrefabManager.Instance.GetPrefab(Beam2PrefabName);
            var beam1 = PrefabManager.Instance.GetPrefab(Beam1PrefabName);
            if (post == null || beam2 == null || beam1 == null)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] Animal pen: '{PostPrefabName}', '{Beam2PrefabName}' or '{Beam1PrefabName}' not found; no pen built.");
                return;
            }

            // No rigidbody of its own: the pieces' colliders compound into the hull's body, and
            // PenBallast (which runs on each spawned ship, not on the prefab) keeps that body
            // balanced as if they weren't there.
            var pen = new GameObject("animal_pen");
            pen.transform.SetParent(clone.transform, false);
            pen.AddComponent<PenBallast>();

            float halfLen = spec.Length / 2f;
            float halfWid = spec.Width / 2f;
            int pieces = 0;

            foreach (var corner in new[] { new Vector3(halfWid, 0, halfLen), new Vector3(-halfWid, 0, halfLen),
                                           new Vector3(halfWid, 0, -halfLen), new Vector3(-halfWid, 0, -halfLen) })
            {
                var p = Place(post, pen.transform, corner + new Vector3(0f, DeckHeight + spec.PostHeight / 2f, spec.CenterZ), Quaternion.identity);
                p.transform.localScale = new Vector3(1f, spec.PostHeight, 1f); // the pole is 1 m tall
                pieces++;
            }

            // Four sides, no gate: a gate needs its Door script, and that needs the networking
            // these pieces lose.  Rails run between the posts' inner faces rather than into their
            // centers, so no rail face lies in the same plane as a post face (which shimmers).
            foreach (float rail in spec.RailHeights)
            {
                float y = DeckHeight + rail;
                pieces += PlaceRail(pen.transform, beam2, beam1, new Vector3(0f, y, spec.CenterZ + halfLen), 0f, spec.Width - PostWidth);    // bow
                pieces += PlaceRail(pen.transform, beam2, beam1, new Vector3(0f, y, spec.CenterZ - halfLen), 0f, spec.Width - PostWidth);   // stern
                pieces += PlaceRail(pen.transform, beam2, beam1, new Vector3(halfWid, y, spec.CenterZ), 90f, spec.Length - PostWidth);  // starboard
                pieces += PlaceRail(pen.transform, beam2, beam1, new Vector3(-halfWid, y, spec.CenterZ), 90f, spec.Length - PostWidth); // port
            }

            // The pieces' "woodwall" material has _RippleDistance 0.03: Custom/Piece displaces
            // vertices by a pattern sampled at world position, a subtle hand-hewn look on a wall
            // that never moves.  On a hull bobbing at anchor the sample point shifts every frame
            // and the rails shimmer constantly -- every one of the ship's own materials has it at
            // 0.  A property block zeroes it per renderer without copying the material.
            var noRipple = new MaterialPropertyBlock();
            noRipple.SetFloat("_RippleDistance", 0f);
            foreach (var renderer in pen.GetComponentsInChildren<Renderer>(true))
                renderer.SetPropertyBlock(noRipple);

            Jotunn.Logger.LogInfo($"[TheGreatestShips] Built animal pen: {pieces} posts and rails, {spec.Width} x {spec.Length} at z {spec.CenterZ}.");
        }

        // A rail of the given length, centered on `center`, running along the yaw'd X axis: as
        // many 2 m beams as fit, then a 1 m beam scaled to whatever is left, laid end to end from
        // one post to the other.
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
            float remainder = length - (x + length / 2f);
            if (remainder > 0.01f)
            {
                var last = Place(beam1, parent, center + rotation * new Vector3(x + remainder / 2f, 0f, 0f), rotation);
                last.transform.localScale = new Vector3(remainder, 1f, 1f);
                placed++;
            }
            return placed;
        }

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var piece = Object.Instantiate(prefab, parent);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = localRotation;
            StripToGeometry(piece);
            return piece;
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

    /// <summary>
    /// Keeps the ship's rigidbody balanced as though the pen weren't there.  Unity derives a
    /// body's center of mass and inertia from its colliders unless they are set explicitly; this
    /// measures both with the pen's colliders switched off, then switches them back on and sets
    /// the values explicitly, which also stops Unity recomputing them.  Runs on every spawned
    /// ship (Start never runs on the inactive prefab).
    /// </summary>
    internal sealed class PenBallast : MonoBehaviour
    {
        private void Start()
        {
            var body = GetComponentInParent<Rigidbody>();
            if (body == null) return;

            var colliders = GetComponentsInChildren<Collider>(true);
            foreach (var c in colliders) c.enabled = false;
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
            var centerOfMass = body.centerOfMass;
            var inertia      = body.inertiaTensor;
            var inertiaRot   = body.inertiaTensorRotation;
            foreach (var c in colliders) c.enabled = true;
            body.centerOfMass          = centerOfMass;
            body.inertiaTensor         = inertia;
            body.inertiaTensorRotation = inertiaRot;
        }
    }
}
