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
    /// catches the knees.  The mast hold stands 0.86 m from the mast's axis and the pose reaches
    /// a fixed arm's length, so a wider hull leaves the hand short of the pole and a narrower
    /// one puts the wrist through it.
    ///
    /// Seats are scaled back to human height and depth (a bigger ship does not need bigger
    /// stools); a hold's attach point is moved so that, in world meters, the hand lands on the
    /// scaled surface where the vanilla hand landed on the vanilla one.  Everything is measured
    /// on the vanilla hull, so applying it again with a new scale -- the server's dimensions
    /// arriving after login, or a setting changed -- lands in the same place.
    /// </summary>
    internal static class CrewFit
    {
        /// <summary>The visual mast's radius about hand height, in meters, on each hull this mod
        /// clones: read off the mast mesh's vertex rings, which taper from the deck to the top
        /// (longship 0.128 to 0.081 over 9.8 m; Karve 0.106 to 0.067 over 8.1 m).  The pole's
        /// box collider is a good deal thinner than the wood on both, which is why it isn't used.</summary>
        internal static float MastRadius(string basePrefab) => basePrefab == "Karve" ? 0.10f : 0.12f;

        /// <param name="ship">The clone prefab or a ship in the world, already at its scale.</param>
        /// <param name="vanilla">The hull it was cloned from, unscaled.</param>
        /// <param name="ratio">The ship's transform scale over the vanilla hull's, per axis.</param>
        /// <param name="holdNudge">Meters to shift the mast hold's standing spot from the game's own,
        /// (right, forward) as the holder faces: the game's spot is a few centimeters off.</param>
        /// <param name="mastBearing">Where around the mast the holder stands, in degrees from the bow
        /// clockwise (90 = to starboard of the mast), facing turned the same way; null = the game's spot.</param>
        /// <param name="mastRadius">The visual mast's radius at hand height, meters: <see cref="MastRadius"/>.</param>
        /// <returns>A summary of what moved, or null when nothing did.</returns>
        internal static string Apply(GameObject ship, GameObject vanilla, Vector3 ratio, Vector2 holdNudge, float? mastBearing, float mastRadius)
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
                    Hold(root, vRoot, vChair.transform, vChair.m_attachPoint, Surface(poles, box, vChair.m_attachPoint.position, mastRadius), ratio, holdNudge, mastBearing, notes);
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
            public Vector3 Point;   // the nearest point of the held surface, world (a box)
            public bool    IsPole;
            public Vector3 Axis;    // a pole's axis (any point on it), world
            public float   Radius;  // a pole's visual radius at hand height, meters
        }

        // A hold is a Chair whose attach point stands beside its collider: the player holds the
        // thing.  The pose is rigid, so the attach point moves so that the hand, a fixed reach
        // from it, lands on the scaled surface where the vanilla hand landed on the vanilla one.
        //
        // A mast is a round pole: the hand lies on the line from the axis to the holder, at the
        // pole's radius.  A hull scaled unevenly makes the pole an ellipse, so the surface along
        // that line moves out by the ellipse's radius there less the pole's, and the holder with
        // it.  A pole is also the same all round, so the holder can stand anywhere about it: the
        // spot and the facing swing together about the axis to the bearing asked.
        //
        // Anything else (the prow's stem) is held at the nearest point of its collider, which
        // scales with the hull, the vanilla reach kept from there.
        private static void Hold(Transform root, Transform vRoot, Transform vChair, Transform vAttach, Held held, Vector3 ratio,
                                 Vector2 nudge, float? bearing, List<string> notes)
        {
            var attach = Find(root, vRoot, vAttach);
            if (attach == null) return;

            var h = vRoot.InverseTransformPoint(vAttach.position);   // hull units = vanilla meters
            var vFacing = Quaternion.Inverse(vRoot.rotation) * vAttach.rotation;
            float turn = 0f;
            Vector3 anchor;   // a point of the hull, in hull units: the scale carries it
            Vector3 reach;    // from the anchor, once scaled, to the holder: world meters
            if (held.IsPole)
            {
                anchor = vRoot.InverseTransformPoint(held.Axis);
                var toHolder = Flat(h - anchor);
                if (toHolder.magnitude < 0.05f) return;
                if (bearing.HasValue)
                    turn = Mathf.DeltaAngle(0f, bearing.Value - Mathf.Atan2(toHolder.x, toHolder.z) * Mathf.Rad2Deg);
                var swing = Quaternion.Euler(0f, turn, 0f);
                toHolder = swing * toHolder;
                var dir = toHolder.normalized;
                // the scaled pole's radius along the holder's line: a round pole's across a hull
                // scaled evenly, more across one widened
                float radius = held.Radius / Mathf.Sqrt(Sq(dir.x / ratio.x) + Sq(dir.z / ratio.z));
                reach = toHolder + dir * (radius - held.Radius) + swing * vFacing * new Vector3(nudge.x, 0f, nudge.y);
            }
            else
            {
                anchor = vRoot.InverseTransformPoint(held.Point);
                reach  = Flat(h - anchor);
                if (reach.magnitude < 0.05f) return;
            }

            // Hull units again: the reach is world meters, and the hull's scale will multiply.
            var hFit = anchor + new Vector3(reach.x / ratio.x, 0f, reach.z / ratio.z);
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
        private static Held Surface(List<BoxCollider> poles, BoxCollider own, Vector3 attachWorld, float poleRadius)
        {
            var held = new Held { Point = Nearest(own, attachWorld), IsPole = false };
            float bestD = Flat(held.Point - attachWorld).magnitude;
            foreach (var pole in poles)
            {
                var n = Nearest(pole, attachWorld);
                float dist = Flat(n - attachWorld).magnitude;
                if (dist < 1.5f && dist < bestD)
                {
                    held = new Held { Point = n, IsPole = true, Axis = pole.transform.TransformPoint(pole.center), Radius = poleRadius };
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
        private static float Sq(float v) => v * v;

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
