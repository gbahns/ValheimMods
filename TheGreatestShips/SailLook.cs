using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheGreatestShips
{
    /// <summary>
    /// Two client-side looks: how far the camera may pull back while steering, and how see-through
    /// the sails are.  Both are per player and apply live.
    ///
    /// The camera's boat limit is one number for every ship, so a hull half again a longship's
    /// size fills that much more of the frame at full zoom-out; raising the limit is the only way
    /// to see the whole of a Big Busse.
    ///
    /// The sail's own material is a cutout (Custom/Vegetation ignores alpha) and draws both faces
    /// of the cloth.  To see through it, the renderer gets a second material -- Unity draws the
    /// mesh once more per extra material -- made from the sail's, on the game's particle surface
    /// shader in fade mode (Standard TwoSided has no cull control and culls the back face away).
    /// By default the original, culled to one face, keeps the side seen from the bow looking
    /// normal, and the fade copy, culled to the other, makes the side seen from the tiller
    /// see-through; "Sail Opacity Both Sides" uses the fade copy alone, both faces.  Which face
    /// is which is measured from the renderer's orientation against the ship's bow, so the Karve
    /// and the Longship need no special cases.  Only the ship the local player is standing on is
    /// changed (SailWatch follows them aboard and off), so other ships look as the game draws
    /// them; everything is put back on leaving, or when opacity returns to 1.  Vanilla ships are
    /// included: the point is to see past the sail, whichever ship you are on.
    /// </summary>
    /// <summary>Keeps the faded sails on the ship the local player is standing on, and only there.</summary>
    internal sealed class SailWatch : MonoBehaviour
    {
        private float _next;
        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.25f;
            SailLook.Update();
        }
    }

    internal static class SailLook
    {
        private static float _vanillaBoatDistance = -1f;

        // ── Camera ──────────────────────────────────────────────────────────────────

        internal static void ApplyCamera(GameCamera camera)
        {
            if (camera == null) return;
            if (_vanillaBoatDistance < 0f)
            {
                _vanillaBoatDistance = camera.m_maxDistanceBoat;
                Jotunn.Logger.LogInfo($"[TheGreatestShips] The game's boat camera limit is {_vanillaBoatDistance} m (on foot {camera.m_maxDistance} m).");
            }
            camera.m_maxDistanceBoat = Mathf.Clamp(ShipConfig.BoatCameraDistance.Value, 8, 40);
        }

        // ── Sails ───────────────────────────────────────────────────────────────────

        private const string FadeShaderName = "Particles/Standard Surface2";

        // Every renderer we have changed, with the materials it had.
        private static readonly Dictionary<Renderer, Material[]> _original = new Dictionary<Renderer, Material[]>();
        // Copies made from each sail material: (source, cull) -> cutout copy, and -> fade copy.
        private static readonly Dictionary<(Material, CullMode), Material> _cutoutCopies = new Dictionary<(Material, CullMode), Material>();
        private static readonly Dictionary<(Material, CullMode), Material> _fadeCopies   = new Dictionary<(Material, CullMode), Material>();

        private static Ship _current;   // the ship whose sails are faded: the one the local player stands on

        /// <summary>Called a few times a second (SailWatch) and on every setting change.</summary>
        internal static void Update()
        {
            var player = Player.m_localPlayer;
            var ship = player != null ? player.GetStandingOnShip() : null;
            float opacity = Mathf.Clamp01(ShipConfig.SailOpacity.Value);
            if (opacity >= 0.999f) ship = null;

            if (ship != _current)
            {
                RestoreAll();
                _current = ship;
            }
            if (_current != null) ApplySails(_current, opacity, ShipConfig.SailOpacityBothSides.Value);
        }

        internal static void ApplySails() => Update();

        private static void RestoreAll()
        {
            int restored = 0;
            foreach (var pair in _original)
                if (pair.Key != null) { pair.Key.sharedMaterials = pair.Value; restored++; }
            _original.Clear();
            if (restored > 0) Jotunn.Logger.LogInfo($"[TheGreatestShips] Sails opaque again ({restored} renderer(s) restored).");
        }

        private static void ApplySails(Ship ship, float opacity, bool bothSides)
        {
            var fadeShader = FindLoadedShader(FadeShaderName);
            if (fadeShader == null)
            {
                Jotunn.Logger.LogWarning($"[TheGreatestShips] Shader '{FadeShaderName}' not found; Sail Opacity can't be applied.");
                return;
            }

            int changed = 0;
            foreach (var renderer in Sails(ship.gameObject))
            {
                if (!_original.ContainsKey(renderer))
                    _original[renderer] = renderer.sharedMaterials;
                var originals = _original[renderer];

                // Which face looks aft, toward the tiller?  Judged from the renderer's local X
                // against the ship's bow -- the sign was settled by looking: with X toward the
                // bow, the face seen from the tiller is Unity's front face (drawn by Cull Back).
                bool xToBow = Vector3.Dot(renderer.transform.right, ship.transform.forward) >= 0f;
                CullMode aftFace  = xToBow ? CullMode.Back  : CullMode.Front;
                CullMode foreFace = xToBow ? CullMode.Front : CullMode.Back;

                var materials = new List<Material>();
                foreach (var source in originals)
                {
                    if (source == null || !IsSail(source)) { materials.Add(source); continue; }
                    if (bothSides)
                    {
                        materials.Add(FadeCopy(source, fadeShader, CullMode.Off, opacity));
                    }
                    else
                    {
                        materials.Add(CutoutCopy(source, foreFace));
                        materials.Add(FadeCopy(source, fadeShader, aftFace, opacity));
                    }
                }
                var current = renderer.sharedMaterials;
                bool same = current.Length == materials.Count;
                for (int i = 0; same && i < current.Length; i++) same = current[i] == materials[i];
                if (same) continue;   // already as wanted (the watcher runs often)
                renderer.sharedMaterials = materials.ToArray();
                changed++;
            }
            if (changed > 0)
                Jotunn.Logger.LogInfo($"[TheGreatestShips] Sails of the ship you're on at {opacity:0.00} opacity, {(bothSides ? "both sides" : "the side seen from the tiller")} ({changed} renderer(s)).");
        }

        private static bool IsSail(Material m) => m.name.StartsWith("sail", System.StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<Renderer> Sails(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = _original.TryGetValue(renderer, out var saved) ? saved : renderer.sharedMaterials;
                foreach (var m in mats)
                    if (m != null && IsSail(m)) { yield return renderer; break; }
            }
        }

        private static Material CutoutCopy(Material source, CullMode cull)
        {
            if (_cutoutCopies.TryGetValue((source, cull), out var copy) && copy != null) return copy;
            copy = new Material(source) { name = source.name + "_oneface" };
            if (copy.HasProperty("_Cull")) copy.SetInt("_Cull", (int)cull);
            _cutoutCopies[(source, cull)] = copy;
            return copy;
        }

        private static Material FadeCopy(Material source, Shader fadeShader, CullMode cull, float opacity)
        {
            if (!_fadeCopies.TryGetValue((source, cull), out var copy) || copy == null)
            {
                copy = new Material(source) { name = source.name + "_fade", shader = fadeShader };   // keeps _MainTex, _BumpMap, _Color, _Cutoff
                copy.SetFloat("_Mode", 2f);                                  // Fade
                copy.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                copy.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                copy.SetInt("_ZWrite", 0);
                copy.SetInt("_Cull", (int)cull);
                copy.DisableKeyword("_ALPHATEST_ON");
                copy.EnableKeyword("_ALPHABLEND_ON");
                copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                copy.DisableKeyword("_ALPHAMODULATE_ON");
                copy.renderQueue = (int)RenderQueue.Transparent;
                _fadeCopies[(source, cull)] = copy;
            }
            var c = source.color;
            copy.color = new Color(c.r, c.g, c.b, opacity);
            return copy;
        }

        // Shader.Find only sees shaders Unity marks "always included"; the game's own come from
        // its bundles, so look through what is loaded instead.
        private static Shader FindLoadedShader(string name)
        {
            foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
                if (shader != null && shader.name == name) return shader;
            return null;
        }
    }
}
