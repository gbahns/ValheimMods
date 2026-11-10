using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// The pictures the game has, shown as pictures.
    ///
    /// <para><c>tgm_items</c> exists to help a player choose an icon, and it printed a list of
    /// names into the console -- "DvergrNeedle, Tankard_dvergr, piece:piece_dvergr_lantern" --
    /// which is the one form in which a picture cannot be judged. A name tells you a sprite
    /// exists; choosing between twenty-one of them means seeing them, at the size they will
    /// actually be drawn.</para>
    ///
    /// <para>The console cannot show a sprite, so this is a panel. It is deliberately a plain
    /// one: no dragging, no resizing, no editing, nothing to save. It is opened to look at
    /// something and closed again, and everything it would otherwise have to remember is a
    /// setting that would have to be kept somewhere.</para>
    /// </summary>
    internal static class PicturePanel
    {
        private const float Width = 640f, Height = 560f;
        private const float RowHeight = 28f;
        private const float IconSize = 24f;
        private const float Pad = 12f;
        private const float TitleHeight = 28f;
        private const float HintHeight = 16f;

        private static GameObject _root;
        private static RectTransform _viewport, _content;
        private static float _scroll, _wheel, _contentHeight;

        internal static bool IsOpen => _root != null;

        internal static void Close()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _viewport = null;
            _content = null;
            _scroll = 0f;
            _wheel = 0f;
        }

        /// <summary>Show every picture whose name contains this word. Returns how many there were.</summary>
        internal static int Show(string word, List<string> keys)
        {
            Close();
            if (keys == null || keys.Count == 0) return 0;
            var parent = Hud.instance != null ? Hud.instance.transform as RectTransform : null;
            if (parent == null) { TheGreatestMapMod.Message("No HUD to draw the pictures on."); return 0; }

            var panel = MenuKit.Panel(parent, "TGM_Pictures");
            _root = panel.gameObject;
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(Width, Height);

            var title = MenuKit.Text(_root.transform, "Title",
                $"{keys.Count} picture(s) matching \"{word}\"", 16f, MenuKit.Header);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            Place(title.rectTransform, Pad, -Pad, Width - Pad * 2f, TitleHeight);

            // Clipped rather than scrolled by a ScrollRect, which is how the legend does it and
            // saves a scrollbar nobody needs for a list this short.
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(_root.transform, false);
            _viewport = (RectTransform)viewport.transform;
            float listTop = Pad + TitleHeight + 4f;
            float listHeight = Height - listTop - Pad - HintHeight;
            Place(_viewport, Pad, -listTop, Width - Pad * 2f, listHeight);

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.anchoredPosition = Vector2.zero;

            var map = Minimap.instance;
            float y = 0f;
            int drawn = 0;
            foreach (var key in keys)
            {
                var sprite = IconRegistry.Resolve(map, key);
                var row = new GameObject("Row", typeof(RectTransform));
                row.transform.SetParent(_content, false);
                Place((RectTransform)row.transform, 0f, -y, Width - Pad * 2f, RowHeight);

                if (sprite != null)
                {
                    var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    icon.transform.SetParent(row.transform, false);
                    var img = icon.GetComponent<Image>();
                    img.sprite = sprite;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    Place((RectTransform)icon.transform, 2f, -(RowHeight - IconSize) / 2f, IconSize, IconSize);
                    drawn++;
                }

                // The key exactly as it would be typed into a catalog line or Hidden Icons, which
                // is the whole point of being here: a picture you like is no use if the name
                // beside it is not the one the setting wants.
                var label = MenuKit.Text(row.transform, "Name", key, 14f,
                                         sprite != null ? MenuKit.Body : new Color(1f, 0.45f, 0.4f));
                label.alignment = TextAlignmentOptions.MidlineLeft;
                Place(label.rectTransform, IconSize + 10f, -(RowHeight - 20f) / 2f, Width - Pad * 2f - IconSize - 14f, 20f);

                y += RowHeight;
            }
            _contentHeight = y;
            _content.sizeDelta = new Vector2(0f, _contentHeight);

            var hint = MenuKit.Text(_root.transform, "Hint",
                drawn == keys.Count
                    ? "wheel to scroll · Esc to close · the name is what a catalog line or Hidden Icons wants"
                    : $"{keys.Count - drawn} of these have no sprite in this game · wheel to scroll · Esc to close",
                12f, MenuKit.Dim);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            Place(hint.rectTransform, Pad, -(Height - HintHeight - 2f), Width - Pad * 2f, HintHeight);
            return keys.Count;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }

        /// <summary>The wheel value the camera would have used, captured before it was zeroed.</summary>
        internal static void NoteWheel(float value)
        {
            if (value != 0f) _wheel = value;
        }

        internal static void Update()
        {
            if (_root == null) return;
            if (ZInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            float wheel = _wheel;
            _wheel = 0f;
            if (Mathf.Abs(wheel) < 0.001f || _content == null || _viewport == null) return;
            _scroll -= wheel * 120f;
            float most = Mathf.Max(0f, _contentHeight - _viewport.rect.height);
            _scroll = Mathf.Clamp(_scroll, 0f, most);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }
    }

    // The wheel belongs to this panel while it is open, for the same reason it belongs to the
    // legend: the camera reads it straight from ZInput and would zoom the view behind the panel.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel_Pictures_Patch
    {
        private static void Postfix(ref float __result)
        {
            if (__result == 0f || !PicturePanel.IsOpen) return;
            PicturePanel.NoteWheel(__result);
            __result = 0f;
        }
    }
}
