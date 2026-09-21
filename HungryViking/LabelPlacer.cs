using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HungryViking
{
    public enum LabelPlacement { Automatic, Manual }

    // A warning label pinned to the top center of the screen. HungryVikingMod stacks the
    // visible ones, in a fixed order, starting at whatever offset LabelPlacer picks.
    internal interface IWarningLabel
    {
        Canvas Canvas       { get; }
        bool   LabelVisible { get; }
        float  LabelWidth   { get; }
        void   SetLabelTop(float top);
    }

    internal static class WarningLabel
    {
        public const float Height = 30f;
    }

    // Picks the screen-top offset for the stack of warning labels. In Automatic mode it scans
    // every screen-space canvas for visible graphics in the labels' column near the top of the
    // screen (another mod's compass, the boss health bar) and finds the first gap below them
    // tall enough for the stack.
    internal class LabelPlacer
    {
        public const float DefaultTop = 40f;

        private const float Gap             = 6f;
        private const float ColumnPadding   = 16f;
        private const float ScanInterval    = 0.5f;
        private const float HoldSeconds     = 4f;    // wait this long before moving back up
        private const float TopBandFraction = 0.3f;  // only elements starting in the top 30% count
        private const float MaxTallFraction = 0.4f;  // taller than this is a backdrop, not a widget
        private const float MaxTopFraction  = 0.4f;  // never push the labels below this

        private readonly Canvas[]         _ownCanvases;
        private readonly List<Graphic>    _graphics = new List<Graphic>();
        private readonly List<Vector2>    _spans    = new List<Vector2>(); // x = top, y = bottom
        private readonly List<Graphic>    _sources  = new List<Graphic>(); // parallel to _spans
        private readonly List<Transform>  _excluded = new List<Transform>();
        private readonly Vector3[]        _corners  = new Vector3[4];

        private float _nextScan;
        private float _target = DefaultTop;
        private float _holdUntil;
        private float _current = DefaultTop;
        private bool  _wasShowing;

        public LabelPlacer(params Canvas[] ownCanvases)
        {
            _ownCanvases = ownCanvases;
        }

        // Returns the distance in screen pixels from the top of the screen to the top of the
        // first label. columnWidth is the widest visible label; stackHeight is all of them.
        public float Top(LabelPlacement mode, float manualTop, bool showing,
                         float columnWidth, float stackHeight)
        {
            float maxTop = Mathf.Max(0f, Screen.height - stackHeight);

            if (mode == LabelPlacement.Manual || !showing)
            {
                _wasShowing = false;
                _current    = _target = DefaultTop;
                return Mathf.Clamp(mode == LabelPlacement.Manual ? manualTop : DefaultTop, 0f, maxTop);
            }

            float now = Time.unscaledTime;
            if (!_wasShowing || now >= _nextScan)
            {
                float found = Scan(columnWidth, stackHeight);

                // Move down at once, but back up only once the space has stayed free a while,
                // so an element that blinks on and off doesn't bounce the labels with it.
                if (found >= _target)
                {
                    _target    = found;
                    _holdUntil = now + HoldSeconds;
                }
                else if (now >= _holdUntil)
                {
                    _target = found;
                }
                _nextScan = now + ScanInterval;
            }

            // Snap into place when the labels first appear; glide when they are already showing.
            _current = _wasShowing
                ? Mathf.Lerp(_current, _target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime))
                : _target;
            _wasShowing = true;

            return Mathf.Clamp(_current, 0f, maxTop);
        }

        private float Scan(float columnWidth, float stackHeight)
        {
            float screenW = Screen.width;
            float screenH = Screen.height;
            float colMin  = (screenW - columnWidth) * 0.5f - ColumnPadding;
            float colMax  = (screenW + columnWidth) * 0.5f + ColumnPadding;

            CollectExclusions();
            _spans.Clear();
            _sources.Clear();

            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
                if (canvas.renderMode == RenderMode.WorldSpace) continue;
                if (System.Array.IndexOf(_ownCanvases, canvas) >= 0) continue;

                Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

                canvas.GetComponentsInChildren(false, _graphics);
                foreach (var g in _graphics)
                {
                    if (!IsVisible(g) || IsExcluded(g.transform)) continue;

                    g.rectTransform.GetWorldCorners(_corners);
                    float xMin = float.MaxValue, xMax = float.MinValue;
                    float yMin = float.MaxValue, yMax = float.MinValue;
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, _corners[i]);
                        xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                        yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
                    }

                    float top    = screenH - yMax;
                    float bottom = screenH - yMin;
                    if (bottom - top > screenH * MaxTallFraction) continue;
                    if (top > screenH * TopBandFraction || bottom < 0f) continue;
                    if (xMax < colMin || xMin > colMax) continue;

                    _spans.Add(new Vector2(top, bottom));
                    _sources.Add(g);
                }
            }
            _graphics.Clear();

            // Slide the stack down past every element it would overlap until it fits in a gap.
            float y = DefaultTop;
            bool moved = true;
            while (moved)
            {
                moved = false;
                foreach (var s in _spans)
                {
                    if (s.x < y + stackHeight + Gap && s.y + Gap > y)
                    {
                        y     = s.y + Gap;
                        moved = true;
                    }
                }
            }

            return Mathf.Min(y, screenH * MaxTopFraction);
        }

        // For hv_labels: scan now and list every element the labels would have to clear.
        public string Describe(float columnWidth, float stackHeight)
        {
            float top = Scan(columnWidth, stackHeight);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Screen {Screen.width}x{Screen.height}, column {columnWidth:0}px wide, stack {stackHeight:0}px tall");
            for (int i = 0; i < _spans.Count; i++)
                sb.AppendLine($"  {_spans[i].x,6:0} - {_spans[i].y,6:0}  {PathOf(_sources[i].transform)}");
            if (_spans.Count == 0)
                sb.AppendLine("  nothing in the way");
            sb.Append($"Automatic placement puts the first label {top:0}px from the top.");
            _sources.Clear();
            return sb.ToString();
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (t = t.parent; t != null; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }

        private static bool IsVisible(Graphic g)
        {
            if (!g.isActiveAndEnabled || g.canvasRenderer.cull) return false;
            if (g.color.a * g.canvasRenderer.GetInheritedAlpha() < 0.05f) return false;
            if (g is Text text && string.IsNullOrEmpty(text.text)) return false;
            if (g is TMP_Text tmp && string.IsNullOrEmpty(tmp.text)) return false;
            if (g.TryGetComponent(out Mask mask) && mask.enabled && !mask.showMaskGraphic) return false;
            return true;
        }

        // Vanilla HUD pieces that follow things in the world — enemy health bars, damage numbers,
        // chat bubbles — and short-lived notices would drag the labels around, so they don't count.
        // The boss bar is the exception: it stays put at the top center for the whole fight.
        private void CollectExclusions()
        {
            _excluded.Clear();

            var enemyHud = EnemyHud.instance;
            if (enemyHud != null && enemyHud.m_hudRoot != null)
            {
                string boss = enemyHud.m_baseHudBoss != null ? enemyHud.m_baseHudBoss.name + "(Clone)" : null;
                foreach (Transform child in enemyHud.m_hudRoot.transform)
                    if (child.name != boss)
                        _excluded.Add(child);
            }

            if (DamageText.instance != null)
                ExcludeClones(DamageText.instance.transform, DamageText.instance.m_worldTextBase);

            if (Chat.instance != null)
                ExcludeClones(Chat.instance.transform, Chat.instance.m_worldTextBase,
                              Chat.instance.m_npcTextBase, Chat.instance.m_npcTextBaseLarge);

            if (MessageHud.instance != null)
                ExcludeClones(MessageHud.instance.transform, MessageHud.instance.m_biomeFoundPrefab);
        }

        private void ExcludeClones(Transform parent, params GameObject[] prefabs)
        {
            foreach (Transform child in parent)
                foreach (var prefab in prefabs)
                    if (prefab != null && child.name == prefab.name + "(Clone)")
                        _excluded.Add(child);
        }

        private bool IsExcluded(Transform t)
        {
            foreach (var root in _excluded)
                if (root != null && t.IsChildOf(root))
                    return true;
            return false;
        }
    }
}
