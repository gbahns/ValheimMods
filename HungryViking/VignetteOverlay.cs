using UnityEngine;
using UnityEngine.UI;

namespace HungryViking
{
    // Screen-edge vignette driven by food-expiry tier.
    // The vignette texture is generated at runtime — no bundled assets required.
    public class VignetteOverlay : MonoBehaviour
    {
        private RawImage _image;
        private Text     _hungerLabel;
        private float    _hungerUrgency;

        private static readonly Color LabelBaseColor = new Color(1f, 0.35f, 0.1f, 1f);
        private static readonly Color LabelRedColor  = new Color(1f, 0f,    0f,   1f);

        private float _baseAlpha;
        private Color _baseColor;
        private float _labelPhase;
        private float _innerBoundary = 0.82f;

        private void Awake()
        {
            var canvasGo = new GameObject("HungryViking_Vignette");
            DontDestroyOnLoad(canvasGo);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var imgGo = new GameObject("Image");
            imgGo.transform.SetParent(canvasGo.transform, false);

            _image               = imgGo.AddComponent<RawImage>();
            _image.texture       = BuildVignetteTexture(64, _innerBoundary);
            _image.raycastTarget = false;
            _image.color         = Color.clear;

            var rt          = (RectTransform)imgGo.transform;
            rt.anchorMin    = Vector2.zero;
            rt.anchorMax    = Vector2.one;
            rt.offsetMin    = Vector2.zero;
            rt.offsetMax    = Vector2.zero;

            // "You are hungry" label — top-center, visible whenever any food slot is empty.
            var labelGo = new GameObject("HungerLabel");
            labelGo.transform.SetParent(canvasGo.transform, false);

            _hungerLabel               = labelGo.AddComponent<Text>();
            _hungerLabel.text          = "";
            _hungerLabel.font          = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _hungerLabel.fontSize      = 22;
            _hungerLabel.fontStyle     = FontStyle.Bold;
            _hungerLabel.alignment     = TextAnchor.UpperCenter;
            _hungerLabel.color         = new Color(1f, 0.35f, 0.1f, 1f);
            _hungerLabel.raycastTarget = false;
            _hungerLabel.gameObject.SetActive(false);

            var labelRt         = (RectTransform)labelGo.transform;
            labelRt.anchorMin   = new Vector2(0f, 1f);
            labelRt.anchorMax   = new Vector2(1f, 1f);
            labelRt.pivot       = new Vector2(0.5f, 1f);
            labelRt.offsetMin   = new Vector2(0f, -70f);
            labelRt.offsetMax   = new Vector2(0f, -40f);
        }

        // Sustained base effect — call every frame with 0 alpha to clear it.
        public void SetBase(float alpha, Color color)
        {
            _baseAlpha = alpha;
            _baseColor = color;
        }

        private void Update()
        {
            // Advance shared phase for both vignette and label oscillation.
            // Frequency ramps 0.2→1 Hz with urgency; phase-accumulator keeps it smooth.
            float osc = 0f;
            if (_hungerUrgency > 0f)
            {
                float freq  = Mathf.Lerp(0.2f, 1.0f, _hungerUrgency);
                _labelPhase = (_labelPhase + freq * Mathf.PI * 2f * Time.deltaTime) % (Mathf.PI * 2f);
                osc         = (Mathf.Sin(_labelPhase) + 1f) * 0.5f; // 0→1
            }

            // Oscillate: at full urgency the vignette dips to 50% of its base alpha.
            float floor = 1f - _hungerUrgency * 0.5f;
            float alpha = _baseAlpha * Mathf.Lerp(floor, 1f, osc);

            _image.color = alpha < 0.005f
                ? Color.clear
                : new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);

            if (_hungerLabel.gameObject.activeSelf)
                _hungerLabel.color = Color.Lerp(LabelBaseColor, LabelRedColor, osc * _hungerUrgency);
        }

        public void SetInnerBoundary(float value)
        {
            if (Mathf.Approximately(_innerBoundary, value)) return;
            _innerBoundary = value;
            var old = _image.texture;
            _image.texture = BuildVignetteTexture(64, _innerBoundary);
            Destroy(old);
        }

        // Pass null or empty string to hide the label.
        // urgency (0–1) controls how far the color oscillation swings toward red.
        public void SetHungerLabel(string text, float urgency = 0f)
        {
            _hungerUrgency = urgency;
            if (string.IsNullOrEmpty(text))
            {
                _hungerLabel.gameObject.SetActive(false);
            }
            else
            {
                _hungerLabel.text = text;
                _hungerLabel.gameObject.SetActive(true);
            }
        }

        // Edge-hugging vignette: transparent center → opaque screen edges.
        // Chebyshev distance (max of |x|, |y|) treats all four edges uniformly
        // instead of biasing toward corners like a radial gradient does.
        // SmoothStep(0.6, 1.0) keeps the inner 60% of the screen fully clear.
        // Uses white pixels so _image.color controls the tint.
        private static Texture2D BuildVignetteTexture(int size, float innerBoundary)
        {
            var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float h  = size * 0.5f;
            float range = Mathf.Max(1.0f - innerBoundary, 0.001f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx    = (x + 0.5f - h) / h;
                    float cy    = (y + 0.5f - h) / h;
                    float dist  = Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cy));
                    float t     = Mathf.Clamp01((dist - innerBoundary) / range);
                    float alpha = t * t * (3f - 2f * t);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return tex;
        }
    }
}
