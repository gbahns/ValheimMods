using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// An icon in vanilla's status effect row while the pocket map is out, beside Rested and Wet,
    /// so a glance tells you whether what you find is being written down. It is an ordinary status
    /// effect added straight to the local player: vanilla draws it, nothing about it is saved, and
    /// nothing is sent to anyone, so it needs no registration with the game.
    /// </summary>
    internal static class MapOutStatus
    {
        private static StatusEffect _effect;
        private static int _hash;

        internal static void Update()
        {
            var player = Player.m_localPlayer;
            var seman = player != null ? player.GetSEMan() : null;
            if (seman == null) return;
            bool want = PocketMap.IsOut && TgmConfig.MapOutStatusIcon != null && TgmConfig.MapOutStatusIcon.Value;
            var effect = Effect();
            bool have = seman.HaveStatusEffect(_hash);
            if (want && !have) seman.AddStatusEffect(effect);
            else if (!want && have) seman.RemoveStatusEffect(_hash, true);
        }

        private static StatusEffect Effect()
        {
            if (_effect != null) return _effect;
            var se = ScriptableObject.CreateInstance<StatusEffect>();
            se.name = "TGM_MapOutStatus"; // the game identifies a status effect by its asset name
            se.m_name = "Map out";
            se.m_tooltip = "Your map is out: what you have found nearby is being written down.";
            se.m_icon = Icon();
            se.m_ttl = 0f; // stays until the map is put away
            se.hideFlags = HideFlags.HideAndDontSave;
            _effect = se;
            _hash = se.NameHash();
            return se;
        }

        // ── the drawn icon ──────────────────────────────────────────────────────────

        private static readonly Color Light = new Color(0.95f, 0.87f, 0.66f, 1f);
        private static readonly Color Shade = new Color(0.79f, 0.68f, 0.47f, 1f);
        private static readonly Color Edge = new Color(0.30f, 0.20f, 0.10f, 1f);
        private static readonly Color Crease = new Color(0.45f, 0.33f, 0.18f, 1f);
        private static readonly Color Trail = new Color(0.42f, 0.28f, 0.14f, 1f);
        private static readonly Color Mark = new Color(0.80f, 0.14f, 0.10f, 1f);

        private const float Left = 6f, Right = 58f, Bottom = 10f, Top = 52f, Rise = 5f;

        /// <summary>
        /// A map folded in three, its panels zigzagging and shaded alternately, with a dashed trail
        /// that ends at a red X. Drawn once at runtime, as the marker button's pin is.
        /// </summary>
        private static Sprite Icon()
        {
            const int n = 64, ss = 4;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            var c = Sample(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss);
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    px[y * n + x] = a <= 0f
                        ? new Color32(0, 0, 0, 0)
                        : (Color32)new Color(r / a, g / a, b / a, a / (ss * ss));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Color Sample(float x, float y)
        {
            if (x < Left || x > Right) return Color.clear;
            float w = (Right - Left) / 3f;
            int panel = Mathf.Min(2, (int)((x - Left) / w));
            float t = (x - Left - panel * w) / w;
            bool rising = panel != 1;
            // Everything below is drawn on the unfolded sheet, then lifted with its panel.
            float lift = Rise * (rising ? t : 1f - t);
            float v = y - lift;
            if (v < Bottom || v > Top) return Color.clear;

            const float border = 1.8f;
            if (v - Bottom < border || Top - v < border || x - Left < border || Right - x < border) return Edge;
            float crease = Mathf.Min(Mathf.Abs(x - (Left + w)), Mathf.Abs(x - (Left + 2f * w)));
            if (crease < 0.7f) return Crease;

            // The X where the trail ends.
            float dx = x - 46f, dy = v - 37f;
            if (Mathf.Abs(dx) <= 5.5f && Mathf.Abs(dy) <= 5.5f && (Mathf.Abs(dx - dy) < 1.9f || Mathf.Abs(dx + dy) < 1.9f))
                return Mark;

            // A dashed trail winding up toward it.
            if (x >= 11f && x <= 40f)
            {
                float u = x - 11f;
                float center = 19f + u * 0.45f + 4f * Mathf.Sin(u * 0.3f);
                float slope = 0.45f + 1.2f * Mathf.Cos(u * 0.3f);
                // Measured across the line rather than straight up, so the dashes keep their
                // weight where the trail climbs.
                if (Mathf.Abs(v - center) / Mathf.Sqrt(1f + slope * slope) < 1.1f && u % 5.5f < 3.5f) return Trail;
            }
            return rising ? Light : Shade;
        }
    }
}
