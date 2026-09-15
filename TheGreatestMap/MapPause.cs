using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// Option: pause the game while the large map screen is open, the way the ESC menu does.
    /// It goes through vanilla's own Game.Pause / Game.Unpause, the two calls the menu makes, so
    /// alone in a solo or hosted game the game freezes by itself, and on a dedicated server it is
    /// Pause My Server, if installed, that decides: it hooks those same two calls and grants the
    /// pause only while you are the only player online. Nothing here knows about that mod.
    /// </summary>
    internal static class MapPause
    {
        private static bool _holding;

        internal static bool Holding => _holding;

        /// <summary>Hold the pause exactly while the option is on, the large map is open and there is a player; let go otherwise.</summary>
        internal static void Refresh()
        {
            var map = Minimap.instance;
            bool onAScreen = (map != null && map.m_mode == Minimap.MapMode.Large) || LegendPanel.IsOpen;
            bool want = TgmConfig.PauseWhileMapOpen != null && TgmConfig.PauseWhileMapOpen.Value
                && onAScreen && Player.m_localPlayer != null;
            if (want == _holding) return;
            _holding = want;
            if (want) Game.Pause();
            else Game.Unpause();
        }

        internal static void Release()
        {
            if (!_holding) return;
            _holding = false;
            Game.Unpause();
        }
    }

    /// <summary>
    /// The pause toggle in the top-right corner of the large map, built the same way as the one on
    /// GrabMaterials' inventory panel: two bars drawn from plain rectangles, because the game's
    /// font has no media-control glyph, plus a diagonal slash when a pause was asked for and
    /// refused. The mark never claims a pause that is not happening, since the request can be
    /// turned down by a server with other players on it, or one without Pause My Server at all.
    /// </summary>
    internal static class PauseButton
    {
        private static PauseToggle _map;

        internal static void Reset() => _map?.Destroy();

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null || map.m_largeRoot == null
                || TgmConfig.ShowPauseButton == null || !TgmConfig.ShowPauseButton.Value)
            {
                _map?.Destroy();
                return;
            }
            if (_map == null || !_map.Alive) _map = PauseToggle.Build(map.m_largeRoot.transform as RectTransform, new Vector2(-16f, -16f));
            if (_map == null) return;
            Position(_map);
            _map.Paint();
        }

        /// <summary>
        /// Sit in the top-right corner, but left of the map-pin button rather than up against it.
        /// Measured from where that button actually is, so the gap holds at any UI scale, and
        /// falling back to the plain corner when the pin button is switched off.
        /// </summary>
        private static void Position(PauseToggle toggle)
        {
            var rect = toggle.Rect;
            var parent = rect != null ? rect.parent as RectTransform : null;
            if (parent == null) return;
            const float margin = 16f;
            const float gap = 14f;
            float x = -margin;
            if (MarkerToggle.TryGetWorldLeft(out float worldLeft))
            {
                float local = parent.InverseTransformPoint(new Vector3(worldLeft, 0f, 0f)).x;
                x = Mathf.Min(-margin, local - gap - parent.rect.xMax);
            }
            if (!Mathf.Approximately(rect.anchoredPosition.x, x))
                rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
        }
    }

    /// <summary>
    /// The pause mark itself, so the map and the legend can each have one: two bars drawn from
    /// plain rectangles, because the game's font has no media-control glyph, plus a diagonal slash
    /// when a pause was asked for and refused. It never claims a pause that is not happening.
    /// </summary>
    internal sealed class PauseToggle
    {
        private static readonly Color Off = new Color(0.6f, 0.6f, 0.6f, 0.85f);
        private static readonly Color Paused = new Color(1f, 0.63f, 0.24f, 1f); // Valheim orange
        private static readonly Color Refused = new Color(1f, 0.45f, 0.4f, 1f);

        private GameObject _root;
        private Image _left, _right, _slash;

        internal bool Alive => _root != null;
        internal RectTransform Rect => _root != null ? (RectTransform)_root.transform : null;

        internal void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _left = _right = _slash = null;
        }

        internal static PauseToggle Build(RectTransform parent, Vector2 anchoredPosition)
        {
            if (parent == null) return null;
            var t = new PauseToggle();
            t._root = new GameObject("TGM_PauseToggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            t._root.transform.SetParent(parent, false);
            var rect = (RectTransform)t._root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(28f, 28f);

            var hit = t._root.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f); // invisible, but it is what catches the click
            hit.raycastTarget = true;

            t._left = Bar(t._root.transform, new Vector2(6f, 18f), new Vector2(-5f, 0f));
            t._right = Bar(t._root.transform, new Vector2(6f, 18f), new Vector2(5f, 0f));
            t._slash = Bar(t._root.transform, new Vector2(30f, 3f), Vector2.zero, 45f);
            t._slash.gameObject.SetActive(false);

            var button = t._root.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                if (TgmConfig.PauseWhileMapOpen == null) return;
                TgmConfig.PauseWhileMapOpen.Value = !TgmConfig.PauseWhileMapOpen.Value;
                MapPause.Refresh();
                t.Paint();
            });
            t._root.transform.SetAsLastSibling();
            t.Paint();
            return t;
        }

        private static Image Bar(Transform parent, Vector2 size, Vector2 pos, float rotation = 0f)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            if (rotation != 0f) rt.localRotation = Quaternion.Euler(0f, 0f, rotation);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Gray when switched off, orange while the game really is paused, red with a slash when the pause was refused.</summary>
        internal void Paint()
        {
            if (_left == null) return;
            bool on = TgmConfig.PauseWhileMapOpen != null && TgmConfig.PauseWhileMapOpen.Value;
            bool refused = false;
            Color color;
            if (!on) color = Off;
            else if (Game.IsPaused()) color = Paused;
            else { color = Refused; refused = true; }
            _left.color = color;
            _right.color = color;
            _slash.color = color;
            if (_slash.gameObject.activeSelf != refused) _slash.gameObject.SetActive(refused);
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    internal static class Minimap_SetMapMode_Pause_Patch
    {
        private static void Postfix() => MapPause.Refresh();
    }

    /// <summary>
    /// The map screen keeps its own clocks on scaled time: the flick-scroll inertia damping, the
    /// click-versus-drag and double-click timing, the input delay after "show point on map" and
    /// the short delay around the pin name box. At time scale zero the inertia would never damp
    /// (the map slides forever after a flick) and any two clicks would count as a double-click.
    /// Switching those methods to unscaled time keeps the map screen usable while the game is
    /// paused, by this mod or by an admin pause; at time scale one nothing changes.
    /// </summary>
    [HarmonyPatch]
    internal static class Minimap_UnscaledTime_Patch
    {
        private static readonly string[] Methods =
        {
            "Update", "UpdateNameInput", "UpdateMap", "UpdateEventPin", "UpdatePersistentEventPins", "OnMapLeftDown", "OnMapLeftUp",
        };

        private static readonly MethodInfo GetTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.time));
        private static readonly MethodInfo GetUnscaledTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledTime));
        private static readonly MethodInfo GetDeltaTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
        private static readonly MethodInfo GetUnscaledDeltaTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in Methods)
            {
                var method = AccessTools.Method(typeof(Minimap), name);
                if (method != null) yield return method;
                else TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Minimap.{name} not found; the map screen may misbehave while paused.");
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(GetTime)) instruction.operand = GetUnscaledTime;
                else if (instruction.Calls(GetDeltaTime)) instruction.operand = GetUnscaledDeltaTime;
                yield return instruction;
            }
        }
    }
}
