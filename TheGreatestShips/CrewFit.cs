using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// Keeps the crew's places fitting a player once the hull is scaled.  The ship's scale
    /// stretches everything aboard, but the player who sits or stands there is not stretched.
    /// A stool's attach point is at the sitter's feet and the sitting pose lifts the hips half
    /// a meter, so a stool half again as tall swallows the hips.  The mast hold stands 0.9 m
    /// from the mast's axis and the pose reaches a fixed arm's length, so a wider hull leaves
    /// the hand short of the pole and a narrower one puts the wrist through it.
    ///
    /// Seats are scaled back to human height (a bigger ship does not need taller stools); a
    /// hold's attach point is moved so its distance to the thing held is what the vanilla pose
    /// was made for.  Everything is measured on the vanilla hull, so applying it again with a
    /// new scale -- the server's dimensions arriving after login -- lands in the same place.
    /// </summary>
    internal static class CrewFit
    {
        /// <param name="ship">The clone prefab or a ship in the world, already at its scale.</param>
        /// <param name="vanilla">The hull it was cloned from, unscaled.</param>
        /// <param name="ratio">The ship's transform scale over the vanilla hull's, per axis.</param>
        /// <returns>A summary of what moved, or null when nothing did.</returns>
        internal static string Apply(GameObject ship, GameObject vanilla, Vector3 ratio)
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
                    Hold(root, vRoot, vChair.transform, vChair.m_attachPoint, Surface(poles, box, vChair.m_attachPoint.position), ratio, notes);
            }

            foreach (var vControls in vanilla.GetComponentsInChildren<ShipControlls>(true))
            {
                // The longship's helmsman sits on a "box" mesh under the controls; the Karve's
                // on a "sitboard" beside them.  Both keep human height.
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
        // back in height, so the seat's top stays where the pose puts the hips.
        private static void Seat(Transform root, Transform vRoot, Transform vChair, Vector3 ratio, List<string> notes)
        {
            var chair = Find(root, vRoot, vChair);
            if (chair == null) return;
            var v = vChair.localScale;
            var target = new Vector3(v.x, v.y / ratio.y, v.z);
            if (chair.localScale == target) return;
            chair.localScale = target;
            if (!Mathf.Approximately(ratio.y, 1f)) notes.Add($"{vChair.name} kept at human height");
        }

        // A seat mesh that is not a Chair of its own: height and position scaled back together,
        // so its top stays at the same height above its parent.
        private static void Lower(Transform root, Transform vRoot, Transform vMesh, Vector3 ratio, List<string> notes)
        {
            var mesh = Find(root, vRoot, vMesh);
            if (mesh == null) return;
            var p = vMesh.localPosition;
            var s = vMesh.localScale;
            var targetPos   = new Vector3(p.x, p.y / ratio.y, p.z);
            var targetScale = new Vector3(s.x, s.y / ratio.y, s.z);
            if (mesh.localPosition == targetPos && mesh.localScale == targetScale) return;
            mesh.localPosition = targetPos;
            mesh.localScale    = targetScale;
            if (!Mathf.Approximately(ratio.y, 1f)) notes.Add($"{vMesh.name} kept at human height");
        }

        // ── Holds ──────────────────────────────────────────────────────────────────────────

        // A hold is a Chair whose attach point stands beside its collider: the player holds the
        // thing.  The attach point is moved along the line to the held surface so that, in
        // world meters, it is as far from the surface as on the vanilla hull.
        private static void Hold(Transform root, Transform vRoot, Transform vChair, Transform vAttach, Vector3 surfaceWorld, Vector3 ratio, List<string> notes)
        {
            var attach = Find(root, vRoot, vAttach);
            if (attach == null) return;

            var h = vRoot.InverseTransformPoint(vAttach.position);   // hull units, unscaled
            var p = vRoot.InverseTransformPoint(surfaceWorld);
            var u = h - p; u.y = 0f;
            float d = u.magnitude;
            if (d < 0.05f) return;
            float scaledD = Vector3.Scale(ratio, u).magnitude;        // what the scale makes of that reach
            var hFit = p + u * (d / scaledD);                           // same reach in world meters
            hFit.y = h.y;

            var worldFit = root.TransformPoint(hFit);
            if ((attach.position - worldFit).sqrMagnitude < 1e-6f) return;
            attach.position = worldFit;
            float moved = Vector3.Scale(ratio, hFit - h).magnitude;
            if (moved > 0.01f) notes.Add($"{vChair.name} hold {(scaledD > d ? "in" : "out")} by {moved:0.00} m");
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

        // The point the hand is reaching for: the nearest pole within arm's reach, else the
        // chair's own collider (the prow's stem is the "front" chair's collider).
        private static Vector3 Surface(List<BoxCollider> poles, BoxCollider own, Vector3 attachWorld)
        {
            Vector3 best = Nearest(own, attachWorld);
            float bestD = Flat(best - attachWorld).magnitude;
            foreach (var pole in poles)
            {
                var n = Nearest(pole, attachWorld);
                float dist = Flat(n - attachWorld).magnitude;
                if (dist < 1.5f && dist < bestD) { best = n; bestD = dist; }
            }
            return best;
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
