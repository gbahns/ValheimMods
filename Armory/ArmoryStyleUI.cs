using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Armory
{
    /// <summary>
    /// The style picker under the Manage Loadouts panel's Style button: a dropdown each for the
    /// rack's model, wood and decoration, and for the Armoire its crown's shape and height,
    /// with a picture of the result beside them and the rack itself redrawn as you go
    /// (RackVisual redraws it the frame the ZDO changes).  "Use my default" clears the rack's
    /// own style so it follows the Default settings again.
    ///
    /// Anyone who can manage the rack's loadouts can restyle it, within a ward's rules.  The
    /// metal decorations are offered once the player has reached that metal (RackVisual.Unlocked).
    /// </summary>
    internal static class ArmoryStyleUI
    {
        private static GameObject _panel;
        private static ArmoryRack _rack;
        private static Dropdown   _modelDrop, _decorDrop, _woodDrop, _curveDrop, _crownDrop;
        private static GameObject _curveRow, _crownRow;   // shown only for the Armoire
        private static RectTransform _defaultRt, _hintRt; // move up when those rows are hidden
        private static readonly List<RackModel>   _modelChoices = new List<RackModel>();
        private static readonly List<RackDecor>   _decorChoices = new List<RackDecor>();
        private static readonly List<RackWood>    _woodChoices  = new List<RackWood>();
        private static readonly List<CrownCurve>  _curveChoices = new List<CrownCurve>();
        private static readonly List<CrownHeight> _crownChoices = new List<CrownHeight>();
        private static bool       _filling;    // options being rewritten, not the player choosing
        private static Button     _defaultButton;
        private static RawImage   _preview;
        private static RackModel?   _model;   // what this client last asked for; null follows the setting
        private static RackDecor?   _decor;
        private static RackWood?    _wood;
        private static CrownCurve?  _curve;
        private static CrownHeight? _crown;

        /// <summary>The look the picker currently shows: this client's choices over the settings.</summary>
        private static RackStyle Current() => new RackStyle(_model ?? RackVisual.Model, _decor ?? RackVisual.Decor,
            _wood ?? RackVisual.Wood, _curve ?? RackVisual.Curve, _crown ?? RackVisual.Crown);

        public static bool IsOpen => _panel != null;

        public static void Toggle(ArmoryRack rack, Transform parent)
        {
            if (IsOpen) Close(); else Open(rack, parent);
        }

        public static void Open(ArmoryRack rack, Transform parent)
        {
            Close();
            if (rack == null || parent == null || GUIManager.Instance == null) return;
            _rack  = rack;
            _model = rack.OwnModel;
            _decor = rack.OwnDecor;
            _wood  = rack.OwnWood;
            _curve = rack.OwnCurve;
            _crown = rack.OwnCrown;

            // The picker fills the panel below its title strip, however the player has sized
            // it; the controls keep a fixed column on the left and the picture takes the rest.
            const float controls = 330f;
            _panel = new GameObject("ArmoryStylePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _panel.transform.SetParent(parent, false);
            var rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(16f, 16f);
            rt.offsetMax = new Vector2(-16f, -56f);
            _panel.GetComponent<Image>().color = new Color(0.07f, 0.055f, 0.04f, 0.97f);
            _panel.transform.SetAsLastSibling();

            _modelDrop = Row(-30f,  "Model",        i => { if (!_filling && i >= 0 && i < _modelChoices.Count) Apply(_modelChoices[i], _decor, _wood, _curve, _crown); }, out _);
            _woodDrop  = Row(-78f,  "Wood",         i => { if (!_filling && i >= 0 && i < _woodChoices.Count)  Apply(_model, _decor, _woodChoices[i], _curve, _crown); }, out _);
            _decorDrop = Row(-126f, "Decoration",   i => { if (!_filling && i >= 0 && i < _decorChoices.Count) Apply(_model, _decorChoices[i], _wood, _curve, _crown); }, out _);
            _curveDrop = Row(-174f, "Crown shape",  i => { if (!_filling && i >= 0 && i < _curveChoices.Count) Apply(_model, _decor, _wood, _curveChoices[i], _crown); }, out _curveRow);
            _crownDrop = Row(-222f, "Crown height", i => { if (!_filling && i >= 0 && i < _crownChoices.Count) Apply(_model, _decor, _wood, _curve, _crownChoices[i]); }, out _crownRow);

            var def = GUIManager.Instance.CreateButton("Use my default", _panel.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(controls / 2f, -182f), 200f, 36f);
            _defaultRt = def.GetComponent<RectTransform>();
            _defaultButton = def.GetComponent<Button>();
            _defaultButton.onClick.AddListener(() => Apply(null, null, null, null, null));

            var hint = GUIManager.Instance.CreateText("Metal decorations open up as you reach each metal.\nEveryone sees this rack the way you dress it.",
                _panel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(controls / 2f, -248f),
                GUIManager.Instance.AveriaSerif, 13, new Color(0.75f, 0.7f, 0.62f), true, Color.black, controls - 40f, 60f, false);
            hint.GetComponent<Text>().alignment = TextAnchor.UpperCenter;
            _hintRt = hint.GetComponent<RectTransform>();

            // The picture, on the right.
            var img = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            img.transform.SetParent(_panel.transform, false);
            var irt = img.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 0f);
            irt.anchorMax = new Vector2(1f, 1f);
            irt.offsetMin = new Vector2(controls + 10f, 20f);
            irt.offsetMax = new Vector2(-20f, -20f);
            _preview = img.GetComponent<RawImage>();
            img.AddComponent<Repaint>();

            Refresh();
        }

        public static void Close()
        {
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            ArmoryPreview.Hide();
            _panel = null; _rack = null; _modelDrop = null; _decorDrop = null; _woodDrop = null; _curveDrop = null; _crownDrop = null;
            _curveRow = null; _crownRow = null; _defaultRt = null; _hintRt = null; _defaultButton = null; _preview = null;
        }

        // A label and a dropdown, under a zero-sized holder at the panel's top-left so the row
        // can be shown or hidden as one; the children keep their panel coordinates.
        private static Dropdown Row(float y, string title, Action<int> onChanged, out GameObject row)
        {
            row = new GameObject("Row " + title, typeof(RectTransform));
            row.transform.SetParent(_panel.transform, false);
            var rrt = row.GetComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0f, 1f);
            rrt.anchoredPosition = Vector2.zero;
            rrt.sizeDelta = Vector2.zero;
            var t = row.transform;
            // Both are centered on their position: the label runs from x 16 to 136 ("Crown
            // height" is the widest), the box from 146 to 326 ("Carved shields" is the widest
            // entry, and Jotunn gives the caption 50 px less than the box).
            const float labelW = 120f, boxW = 180f;
            var cap = GUIManager.Instance.CreateText(title, t, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f + labelW / 2f, y),
                GUIManager.Instance.AveriaSerifBold, 18, GUIManager.Instance.ValheimOrange, true, Color.black, labelW, 36f, false);
            cap.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;

            var go = GUIManager.Instance.CreateDropDown(t, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(146f + boxW / 2f, y), 15, boxW, 36f);
            var drop = go.GetComponent<Dropdown>();
            drop.onValueChanged.AddListener(i => onChanged(i));
            FitItems(drop, 36f);
            go.AddComponent<ArrowKeys>();
            return drop;
        }

        // Jotunn gives the item label the dropdown's height but leaves Unity's 20 px row, so
        // each label spills over its neighbors and, being a raycast target drawn later, takes
        // the clicks meant for the row above.  Rows become as tall as their text.
        private static void FitItems(Dropdown drop, float rowHeight)
        {
            var item = drop.template != null ? drop.template.Find("Viewport/Content/Item") as RectTransform : null;
            if (item == null) return;
            item.sizeDelta = new Vector2(item.sizeDelta.x, rowHeight);
            var label = drop.itemText != null ? drop.itemText.rectTransform : null;
            if (label == null) return;
            label.anchorMin = new Vector2(label.anchorMin.x, 0f);
            label.anchorMax = new Vector2(label.anchorMax.x, 1f);
            label.sizeDelta = new Vector2(label.sizeDelta.x, 0f);
            label.anchoredPosition = new Vector2(label.anchoredPosition.x, 0f);
        }

        // The lists show what the rack wears, the setting's choice included; "Use my default"
        // is how a rack goes back to following the setting.  A decoration somebody else hung
        // on this rack stays listed even when this player has not reached its metal.  Panels
        // is the game's own wood, so the Wood dropdown rests while it is the model; only the
        // Armoire has a crown to shape, so its two rows show for that model alone, and the
        // height rests while the crown is Flat.
        private static void Fill()
        {
            if (_modelDrop == null || _decorDrop == null || _woodDrop == null || _curveDrop == null || _crownDrop == null) return;
            _filling = true;
            try
            {
                _modelChoices.Clear();
                var models = new List<string>();
                foreach (RackModel m in Enum.GetValues(typeof(RackModel))) { _modelChoices.Add(m); models.Add(RackVisual.DisplayName(m)); }
                SetOptions(_modelDrop, models, _modelChoices.IndexOf(_model ?? RackVisual.Model));

                _decorChoices.Clear();
                var decors = new List<string>();
                foreach (RackDecor d in Enum.GetValues(typeof(RackDecor)))
                {
                    if (!RackVisual.Unlocked(d) && d != _decor) continue;
                    _decorChoices.Add(d); decors.Add(RackVisual.DisplayName(d));
                }
                SetOptions(_decorDrop, decors, _decorChoices.IndexOf(_decor ?? RackVisual.Decor));

                _woodChoices.Clear();
                var woods = new List<string>();
                foreach (RackWood w in Enum.GetValues(typeof(RackWood))) { _woodChoices.Add(w); woods.Add(w.ToString()); }
                SetOptions(_woodDrop, woods, _woodChoices.IndexOf(_wood ?? RackVisual.Wood));
                _woodDrop.interactable = (_model ?? RackVisual.Model) != RackModel.Panels;

                _curveChoices.Clear();
                var curves = new List<string>();
                foreach (CrownCurve c in Enum.GetValues(typeof(CrownCurve))) { _curveChoices.Add(c); curves.Add(c.ToString()); }
                SetOptions(_curveDrop, curves, _curveChoices.IndexOf(_curve ?? RackVisual.Curve));

                _crownChoices.Clear();
                var crowns = new List<string>();
                foreach (CrownHeight h in Enum.GetValues(typeof(CrownHeight))) { _crownChoices.Add(h); crowns.Add(h.ToString()); }
                SetOptions(_crownDrop, crowns, _crownChoices.IndexOf(_crown ?? RackVisual.Crown));

                bool armoire = (_model ?? RackVisual.Model) == RackModel.Armoire;
                if (_curveRow != null) _curveRow.SetActive(armoire);
                if (_crownRow != null) _crownRow.SetActive(armoire);
                _crownDrop.interactable = (_curve ?? RackVisual.Curve) != CrownCurve.Flat;
                float drop = armoire ? 96f : 0f;   // two more rows above the button and hint
                if (_defaultRt != null) _defaultRt.anchoredPosition = new Vector2(_defaultRt.anchoredPosition.x, -182f - drop);
                if (_hintRt != null)    _hintRt.anchoredPosition    = new Vector2(_hintRt.anchoredPosition.x,    -248f - drop);
            }
            finally { _filling = false; }
        }

        // Unity's dropdown list never grows past its template, so the template is sized to
        // the options: the whole list at once, no scrollbar.
        private static void SetOptions(Dropdown drop, List<string> options, int selected)
        {
            drop.ClearOptions();
            drop.AddOptions(options);
            drop.SetValueWithoutNotify(Math.Max(0, selected));
            drop.RefreshShownValue();

            var template = drop.template;
            if (template == null) return;
            var item = template.Find("Viewport/Content/Item") as RectTransform;
            float itemH = item != null && item.rect.height > 1f ? item.rect.height : 28f;
            template.sizeDelta = new Vector2(template.sizeDelta.x, itemH * options.Count + 10f);
            var scroll = template.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        }

        // Up and down arrows move the last-clicked dropdown's selection.  Focus is tracked
        // here from the pointer, not read off the EventSystem, which the game's input module
        // does not keep pointed at a clicked dropdown.
        private static ArrowKeys _focused;

        private sealed class ArrowKeys : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler
        {
            private Dropdown _drop;
            private void Awake() { _drop = GetComponent<Dropdown>(); }
            private void OnDestroy() { if (_focused == this) _focused = null; }
            public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) { _focused = this; }

            private void Update()
            {
                if (_focused != this || _drop == null || _drop.options.Count == 0) return;
                int by = Pressed(KeyCode.DownArrow) ? 1 : Pressed(KeyCode.UpArrow) ? -1 : 0;
                if (by == 0) return;
                if (_drop.transform.Find("Dropdown List") != null) _drop.Hide();   // the open list would show the old highlight
                _drop.value = Mathf.Clamp(_drop.value + by, 0, _drop.options.Count - 1);
            }

            private static bool Pressed(KeyCode key)
            {
                try { if (ZInput.GetKeyDown(key)) return true; } catch { }
                try { return Input.GetKeyDown(key); } catch { return false; }
            }
        }

        // The picture is drawn at the size it is shown; a resize of the panel while the picker
        // is open draws it again at the new size, once the layout has settled.
        private sealed class Repaint : MonoBehaviour
        {
            private bool _due;
            private void OnRectTransformDimensionsChange() { _due = true; }
            private void LateUpdate()
            {
                if (!_due || _panel == null) return;
                _due = false;
                try { ArmoryPreview.Show(_preview, Current()); }
                catch (Exception e) { Jotunn.Logger.LogWarning($"[Armory] Preview failed: {e.Message}"); }
            }
        }

        private static void Apply(RackModel? model, RackDecor? decor, RackWood? wood, CrownCurve? curve, CrownHeight? crown)
        {
            if (_rack == null) return;
            // Restyling is a modification, so it answers to a ward the way renaming does.
            if (!PrivateArea.CheckAccess(_rack.transform.position))
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                return;
            }
            _model = model; _decor = decor; _wood = wood; _curve = curve; _crown = crown;
            _rack.SetStyle(model, decor, wood, curve, crown);
            Refresh();
        }

        /// <summary>A style setting changed while the picker is open: draw its picture again.</summary>
        internal static void Redraw() => Refresh();

        private static void Refresh()
        {
            if (_panel == null) return;
            Fill();
            if (_defaultButton != null) _defaultButton.interactable = _model.HasValue || _decor.HasValue || _wood.HasValue || _curve.HasValue || _crown.HasValue;
            try { ArmoryPreview.Show(_preview, Current()); }
            catch (Exception e) { Jotunn.Logger.LogWarning($"[Armory] Preview failed: {e.Message}"); }
        }
    }
}
