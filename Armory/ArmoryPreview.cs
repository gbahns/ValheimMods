using System;
using Object = UnityEngine.Object;
using System.IO;
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
    ///
    /// The same rig photographs the player's default rack for the hammer menu's icon (#86),
    /// on a transparent ground, so the entry looks like what gets built.
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
        internal static void Show(RawImage target, RackStyle style)
        {
            if (target == null) return;
            if (_rig == null) MakeRig();
            FitTexture(target);
            if (_subject != null) Object.DestroyImmediate(_subject);

            _subject = new GameObject("ArmoryPreviewRack");
            _subject.transform.SetParent(_rig.transform, false);
            RackVisual.Build(_subject, style);

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

        /// <summary>
        /// A square picture of a rack on a transparent ground, for the hammer menu.  Rendered
        /// against black and against white, and the two tell the coverage of every pixel, so
        /// the edges come out soft and the game's shaders' alpha, whatever they write, does
        /// not matter.  Null if anything fails; the caller keeps its old icon.
        /// </summary>
        internal static Sprite Icon(RackStyle style, int size)
        {
            GameObject rig = null; RenderTexture rt = null;
            var prevActive = RenderTexture.active;
            try
            {
                rig = BuildRig(out var camera);
                var subject = new GameObject("ArmoryIconRack");
                subject.transform.SetParent(rig.transform, false);
                RackVisual.Build(subject, style);

                rt = new RenderTexture(size, size, 24) { name = "ArmoryIcon", antiAliasing = 4 };
                camera.targetTexture = rt;
                var onBlack = Shoot(camera, rt, Color.black, size);
                var onWhite = Shoot(camera, rt, Color.white, size);

                var black = onBlack.GetPixels32();
                var white = onWhite.GetPixels32();
                var icon = new Color32[black.Length];
                for (int i = 0; i < black.Length; i++)
                {
                    // Coverage is one minus how much the white ground shows through.
                    float a = 1f - ((white[i].r - black[i].r) + (white[i].g - black[i].g) + (white[i].b - black[i].b)) / (3f * 255f);
                    a = Mathf.Clamp01(a);
                    if (a < 0.004f) { icon[i] = new Color32(0, 0, 0, 0); continue; }
                    // The black render is the color times the coverage; undo that.
                    icon[i] = new Color32((byte)Mathf.Min(255f, black[i].r / a), (byte)Mathf.Min(255f, black[i].g / a),
                                          (byte)Mathf.Min(255f, black[i].b / a), (byte)(a * 255f));
                }
                Object.Destroy(onBlack);
                Object.Destroy(onWhite);

                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ArmoryIcon" };
                tex.SetPixels32(icon);
                tex.Apply();
                SavePng(tex);
                return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[Armory] Icon render failed: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
                if (rig != null) Object.DestroyImmediate(rig);
            }
        }

        // One render against a plain ground, read back into a texture.
        private static Texture2D Shoot(Camera camera, RenderTexture rt, Color ground, int size)
        {
            camera.backgroundColor = ground;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try { camera.Render(); }
            finally { RenderSettings.fog = fog; }
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            return tex;
        }

        // The icon as a file, BepInEx/config/Armory/build_icon.png, so it can be looked at
        // without opening the hammer.  ImageConversion is reached by reflection, like the
        // embedded icon's loader in ArmoryPieces: referencing the module does not build.
        private static void SavePng(Texture2D tex)
        {
            try
            {
                var t = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var m = t?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
                if (!(m?.Invoke(null, new object[] { tex }) is byte[] bytes)) return;
                var dir = Path.Combine(BepInEx.Paths.ConfigPath, "Armory");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "build_icon.png"), bytes);
            }
            catch (Exception e) { Jotunn.Logger.LogDebug($"[Armory] Icon not saved: {e.Message}"); }
        }

        private static void MakeRig() { _rig = BuildRig(out _camera); }

        // A rig under the player (or the origin, before there is one): the camera, disabled so
        // it only renders when asked, and two lamps.
        private static GameObject BuildRig(out Camera camera)
        {
            var player = Player.m_localPlayer;
            var at = player != null ? player.transform.position : Vector3.zero;
            var rig = new GameObject("ArmoryPreviewRig");
            rig.transform.position = new Vector3(at.x, Depth, at.z);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(rig.transform, false);
            // Three-quarter view from the front left, a little above center, framing 3 m of rack.
            camGo.transform.localPosition = new Vector3(-2.6f, 2.2f, 5.2f);
            camGo.transform.LookAt(rig.transform.position + new Vector3(0f, 1.45f, 0f));
            camera = camGo.AddComponent<Camera>();
            camera.enabled         = false;            // rendered by hand; the caller gives it its target
            camera.clearFlags      = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.065f, 0.05f, 1f);
            camera.fieldOfView     = 34f;
            camera.nearClipPlane   = 0.2f;
            camera.farClipPlane    = 30f;
            camera.cullingMask     = ~(1 << 5);        // everything but UI
            camera.allowHDR        = false;
            camera.allowMSAA       = true;

            // Daylight does not reach this deep and the sun sets anyway: a lamp by the camera,
            // and one above and behind for the crown and the sides.
            Lamp(rig, new Vector3(-2.0f, 2.6f, 4.0f), 1.6f, 12f);
            Lamp(rig, new Vector3( 2.5f, 3.5f, 1.5f), 0.7f, 10f);
            return rig;
        }

        private static void Lamp(GameObject rig, Vector3 local, float intensity, float range)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(rig.transform, false);
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
