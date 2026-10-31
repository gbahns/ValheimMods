using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// Keeps the crew's places fitting a player once the hull is scaled.  The ship's scale
    /// stretches everything aboard, but the player who sits or stands there is not stretched.
    /// A stool's attach point is at the sitter's feet and the sitting pose lifts the hips half
    /// a meter, so a stool half again as tall swallows the hips, and one half again as deep
    /// catches the knees.  The mast hold stands 0.9 m from the mast's axis and the pose reaches
    /// a fixed arm's length, so a wider hull leaves the hand short of the pole and a narrower
    /// one puts the wrist through it.
    ///
    /// Seats are scaled back to human height and depth (a bigger ship does not need bigger
    /// stools); a hold's attach point is moved so that, in world meters, it stands where the
    /// vanilla pose was made to stand relative to the thing held.  Everything is measured on
    /// the vanilla hull, so applying it again with a new scale -- the server's dimensions
    /// arriving after login, or a setting changed -- lands in the same place.
    /// </summary>
    internal static class CrewFit
    {
        /// <param name="ship">The clone prefab or a ship in the world, already at its scale.</param>
        /// <param name="vanilla">The hull it was cloned from, unscaled.</param>
        /// <param name="ratio">The ship's transform scale over the vanilla hull's, per axis.</param>
        /// <param name="holdNudge">Meters to shift the mast hold's standing spot from the game's own,
        /// (right, forward) as the holder faces: the game's spot is a few centimeters off.</param>
        /// <param name="mastBearing">Where around the mast the holder stands, in degrees from the bow
        /// clockwise (90 = to starboard of the mast), facing turned the same way; null = the game's spot.</param>
        /// <returns>A summary of what moved, or null when nothing did.</returns>
        internal static string Apply(GameObject ship, GameObject vanilla, Vector3 ratio, Vector2 holdNudge, float? mastBearing)
        {
            if (ship == null || vanilla == null) return null;
            var root  = ship.transform;
            var vRoot = vanilla.transform;
            var notes = new List<string>();

            var poles = Poles(vanilla);
            foreach (var vChair in vanilla.GetComponentsInChildren<Chair>(true))
            {
                if (vChair.m_attachPoint == null) continue;
                var box = vChair.GetComponent<BoxCollider>();
                if (box == null || Inside(box, vChair.m_attachPoint.position))
                    Seat(root, vRoot, vChair.transform, ratio, notes);
                else
                    Hold(root, vRoot, vChair.transform, vChair.m_attachPoint, Surface(poles, box, vChair.m_attachPoint.position), ratio, holdNudge, mastBearing, notes);
            }

            foreach (var vControls in vanilla.GetComponentsInChildren<ShipControlls>(true))
            {
                // The longship's helmsman sits on a "box" mesh under the controls; the Karve's
                // on a "sitboard" beside them.  Both keep human size.
                foreach (Transform child in vControls.transform)
                    if (child.name.StartsWith("box")) Lower(root, vRoot, child, ratio, notes);
                var parent = vControls.transform.parent;
                if (parent != null)
                    foreach (Transform sibling in parent)
                        if (sibling.name.StartsWith("sitboard")) Lower(root, vRoot, sibling, ratio, notes);
            }

            return notes.Count == 0 ? null : string.Join(", ", notes);
        }

        // ── Seats ──────────────────────────────────────────────────────────────────────────

        // A seat is a Chair whose attach point stands within its own collider's footprint: the
        // sitter is on it.  Its whole object -- stool mesh, collider, attach point -- is scaled
        // back in height and depth, so the seat's top stays where the pose puts the hips and
        // its front edge where the pose puts the knees.  Width stays the hull's: a wider bench
        // troubles nobody.
        private static void Seat(Transform root, Transform vRoot, Transform vChair, Vector3 ratio, List<string> notes)
        {
            var chair = Find(root, vRoot, vChair);
            if (chair == null) return;
            var v = vChair.localScale;
            var target = new Vector3(v.x, v.y / ratio.y, v.z / ratio.z);
            if (chair.localScale == target) return;
            chair.localScale = target;
            if (!Mathf.Approximately(ratio.y, 1f) || !Mathf.Approximately(ratio.z, 1f)) notes.Add($"{vChair.name} kept human-sized");
        }

        // A seat mesh that is not a Chair of its own: height and position scaled back together,
        // so its top stays at the same height above its parent; depth scaled back too.
        private static void Lower(Transform root, Transform vRoot, Transform vMesh, Vector3 ratio, List<string> notes)
        {
            var mesh = Find(root, vRoot, vMesh);
            if (mesh == null) return;
            var p = vMesh.localPosition;
            var s = vMesh.localScale;
            var targetPos   = new Vector3(p.x, p.y / ratio.y, p.z);
            var targetScale = new Vector3(s.x, s.y / ratio.y, s.z / ratio.z);
            if (mesh.localPosition == targetPos && mesh.localScale == targetScale) return;
            mesh.localPosition = targetPos;
            mesh.localScale    = targetScale;
            if (!Mathf.Approximately(ratio.y, 1f) || !Mathf.Approximately(ratio.z, 1f)) notes.Add($"{vMesh.name} kept human-sized");
        }

        // ── Holds ──────────────────────────────────────────────────────────────────────────

        private struct Held
        {
            public Vector3 Point;   // the nearest point of the held surface, world
            public bool    IsPole;
            public Vector3 Axis;    // a pole's axis (any point on it), world
        }

        // A hold is a Chair whose attach point stands beside its collider: the player holds the
        // thing.  The pose is rigid, so the attach point is placed where the vanilla one stood
        // relative to the held surface, in world meters -- the surface scales with the hull,
        // the reach does not.  A mast is round, so the holder can stand anywhere around it:
        // the spot and the facing swing together about the pole's axis to the bearing asked.
        private static void Hold(Transform root, Transform vRoot, Transform vChair, Transform vAttach, Held held, Vector3 ratio,
                                 Vector2 nudge, float? bearing, List<string> notes)
        {
            var attach = Find(root, vRoot, vAttach);
            if (attach == null) return;

            var h = vRoot.InverseTransformPoint(vAttach.position);   // hull units = vanilla meters
            var p = vRoot.InverseTransformPoint(held.Point);
            var u = h - p; u.y = 0f;
            if (u.magnitude < 0.05f) return;

            // The vanilla reach, plus the nudge in the pose's frame (the vanilla holds face the
            // bow: right is +x, forward +z).
            var vFacing = Quaternion.Inverse(vRoot.rotation) * vAttach.rotation;
            var reach = u + (held.IsPole ? vFacing * new Vector3(nudge.x, 0f, nudge.y) : Vector3.zero);

            float turn = 0f;
            var pivot = p;
            if (held.IsPole && bearing.HasValue)
            {
                var axis = vRoot.InverseTransformPoint(held.Axis);
                var off = h - axis; off.y = 0f;
                float vanillaBearing = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;
                turn = Mathf.DeltaAngle(0f, bearing.Value - vanillaBearing);
                var swing = Quaternion.Euler(0f, turn, 0f);
                var pOff = p - axis; pOff.y = 0f;
                pivot = new Vector3(axis.x, p.y, axis.z) + swing * pOff;   // the surface point, swung round the pole
                reach = swing * reach;
            }

            // Hull units again: the reach is world meters, and the hull's scale will multiply.
            var hFit = pivot + new Vector3(reach.x / ratio.x, 0f, reach.z / ratio.z);
            hFit.y = h.y;

            var worldFit = root.TransformPoint(hFit);
            var worldRot = root.rotation * Quaternion.Euler(0f, turn, 0f) * vFacing;
            bool moved = (attach.position - worldFit).sqrMagnitude > 1e-6f || Quaternion.Angle(attach.rotation, worldRot) > 0.01f;
            attach.position = worldFit;
            attach.rotation = worldRot;
            if (!moved) return;

            float shift = Vector3.Scale(ratio, hFit - h).magnitude;
            var note = new StringBuilder($"{vChair.name} hold");
            if (turn != 0f) note.Append($" swung to bearing {bearing.Value:0}");
            if (shift > 0.01f) note.Append($" {(turn != 0f ? "and " : "")}moved {shift:0.00} m");
            notes.Add(note.ToString());
        }

        // The thin, tall colliders aboard: mast poles.
        private static List<BoxCollider> Poles(GameObject vanilla)
        {
            var poles = new List<BoxCollider>();
            foreach (var box in vanilla.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger) continue;
                var size = Vector3.Scale(box.size, box.transform.lossyScale);
                if (Mathf.Abs(size.x) < 0.5f && Mathf.Abs(size.z) < 0.5f && Mathf.Abs(size.y) > 2f)
                    poles.Add(box);
            }
            return poles;
        }

        // The thing the hand is reaching for: the nearest pole within arm's reach, else the
        // chair's own collider (the prow's stem is the "front" chair's collider).
        private static Held Surface(List<BoxCollider> poles, BoxCollider own, Vector3 attachWorld)
        {
            var held = new Held { Point = Nearest(own, attachWorld), IsPole = false };
            float bestD = Flat(held.Point - attachWorld).magnitude;
            foreach (var pole in poles)
            {
                var n = Nearest(pole, attachWorld);
                float dist = Flat(n - attachWorld).magnitude;
                if (dist < 1.5f && dist < bestD)
                {
                    held = new Held { Point = n, IsPole = true, Axis = pole.transform.TransformPoint(pole.center) };
                    bestD = dist;
                }
            }
            return held;
        }

        private static bool Inside(BoxCollider box, Vector3 world)
        {
            var l = box.transform.InverseTransformPoint(world) - box.center;
            return Mathf.Abs(l.x) <= box.size.x / 2f && Mathf.Abs(l.z) <= box.size.z / 2f;
        }

        private static Vector3 Nearest(BoxCollider box, Vector3 world)
        {
            var l = box.transform.InverseTransformPoint(world) - box.center;
            var half = box.size / 2f;
            l = new Vector3(Mathf.Clamp(l.x, -half.x, half.x), Mathf.Clamp(l.y, -half.y, half.y), Mathf.Clamp(l.z, -half.z, half.z));
            return box.transform.TransformPoint(l + box.center);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // The ship's counterpart of a vanilla transform, by path under the root.
        private static Transform Find(Transform root, Transform vRoot, Transform vanilla)
        {
            var path = new StringBuilder(vanilla.name);
            for (var t = vanilla.parent; t != null && t != vRoot; t = t.parent)
                path.Insert(0, t.name + "/");
            return root.Find(path.ToString());
        }
    }
}
