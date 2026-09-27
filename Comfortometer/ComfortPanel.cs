using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Comfortometer
{
    /// <summary>
    /// The panel: a compact box on the HUD with the comfort level in its title and one line per
    /// piece under it. It never takes the keyboard or the mouse; while you play it is just
    /// drawn. Arrange mode frees the cursor so the title strip can be dragged and the grip in
    /// the corner can resize it, and the placement is remembered in the config.
    /// </summary>
    internal static class ComfortPanel
    {
        private const float DefaultW = 250f, DefaultH = 190f;
        private const float MinW = 150f, MinH = 40f, MaxW = 700f, MaxH = 1000f;
        private const float Pad = 6f;
        private const float RefreshSeconds = 0.2f;

        internal static bool IsOpen { get; private set; }
        internal static bool Arranging { get; private set; }

        /// <summary>Arrange mode wants the pointer: the cursor free, the camera still, clicks not attacks.</summary>
        internal static bool WantsPointer => IsOpen && Arranging && _root != null && Player.m_localPlayer != null;

        private sealed class RowUi
        {
            public GameObject Root;
            public TextMeshProUGUI Name, Comfort, Distance;
        }

        private static GameObject _root;
        private static RectTransform _rect;
        private static CanvasGroup _group;
        private static Outline _outline;
        private static TextMeshProUGUI _title, _sub, _footer;
        private static RectTransform _mover, _grip, _rowsRoot;
        private static Button _closeX;
        private static readonly List<RowUi> _rows = new List<RowUi>();
        private static float _w = DefaultW, _h = DefaultH;
        private static float _nextRefresh;
        private static ComfortReport _last;

        private static float Font => ComfortometerMod.FontSize != null ? ComfortometerMod.FontSize.Value : 15f;
        private static float RowH => Mathf.Round(Font * 1.35f);
        private static float TitleH => RowH + 6f;

        // ── open / close ────────────────────────────────────────────────────────────

        internal static void Open()
        {
            IsOpen = true;
            ComfortScan.Forget();
            _nextRefresh = 0f;
            Tick();
        }

        internal static void Close()
        {
            IsOpen = false;
            SetArranging(false);
            if (_root != null) _root.SetActive(false);
        }

        internal static void SetArranging(bool on)
        {
            if (!IsOpen && on) return;
            Arranging = on;
            ApplyArranging();
        }

        /// <summary>Throws the widgets away so the next tick builds them again, e.g. after a font change.</summary>
        internal static void Rebuild()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _rows.Clear();
            _nextRefresh = 0f;
        }

        internal static void Refresh() => _nextRefresh = 0f;

        // ── per frame ───────────────────────────────────────────────────────────────

        internal static void Tick()
        {
            if (!IsOpen) return;
            var player = Player.m_localPlayer;
            if (player == null || Hud.instance == null)
            {
                // Between worlds. The HUD is rebuilt on the next spawn, and this panel with it.
                if (Arranging) SetArranging(false);
                if (_root != null) _root.SetActive(false);
                return;
            }
            if (_root == null && !Build()) return;
            if (!_root.activeSelf) _root.SetActive(true);

            // A drag needs the pointer to reach the panel; otherwise it must never catch it, or a
            // locked cursor parked at the screen center would scroll or click whatever is under it.
            bool pointerFree = Arranging || Cursor.visible;
            if (_group.blocksRaycasts != pointerFree) _group.blocksRaycasts = pointerFree;

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            try
            {
                _last = ComfortScan.Scan(player);
                Render(_last);
                _failures = 0;
            }
            catch (System.Exception e)
            {
                // Never leave a half-drawn panel on screen: build it again from nothing, and if
                // that keeps failing, take it down rather than throw every fifth of a second.
                _failures++;
                ComfortometerMod.Log.LogError($"Comfortometer panel failed to draw ({_failures}): {e}");
                if (_failures >= 3) { ComfortometerMod.Log.LogError("Comfortometer: giving up on the panel until it is opened again."); Close(); _failures = 0; }
                else Rebuild();
            }
        }

        private static int _failures;

        // ── building ────────────────────────────────────────────────────────────────

        private static bool Build()
        {
            // The rows of the previous panel died with it: the HUD is destroyed and made again
            // on every logout and login, and this panel with it. Keeping the dead rows here made
            // Render throw on the first one, which left the new panel half laid out, with the
            // grip still at Unity's default 100x100 rect in the middle. (0.1.2)
            _rows.Clear();
            var hud = Hud.instance;
            var parent = hud.m_rootObject != null ? hud.m_rootObject.transform as RectTransform : hud.transform as RectTransform;
            if (parent == null) return false;

            var panel = UiBits.Panel(parent, "Comfortometer_Panel", new Color(0.05f, 0.04f, 0.03f, 0.78f));
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _group = _root.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _outline = _root.AddComponent<Outline>();
            _outline.effectDistance = new Vector2(1f, -1f);

            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);

            _title = UiBits.Text(_root.transform, "Title", "Comfort", Font + 3f, TextAlignmentOptions.Left, UiBits.Gold);
            _title.fontStyle = FontStyles.Bold;
            _sub = UiBits.Text(_root.transform, "Sub", "", Font - 1f, TextAlignmentOptions.Right, UiBits.Dim);
            _footer = UiBits.Text(_root.transform, "Footer", "", Font - 1f, TextAlignmentOptions.Left, UiBits.Dim);

            // Rows live in their own container, created before the mover and the x so those stay on top.
            var rowsGo = new GameObject("Rows", typeof(RectTransform));
            rowsGo.transform.SetParent(_root.transform, false);
            _rowsRoot = rowsGo.GetComponent<RectTransform>();
            UiBits.Stretch(_rowsRoot);

            // Resize grip in the bottom-right corner.
            var gripGo = new GameObject("Grip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiBits.DragHandle));
            gripGo.transform.SetParent(_root.transform, false);
            _grip = gripGo.GetComponent<RectTransform>();
            var gripImg = gripGo.GetComponent<Image>();
            gripImg.sprite = UiBits.Grip();
            gripImg.color = new Color(1f, 0.85f, 0.45f, 0.75f);
            gripImg.raycastTarget = true;
            var gripHandle = gripGo.GetComponent<UiBits.DragHandle>();
            gripHandle.OnDrag = OnGripDrag;
            gripHandle.OnEnd = SavePlacement;
            gripGo.SetActive(false);          // shown by ApplyArranging while arranging

            // The title strip drags the whole panel. Last sibling, so a drag reaches it and not
            // the background, which would swallow the pointer without handling the drag.
            var moveGo = new GameObject("Mover", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiBits.DragHandle));
            moveGo.transform.SetParent(_root.transform, false);
            _mover = moveGo.GetComponent<RectTransform>();
            var moveImg = moveGo.GetComponent<Image>();
            moveImg.color = new Color(0f, 0f, 0f, 0f);
            moveImg.raycastTarget = true;
            var mover = moveGo.GetComponent<UiBits.DragHandle>();
            mover.OnDrag = OnMoveDrag;
            mover.OnEnd = SavePlacement;

            // An x in the corner closes the panel, in the game's own button style, the way the
            // other DeathMonger panels have one. It can only be clicked while the mouse is free.
            _closeX = UiBits.ValheimButton(_root.transform, "CloseX", "x", Close);

            LoadPlacement();
            Layout();
            ApplyArranging();
            return true;
        }

        private static RowUi MakeRow()
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_rowsRoot, false);
            var row = new RowUi
            {
                Root = go,
                Name = UiBits.Text(go.transform, "Name", "", Font, TextAlignmentOptions.Left),
                Comfort = UiBits.Text(go.transform, "Comfort", "", Font, TextAlignmentOptions.Right, UiBits.Gold),
                Distance = UiBits.Text(go.transform, "Distance", "", Font - 1f, TextAlignmentOptions.Right, UiBits.Dim),
            };
            return row;
        }

        // ── layout ──────────────────────────────────────────────────────────────────

        private static float ComfortW => Mathf.Round(Font * 2.2f);
        private static float DistanceW => Mathf.Round(Font * (ComfortometerMod.ShowRange.Value ? 5.4f : 3.6f));

        private static void Layout()
        {
            if (_root == null) return;
            _rect.sizeDelta = new Vector2(_w, _h);

            float xSize = Mathf.Round(TitleH - 4f);
            float titleW = Mathf.Round(_w * 0.42f);
            UiBits.Place(_title.rectTransform, Pad + 2f, 2f, titleW, TitleH);
            UiBits.Place(_sub.rectTransform, Pad + titleW, 4f, _w - 2f * Pad - titleW - xSize - 4f, TitleH - 2f);
            UiBits.Place(_closeX.GetComponent<RectTransform>(), _w - Pad - xSize + 2f, 2f, xSize, xSize);

            float rowW = _w - 2f * Pad;
            float y = TitleH + 2f;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                UiBits.Place(row.Root.GetComponent<RectTransform>(), Pad, y + i * RowH, rowW, RowH);
                LayoutRow(row, rowW);
            }
            UiBits.Place(_mover, 0f, 0f, _w - xSize - Pad, TitleH + 2f);
            _grip.anchorMin = new Vector2(1f, 0f);
            _grip.anchorMax = new Vector2(1f, 0f);
            _grip.pivot = new Vector2(1f, 0f);
            _grip.anchoredPosition = new Vector2(-3f, 3f);
            _grip.sizeDelta = new Vector2(14f, 14f);
        }

        private static void LayoutRow(RowUi row, float rowW)
        {
            float nameW = Mathf.Max(20f, rowW - ComfortW - DistanceW - 8f);
            UiBits.Place(row.Name.rectTransform, 2f, 0f, nameW, RowH);
            UiBits.Place(row.Comfort.rectTransform, nameW + 4f, 0f, ComfortW, RowH);
            UiBits.Place(row.Distance.rectTransform, nameW + ComfortW + 8f, 0f, DistanceW - 4f, RowH);
        }

        /// <summary>How many rows fit under the title at the current height.</summary>
        private static int Capacity() => Mathf.Max(0, Mathf.FloorToInt((_h - TitleH - 2f - Pad) / RowH));

        // ── rendering ───────────────────────────────────────────────────────────────

        private static void Render(ComfortReport report)
        {
            if (_root == null || report == null) return;

            _title.text = $"Comfort {report.Level}";
            string rested = ComfortometerMod.ShowRestedTime.Value ? $" · rested {Mathf.RoundToInt(report.RestedSeconds / 60f)} min" : "";
            _sub.text = (report.Sheltered ? "sheltered" : "NO SHELTER") + rested;
            _sub.color = report.Sheltered ? UiBits.Dim : UiBits.Red;

            int total = report.Rows.Count;
            if (ComfortometerMod.AutoHeight.Value)
            {
                // Grow or shrink to the list, keeping the top edge where it is.
                float wanted = Mathf.Clamp(TitleH + 2f + Mathf.Max(1, total) * RowH + Pad, MinH, MaxH);
                if (!Mathf.Approximately(wanted, _h))
                {
                    _rect.anchoredPosition += new Vector2(0f, -(wanted - _h) / 2f);
                    _h = wanted;
                }
            }
            int capacity = Capacity();
            int shown = total;
            string footer = null;
            if (total == 0)
            {
                footer = "nothing within 10 m";
                shown = 0;
            }
            else if (total > capacity)
            {
                shown = Mathf.Max(0, capacity - 1);
                footer = $"+{total - shown} more";
            }

            while (_rows.Count < shown) _rows.Add(MakeRow());
            for (int i = 0; i < _rows.Count; i++)
            {
                var ui = _rows[i];
                if (i >= shown) { ui.Root.SetActive(false); continue; }
                ui.Root.SetActive(true);
                Fill(ui, report.Rows[i]);
            }

            Layout();

            // After Layout, which places the rows: the footer takes the slot under the last one.
            if (footer != null && capacity > 0)
            {
                _footer.gameObject.SetActive(true);
                _footer.text = footer;
                UiBits.Place(_footer.rectTransform, Pad + 2f, TitleH + 2f + shown * RowH, _w - 2f * Pad - 4f, RowH);
            }
            else _footer.gameObject.SetActive(false);
        }

        private static void Fill(RowUi ui, ComfortRow row)
        {
            string name = row.Name;
            if (row.InRange >= 2) name += $" ×{row.InRange}";
            Color nameColor, comfortColor, distColor;
            switch (row.State)
            {
                case RowState.Counted:
                    nameColor = UiBits.Body; comfortColor = UiBits.Gold; distColor = UiBits.Dim;
                    break;
                case RowState.Superseded:
                    if (row.InRange > 0 && !row.Active) name += row.Fireplace ? " (unlit)" : " (off)";
                    if (row.Group.Length > 0) name += $" <alpha=#88>({row.Group})";
                    nameColor = comfortColor = distColor = UiBits.Dim;
                    break;
                case RowState.Inactive:
                    name += row.Fireplace ? " (unlit)" : " (off)";
                    nameColor = comfortColor = distColor = UiBits.Red;
                    break;
                case RowState.NoShelter:
                    nameColor = comfortColor = distColor = UiBits.Red;
                    break;
                default:   // Lost
                    nameColor = comfortColor = distColor = UiBits.Red;
                    break;
            }
            ui.Name.text = name;
            ui.Name.color = nameColor;
            ui.Name.fontStyle = ComfortometerMod.BoldNames.Value ? FontStyles.Bold : FontStyles.Normal;
            ui.Comfort.text = row.Comfort > 0 ? $"+{row.Comfort}" : "";
            ui.Comfort.color = comfortColor;
            ui.Distance.text = FormatDistance(row.Distance);
            ui.Distance.color = row.Distance >= ComfortScan.Radius ? UiBits.Red : distColor;
        }

        private static string FormatDistance(float d)
        {
            string n = d < 10f ? $"{d:0.0}" : $"{d:0}";
            return ComfortometerMod.ShowRange.Value ? $"{n} / {ComfortScan.Radius:0} m" : $"{n} m";
        }

        // ── arrange mode ────────────────────────────────────────────────────────────

        private static void ApplyArranging()
        {
            if (_root == null) return;
            _outline.effectColor = Arranging ? new Color(1f, 0.85f, 0.45f, 0.95f) : new Color(0.55f, 0.45f, 0.3f, 0.7f);
            _grip.gameObject.SetActive(Arranging);
            if (_last != null) Render(_last);
        }

        /// <summary>The grip follows the pointer: the bottom-right corner moves, the top-left stays.</summary>
        private static void OnGripDrag(Vector2 screenDelta)
        {
            if (_root == null) return;
            float scale = CanvasScale();
            float nw = Mathf.Clamp(_w + screenDelta.x / scale, MinW, MaxW);
            float nh = ComfortometerMod.AutoHeight.Value ? _h : Mathf.Clamp(_h - screenDelta.y / scale, MinH, MaxH);
            _rect.anchoredPosition += new Vector2((nw - _w) / 2f, -(nh - _h) / 2f);
            _w = nw;
            _h = nh;
            if (_last != null) Render(_last); else Layout();
        }

        private static void OnMoveDrag(Vector2 screenDelta)
        {
            if (_root == null) return;
            _rect.anchoredPosition += screenDelta / CanvasScale();
            ClampToScreen();
        }

        private static float CanvasScale()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            return scale <= 0f ? 1f : scale;
        }

        /// <summary>Keeps at least a corner of the panel on screen, so it can always be dragged back.</summary>
        private static void ClampToScreen()
        {
            var parent = _rect.parent as RectTransform;
            if (parent == null) return;
            float halfW = parent.rect.width / 2f, halfH = parent.rect.height / 2f;
            var p = _rect.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -halfW, halfW);
            p.y = Mathf.Clamp(p.y, -halfH, halfH);
            _rect.anchoredPosition = p;
        }

        private static void LoadPlacement()
        {
            _w = DefaultW;
            _h = DefaultH;
            if (UiBits.TryPair(ComfortometerMod.PanelSize.Value, out float w, out float h))
            {
                _w = Mathf.Clamp(w, MinW, MaxW);
                _h = Mathf.Clamp(h, MinH, MaxH);
            }
            if (UiBits.TryPair(ComfortometerMod.PanelPosition.Value, out float x, out float y))
                _rect.anchoredPosition = new Vector2(x, y);
            else
            {
                // Default: against the right edge, under the minimap.
                var parent = _rect.parent as RectTransform;
                float halfW = parent != null ? parent.rect.width / 2f : 960f;
                float halfH = parent != null ? parent.rect.height / 2f : 540f;
                _rect.anchoredPosition = new Vector2(halfW - _w / 2f - 16f, halfH - _h / 2f - 300f);
            }
            ClampToScreen();
        }

        private static void SavePlacement()
        {
            ComfortometerMod.PanelSize.Value = UiBits.Pair(_w, _h);
            if (_rect != null) ComfortometerMod.PanelPosition.Value = UiBits.Pair(_rect.anchoredPosition.x, _rect.anchoredPosition.y);
        }
    }
}
