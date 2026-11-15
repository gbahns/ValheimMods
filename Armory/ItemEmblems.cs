using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Armory
{
    /// <summary>
    /// Real items hung on the armoire's doors as decoration: a shield over two crossed swords
    /// on the left upper panel and two crossed axes on the right, built from the game's own item prefabs so they look exactly like
    /// the things they are.  Each copy is stripped to its meshes; nothing can be picked up.
    ///
    /// An item prefab holds more than the thing in the hand: the game's VisEquipment hangs
    /// only its "attach" child on a hand, and "attach_back" is how it looks sheathed (for the
    /// Berserkir axes, both axes crossed, which measured as a 1.5 m cube, turned the axe
    /// blade-out and pushed the shield half a meter off the door).  So where a prefab has an
    /// "attach", that is the whole emblem and everything beside it is dropped.
    ///
    /// Item prefabs are not modeled to any shared convention, so each one is measured: the
    /// bounds' longest axis is the weapon's length (the far end from the origin, which sits at
    /// the grip, is the business end) and its shortest axis is the flat of the blade or the
    /// board of the shield.  The item is turned so the flat faces out of the door and the length
    /// runs where asked, and scaled to the size asked for.
    /// </summary>
    internal static class ItemEmblems
    {
        // LoneAxe: the axe door shows one axe standing upright, the shield behind it if
        // WeaponsOver (the Berserkir axes are a pair already).  SwordSize: the swords' length as a share of the panel width; 0
        // for the usual 1.15.  WeaponsOver: the crossed pair lies over the shield instead of
        // behind it, for a shield whose board sits so far forward that it hides them.  Sunk:
        // how far that shield sinks into the door, 0 for the usual 1.5 cm; Seat: how deep the
        // weapons sink into its rim, 0 for the usual 5 mm.
        internal struct Set { public string Shield, Sword, Axe; public bool LoneAxe; public float SwordSize; public bool WeaponsOver; public float Sunk, Seat; }

        internal static readonly Set Bronze     = new Set { Shield = "ShieldBronzeBuckler", Sword = "SwordBronze",  Axe = "AxeBronze" };
        internal static readonly Set Iron       = new Set { Shield = "ShieldBanded",     Sword = "SwordIron",       Axe = "AxeIron", WeaponsOver = true };
        internal static readonly Set Silver     = new Set { Shield = "ShieldSilver",     Sword = "SwordSilver",     Axe = null, WeaponsOver = true, Sunk = 0.025f };   // no silver axe: swords on both doors
        internal static readonly Set Blackmetal = new Set { Shield = "ShieldBlackmetal", Sword = "SwordBlackmetal", Axe = "AxeBlackMetal", WeaponsOver = true, Sunk = 0.025f, Seat = 0.035f };
        internal static readonly Set Carapace   = new Set { Shield = null,               Sword = "SwordMistwalker", Axe = "AxeJotunBane" };   // no shield: the buckler's spike stood the weapons off the door
        internal static readonly Set Flametal   = new Set { Shield = null,               Sword = "SwordNiedhogg",   Axe = "AxeBerzerkr", LoneAxe = true, SwordSize = 1.45f };   // no shield: the flametal one stood the weapons too far out

        /// <summary>
        /// Whether the player has reached this metal: knows a recipe for any of the three, or
        /// has held one.  Nothing hides what another player already hung on a rack.
        /// </summary>
        internal static bool Known(Set set)
        {
            var player = Player.m_localPlayer;
            if (player == null) return true;
            foreach (var name in new[] { set.Shield, set.Sword, set.Axe })
            {
                if (name == null) continue;
                if (player.IsRecipeKnown(name)) return true;
                var drop = PrefabManager.Instance.GetPrefab(name)?.GetComponent<ItemDrop>();
                if (drop != null && player.IsMaterialKnown(drop.m_itemData.m_shared.m_name)) return true;
            }
            return false;
        }

        /// <summary>
        /// Dresses one door.  Everything is in the hinge's local space: <paramref name="center"/>
        /// is where the emblem goes on the door's face (z is the face itself), <paramref name="width"/>
        /// the panel width it has to fit, <paramref name="height"/> the height of its straight
        /// part below the arch, <paramref name="side"/> -1 for the left door.
        /// </summary>
        internal static void Dress(Transform hinge, Vector3 center, float width, float height, int side, Set set)
        {
            const float tilt = 32f;
            // Weapons in front of a shield: the shield's grip side sinks into the wood and the
            // weapons seat just into its rim, so the blades clear the rim and the boss.
            float sunk = set.Sunk > 0f ? set.Sunk : 0.015f;
            float seat = set.Seat > 0f ? set.Seat : 0.005f;
            float back = center.z + 0.004f;
            bool swords = side < 0 || set.Axe == null;
            if (!swords && set.LoneAxe)
            {
                // One axe standing on the door, head up and toward the outer edge, over the
                // shield when the set has its weapons in front and alone otherwise.
                float z = set.Shield != null && set.WeaponsOver ? Mathf.Max(Place(hinge, set.Shield, "Shield", center, back - sunk, width * 0.78f, Vector3.up, 0, false) - seat, back) : back;
                Place(hinge, set.Axe, "Axe", center, z, Mathf.Min(width * 1.7f, height * 0.95f), Vector3.up, side, true);
                return;
            }
            // A crossed pair behind the shield, heads up and facing outward: two swords on the
            // left door, two axes on the right.  Both lie flat on the door and pass through
            // each other where they cross, which the shield covers.
            string weapon = swords ? set.Sword : set.Axe, label = swords ? "Sword" : "Axe";
            float size = width * (swords && set.SwordSize > 0f ? set.SwordSize : 1.15f);
            if (set.WeaponsOver || set.Shield == null)
            {
                float zS = set.Shield != null ? Place(hinge, set.Shield, "Shield", center, back - sunk, width * 0.78f, Vector3.up, 0, false) : back + seat;
                float zW = Mathf.Max(zS - seat, back);   // never into the wood
                Place(hinge, weapon, label + "A", center, zW, size, Quaternion.Euler(0f, 0f, -tilt) * Vector3.up, +1, !swords);
                Place(hinge, weapon, label + "B", center, zW, size, Quaternion.Euler(0f, 0f,  tilt) * Vector3.up, -1, !swords);
                return;
            }
            float zA = Place(hinge, weapon, label + "A", center, back, size, Quaternion.Euler(0f, 0f, -tilt) * Vector3.up, +1, !swords);
            float zB = Place(hinge, weapon, label + "B", center, back, size, Quaternion.Euler(0f, 0f,  tilt) * Vector3.up, -1, !swords);
            // The shield's back (its grip, if the model has one) sinks into the weapons' guards,
            // so the board sits as close to the door as the cross under it allows.
            Place(hinge, set.Shield, "Shield", center, Mathf.Max(zA, zB) - 0.045f, width * 0.78f, Vector3.up, 0, false);
        }

        // Puts one item on the door with its back at backZ and returns the z its front reaches.
        // headX says which way (+x or -x) a weapon's head should face; 0 for a shield, which is
        // round and only needs its board toward the viewer.
        private static float Place(Transform hinge, string prefab, string label, Vector3 center, float backZ, float size, Vector3 along, int headX, bool wideEnd)
        {
            bool round = headX == 0;
            var source = PrefabManager.Instance.GetPrefab(prefab);
            if (source == null)
            {
                Jotunn.Logger.LogWarning($"[Armory] Emblem item '{prefab}' not found; the door goes without its {label.ToLowerInvariant()}.");
                return backZ;
            }
            var item = ArmoryPieces.InstantiateInert(source, hinge);
            item.name = "ArmoryEmblem" + label;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale    = Vector3.one;
            KeepHeldModel(item, prefab);
            Strip(item);
            SetLayer(item.transform, hinge.gameObject.layer);

            if (!LocalBounds(item, out var b))
            {
                Jotunn.Logger.LogWarning($"[Armory] Emblem item '{prefab}' has no mesh; removed.");
                Object.DestroyImmediate(item);
                return backZ;
            }
            var ext = b.size;
            int thin = Smallest(ext), lon = Largest(ext);
            if (lon == thin) lon = (thin + 1) % 3;
            // The flat faces +Z; for a shield the face is the side away from the grip at the origin.
            var flat = Axis(thin) * (round && b.center[thin] < 0f ? -1f : 1f);
            // The length runs along `along`, business end first: for a sword the end away from
            // the grip at the origin, for an axe the end its head widens.
            int across = 3 - thin - lon;
            // For an axe the wide end, measured from its vertices; the far end from the origin
            // when that cannot be measured, and always for a sword.
            float end = wideEnd ? WideEnd(item, b, lon, across) : 0f;
            if (end == 0f) end = b.center[lon] < 0f ? -1f : 1f;
            var length = Axis(lon) * end;
            if (round) length = Axis((thin + 1) % 3);   // any in-plane axis will do

            float extent = round ? Mathf.Max(ext[(thin + 1) % 3], ext[(thin + 2) % 3]) : ext[lon];
            float scale  = extent > 1e-4f ? size / extent : 1f;
            var rot = Quaternion.LookRotation(Vector3.forward, along) * Quaternion.Inverse(Quaternion.LookRotation(flat, length));
            if (!round)
            {
                // The head is whatever sticks out sideways from the grip line; the axis that
                // is neither length nor flat, on the side the bounds lean to.
                var head = rot * (Axis(across) * (b.center[across] < 0f ? -1f : 1f));
                if (head.x * headX < 0f) rot = Quaternion.AngleAxis(180f, along) * rot;   // the other face shows, the head swaps sides
            }

            float thick = ext[thin] * scale;
            // A shield hangs by its board, not its handles: whatever sticks out behind the rim
            // (the grip, the straps) goes into the wood, and it reports its rim's height, not
            // its boss's, so what lies over it rests on the rim and the boss (the carapace
            // buckler's whole spike) comes through the crossing.
            float behind = 0f, front = thick;
            if (round)
            {
                RimProfile(item, b, thin, flat[thin], out float rimBack, out float rimFront);
                behind = rimBack * scale;
                front  = (rimFront - rimBack) * scale;
            }
            var spot = new Vector3(center.x, center.y, backZ - behind + thick / 2f);
            item.transform.localScale    = Vector3.one * scale;
            item.transform.localRotation = rot;
            item.transform.localPosition = spot - rot * (b.center * scale);

            Jotunn.Logger.LogDebug($"[Armory] Emblem {label} '{prefab}': extents {ext.x:0.00}x{ext.y:0.00}x{ext.z:0.00} m, " +
                                  $"flat {Axis(thin)}, length {Axis(lon)} end {end:+0;-0}, scale {scale:0.00}, back at z {backZ - center.z:0.000}; parts {Parts(item)}.");
            return backZ + front;
        }

        // Where a round item's rim lies, measured from the back of its bounds in the flat's
        // direction: rimBack is the board's back (handles behind it are not counted), rimFront
        // the rim's height.  The outer quarter of the radius counts as rim.  The whole bounds
        // when the vertices cannot be read.
        private static void RimProfile(GameObject item, Bounds b, int thin, float flatSign, out float rimBack, out float rimFront)
        {
            int u = (thin + 1) % 3, v = (thin + 2) % 3;
            float radius = Mathf.Max(b.extents[u], b.extents[v]);
            float back = flatSign > 0f ? b.min[thin] : -b.max[thin];
            float rim = float.NegativeInfinity, rear = float.PositiveInfinity;
            try
            {
                foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = null;
                    if (r is SkinnedMeshRenderer s) mesh = s.sharedMesh;
                    else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                    if (mesh == null) continue;
                    var verts = ReadVertices(mesh);
                    if (verts == null) continue;
                    var toItem = item.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    foreach (var vert in verts)
                    {
                        var p = toItem.MultiplyPoint3x4(vert);
                        float du = p[u] - b.center[u], dv = p[v] - b.center[v];
                        if (du * du + dv * dv < radius * radius * 0.5625f) continue;   // inside 0.75 R
                        float h = p[thin] * flatSign;
                        rim = Mathf.Max(rim, h); rear = Mathf.Min(rear, h);
                    }
                }
            }
            catch { }
            if (float.IsInfinity(rim)) { rimBack = 0f; rimFront = b.size[thin]; }
            else { rimBack = Mathf.Clamp(rear - back, 0f, b.size[thin]); rimFront = Mathf.Clamp(rim - back, rimBack, b.size[thin]); }
            Jotunn.Logger.LogDebug($"[Armory] Emblem '{item.name}': rim from {rimBack:0.000} to {rimFront:0.000} of {b.size[thin]:0.000} m thick.");
        }

        // The model the game puts in a hand is the "attach" child; the sheathed "attach_back",
        // a skinned "attach_skin", the "equipoffset" marker and whatever else sits beside it
        // would be measured too, so they go.  Prefabs without an "attach" are left whole.
        private static void KeepHeldModel(GameObject item, string prefab)
        {
            Transform attach = null;
            foreach (Transform c in item.transform) if (c.name == "attach") { attach = c; break; }
            if (attach == null) return;
            var dropped = new System.Text.StringBuilder();
            for (int i = item.transform.childCount - 1; i >= 0; i--)
            {
                var c = item.transform.GetChild(i);
                if (c == attach) continue;
                if (c.GetComponentInChildren<Renderer>(true) != null) dropped.Append(dropped.Length > 0 ? ", " : "").Append(c.name);
                Object.DestroyImmediate(c.gameObject);
            }
            attach.gameObject.SetActive(true);
            if (dropped.Length > 0) Jotunn.Logger.LogDebug($"[Armory] Emblem item '{prefab}': measured from its 'attach' model; dropped {dropped}.");
        }

        // Leaves transforms and renderers; everything that makes an item an item goes.
        private static void Strip(GameObject item)
        {
            foreach (var c in item.GetComponentsInChildren<ItemDrop>(true))       Object.DestroyImmediate(c);
            foreach (var c in item.GetComponentsInChildren<MonoBehaviour>(true))  Object.DestroyImmediate(c);
            foreach (var c in item.GetComponentsInChildren<Joint>(true))          Object.DestroyImmediate(c);
            foreach (var c in item.GetComponentsInChildren<Rigidbody>(true))      Object.DestroyImmediate(c);
            foreach (var c in item.GetComponentsInChildren<Collider>(true))       Object.DestroyImmediate(c);
            foreach (var g in item.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers) if (r != null) Object.DestroyImmediate(r.gameObject);
                Object.DestroyImmediate(g);
            }
            // Whatever else came along (particles, sounds, lights: modules this project does
            // not reference) goes by name; a particle system takes its object with it, since its
            // renderer cannot outlive it.
            foreach (var t in item.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var comps = t.GetComponents<Component>();
                bool particles = false;
                foreach (var c in comps) if (c != null && c.GetType().Name == "ParticleSystem") particles = true;
                if (particles && t != item.transform) { Object.DestroyImmediate(t.gameObject); continue; }
                foreach (var c in comps)
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                    Object.DestroyImmediate(c);
                }
                t.gameObject.SetActive(true);
            }
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        // The renderers' bounds in the item's own space, from each mesh's bounds (vertices are
        // not readable on the game's meshes).
        // Which end of the long axis the mesh spreads widest across: +1 for the far end, -1
        // for the near, 0 when the vertices cannot be read and the caller falls back.  Only
        // the outer third at each end counts; a big head straddles the middle.
        private static float WideEnd(GameObject item, Bounds b, int lon, int across)
        {
            float far = 0f, near = 0f;
            float farFrom = b.max[lon] - b.size[lon] / 3f, nearTo = b.min[lon] + b.size[lon] / 3f;
            bool any = false;
            try
            {
                foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = null;
                    if (r is SkinnedMeshRenderer s) mesh = s.sharedMesh;
                    else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                    if (mesh == null) continue;
                    var verts = ReadVertices(mesh);
                    if (verts == null) continue;
                    var toItem = item.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    foreach (var v in verts)
                    {
                        var p = toItem.MultiplyPoint3x4(v);
                        float spread = Mathf.Abs(p[across] - b.center[across]);
                        if (p[lon] >= farFrom) far = Mathf.Max(far, spread);
                        else if (p[lon] <= nearTo) near = Mathf.Max(near, spread);
                        any = true;
                    }
                }
            }
            catch { return 0f; }
            Jotunn.Logger.LogDebug($"[Armory] Emblem '{item.name}': spread at the far end {far:0.000}, near end {near:0.000}{(any ? "" : " (mesh not readable)")}.");
            if (!any || Mathf.Abs(far - near) < 0.01f) return 0f;
            return far > near ? 1f : -1f;
        }

        // The vertices, read back from the GPU when the mesh is not readable from script,
        // which Valheim's are not.  Null when that cannot be done either, or when what came
        // back does not fit the mesh's own bounds: some meshes answer with zeros.  The mesh
        // is the game's own and is not touched (changing its buffer target reset it).
        private static readonly Dictionary<Mesh, Vector3[]> _read = new Dictionary<Mesh, Vector3[]>();

        private static Vector3[] ReadVertices(Mesh mesh)
        {
            if (mesh.isReadable) return mesh.vertices;
            if (_read.TryGetValue(mesh, out var cached)) return cached;
            var verts = ReadBack(mesh);
            if (verts != null && !Fits(verts, mesh.bounds))
            {
                Jotunn.Logger.LogDebug($"[Armory] Emblem mesh '{mesh.name}': the GPU copy does not match its bounds; measured from the bounds instead.");
                verts = null;
            }
            _read[mesh] = verts;
            return verts;
        }

        private static bool Fits(Vector3[] verts, Bounds b)
        {
            var got = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) got.Encapsulate(v);
            var slack = b.size * 0.05f + Vector3.one * 0.01f;
            return got.size.x > 1e-4f && got.size.y + got.size.z > 1e-4f
                && Vector3.Max(got.min - b.min, Vector3.zero).magnitude + Vector3.Max(b.max - got.max, Vector3.zero).magnitude < slack.magnitude
                && Vector3.Max(b.min - got.min, Vector3.zero).magnitude + Vector3.Max(got.max - b.max, Vector3.zero).magnitude < slack.magnitude;
        }

        private static Vector3[] ReadBack(Mesh mesh)
        {
            try
            {
                int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
                if (stream < 0 || mesh.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32
                               || mesh.GetVertexAttributeDimension(VertexAttribute.Position) < 3) return null;
                int offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
                int stride = mesh.GetVertexBufferStride(stream);
                using (var buffer = mesh.GetVertexBuffer(stream))
                {
                    if (buffer == null) return null;
                    var raw = new byte[buffer.count * buffer.stride];
                    buffer.GetData(raw);
                    var verts = new Vector3[mesh.vertexCount];
                    for (int i = 0; i < verts.Length; i++)
                    {
                        int o = i * stride + offset;
                        if (o + 12 > raw.Length) return null;
                        verts[i] = new Vector3(System.BitConverter.ToSingle(raw, o), System.BitConverter.ToSingle(raw, o + 4), System.BitConverter.ToSingle(raw, o + 8));
                    }
                    return verts;
                }
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogDebug($"[Armory] Emblem mesh '{mesh.name}' could not be read back: {e.Message}");
                return null;
            }
        }

        // What an item is made of, for the log: each renderer's name and its bounds in item space.
        private static string Parts(GameObject item)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer s) mesh = s.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                var toItem = item.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                string span = "no mesh";
                if (mesh != null)
                {
                    var c = toItem.MultiplyPoint3x4(mesh.bounds.center);
                    var e = toItem.MultiplyVector(mesh.bounds.extents);
                    span = $"center ({c.x:0.00}, {c.y:0.00}, {c.z:0.00}) extents ({Mathf.Abs(e.x):0.00}, {Mathf.Abs(e.y):0.00}, {Mathf.Abs(e.z):0.00})";
                }
                sb.Append(sb.Length > 0 ? "; " : "").Append(r.name).Append(mesh != null ? "/" + mesh.name : "").Append(": ").Append(span);
            }
            return sb.ToString();
        }

        private static bool LocalBounds(GameObject item, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer s) mesh = s.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                if (mesh == null) continue;
                var toItem = item.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                // The vertices themselves when they can be had: a renderer turned inside its
                // item makes the corners of its box a far looser fit than the mesh.
                var verts = ReadVertices(mesh);
                if (verts != null && verts.Length > 0)
                {
                    foreach (var v in verts)
                    {
                        var p = toItem.MultiplyPoint3x4(v);
                        if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                        else bounds.Encapsulate(p);
                    }
                    continue;
                }
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toItem.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }

        private static int Smallest(Vector3 v) => v.x <= v.y && v.x <= v.z ? 0 : v.y <= v.z ? 1 : 2;
        private static int Largest(Vector3 v)  => v.x >= v.y && v.x >= v.z ? 0 : v.y >= v.z ? 1 : 2;
        private static Vector3 Axis(int i)     => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    }
}
