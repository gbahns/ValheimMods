using System;
using System.Reflection;
using UnityEngine;

namespace HungryViking
{
    // Hooks into Valheim's own status effect HUD (same row as Rested, Shelter, etc.).
    public class HungerStatusEffect : StatusEffect
    {
        public static HungerStatusEffect Create()
        {
            var se    = ScriptableObject.CreateInstance<HungerStatusEffect>();
            se.m_name = "Hungry";
            se.m_icon = LoadIcon();
            se.m_ttl  = 0f; // never expires on its own; we manage lifetime manually
            return se;
        }

        private static Sprite LoadIcon()
        {
            var asm  = Assembly.GetExecutingAssembly();
            var name = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("status_icon.png"));
            if (name != null)
            {
                using (var stream = asm.GetManifestResourceStream(name))
                {
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);

                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (LoadImage(tex, bytes))
                        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
                }
            }

            HungryVikingMod.Log.LogWarning("HungryViking: icon.png resource not found, using fallback icon.");
            return BuildFallbackIcon();
        }

        // ImageConversionModule can't be referenced directly (netstandard version conflict),
        // so we resolve LoadImage via reflection at runtime — Unity loads that DLL itself.
        private static bool LoadImage(Texture2D tex, byte[] bytes)
        {
            try
            {
                var t = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var m = t?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                return m != null && (bool)m.Invoke(null, new object[] { tex, bytes });
            }
            catch { return false; }
        }

        // Amber oval with a wavy crease line — fallback if the embedded icon fails to load.
        private static Sprite BuildFallbackIcon()
        {
            const int size = 64;
            var   tex   = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float h     = size * 0.5f;
            var   amber = new Color(1f,    0.65f, 0.1f, 1f);
            var   dark  = new Color(0.35f, 0.15f, 0f,   0.9f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx      = (x + 0.5f - h) / (h * 0.88f);
                    float ny      = (y + 0.5f - h) / (h * 0.80f);
                    float ellipse = nx * nx + ny * ny;

                    if (ellipse >= 1f) { tex.SetPixel(x, y, Color.clear); continue; }

                    float wave     = Mathf.Sin(nx * Mathf.PI * 2.5f) * 0.12f - 0.15f;
                    float lineDist = Mathf.Abs(ny - wave);
                    tex.SetPixel(x, y, lineDist < 0.10f ? dark : amber);
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
