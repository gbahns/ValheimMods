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
        private const float GateWidth  = 2f;   // the opening; a shorter side is all gate

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
            pen.AddComponent<PenLip>();

            // Under attack the hull takes sudden impulses -- hits on the ship, leech bumps, a
            // grounding -- and jumps a few centimeters in one physics step.  A penned animal is
            // its own rigidbody and doesn't jump with it, so a rail can end up overlapping the
            // animal, and Unity resolves an overlap along the shortest path out: past the rail's
            // midline, that's outboard, and the animal is ejected through the wall.  Speculative
            // contacts are generated ahead of the hull's motion instead of after the overlap, so
            // the rail pushes the animal along with it.
            var body = clone.GetComponent<Rigidbody>();
            if (body != null) body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            float halfLen = spec.Length / 2f;
            float halfWid = spec.Width / 2f;
            int pieces = 0;
            int wood = 0; // what the same pieces cost to build: pole 1, 2 m beam 2, 1 m beam 1

            foreach (var corner in new[] { new Vector3(halfWid, 0, halfLen), new Vector3(-halfWid, 0, halfLen),
                                           new Vector3(halfWid, 0, -halfLen), new Vector3(-halfWid, 0, -halfLen) })
            {
                var p = Place(post, pen.transform, corner + new Vector3(0f, DeckHeight + spec.PostHeight / 2f, spec.CenterZ), Quaternion.identity);
                p.transform.localScale = new Vector3(1f, spec.PostHeight, 1f); // the pole is 1 m tall
                pieces++; wood += 1;
            }

            // Rails run between the posts' inner faces rather than into their centers, so no rail
            // face lies in the same plane as a post face (which shimmers).  The starboard side
            // holds the gate (PenGate): a section of rail hinged at its base that folds outboard
            // into a ramp.  On a short pen the whole side is the gate; on a long one it is a 2 m
            // section between two more posts, with the rails running out to them.
            float wallTop = 0f;
            foreach (float rail in spec.RailHeights) wallTop = Mathf.Max(wallTop, rail + 0.2f);
            const float wallThickness = 0.2f;

            float sideSpan  = spec.Length - PostWidth;                 // between the corner posts' inner faces
            float gateWidth = Mathf.Min(GateWidth, sideSpan);
            float segment   = gateWidth >= sideSpan - 0.01f ? 0f : (sideSpan - gateWidth) / 2f - PostWidth;   // corner post to gate post
            float segmentZ  = (halfLen - PostWidth / 2f) - segment / 2f;                                       // its center, from the pen's
            float sideWall  = halfLen - gateWidth / 2f;                 // a gate post and whatever rail is beyond it
            float sideWallZ = (halfLen + gateWidth / 2f) / 2f;

            // Bow and stern: rails and the flat wall.
            foreach (float rail in spec.RailHeights)
            {
                float y = DeckHeight + rail;
                foreach (float end in new[] { halfLen, -halfLen })
                {
                    var (long2, short1) = PlaceRail(pen.transform, beam2, beam1, new Vector3(0f, y, spec.CenterZ + end), 0f, spec.Width - PostWidth);
                    pieces += long2 + short1;
                    wood   += long2 * 2 + short1;
                }
            }
            Wall(pen.transform, new Vector3(0f, DeckHeight + wallTop / 2f, spec.CenterZ + halfLen - wallThickness / 2f), new Vector3(spec.Width, wallTop, wallThickness), post.layer);
            Wall(pen.transform, new Vector3(0f, DeckHeight + wallTop / 2f, spec.CenterZ - halfLen + wallThickness / 2f), new Vector3(spec.Width, wallTop, wallThickness), post.layer);

            // Each side: a gate mid-side, gate posts if the side is longer than the gate, rails
            // from the corner posts to the gate posts, and the flat wall in three parts -- the
            // middle one belongs to the gate and, lying down, is the ramp's surface.
            var gates = new List<GameObject>();
            foreach (float side in new[] { 1f, -1f })   // +X starboard, -X port
            {
                float x = side * halfWid;
                if (segment > 0.01f)
                {
                    foreach (float dz in new[] { gateWidth / 2f + PostWidth / 2f, -(gateWidth / 2f + PostWidth / 2f) })
                    {
                        var p = Place(post, pen.transform, new Vector3(x, DeckHeight + spec.PostHeight / 2f, spec.CenterZ + dz), Quaternion.identity);
                        p.transform.localScale = new Vector3(1f, spec.PostHeight, 1f);
                        pieces++; wood += 1;
                    }
                }

                // The gate's pivot: on the rail line at deck level, mid-side.  Everything under
                // it turns with it.
                var gate = new GameObject(side > 0 ? "pen_gate" : "pen_gate_port");
                gate.transform.SetParent(pen.transform, false);
                gate.transform.localPosition = new Vector3(x, DeckHeight, spec.CenterZ);
                gate.AddComponent<PenGate>().Port = side < 0;
                gates.Add(gate);

                foreach (float rail in spec.RailHeights)
                {
                    float y = DeckHeight + rail;
                    if (segment > 0.01f)
                        foreach (float dz in new[] { segmentZ, -segmentZ })
                        {
                            var (long2, short1) = PlaceRail(pen.transform, beam2, beam1, new Vector3(x, y, spec.CenterZ + dz), 90f, segment);
                            pieces += long2 + short1;
                            wood   += long2 * 2 + short1;
                        }
                    var (gate2, gate1) = PlaceRail(gate.transform, beam2, beam1, new Vector3(0f, rail, 0f), 90f, gateWidth);
                    pieces += gate2 + gate1;
                    wood   += gate2 * 2 + gate1;
                }

                float wallX = side * (halfWid - wallThickness / 2f);
                Wall(pen.transform,  new Vector3(wallX, DeckHeight + wallTop / 2f, spec.CenterZ + sideWallZ), new Vector3(wallThickness, wallTop, sideWall), post.layer);
                Wall(pen.transform,  new Vector3(wallX, DeckHeight + wallTop / 2f, spec.CenterZ - sideWallZ), new Vector3(wallThickness, wallTop, sideWall), post.layer);
                Wall(gate.transform, new Vector3(-side * wallThickness / 2f, wallTop / 2f, 0f), new Vector3(wallThickness, wallTop, gateWidth), post.layer);
            }

            // An invisible lip along the top of each wall, jutting inward: an animal lifted by a
            // shove or a heeling deck meets a ceiling at the wall instead of an edge.  Only while
            // sailing (PenLip switches it), so animals can still be dropped in over the wall at
            // rest.  Starts off; PenBallast restores that after its measurement.
            const float lipWidth = 0.6f, lipThickness = 0.2f;
            foreach (var (center, size) in new[] {
                (new Vector3(0f, 0f, spec.CenterZ + halfLen - wallThickness - lipWidth / 2f), new Vector3(spec.Width, lipThickness, lipWidth)),   // bow
                (new Vector3(0f, 0f, spec.CenterZ - halfLen + wallThickness + lipWidth / 2f), new Vector3(spec.Width, lipThickness, lipWidth)),   // stern
                (new Vector3(halfWid - wallThickness - lipWidth / 2f, 0f, spec.CenterZ), new Vector3(lipWidth, lipThickness, spec.Length)),      // starboard
                (new Vector3(-halfWid + wallThickness + lipWidth / 2f, 0f, spec.CenterZ), new Vector3(lipWidth, lipThickness, spec.Length)) })   // port
            {
                var lip = new GameObject("pen_lip") { layer = post.layer };
                lip.transform.SetParent(pen.transform, false);
                lip.transform.localPosition = center + new Vector3(0f, DeckHeight + wallTop + lipThickness / 2f, 0f);
                var box = lip.AddComponent<BoxCollider>();
                box.size = size;
                box.enabled = false;
            }

            // The hover system wants the Hoverable on the collider's own object (see PenGateHandle).
            foreach (var gate in gates)
                foreach (var collider in gate.GetComponentsInChildren<Collider>(true))
                    if (collider.GetComponent<PenGateHandle>() == null)
                        collider.gameObject.AddComponent<PenGateHandle>();

            // The pieces' "woodwall" material is Custom/Piece with _VALUENOISEVERTEX_ON: the
            // shader nudges every vertex by a noise texture sampled at its *world* position, a
            // static hand-hewn look on a wall that never moves.  On a hull that bobs the sample
            // point shifts every frame and the rails crawl constantly.  The ship's own wood is the
            // same shader flagged _MOVEABLEOBJECT_ON with no vertex noise, so each material is
            // copied and given the ship's flags.  (A property block would do until the first
            // hover highlight, when MaterialMan writes its own block over every renderer.)
            var stilled = new Dictionary<Material, Material>();
            foreach (var renderer in pen.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null) continue;
                    if (!stilled.TryGetValue(material, out var copy))
                    {
                        copy = new Material(material) { name = material.name + "_" + clone.name };
                        copy.DisableKeyword("_VALUENOISEVERTEX_ON");
                        copy.EnableKeyword("_MOVEABLEOBJECT_ON");
                        foreach (var (prop, value) in new[] { ("_ValueNoiseVertex", 0f), ("_ValueNoise", 0f), ("_RippleDistance", 0f), ("_MoveableObject", 1f) })
                            if (copy.HasProperty(prop)) copy.SetFloat(prop, value);
                        stilled[material] = copy;
                    }
                    materials[i] = copy;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }

            // Everything in the pen goes on the hull's own layer, "vehicle": the camera's block
            // mask skips it (the pieces' "piece" layer is exactly what that mask exists to stop
            // at, so orbiting the camera over the pen kept pulling it in), characters still
            // collide with it, and the monsters' line-of-sight mask still includes it.
            int vehicle = LayerMask.NameToLayer("vehicle");
            if (vehicle >= 0)
                foreach (var t in pen.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = vehicle;

            Jotunn.Logger.LogInfo($"[TheGreatestShips] Built animal pen: {pieces} posts and rails (a {gateWidth:0.0} m gate each side) plus a flat inner wall to {wallTop:0.00}, {spec.Width} x {spec.Length} at z {spec.CenterZ}; the same pieces would cost {wood} Wood.");
        }

        // A rail of the given length, centered on `center`, running along the yaw'd X axis: as
        // many 2 m beams as fit, then a 1 m beam scaled to whatever is left, laid end to end from
        // one post to the other.  Returns how many of each it placed.
        private static (int long2, int short1) PlaceRail(Transform parent, GameObject beam2, GameObject beam1, Vector3 center, float yaw, float length)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            float x = -length / 2f;
            int long2 = 0, short1 = 0;
            while (length - (x + length / 2f) >= 2f - 0.01f)
            {
                Place(beam2, parent, center + rotation * new Vector3(x + 1f, 0f, 0f), rotation);
                x += 2f; long2++;
            }
            float remainder = length - (x + length / 2f);
            if (remainder > 0.01f)
            {
                var last = Place(beam1, parent, center + rotation * new Vector3(x + remainder / 2f, 0f, 0f), rotation);
                last.transform.localScale = new Vector3(remainder, 1f, 1f);
                short1++;
            }
            return (long2, short1);
        }

        private static void Wall(Transform parent, Vector3 localPosition, Vector3 size, int layer)
        {
            var wall = new GameObject("pen_wall") { layer = layer };
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPosition;
            wall.AddComponent<BoxCollider>().size = size;
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
    /// <summary>
    /// Switches the pen's lip colliders on while the ship is under way -- sail or rudder set to
    /// anything but Stop, or the hull moving faster than a drift -- and off at rest, so animals
    /// can be dropped in over the wall at a dock.  Checked a few times a second on every client;
    /// the speed setting and velocity are synced, so everyone agrees.
    /// </summary>
    internal sealed class PenLip : MonoBehaviour
    {
        private const float DriftSpeed = 1f; // m/s: below this, "at rest"
        private Ship _ship;
        private Rigidbody _body;
        private Collider[] _lips;
        private float _next;
        private bool _on;

        private void Start()
        {
            _ship = GetComponentInParent<Ship>();
            _body = GetComponentInParent<Rigidbody>();
            var lips = new List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (c.name == "pen_lip") lips.Add(c);
            _lips = lips.ToArray();
        }

        private void Update()
        {
            if (_ship == null || _lips == null || Time.time < _next) return;
            _next = Time.time + 0.25f;
            bool sailing = _ship.GetSpeedSetting() != Ship.Speed.Stop
                        || (_body != null && _body.linearVelocity.magnitude > DriftSpeed);
            if (sailing == _on) return;
            _on = sailing;
            foreach (var lip in _lips) lip.enabled = sailing;
        }
    }

    internal sealed class PenBallast : MonoBehaviour
    {
        private void Start()
        {
            var body = GetComponentInParent<Rigidbody>();
            if (body == null) return;

            var colliders = GetComponentsInChildren<Collider>(true);
            var wasEnabled = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++) { wasEnabled[i] = colliders[i].enabled; colliders[i].enabled = false; }
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
            var centerOfMass = body.centerOfMass;
            var inertia      = body.inertiaTensor;
            var inertiaRot   = body.inertiaTensorRotation;
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = wasEnabled[i];
            body.centerOfMass          = centerOfMass;
            body.inertiaTensor         = inertia;
            body.inertiaTensorRotation = inertiaRot;
        }
    }
}
