using UnityEngine;
using UnityEngine.UI;

namespace Armory
{
    /// <summary>
    /// A picture of a rack for the style picker: the chosen model and wood built far below the
    /// world, lit by its own lamp and photographed by a camera of its own into a texture the
    /// picker shows.  Rendered once per change, not per frame.  The rack in the world redraws
    /// too, but with the panel open its doors stand open and the inventory covers half the
    /// screen, so the picture is what the player actually compares.
    /// </summary>
    internal static class ArmoryPreview
    {
        private const int   Width = 256, Height = 320;   // when the image has no size yet
        private const float Depth = -2000f;   // under every seabed; nothing of the world is drawn there

        private static GameObject    _rig;
        private static GameObject    _subject;
        private static Camera        _camera;
        private static RenderTexture _texture;

        /// <summary>Builds (or rebuilds) the picture and hands it to the image.</summary>
        internal static void Show(RawImage target, RackModel model, RackDecor decor, RackWood wood)
        {
            if (target == null) return;
            if (_rig == null) MakeRig();
            FitTexture(target);
            if (_subject != null) Object.DestroyImmediate(_subject);

            _subject = new GameObject("ArmoryPreviewRack");
            _subject.transform.SetParent(_rig.transform, false);
            RackVisual.Build(_subject, model, decor, wood);

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try { _camera.Render(); }
            finally { RenderSettings.fog = fog; }
            target.texture = _texture;
        }

        internal static void Hide()
        {
            if (_rig != null) Object.Destroy(_rig);
            if (_texture != null) { _texture.Release(); Object.Destroy(_texture); }
            _rig = null; _subject = null; _camera = null; _texture = null;
        }

        // A texture of the image's own size in screen pixels, so the picture is as sharp as
        // the space it gets; remade when that changes.
        private static void FitTexture(RawImage target)
        {
            var rect = target.rectTransform.rect;
            var canvas = target.canvas;
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            int w = rect.width  > 8f ? Mathf.Clamp(Mathf.RoundToInt(rect.width  * scale), 64, 2048) : Width;
            int h = rect.height > 8f ? Mathf.Clamp(Mathf.RoundToInt(rect.height * scale), 64, 2048) : Height;
            if (_texture != null && _texture.width == w && _texture.height == h) return;
            if (_texture != null) { _texture.Release(); Object.Destroy(_texture); }
            _texture = new RenderTexture(w, h, 24) { name = "ArmoryPreview", antiAliasing = 2 };
            _camera.targetTexture = _texture;
        }

        private static void MakeRig()
        {
            var player = Player.m_localPlayer;
            var at = player != null ? player.transform.position : Vector3.zero;
            _rig = new GameObject("ArmoryPreviewRig");
            _rig.transform.position = new Vector3(at.x, Depth, at.z);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(_rig.transform, false);
            // Three-quarter view from the front left, a little above center, framing 3 m of rack.
            camGo.transform.localPosition = new Vector3(-2.6f, 2.2f, 5.2f);
            camGo.transform.LookAt(_rig.transform.position + new Vector3(0f, 1.45f, 0f));
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled         = false;            // rendered by hand, in Show; FitTexture gives it its target
            _camera.clearFlags      = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.08f, 0.065f, 0.05f, 1f);
            _camera.fieldOfView     = 34f;
            _camera.nearClipPlane   = 0.2f;
            _camera.farClipPlane    = 30f;
            _camera.cullingMask     = ~(1 << 5);        // everything but UI
            _camera.allowHDR        = false;
            _camera.allowMSAA       = true;

            // Daylight does not reach this deep and the sun sets anyway: a lamp by the camera,
            // and one above and behind for the crown and the sides.
            Lamp(new Vector3(-2.0f, 2.6f, 4.0f), 1.6f, 12f);
            Lamp(new Vector3( 2.5f, 3.5f, 1.5f), 0.7f, 10f);
        }

        private static void Lamp(Vector3 local, float intensity, float range)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(_rig.transform, false);
            go.transform.localPosition = local;
            var light = go.AddComponent<Light>();
            light.type      = LightType.Point;
            light.intensity = intensity;
            light.range     = range;
            light.color     = new Color(1f, 0.93f, 0.82f);
            light.shadows   = LightShadows.None;
        }
    }
}
