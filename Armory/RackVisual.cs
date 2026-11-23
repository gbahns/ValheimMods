using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace Armory
{
    /// <summary>Which model the rack wears.</summary>
    public enum RackModel
    {
        Armoire,   // the Blender model in Model/armoire.py: bevels, arched panels, strap hinges, and a crown of the rack's CrownCurve
        Cabinet,   // the model built from boxes in this file
        Panels,    // the original: vanilla floor planks and shutters, tinted
    }

    /// <summary>What hangs on the doors, on any model.</summary>
    public enum RackDecor
    {
        None,
        Shields,     // a carved round shield over crossed axes on each door, from the Blender model
        Bronze,      // the game's bronze buckler and crossed bronze swords on one door, axes on the other
        Iron,        // ... the banded shield, iron swords and iron axes
        Silver,      // ... the silver shield and silver swords (there is no silver axe)
        Blackmetal,  // ... the black metal shield, swords and axes
        Carapace,    // ... Mistwalkers and Jotun banes, no shield
        Flametal,    // ... Niedhoggs and the Berserkir axes, no shield
    }

    /// <summary>The Armoire's crown: the stepped one from the model, or a bonnet top whose
    /// front rises in this outline from the shoulders to the crest (#83).</summary>
    public enum CrownCurve
    {
        Flat,     // the three stepped moldings of Model/armoire.obj
        Ogee,     // an S: concave off the shoulder, convex into the crest
        Pointed,  // concave all the way, meeting in a point at the center
    }

    /// <summary>How tall a bonnet crown stands; Flat ignores it.</summary>
    public enum CrownHeight { Low, Medium, High }

    /// <summary>One rack's whole look: the model, what hangs on it, its wood, and the Armoire's
    /// crown (which the other models ignore).</summary>
    public struct RackStyle
    {
        public RackModel   Model;
        public RackDecor   Decor;
        public RackWood    Wood;
        public CrownCurve  Curve;
        public CrownHeight Crown;

        public RackStyle(RackModel model, RackDecor decor, RackWood wood, CrownCurve curve, CrownHeight crown)
        { Model = model; Decor = decor; Wood = wood; Curve = curve; Crown = crown; }

        public bool Same(RackStyle o) => Model == o.Model && Decor == o.Decor && Wood == o.Wood && Curve == o.Curve && Crown == o.Crown;
    }

    /// <summary>Which of the game's woods a built model wears.</summary>
    public enum RackWood
    {
        Darkwood,    // the near-black darkwood, brown plank door panels
        Brownwood,   // the darkwood beam's brown planks, near-black door panels
        Plainwood,   // the plain wood every workbench piece is made of
    }

    /// <summary>
    /// The rack's looks (issue #77).  Everything visible sits under one "ArmoryVisual" child
    /// of the rack, so a look can be swapped live by rebuilding that child; the collider,
    /// WearNTear and the hidden chest the piece was cloned from stay on the root.
    ///
    /// The cabinet is generated geometry, board by board, wearing materials borrowed from
    /// vanilla build pieces so the textures match every other piece and nothing has to be
    /// painted.  The game's textures are atlases (the darkwood one is a near-black strip
    /// beside brown planks), so each submesh maps its faces into one chosen region of its
    /// texture, grain along the board, instead of tiling across the whole picture.  The same
    /// path will draw a Blender model once there is one: the mesh changes, the materials and
    /// the hinge objects stay.
    /// </summary>
    internal static class RackVisual
    {
        public const string RootName   = "ArmoryVisual";
        public const string LeftHinge  = "ArmoryDoorLeft";
        public const string RightHinge = "ArmoryDoorRight";

        // The footprint both looks fill, and the collider: 2 m wide, 3 m tall, 1 m deep,
        // standing on y = 0 with the doors on +Z.  Tried shorter (2.2 and 2.6 m) and asked back
        // to the Panels height.  The Small Armory Rack is this at ArmoryRack.SmallScale: the
        // whole look is built in these units and the ArmoryVisual child is scaled (#84).
        public const float Width = 2f, Height = 3f, Depth = 1f;

        // The settings: what a rack wears until someone gives it its own style.
        public static RackModel   Model => ArmoryMod.RackModel?.Value   ?? RackModel.Armoire;
        public static RackDecor   Decor => ArmoryMod.RackDecor?.Value   ?? RackDecor.None;
        public static RackWood    Wood  => ArmoryMod.RackWood?.Value    ?? RackWood.Darkwood;
        public static CrownCurve  Curve => ArmoryMod.CrownCurve?.Value  ?? CrownCurve.Ogee;
        public static CrownHeight Crown => ArmoryMod.CrownHeight?.Value ?? CrownHeight.Medium;
        public static RackStyle Defaults => new RackStyle(Model, Decor, Wood, Curve, Crown);

        /// <summary>A name for the picker.</summary>
        internal static string DisplayName(RackModel model) => model.ToString();

        /// <summary>A name for the picker, "Black metal" rather than Blackmetal.</summary>
        internal static string DisplayName(RackDecor decor)
        {
            switch (decor)
            {
                case RackDecor.Shields:    return "Carved shields";
                case RackDecor.Blackmetal: return "Black metal";
                default:                   return decor.ToString();
            }
        }

        /// <summary>The items a metal decoration hangs; null for the rest.</summary>
        internal static ItemEmblems.Set? Emblems(RackDecor decor)
        {
            switch (decor)
            {
                case RackDecor.Bronze:     return ItemEmblems.Bronze;
                case RackDecor.Iron:       return ItemEmblems.Iron;
                case RackDecor.Silver:     return ItemEmblems.Silver;
                case RackDecor.Blackmetal: return ItemEmblems.Blackmetal;
                case RackDecor.Carapace:   return ItemEmblems.Carapace;
                case RackDecor.Flametal:   return ItemEmblems.Flametal;
                default:                   return null;
            }
        }

        /// <summary>Whether the picker offers this decoration: the metals wait for the player to reach them.</summary>
        internal static bool Unlocked(RackDecor decor)
        {
            var set = Emblems(decor);
            return !set.HasValue || ItemEmblems.Known(set.Value);
        }

        /// <summary>Builds the rack's look: its own if it was styled in game, else the settings'.</summary>
        internal static void Build(GameObject rack)
        {
            var own = rack.GetComponent<ArmoryRack>();
            Build(rack, own != null ? own.EffectiveStyle : Defaults);
        }

        /// <summary>Builds the given look at the rack's own size: full, or the small rack's.</summary>
        internal static void Build(GameObject rack, RackStyle style)
        {
            var own = rack.GetComponent<ArmoryRack>();
            Build(rack, style, own != null ? own.VisualScale : 1f);
        }

        /// <summary>Redraws one standing rack and tells the game's caches and the rack itself.</summary>
        internal static void Rebuild(ArmoryRack rack)
        {
            var style = rack.EffectiveStyle;
            Build(rack.gameObject, style);
            rack.RefreshHinges();
            RefreshGameCaches(rack.gameObject);
            rack.NoteApplied(style);
        }

        /// <summary>Builds the given look under a fresh ArmoryVisual child, replacing any previous
        /// one, at the given scale (1 = the standard rack's 2 × 3 × 1 m).</summary>
        internal static void Build(GameObject rack, RackStyle style, float scale)
        {
            var model = style.Model; var decor = style.Decor; var wood = style.Wood;
            var old = rack.transform.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            var root = new GameObject(RootName);
            root.layer = rack.layer;
            root.transform.SetParent(rack.transform, false);
            root.transform.localScale = Vector3.one * scale;

            var col = rack.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.center = new Vector3(0f, Height * scale / 2f, 0f);
                col.size   = new Vector3(Width, Height, Depth) * scale;
            }

            switch (model)
            {
                case RackModel.Panels:        ArmoryPieces.BuildPanelLook(root); DecoratePanels(root, decor, wood); break;
                case RackModel.Cabinet:       BuildCabinet(root, wood, decor); break;
                default:                      BuildArmoire(root, wood, decor, style.Curve, style.Crown); break;
            }
        }

        // ── Decorations, on any model's doors ─────────────────────────────────────────

        /// <summary>
        /// Hangs the decoration on one door.  Everything is in the hinge's local space:
        /// <paramref name="center"/> is the middle of the door's upper panel with z on its face,
        /// <paramref name="width"/> and <paramref name="height"/> the panel's, <paramref name="side"/>
        /// -1 for the left door.
        /// </summary>
        private static void Decorate(Transform hinge, Vector3 center, float width, float height, int side, RackDecor decor, RackWood wood)
        {
            if (decor == RackDecor.None) return;
            if (decor == RackDecor.Shields) { Carve(hinge, center, width, side, wood); return; }
            var set = Emblems(decor);
            if (set.HasValue) ItemEmblems.Dress(hinge, center, width, height, side, set.Value);
        }

        // The carved shield and axes are objects of their own in the Blender model, named
        // DoorL_Shield, DoorR_A_AxeHead and so on.  They are lifted off the armoire's upper
        // panel and set down on another door's, scaled to its width.
        private static void Carve(Transform hinge, Vector3 center, float width, int side, RackWood wood)
        {
            var model = LoadModel("armoire_shields.obj");
            if (model == null || !DoorPanel(model, side, Vector3.zero, out var from, out float fromWidth, out _)) return;
            float s = width / fromWidth;
            Split(Parts(wood), out var mats, out var regions);
            var b = new MeshBuilder(regions);
            int n = 0;
            foreach (var p in model)
            {
                if (!p.IsDoor || p.Side != side || !(p.Object.Contains("Shield") || p.Object.Contains("Axe"))) continue;
                var pts = new Vector3[p.Points.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = (p.Points[i] - from) * s + center;
                b.Polygon(p.Material, pts, p.Shift);
                n++;
            }
            if (n > 0) Attach(hinge, "ArmoryCarving", Vector3.zero, b.ToMesh(), mats);
        }

        // The original look's doors are shutter slabs a meter wide; the decoration goes on the
        // upper part of each, standing on the frontmost face the slab has.
        private static void DecoratePanels(GameObject root, RackDecor decor, RackWood wood)
        {
            if (decor == RackDecor.None) return;
            Transform left = root.transform.Find(LeftHinge), right = root.transform.Find(RightHinge);
            if (left == null || right == null) return;
            const float width = 0.8f, height = 1.1f, middle = 2.05f;
            Decorate(left,  new Vector3( 0.5f, middle, Face(left)),  width, height, -1, decor, wood);
            Decorate(right, new Vector3(-0.5f, middle, Face(right)), width, height, +1, decor, wood);
        }

        // The frontmost z of everything drawn under a hinge, in the hinge's space.
        private static float Face(Transform hinge)
        {
            float face = float.NegativeInfinity;
            foreach (var mf in hinge.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var toHinge = hinge.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    face = Mathf.Max(face, toHinge.MultiplyPoint3x4(corner).z);
                }
            }
            return float.IsInfinity(face) ? 0f : face;
        }

        /// <summary>A style setting changed: redraw the prefabs and every standing rack that follows them.</summary>
        internal static void RebuildAll()
        {
            foreach (var prefab in ArmoryPieces.Prefabs) Build(prefab);
            int n = 0;
            foreach (var rack in ArmoryRack.Instances)
            {
                if (rack == null) continue;
                Rebuild(rack);
                n++;
            }
            Jotunn.Logger.LogInfo($"[Armory] Rack model {Model}, {Decor}, in {Wood}; {n} rack(s) redrawn.");
            ArmoryStyleUI.Redraw();   // the picker's picture follows the same settings
            ArmoryPieces.RefreshIcon(); // and so does the hammer menu's icon
        }

        // Private in the game; reached by reflection.
        private static readonly FieldInfo  _wntRenderers       = AccessTools.Field(typeof(WearNTear), "m_renderers");
        private static readonly MethodInfo _wntGatherRenderers = AccessTools.Method(typeof(WearNTear), "GetHighlightRenderers");
        private static readonly MethodInfo _matManRegister     = AccessTools.Method(typeof(MaterialMan), "RegisterRenderers");

        /// <summary>
        /// A standing rack's renderers were just destroyed and remade.  WearNTear listed them
        /// once in Awake and reads their bounds every wear tick (an NRE storm after the first
        /// live switch, 2026-11-13), and MaterialMan keeps its own list for the hover highlight.
        /// Both are told to look again.
        /// </summary>
        private static void RefreshGameCaches(GameObject rack)
        {
            var wnt = rack.GetComponent<WearNTear>();
            if (wnt != null && _wntRenderers != null && _wntGatherRenderers != null && _wntRenderers.GetValue(wnt) != null)
                _wntRenderers.SetValue(wnt, _wntGatherRenderers.Invoke(wnt, null));
            if (MaterialMan.instance != null && _matManRegister != null)
                _matManRegister.Invoke(MaterialMan.instance, new object[] { rack, true });
        }

        // ── Looks: a material and a texture region per part ───────────────────────────

        private const int Body  = 0;   // carcass, plinth, cornice, door frames
        private const int Panel = 1;   // the recessed door panels
        private const int Iron  = 2;   // the handles

        /// <summary>
        /// Where on a texture a part's faces go.  A face's long side runs along V (the grain)
        /// at VPerMeter, its short side across U at UPerMeter; a side that would not fit the
        /// region is compressed to it, unless the region wraps on that axis.
        /// </summary>
        private struct Region
        {
            public float U0, U1, V0, V1, UPerMeter, VPerMeter;
            public bool WrapU, WrapV;
            public bool GrainU;   // the picture's grain runs along U, so the long side goes there
        }

        // Measured off the dumped textures (2026-11-13) and the darkwood beam, whose 2.02 m
        // length covers 0.58 of V and 0.41 m width 0.65 of U.
        private static readonly Region DarkStrip   = new Region { U0 = 0.03f, U1 = 0.26f, V0 = 0f, V1 = 1f, UPerMeter = 0.8f, VPerMeter = 0.3f, WrapV = true };
        private static readonly Region BrownPlanks = new Region { U0 = 0.42f, U1 = 0.98f, V0 = 0f, V1 = 1f, UPerMeter = 1.6f, VPerMeter = 0.3f, WrapV = true };
        // Plain wood is one tileable picture with the grain across it.  The floor tile maps a
        // meter to one UV unit and lets the material's own scale (0.30, 0.13) shrink it, so the
        // same density here is what makes the cabinet match a floor; anything denser came out
        // as noise (2026-11-13).
        private static readonly Region PlainWood   = new Region { U0 = 0f, U1 = 1f, V0 = 0f, V1 = 1f, UPerMeter = 1f, VPerMeter = 1f, WrapU = true, WrapV = true, GrainU = true };
        // A grey patch of the reinforced chest's atlas.
        private static readonly Region IronPatch   = new Region { U0 = 0.69f, U1 = 0.83f, V0 = 0.25f, V1 = 0.50f, UPerMeter = 1f, VPerMeter = 1f };

        private struct Part
        {
            public Material Material;
            public Region   Region;
        }

        private static Part[] Parts(RackWood wood)
        {
            var dark  = Find("darkwood_beam", null, "darkwood_pole", null, "darkwood_wolf", null);
            var plain = Find("wood_floor_1x1", null, "wood_wall", null);
            var iron  = Find("piece_chest", "iron", "piece_chest", "metal", "iron_grate", null, "piece_chest", null);
            if (dark == null)  dark  = plain;
            if (plain == null) plain = dark;
            if (iron == null)  iron  = dark;
            Jotunn.Logger.LogInfo($"[Armory] Cabinet materials: darkwood '{Name(dark)}', plain '{Name(plain)}', iron '{Name(iron)}'.");

            var handles = new Part { Material = iron, Region = IronPatch };
            switch (wood)
            {
                case RackWood.Brownwood:
                    return new[] { new Part { Material = dark, Region = BrownPlanks }, new Part { Material = dark, Region = DarkStrip }, handles };
                case RackWood.Plainwood:
                    return new[] { new Part { Material = plain, Region = PlainWood }, new Part { Material = plain, Region = PlainWood }, handles };
                default:
                    return new[] { new Part { Material = dark, Region = DarkStrip }, new Part { Material = dark, Region = BrownPlanks }, handles };
            }
        }

        private const float Board      = 0.08f;   // the cabinet's side and door thickness
        private const float BoardWidth = 0.32f;   // widest board before a surface is split

        private static void Split(Part[] parts, out Material[] mats, out Region[] regions)
        {
            mats    = new Material[parts.Length];
            regions = new Region[parts.Length];
            for (int i = 0; i < parts.Length; i++) { mats[i] = parts[i].Material; regions[i] = parts[i].Region; }
        }

        // ── The armoire, from the embedded Blender model ──────────────────────────────

        private static readonly Dictionary<string, List<ObjModel.Polygon>> _models = new Dictionary<string, List<ObjModel.Polygon>>();

        private static List<ObjModel.Polygon> LoadModel(string file)
        {
            if (!_models.TryGetValue(file, out var model))
                _models[file] = model = ObjModel.LoadEmbedded(file);
            return model != null && model.Count > 0 ? model : null;
        }

        private static void BuildArmoire(GameObject root, RackWood wood, RackDecor decor, CrownCurve curve, CrownHeight crown)
        {
            bool curved = curve != CrownCurve.Flat;
            // The carved shields are part of the flat-crowned armoire's second export; the
            // bonnet-topped one gets them lifted onto its doors like any other model.
            bool carvedIn = decor == RackDecor.Shields && !curved;
            string file = curved ? "armoire_curved.obj" : carvedIn ? "armoire_shields.obj" : "armoire.obj";
            var model = LoadModel(file);
            if (model == null)
            {
                Jotunn.Logger.LogWarning($"[Armory] No model '{file}'; drawing the cabinet instead.");
                BuildCabinet(root, wood, decor);
                return;
            }
            Split(Parts(wood), out var mats, out var regions);

            // Each door hangs on a hinge at its outer front edge: the outermost X and the
            // frontmost Z of its wooden parts (the iron barrels sit on that edge and stick out).
            float leftX = 0f, rightX = 0f, frontZ = 0f;
            bool any = false;
            foreach (var p in model)
            {
                if (!p.IsDoor || p.Material == Iron) continue;
                foreach (var q in p.Points)
                {
                    if (!any) { leftX = rightX = q.x; frontZ = q.z; any = true; }
                    leftX  = Mathf.Min(leftX, q.x);
                    rightX = Mathf.Max(rightX, q.x);
                    frontZ = Mathf.Max(frontZ, q.z);
                }
            }
            var leftHinge  = new Vector3(leftX, 0f, frontZ);
            var rightHinge = new Vector3(rightX, 0f, frontZ);

            var body = new MeshBuilder(regions);
            float crownTop = 0f;
            var left = new MeshBuilder(regions);
            var right = new MeshBuilder(regions);
            foreach (var p in model)
            {
                // The bonnet model's hood and pediment are drawn in code instead, from the rack's
                // crown shape and height; its molding stays and gives the hood its base.
                if (curved && (p.Object == "Crown_Hood" || p.Object == "Crown_Pediment")) continue;
                if (curved && p.Object == "Crown_0") foreach (var q in p.Points) crownTop = Mathf.Max(crownTop, q.y);
                if (!p.IsDoor)      body.Polygon(p.Material, p.Points, p.Shift);
                else if (p.Side < 0) left.Polygon(p.Material, Offset(p.Points, leftHinge), p.Shift);
                else                 right.Polygon(p.Material, Offset(p.Points, rightHinge), p.Shift);
            }
            if (curved) BuildBonnet(body, crownTop > 0f ? crownTop : Height - 0.29f, curve, crown);
            Attach(root.transform, "ArmoryBody", Vector3.zero, body.ToMesh(), mats);
            var leftDoor  = Attach(root.transform, LeftHinge,  leftHinge,  left.ToMesh(),  mats);
            var rightDoor = Attach(root.transform, RightHinge, rightHinge, right.ToMesh(), mats);

            if (decor != RackDecor.None && !carvedIn)
            {
                if (DoorPanel(model, -1, leftHinge,  out var c, out float w, out float h)) Decorate(leftDoor.transform,  c, w, h, -1, decor, wood);
                if (DoorPanel(model, +1, rightHinge, out c, out w, out h))                 Decorate(rightDoor.transform, c, w, h, +1, decor, wood);
            }
        }

        // ── The bonnet ────────────────────────────────────────────────────────────────

        // A solid hood the armoire's depth, its front rising from the shoulders to the crest,
        // and a pediment board in front of it grown by the lip.
        private static void BuildBonnet(MeshBuilder b, float baseY, CrownCurve curve, CrownHeight height)
        {
            float hw = Width / 2f + 0.05f, hd = Depth / 2f;
            Shape(curve, height, out float rise, out float shoulder, out float lip);

            Hood(b, baseY, hw, shoulder, rise, 0f, curve, -hd - 0.025f, hd + 0.025f, 0.2f);
            if (lip > 0.001f)
                Hood(b, baseY, hw + lip, shoulder + lip * 0.7f, rise, 0f, curve, hd + 0.02f, hd + 0.02f + Mathf.Max(0.02f, lip), 0.5f);
        }

        // The crown's proportions by curve and height: the crest's rise above the shoulders,
        // the shoulders' height, and how far the pediment board outgrows the hood.
        private static void Shape(CrownCurve curve, CrownHeight height, out float rise, out float shoulder, out float lip)
        {
            switch (height)
            {
                case CrownHeight.Low:  rise = 0.12f; shoulder = 0.04f; lip = 0.025f; break;
                case CrownHeight.High: rise = 0.30f; shoulder = 0.08f; lip = 0.035f; break;
                default:               rise = 0.21f; shoulder = 0.06f; lip = 0.03f;  break;
            }
            if (curve == CrownCurve.Pointed) { rise *= 1.15f; shoulder *= 0.75f; }   // a point wants more height and less shoulder
        }

        // The hood's outline, y above the base at each x: flat shoulders, then the curve.
        private static float Outline(float x, float hw, float shoulder, float rise, float crest, CrownCurve curve)
        {
            float slope = hw * (1f - Mathf.Clamp01(crest));
            float s = slope > 1e-4f ? Mathf.Clamp01((hw - Mathf.Abs(x)) / slope) : 1f;
            float f;
            switch (curve)
            {
                case CrownCurve.Pointed: f = s * s; break;
                default:                 f = s * s * (3f - 2f * s); break;
            }
            return shoulder + rise * f;
        }

        // One outlined slab from z0 to z1: column quads for its front and back, quads along
        // the curve on top, a floor, and its two ends.
        private static void Hood(MeshBuilder b, float baseY, float hw, float shoulder, float rise, float crest, CrownCurve curve, float z0, float z1, float shift)
        {
            const int n = 40;
            var xs = new float[n + 1]; var ys = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                xs[i] = -hw + 2f * hw * i / n;
                ys[i] = baseY + Outline(xs[i], hw, shoulder, rise, crest, curve);
            }
            for (int i = 0; i < n; i++)
            {
                float xa = xs[i], xb = xs[i + 1], ya = ys[i], yb = ys[i + 1];
                b.Quad(Body, new Vector3(xa, baseY, z1), new Vector3(xb, baseY, z1), new Vector3(xb, yb, z1), new Vector3(xa, ya, z1), Vector3.forward, shift);
                b.Quad(Body, new Vector3(xa, baseY, z0), new Vector3(xb, baseY, z0), new Vector3(xb, yb, z0), new Vector3(xa, ya, z0), Vector3.back, shift);
                var up = Vector3.Cross(Vector3.forward, new Vector3(xb - xa, yb - ya, 0f)).normalized;
                if (up.y < 0f) up = -up;
                b.Quad(Body, new Vector3(xa, ya, z0), new Vector3(xb, yb, z0), new Vector3(xb, yb, z1), new Vector3(xa, ya, z1), up, shift);
            }
            b.Quad(Body, new Vector3(-hw, baseY, z0), new Vector3(hw, baseY, z0), new Vector3(hw, baseY, z1), new Vector3(-hw, baseY, z1), Vector3.down, shift);
            b.Quad(Body, new Vector3(-hw, baseY, z0), new Vector3(-hw, ys[0], z0), new Vector3(-hw, ys[0], z1), new Vector3(-hw, baseY, z1), Vector3.left, shift);
            b.Quad(Body, new Vector3( hw, baseY, z0), new Vector3( hw, ys[n], z0), new Vector3( hw, ys[n], z1), new Vector3( hw, baseY, z1), Vector3.right, shift);
        }

        // The upper door panel's frame for a decoration: its center under the arch's spring
        // line (the widest part) with z on the door's face, in the hinge's space, and its
        // width and the height of its straight part.
        private static bool DoorPanel(List<ObjModel.Polygon> model, int side, Vector3 hingePos, out Vector3 center, out float width, out float height)
        {
            center = Vector3.zero; width = height = 0f;
            bool any = false;
            var min = Vector3.zero; var max = Vector3.zero;
            float face = 0f;
            foreach (var p in model)
            {
                if (!p.IsDoor || p.Side != side) continue;
                foreach (var q in p.Points)
                {
                    if (p.Material != Iron) face = Mathf.Max(face, q.z);
                    if (!p.Object.EndsWith("PanelUpper")) continue;
                    if (!any) { min = max = q; any = true; }
                    min = Vector3.Min(min, q); max = Vector3.Max(max, q);
                }
            }
            if (!any) return false;
            width  = max.x - min.x;
            float spring = max.y - width / 2f;                        // where the half-round arch starts
            height = spring - min.y;
            center = new Vector3((min.x + max.x) / 2f, (min.y + spring) / 2f, face) - hingePos;
            return true;
        }

        private static Vector3[] Offset(Vector3[] pts, Vector3 by)
        {
            var o = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++) o[i] = pts[i] - by;
            return o;
        }

        // ── The cabinet, from boxes ───────────────────────────────────────────────────

        private static void BuildCabinet(GameObject root, RackWood wood, RackDecor decor)
        {
            Split(Parts(wood), out var mats, out var regions);

            float hw = Width / 2f, hd = Depth / 2f;
            float y0 = 0.18f, y1 = Height - 0.18f;   // the carcass, between plinth and cornice
            float zf = hd - Board;                   // its front face, behind the doors
            float shelf = (y0 + y1) / 2f;

            var b = new MeshBuilder(regions);
            // Plinth and cornice stand a little proud of the body.
            b.Box(Body, new Vector3(-hw - 0.03f, 0f, -hd - 0.03f), new Vector3(hw + 0.03f, y0, hd + 0.03f));
            b.Box(Body, new Vector3(-hw - 0.05f, y1, -hd - 0.05f), new Vector3(hw + 0.05f, Height, hd + 0.05f));
            // Sides, back, floor, ceiling and one shelf, each a run of boards.  Real boards,
            // so the inside is drawn too and the open cabinet has an interior.
            b.Boards(Body, new Vector3(-hw, y0, -hd),                         new Vector3(-hw + Board, y1, zf),             Axis.Z);
            b.Boards(Body, new Vector3(hw - Board, y0, -hd),                  new Vector3(hw, y1, zf),                      Axis.Z);
            b.Boards(Body, new Vector3(-hw + Board, y0, -hd),                 new Vector3(hw - Board, y1, -hd + 0.05f),     Axis.X);
            b.Boards(Body, new Vector3(-hw + Board, y0, -hd + 0.05f),         new Vector3(hw - Board, y0 + 0.06f, zf),      Axis.X);
            b.Boards(Body, new Vector3(-hw + Board, y1 - 0.06f, -hd + 0.05f), new Vector3(hw - Board, y1, zf),              Axis.X);
            b.Boards(Body, new Vector3(-hw + Board, shelf - 0.03f, -hd + 0.05f), new Vector3(hw - Board, shelf + 0.03f, zf - 0.05f), Axis.X);
            Attach(root.transform, "ArmoryBody", Vector3.zero, b.ToMesh(), mats);

            // Doors hang on hinge objects at the outer front corners, the way the original did,
            // so ArmoryRack's swing works unchanged: the left door extends +X from its hinge and
            // the right door -X from its own.
            var leftDoor  = Attach(root.transform, LeftHinge,  new Vector3(-hw, 0f, hd), Door(+1f, y0, y1, regions), mats);
            var rightDoor = Attach(root.transform, RightHinge, new Vector3( hw, 0f, hd), Door(-1f, y0, y1, regions), mats);

            if (decor != RackDecor.None)
            {
                // The upper panel, as Door lays it out: between the stiles, from the middle
                // rail to the top rail, its face recessed into the door.
                const float stile = 0.12f, rail = 0.14f, recess = 0.03f;
                float x0 = 0.02f, x1 = Width / 2f - 0.01f, xi = x0 + stile, xo = x1 - stile;
                float ya = y0 + 0.02f, yb = y1 - 0.02f, ym = (ya + yb) / 2f;
                float bottom = ym + rail / 2f, top = yb - rail;
                float cx = (xi + xo) / 2f, cy = (bottom + top) / 2f;
                Decorate(leftDoor.transform,  new Vector3( cx, cy, -recess), xo - xi, top - bottom, -1, decor, wood);
                Decorate(rightDoor.transform, new Vector3(-cx, cy, -recess), xo - xi, top - bottom, +1, decor, wood);
            }
        }

        /// <summary>
        /// One door in hinge-local space, lying in the front <see cref="Board"/> of the
        /// footprint (z from -Board to 0).  Two stiles, three rails, two recessed planked
        /// panels and a handle at the free edge.  <paramref name="side"/> is +1 for a door
        /// extending +X.
        /// </summary>
        private static Mesh Door(float side, float y0, float y1, Region[] regions)
        {
            const float stile = 0.12f, rail = 0.14f, recess = 0.03f;
            float x0 = 0.02f, x1 = Width / 2f - 0.01f;   // a gap at the hinge and at the center seam
            float ya = y0 + 0.02f, yb = y1 - 0.02f, ym = (ya + yb) / 2f;
            float xi = x0 + stile, xo = x1 - stile;      // the opening between the stiles

            var b = new MeshBuilder(regions);
            void Box(int sub, float xa, float xb, float yA, float yB, float za, float zb, bool planked = false)
            {
                if (side < 0) { float t = xa; xa = -xb; xb = -t; }
                if (planked) b.Boards(sub, new Vector3(xa, yA, za), new Vector3(xb, yB, zb), Axis.X);
                else         b.Box(sub, new Vector3(xa, yA, za), new Vector3(xb, yB, zb));
            }
            Box(Body, x0, xi, ya, yb, -Board, 0f);                                      // hinge stile
            Box(Body, xo, x1, ya, yb, -Board, 0f);                                      // free stile
            Box(Body, xi, xo, ya, ya + rail, -Board, 0f);                               // bottom rail
            Box(Body, xi, xo, ym - rail / 2f, ym + rail / 2f, -Board, 0f);              // middle rail
            Box(Body, xi, xo, yb - rail, yb, -Board, 0f);                               // top rail
            Box(Panel, xi, xo, ya + rail, ym - rail / 2f, -Board, -recess, planked: true);   // lower panel
            Box(Panel, xi, xo, ym + rail / 2f, yb - rail, -Board, -recess, planked: true);   // upper panel

            float hx = xo + stile / 2f;                                                 // handle, mid free stile
            float hy = Mathf.Min(ym, 1.1f);                                             // at hand height, never above the middle rail
            Box(Iron, hx - 0.015f, hx + 0.015f, hy - 0.07f, hy + 0.07f, 0.02f, 0.05f);  // the bar
            Box(Iron, hx - 0.015f, hx + 0.015f, hy - 0.07f, hy - 0.05f, 0f, 0.02f);     // its two feet
            Box(Iron, hx - 0.015f, hx + 0.015f, hy + 0.05f, hy + 0.07f, 0f, 0.02f);
            return b.ToMesh();
        }

        private static GameObject Attach(Transform parent, string name, Vector3 localPos, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            // sharedMaterials, not instances: the game's hover and damage highlights go through
            // property blocks, and the ghost makes its own copies.
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        // ── Materials, borrowed from vanilla pieces ───────────────────────────────────

        private static readonly Dictionary<string, Material> _found = new Dictionary<string, Material>();

        private static string Name(Material m) => m != null ? m.name : "(none)";

        /// <summary>
        /// The piece material of the first prefab that exists, optionally one whose name
        /// carries a hint ("iron" on the reinforced chest).  Arguments come in pairs: prefab
        /// name, hint or null.
        ///
        /// Build pieces carry a snow-cover child mesh for winter weather, and on the darkwood
        /// beam and the floor plank it is the first renderer: taken blindly, the cabinet came
        /// out white (2026-11-13).  Covers are skipped and the Custom/Piece shader preferred.
        /// </summary>
        private static Material Find(params string[] candidates)
        {
            string key = string.Join("|", candidates);
            if (_found.TryGetValue(key, out var cached)) return cached;

            Material result = null;
            for (int c = 0; c + 1 < candidates.Length && result == null; c += 2)
            {
                string hint = candidates[c + 1];
                var prefab = PrefabManager.Instance.GetPrefab(candidates[c]);
                if (prefab == null) continue;
                Material fallback = null;
                var seen = new List<string>();
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        string name = m.name.ToLowerInvariant();
                        seen.Add($"{r.name}:{m.name}");
                        if (name.Contains("snow") || name.Contains("moss") || name.Contains("ghost")) continue;
                        if (hint != null && !name.Contains(hint)) continue;
                        if (result == null && m.shader != null && m.shader.name == "Custom/Piece") result = m;
                        else if (fallback == null) fallback = m;
                    }
                Jotunn.Logger.LogInfo($"[Armory] Materials on {candidates[c]} (hint '{hint ?? "-"}'): {string.Join(", ", seen)}.");
                if (result == null) result = fallback;
            }
            _found[key] = result = Still(result);
            return result;
        }

        // Custom/Piece displaces vertices by a noise texture sampled at world position, which
        // gives beams their hand-hewn wobble: DarkWood_mat has _VALUENOISEVERTEX_ON and a
        // _RippleDistance of 0.03, and the bow lands where it lands.  One armoire's upper-left
        // door panel stood 3 cm proud and swallowed half its strap hinge, the other three
        // straps untouched (2026-11-13).  Turning the keyword off was not enough; zeroing
        // the ripple distance was (2026-11-14).  The rack wears a copy of each material with
        // both off and flagged moveable, as TheGreatestShips' pen does; the color noise stays.
        private static Material Still(Material source)
        {
            if (source == null) return null;
            var copy = new Material(source) { name = source.name + "_armory" };
            copy.DisableKeyword("_VALUENOISEVERTEX_ON");
            copy.EnableKeyword("_MOVEABLEOBJECT_ON");
            if (copy.HasProperty("_ValueNoiseVertex")) copy.SetFloat("_ValueNoiseVertex", 0f);
            if (copy.HasProperty("_MoveableObject"))   copy.SetFloat("_MoveableObject", 1f);
            if (copy.HasProperty("_RippleDistance"))   copy.SetFloat("_RippleDistance", 0f);
            return copy;
        }

        // ── Diagnostic: what the borrowed textures look like ──────────────────────────
        //
        // The game's materials are atlases, and planar UVs land on whichever region a face's
        // axes happen to hit (the cabinet came out two-tone, 2026-11-13).  Choosing a region
        // needs the picture, so the first draw writes each material's main texture, and the
        // UV range the source mesh uses on it, next to the config file.  Development aid;
        // remove once the UV mapping is settled.


        // A GPU copy is readable whatever the import settings; EncodeToPNG goes through
        // reflection for the same netstandard reason ArmoryPieces.UnityLoadImage does.
        private static byte[] ReadablePng(Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var t = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            var enc = t?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
            var bytes = (byte[])enc.Invoke(null, new object[] { copy });
            UnityEngine.Object.Destroy(copy);
            return bytes;
        }

        // ── Geometry ──────────────────────────────────────────────────────────────────

        private enum Axis { X, Y, Z }

        /// <summary>
        /// Boxes into one mesh with per-face flat normals, each face's UVs laid into its
        /// submesh's texture region: the face's long side along V (the grain), its short side
        /// across U, sizes in meters at the region's densities, and compressed to the region
        /// where it would otherwise run off it.  Every box gets its own offset along the
        /// grain so a row of boards does not repeat.
        /// </summary>
        private sealed class MeshBuilder
        {
            private readonly Region[]      _regions;
            private readonly List<Vector3> _v  = new List<Vector3>();
            private readonly List<Vector3> _n  = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int>[]   _tris;
            private int _boxes;

            public MeshBuilder(Region[] regions)
            {
                _regions = regions;
                _tris = new List<int>[regions.Length];
                for (int i = 0; i < regions.Length; i++) _tris[i] = new List<int>();
            }

            /// <summary>A slab split into boards of at most <see cref="BoardWidth"/> along one axis.</summary>
            public void Boards(int sub, Vector3 min, Vector3 max, Axis across)
            {
                int a = (int)across;
                float len = max[a] - min[a];
                int n = Mathf.Max(1, Mathf.CeilToInt(len / BoardWidth - 0.001f));
                float w = len / n;
                for (int i = 0; i < n; i++)
                {
                    var bmin = min; var bmax = max;
                    bmin[a] = min[a] + i * w;
                    bmax[a] = min[a] + (i + 1) * w;
                    Box(sub, bmin, bmax);
                }
            }

            public void Box(int sub, Vector3 min, Vector3 max)
            {
                // A different place along the grain for every box, so neighbors differ.
                float shift = Mathf.Repeat(_boxes++ * 0.618034f, 1f);
                float x0 = min.x, y0 = min.y, z0 = min.z;
                float x1 = max.x, y1 = max.y, z1 = max.z;
                Face(sub, new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right,   shift);
                Face(sub, new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.left,    shift);
                Face(sub, new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up,      shift);
                Face(sub, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), Vector3.down,    shift);
                Face(sub, new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.forward, shift);
                Face(sub, new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back,    shift);
            }

            /// <summary>
            /// A flat convex polygon already wound for Unity (clockwise seen from outside), as
            /// ObjModel delivers them; its normal comes from the winding.
            /// </summary>
            public void Polygon(int sub, Vector3[] pts, float shift)
            {
                if (pts.Length < 3) return;
                var n = Vector3.zero;   // Newell's method: robust for any flat polygon
                for (int i = 0; i < pts.Length; i++)
                {
                    var p = pts[i]; var q = pts[(i + 1) % pts.Length];
                    n.x += (p.y - q.y) * (p.z + q.z);
                    n.y += (p.z - q.z) * (p.x + q.x);
                    n.z += (p.x - q.x) * (p.y + q.y);
                }
                if (n.sqrMagnitude < 1e-14f) return;
                n = n.normalized;       // Newell's normal of this winding is the side Unity draws
                int first = _v.Count;
                foreach (var p in pts) { _v.Add(p); _n.Add(n); }
                MapFace(_regions[sub], n, shift, pts);
                var t3 = _tris[sub];
                for (int i = 1; i + 1 < pts.Length; i++) { t3.Add(first); t3.Add(first + i); t3.Add(first + i + 1); }
            }

            // One quad, wound to face `normal` (Unity draws the side where the vertices run
            // clockwise).
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float shift) => Face(sub, a, b, c, d, normal, shift);

            private void Face(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float shift)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) { var t = b; b = d; d = t; }
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) _n.Add(normal);
                MapFace(_regions[sub], normal, shift, a, b, c, d);
                var t3 = _tris[sub];
                t3.Add(i); t3.Add(i + 1); t3.Add(i + 2);
                t3.Add(i); t3.Add(i + 2); t3.Add(i + 3);
            }

            // The face's two in-plane axes (the two the normal leans away from); the longer one
            // carries the grain (V).  On a square face the vertical wins, then X, so fronts read
            // as upright boards and tops as boards running the width.
            private void MapFace(Region r, Vector3 normal, float shift, params Vector3[] p)
            {
                float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
                int ax1, ax2;   // candidate axes, 0 = x, 1 = y, 2 = z
                if (ax >= ay && ax >= az)      { ax1 = 1; ax2 = 2; }
                else if (ay >= ax && ay >= az) { ax1 = 0; ax2 = 2; }
                else                           { ax1 = 1; ax2 = 0; }
                float len1 = Extent(p, ax1), len2 = Extent(p, ax2);
                int along = len2 > len1 * 1.001f ? ax2 : ax1;
                int across = along == ax1 ? ax2 : ax1;

                float alongLen = Extent(p, along), acrossLen = Extent(p, across);
                float alongMid = Mid(p, along),    acrossMid = Mid(p, across);
                // With GrainU the roles swap: "u" below is the across axis, written into V.
                if (r.GrainU) r = new Region { U0 = r.V0, U1 = r.V1, V0 = r.U0, V1 = r.U1, UPerMeter = r.VPerMeter, VPerMeter = r.UPerMeter, WrapU = r.WrapV, WrapV = r.WrapU, GrainU = true };
                float uW = r.U1 - r.U0, vW = r.V1 - r.V0;
                float du = r.WrapU || acrossLen * r.UPerMeter <= uW ? r.UPerMeter : uW / acrossLen;
                float dv = r.WrapV || alongLen  * r.VPerMeter <= vW ? r.VPerMeter : vW / alongLen;
                float uMid = (r.U0 + r.U1) / 2f, vMid = (r.V0 + r.V1) / 2f;

                foreach (var q in p)
                {
                    float u = r.WrapU ? r.U0 + q[across] * du : uMid + (q[across] - acrossMid) * du;
                    float v = r.WrapV ? r.V0 + q[along] * dv + shift : vMid + (q[along] - alongMid) * dv;
                    _uv.Add(r.GrainU ? new Vector2(v, u) : new Vector2(u, v));
                }
            }

            private static float Extent(Vector3[] p, int axis)
            {
                float min = p[0][axis], max = p[0][axis];
                foreach (var q in p) { min = Mathf.Min(min, q[axis]); max = Mathf.Max(max, q[axis]); }
                return max - min;
            }

            private static float Mid(Vector3[] p, int axis)
            {
                float min = p[0][axis], max = p[0][axis];
                foreach (var q in p) { min = Mathf.Min(min, q[axis]); max = Mathf.Max(max, q[axis]); }
                return (min + max) / 2f;
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh { name = "ArmoryCabinet" };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.subMeshCount = _tris.Length;
                for (int i = 0; i < _tris.Length; i++) mesh.SetTriangles(_tris[i], i);
                mesh.RecalculateTangents();   // the piece shader normal-maps
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
