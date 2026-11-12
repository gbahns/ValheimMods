using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// How much harder a scaled hull is to turn than the vanilla hull it was cloned from.
    ///
    /// Unity computes a ship's inertia from its colliders at their current scale, so a hull
    /// scaled (w, h, l) along its own axes has a yaw inertia of w²X + l²Z, where X and Z are the
    /// vanilla hull's mass spread across its width and along its length (each a sum of
    /// volume x (offset² + extent²/12) over its colliders).  The ratio to the vanilla X + Z is
    /// what the rudder's lever is scaled by, so the same sideways push turns the hull at the
    /// longship's rate.  For a hull scaled the same both ways the ratio is the scale squared
    /// whatever X and Z are; the split only matters for hulls widened or lengthened alone, where
    /// the ratio is near 1 anyway.  Measured from the vanilla prefab, so it is the same every run.
    /// </summary>
    internal static class HullTurning
    {
        internal struct Moments
        {
            public float X;   // across the hull, hull units² x volume
            public float Z;   // along it
        }

        internal static Moments Measure(GameObject vanilla)
        {
            var root = vanilla.transform;
            var m = new Moments();
            foreach (var collider in vanilla.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger) continue;
                if (!Box(root, collider, out var center, out var size)) continue;
                float volume = size.x * size.y * size.z;
                if (volume <= 0f) continue;
                m.X += volume * (center.x * center.x + size.x * size.x / 12f);
                m.Z += volume * (center.z * center.z + size.z * size.z / 12f);
            }
            return m;
        }

        /// <param name="ratio">The hull's scale over the vanilla hull's, per axis.</param>
        /// <returns>The scaled hull's yaw inertia over the vanilla hull's.</returns>
        internal static float Ratio(Moments m, Vector3 ratio)
        {
            float total = m.X + m.Z;
            if (total <= 0f) return ratio.x * ratio.z;   // nothing measured: right for a hull scaled evenly
            return (ratio.x * ratio.x * m.X + ratio.z * ratio.z * m.Z) / total;
        }

        // A collider's bounding box in the hull's own frame, which is where the scale applies.
        // Rotated colliders (the mast's nodes turn 90°) are taken by their corners, so a tilted
        // box is a little generous; the mast's are thin and change nothing either way.
        private static bool Box(Transform root, Collider collider, out Vector3 center, out Vector3 size)
        {
            Vector3 c, half;
            switch (collider)
            {
                case BoxCollider box:
                    c = box.center; half = box.size / 2f; break;
                case SphereCollider sphere:
                    c = sphere.center; half = Vector3.one * sphere.radius; break;
                case CapsuleCollider capsule:
                    c = capsule.center; half = Vector3.one * capsule.radius;
                    half[capsule.direction] = capsule.height / 2f; break;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    c = mesh.sharedMesh.bounds.center; half = mesh.sharedMesh.bounds.extents; break;
                default:
                    center = size = Vector3.zero; return false;
            }
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                var p = root.InverseTransformPoint(collider.transform.TransformPoint(corner));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            center = (min + max) / 2f;
            size   = max - min;
            return true;
        }
    }
}
