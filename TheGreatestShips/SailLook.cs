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
    /// to see the whole of a Big Busse.  The sail is a cutout material (Custom/Vegetation ignores
    /// alpha), so to make one translucent its material is moved to the game's Standard TwoSided
    /// shader in fade mode, keeping the cloth texture and the ship's tint; materials are changed
    /// in place, so ships already afloat change with them, and restored from a saved copy when
    /// opacity goes back to 1.  Vanilla ships' sails are included: the point is to see past the
    /// sail, whichever ship it is on.
    /// </summary>
    internal static class SailLook
    {
        private sealed class Saved
        {
            public Shader Shader;
            public Color  Color;
            public int    RenderQueue;
            public string[] Keywords;
        }

        private static readonly Dictionary<Material, Saved> _saved = new Dictionary<Material, Saved>();
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
            float wanted = ShipConfig.BoatCameraDistance.Value;
            camera.m_maxDistanceBoat = wanted > 0f ? Mathf.Clamp(wanted, 2f, 60f) : _vanillaBoatDistance;
        }

        // ── Sails ───────────────────────────────────────────────────────────────────

        internal static void ApplySails()
        {
            float opacity = Mathf.Clamp01(ShipConfig.SailOpacity.Value);
            var materials = new HashSet<Material>();
            foreach (var prefab in ShipPrefabs.AllShipPrefabs())
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && material.name.StartsWith("sail", System.StringComparison.OrdinalIgnoreCase))
                            materials.Add(material);

            if (opacity >= 0.999f)
            {
                int restored = 0;
                foreach (var material in materials)
                    if (Restore(material)) restored++;
                if (restored > 0) Jotunn.Logger.LogInfo($"[TheGreatestShips] Sails opaque again ({restored} material(s) restored).");
                return;
            }

            var shader = Shader.Find("Standard TwoSided") ?? Shader.Find("Standard");
            if (shader == null)
            {
                Jotunn.Logger.LogWarning("[TheGreatestShips] No Standard shader found; Sail Opacity can't be applied.");
                return;
            }

            foreach (var material in materials)
            {
                if (!_saved.ContainsKey(material))
                    _saved[material] = new Saved { Shader = material.shader, Color = material.color, RenderQueue = material.renderQueue, Keywords = material.shaderKeywords };

                var color = _saved[material].Color;
                if (material.shader != shader) material.shader = shader;
                material.SetFloat("_Mode", 2f);                                  // Fade
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.color = new Color(color.r, color.g, color.b, opacity);
            }
            Jotunn.Logger.LogInfo($"[TheGreatestShips] Sails at {opacity:0.00} opacity ({materials.Count} material(s), {shader.name}).");
        }

        private static bool Restore(Material material)
        {
            if (!_saved.TryGetValue(material, out var saved)) return false;
            material.shader         = saved.Shader;
            material.shaderKeywords = saved.Keywords;
            material.renderQueue    = saved.RenderQueue;
            material.color          = saved.Color;
            _saved.Remove(material);
            return true;
        }
    }
}
