using UnityEngine;
using UnityEngine.UI;

namespace HungryViking
{
    // Center-outward vignette triggered when the player is standing in smoke.
    // Unlike VignetteOverlay (edges-in), this renders from the screen center outward.
    // No status icon — Valheim already shows one for the Smoked SE.
    public class SmokedOverlay : MonoBehaviour
    {
        private RawImage _image;
        private Text     _label;
        private float    _urgency;

        private static readonly Color LabelRedColor = new Color(1f, 0f, 0f, 1f);
        private Color _labelBaseColor = new Color(0.7f, 0.7f, 0.7f, 1f);

        private float _baseAlpha;
        private float _smoothedBase;
        private Color _baseColor;
        private float _labelPhase;
        private float _outerBoundary = 0.55f;

        private void Awake()
        {
            var canvasGo = new GameObject("HungryViking_SmokedVignette");
            DontDestroyOnLoad(canvasGo);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 99;

            var imgGo = new GameObject("Image");
            imgGo.transform.SetParent(canvasGo.transform, false);

            _image               = imgGo.AddComponent<RawImage>();
            _image.texture       = BuildVignetteTexture(64, _outerBoundary);
            _image.raycastTarget = false;
            _image.color         = Color.clear;

            var rt       = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var labelGo = new GameObject("SmokedLabel");
            labelGo.transform.SetParent(canvasGo.transform, false);

            _label               = labelGo.AddComponent<Text>();
            _label.text          = "";
            _label.font          = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _label.fontSize      = 22;
            _label.fontStyle     = FontStyle.Bold;
            _label.alignment     = TextAnchor.UpperCenter;
            _label.color         = _labelBaseColor;
            _label.raycastTarget = false;
            _label.gameObject.SetActive(false);

            var labelRt       = (RectTransform)labelGo.transform;
            labelRt.anchorMin = new Vector2(0f, 1f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.pivot     = new Vector2(0.5f, 1f);
            labelRt.offsetMin = new Vector2(0f, -100f);
            labelRt.offsetMax = new Vector2(0f, -70f);
        }

        public void SetLabelBaseColor(Color color) => _labelBaseColor = color;

        public void SetBase(float alpha, Color color)
        {
            _baseAlpha = alpha;
            _baseColor = color;
        }

        private void Update()
        {
            float rate   = _baseAlpha > _smoothedBase ? Time.deltaTime : Time.deltaTime * 0.5f;
            _smoothedBase = Mathf.MoveTowards(_smoothedBase, _baseAlpha, rate);

            float osc = 0f;
            if (_urgency > 0f)
            {
                float freq  = Mathf.Lerp(0.2f, 1.0f, _urgency);
                _labelPhase = (_labelPhase + freq * Mathf.PI * 2f * Time.deltaTime) % (Mathf.PI * 2f);
                osc         = (Mathf.Sin(_labelPhase) + 1f) * 0.5f;
            }

            float floor = 1f - _urgency * 0.5f;
            float alpha = _smoothedBase * Mathf.Lerp(floor, 1f, osc);

            _image.color = alpha < 0.005f
                ? Color.clear
                : new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);

            if (_label.gameObject.activeSelf)
                _label.color = Color.Lerp(_labelBaseColor, LabelRedColor, osc * _urgency);
        }

        public void SetOuterBoundary(float value)
        {
            if (Mathf.Approximately(_outerBoundary, value)) return;
            _outerBoundary = value;
            var old = _image.texture;
            _image.texture = BuildVignetteTexture(64, _outerBoundary);
            Destroy(old);
        }

        public void SetLabel(string text, float urgency = 0f)
        {
            _urgency = urgency;
            if (string.IsNullOrEmpty(text))
                _label.gameObject.SetActive(false);
            else
            {
                _label.text = text;
                _label.gameObject.SetActive(true);
            }
        }

        // Two-phase vignette controlled by extent (0–1):
        //   extent 1.0→0.5 : full-screen coverage, center floor fades 25%→0%
        //   extent 0.5→0.0 : center floor is 0%, transparent dead zone grows outward from center
        // The gradient always reaches 100% at the screen edge.
        private static Texture2D BuildVignetteTexture(int size, float extent)
        {
            var   tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float h   = size * 0.5f;

            float innerFloor, innerDeadZone;
            if (extent >= 0.5f)
            {
                innerFloor    = (extent - 0.5f) * 0.5f; // 0.25 at extent=1, 0 at extent=0.5
                innerDeadZone = 0f;
            }
            else
            {
                innerFloor    = 0f;
                innerDeadZone = 1f - 2f * extent; // 0 at extent=0.5, 1 at extent=0
            }

            float range = Mathf.Max(1f - innerDeadZone, 0.001f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx    = (x + 0.5f - h) / h;
                    float cy    = (y + 0.5f - h) / h;
                    float dist  = Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cy));
                    float alpha;
                    if (dist < innerDeadZone)
                    {
                        alpha = 0f;
                    }
                    else
                    {
                        float t     = Mathf.Clamp01((dist - innerDeadZone) / range);
                        float curve = t * t * (3f - 2f * t);
                        alpha       = Mathf.Lerp(innerFloor, 1f, curve);
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return tex;
        }
    }
}
