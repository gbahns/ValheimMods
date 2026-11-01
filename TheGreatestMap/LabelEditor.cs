using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TheGreatestMap
{
    /// <summary>
    /// A box for typing a marker's name, opened from the marker's own menu.
    ///
    /// <para>Most markers carry no name, on purpose: the picture says what a berry bush is, and a
    /// map with a thousand names written across it is a wall of words over the terrain. But the
    /// names a player wants are the ones nothing can work out -- "good fishing", "Marco's base",
    /// "the tin we never finished" -- and until now the only way to get one onto a recorded marker
    /// was to let the recorder write it, which means accepting whatever the catalog calls the
    /// thing.</para>
    ///
    /// <para>A box of its own rather than a row that turns into a field, because the marker menu
    /// is built once and read once: it has no redraw-in-place, and giving it one for a single row
    /// is more machinery than a small window.</para>
    ///
    /// <para>The name travels like any other change to a marker, so what you type is on everybody
    /// else's map at the next sync -- which is the point, since the names worth typing are the
    /// ones you would otherwise have to say out loud.</para>
    /// </summary>
    internal static class LabelEditor
    {
        private const float Width = 300f;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 26f;
        private const float Pad = 4f;

        private static GameObject _root;
        private static RectTransform _rect;
        private static Camera _camera;
        private static TMP_InputField _field;
        private static PinKey _editing;
        private static int _openedFrame = -10;

        internal static bool IsOpen => _root != null;

        /// <summary>
        /// True while the box has the keyboard. Every hotkey in this mod is gated on it, and the
        /// game's own are too by way of the patch below -- otherwise naming a marker "bed" takes
        /// the map out and lies down in it.
        /// </summary>
        internal static bool Typing => _field != null && _field.isFocused;

        internal static bool Hovered =>
            _root != null && _rect != null
            && RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera);

        internal static void Open(SharedPin pin, Vector2 screenPos)
        {
            Close();
            var map = Minimap.instance;
            if (map == null || pin == null || map.m_largeRoot == null) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;
            _editing = pin.Key;

            var panel = MenuKit.Panel(parent, "TGM_LabelEditor");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);

            float y = -Pad;
            var header = MenuKit.Text(_root.transform, "Header",
                string.IsNullOrEmpty(pin.Name) ? "Name this marker" : "Rename this marker", 15f, MenuKit.Header);
            Place(header.rectTransform, 10f, y, Width - 20f, HeaderHeight);
            header.alignment = TextAlignmentOptions.MidlineLeft;
            y -= HeaderHeight;

            BuildField(pin.Name ?? "", 0f, y);
            y -= RowHeight;

            var hint = MenuKit.Text(_root.transform, "Hint",
                "Enter to keep it, Escape to leave it be. Empty takes the name off.", 12f, MenuKit.Dim);
            Place(hint.rectTransform, 10f, y, Width - 20f, RowHeight);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            y -= RowHeight;

            float height = -y + Pad;
            _rect.sizeDelta = new Vector2(Width, height);

            var canvas = parent.GetComponentInParent<Canvas>();
            _camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.rootCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, _camera, out var local);
            var pr = parent.rect;
            float x = local.x - pr.xMin;
            float top = pr.yMax - local.y;
            if (x + Width > pr.width) x = Mathf.Max(0f, pr.width - Width);
            if (top + height > pr.height) top = Mathf.Max(0f, pr.height - height);
            _rect.anchoredPosition = new Vector2(x, -top);
            _root.transform.SetAsLastSibling();
            _openedFrame = Time.frameCount;
            // Focused the moment it opens: somebody who asked to name a marker is going to type.
            _field.ActivateInputField();
            _field.caretPosition = _field.text.Length;
        }

        private static void BuildField(string text, float x, float y)
        {
            var go = new GameObject("Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(_root.transform, false);
            Place((RectTransform)go.transform, x + 8f, y - 2f, Width - 16f, RowHeight - 6f);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);

            var shown = MenuKit.Text(go.transform, "Text", "", 13f, MenuKit.Body);
            shown.alignment = TextAlignmentOptions.MidlineLeft;
            Fill(shown.rectTransform);

            var hint = MenuKit.Text(go.transform, "Hint", "a name for this one...", 13f, MenuKit.Dim);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            Fill(hint.rectTransform);

            _field = go.GetComponent<TMP_InputField>();
            _field.textViewport = (RectTransform)go.transform;
            _field.textComponent = shown;
            _field.placeholder = hint;
            _field.lineType = TMP_InputField.LineType.SingleLine;
            // TextMeshPro draws its caret in its own dark default and ignores caretColor until it
            // is told to use a custom one, which on a dark panel leaves you typing blind.
            _field.customCaretColor = true;
            _field.caretColor = new Color(1f, 0.85f, 0.45f, 1f);
            _field.caretWidth = 2;
            _field.caretBlinkRate = 0.8f;
            _field.selectionColor = new Color(1f, 0.85f, 0.45f, 0.45f);
            _field.characterLimit = 64;
            _field.SetTextWithoutNotify(text);
            _field.onSubmit.AddListener(Commit);
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 0f);
            rt.offsetMax = new Vector2(-6f, 0f);
        }

        private static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void Commit(string text)
        {
            var id = _editing;
            Close();
            if (id.IsNone) return;
            string name = (text ?? "").Trim();
            if (!ClientPins.TryGet(id, out var shared)) return;
            if (string.Equals(shared.Name ?? "", name, System.StringComparison.Ordinal)) return;
            ClientPins.RenameMarker(id, name);
            ClientPins.Restyle();
            TheGreatestMapMod.Message(name.Length == 0
                ? "Name taken off the marker."
                : $"Marker named \"{name}\".");
        }

        internal static void Close()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _rect = null;
            _field = null;
            _editing = default(PinKey);
        }

        internal static void Update()
        {
            if (_root == null) return;
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large) { Close(); return; }
            // Escape leaves what was there alone, which is what Escape means in a text field
            // everywhere else. The field eats the key while it has focus, so this sees it on the
            // way out and closes the box the player has just stepped out of.
            if (ZInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.frameCount - _openedFrame <= 1) return;   // the click that opened it
            // A click in the box lands inside the panel, so typing never closes this.
            if (ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1))
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera)) Close();
        }
    }

    /// <summary>
    /// While the box has the keyboard, the game is told a text input is up, which is how it
    /// already knows to leave its own keys alone. The same trick the find box and the legend use,
    /// for the same reason: every letter typed into a name is also a hotkey.
    /// </summary>
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class TextInput_IsVisible_LabelEditor_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (LabelEditor.Typing) __result = true;
        }
    }
}
