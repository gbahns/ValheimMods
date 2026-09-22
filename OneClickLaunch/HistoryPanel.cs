using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OneClickLaunch
{
    /// <summary>
    /// The whole history as a scrollable list over the main menu: every remembered pair, a
    /// click to launch, a small button to forget one. Opened by right-clicking any of the
    /// Continue buttons, or by the optional More button. Built from the menu's own button so
    /// it keeps the game's font and style; the window background is borrowed from the vanilla
    /// start-game panel.
    /// </summary>
    internal static class HistoryPanel
    {
        private const float WindowWidth = 560f;
        private const float RowHeight   = 40f;
        private const float RowSpacing  = 6f;
        private const float RowFontSize = 22f;
        private const float TopPad      = 64f;   // the title
        private const float BottomPad   = 20f;
        private const float SidePad     = 24f;

        private static GameObject    s_panel;
        private static FejdStartup   s_fs;
        private static Button        s_template;
        private static RectTransform s_window;
        private static RectTransform s_content;
        private static ScrollRect    s_scroll;

        internal static bool IsOpen => s_panel != null && s_panel.activeSelf;

        internal static void Toggle(FejdStartup fs, Button template)
        {
            if (IsOpen) Close();
            else Open(fs, template);
        }

        internal static void Open(FejdStartup fs, Button template)
        {
            if (fs == null || template == null || UnifiedPopup.IsVisible()) return;
            try
            {
                if (s_panel == null || s_fs != fs)
                {
                    s_fs = fs;
                    s_template = template;
                    Build(fs);
                }
                Fill();
                s_panel.transform.SetAsLastSibling();
                s_panel.SetActive(true);
            }
            catch (Exception ex)
            {
                OneClickLaunchMod.Log.LogError($"Could not open the history panel: {ex}");
            }
        }

        internal static void Close()
        {
            if (s_panel != null) s_panel.SetActive(false);
        }

        /// <summary>Redraw the rows if the panel is open, after a setting changed.</summary>
        internal static void Refresh()
        {
            if (IsOpen) Fill();
        }

        // ---------------------------------------------------------------------- build ----

        private static void Build(FejdStartup fs)
        {
            // Under the main menu object, so it hides and fades with the menu, above everything
            // in it. The blocker covers the screen, dims it, and closes the panel when clicked.
            Transform parent = fs.m_mainMenu != null ? fs.m_mainMenu.transform : fs.m_menuList.transform.parent;
            s_panel = new GameObject("OneClickLaunch_History", typeof(RectTransform), typeof(Image), typeof(ClickRelay), typeof(EscapeCloses));
            s_panel.transform.SetParent(parent, false);
            Stretch(s_panel.transform as RectTransform);
            var blocker = s_panel.GetComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.6f);
            blocker.raycastTarget = true;
            s_panel.GetComponent<ClickRelay>().OnClick = Close;

            var window = new GameObject("Window", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(s_panel.transform, false);
            s_window = window.transform as RectTransform;
            s_window.anchorMin = s_window.anchorMax = new Vector2(0.5f, 0.5f);
            s_window.pivot = new Vector2(0.5f, 0.5f);
            s_window.sizeDelta = new Vector2(WindowWidth, TopPad + RowHeight + BottomPad);
            var windowImage = window.GetComponent<Image>();
            windowImage.raycastTarget = true;   // swallows clicks so they do not reach the blocker
            CopyBackground(windowImage, fs);

            TMP_Text templateLabel = s_template.GetComponentInChildren<TMP_Text>(true);

            var title = MakeText(window.transform, "Title", "Remembered games", templateLabel, 28f);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -16f);
            titleRect.sizeDelta = new Vector2(-2f * SidePad - 40f, 40f);
            title.alignment = TextAlignmentOptions.Center;

            // An x in the corner closes it, in the game's own button style (the same look
            // Jotunn gives the close buttons in Armory), without needing Jotunn for it.
            MakeValheimButton(window.transform, "CloseX", "x", new Vector2(-32f, -32f), 36f, 36f, Close);

            // The list: a ScrollRect whose content grows with its rows, with a scrollbar that
            // only appears when the rows outgrow the window.
            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scroll.transform.SetParent(window.transform, false);
            var scrollRect = scroll.transform as RectTransform;
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(SidePad, BottomPad);
            scrollRect.offsetMax = new Vector2(-SidePad, -TopPad);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scroll.transform, false);
            Stretch(viewport.transform as RectTransform);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            s_content = content.transform as RectTransform;
            s_content.anchorMin = new Vector2(0f, 1f);
            s_content.anchorMax = new Vector2(1f, 1f);
            s_content.pivot = new Vector2(0.5f, 1f);
            s_content.anchoredPosition = Vector2.zero;
            s_content.sizeDelta = new Vector2(0f, 0f);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            bar.transform.SetParent(scroll.transform, false);
            var barRect = bar.transform as RectTransform;
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 1f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(8f, 0f);
            bar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(bar.transform, false);
            Stretch(handle.transform as RectTransform);
            Color accent = MenuPatches.ButtonColor();
            handle.GetComponent<Image>().color = new Color(accent.r, accent.g, accent.b, 0.7f);
            var scrollbar = bar.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle.transform as RectTransform;
            scrollbar.targetGraphic = handle.GetComponent<Image>();

            s_scroll = scroll.GetComponent<ScrollRect>();
            s_scroll.viewport = viewport.transform as RectTransform;
            s_scroll.content = s_content;
            s_scroll.horizontal = false;
            s_scroll.vertical = true;
            s_scroll.movementType = ScrollRect.MovementType.Clamped;
            s_scroll.scrollSensitivity = 40f;
            s_scroll.verticalScrollbar = scrollbar;
            s_scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            s_scroll.verticalScrollbarSpacing = 6f;

            s_panel.SetActive(false);
        }

        private static void Fill()
        {
            for (int i = s_content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(s_content.GetChild(i).gameObject);

            Color color = MenuPatches.ButtonColor();
            string dim = ColorUtility.ToHtmlStringRGB(color) + "99";
            SizeWindow(Mathf.Max(1, History.Entries.Count));
            if (History.Entries.Count == 0)
            {
                TMP_Text templateLabel = s_template.GetComponentInChildren<TMP_Text>(true);
                var empty = MakeText(s_content, "Empty", "Nothing remembered yet. Play once and it will be here.", templateLabel, RowFontSize);
                empty.alignment = TextAlignmentOptions.Center;
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
                return;
            }

            foreach (Entry entry in History.Entries)
            {
                Entry captured = entry;
                var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(s_content, false);
                var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 8f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = true;
                row.GetComponent<LayoutElement>().preferredHeight = RowHeight;

                // TMP's alpha tag has no closing form (a literal "</alpha>" shows), so the
                // dimmer "ago" is a nested color tag with its own alpha instead.
                string text = $"{captured.Label()}   <size=70%><color=#{dim}>{Ago(captured.lastPlayedUtcTicks)}</color></size>";
                Button launch = MenuPatches.MakeButton(s_template, "Launch", text, () =>
                {
                    Close();
                    MenuPatches.Launch(s_fs, captured);
                }, color);
                launch.transform.SetParent(row.transform, false);
                var launchElement = launch.gameObject.AddComponent<LayoutElement>();
                launchElement.flexibleWidth = 1f;
                launchElement.preferredHeight = RowHeight;
                SetFontSize(launch, RowFontSize);
                TMP_Text launchLabel = launch.GetComponentInChildren<TMP_Text>(true);
                if (launchLabel != null) launchLabel.alignment = TextAlignmentOptions.MidlineLeft;

                // A small red x forgets the pair.
                Button forget = MakeValheimButton(row.transform, "Forget", "x", Vector2.zero, 28f, 28f, () =>
                {
                    History.Remove(captured);
                    MenuPatches.Rebuild(s_fs);
                    Fill();
                }, framed: false, textColor: new Color(0.9f, 0.2f, 0.15f, 1f), fontSize: 24);
                var forgetElement = forget.gameObject.AddComponent<LayoutElement>();
                forgetElement.preferredWidth = 28f;
                forgetElement.preferredHeight = RowHeight;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(s_content);
            if (s_scroll != null) s_scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>Tall enough for every row, up to most of the screen; past that the list scrolls.</summary>
        private static void SizeWindow(int rows)
        {
            float rowsHeight = rows * RowHeight + (rows - 1) * RowSpacing;
            float wanted = TopPad + rowsHeight + BottomPad;
            Canvas canvas = s_panel.GetComponentInParent<Canvas>();
            RectTransform root = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            float max = root != null ? root.rect.height * 0.85f : 900f;
            s_window.sizeDelta = new Vector2(WindowWidth, Mathf.Min(wanted, max));
        }

        // -------------------------------------------------------------------- helpers ----

        private static string Ago(long utcTicks)
        {
            if (utcTicks <= 0) return "";
            TimeSpan span = DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc);
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 2) return "yesterday";
            if (span.TotalDays < 14) return $"{(int)span.TotalDays} days ago";
            if (span.TotalDays < 60) return $"{(int)(span.TotalDays / 7)} weeks ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} months ago";
            return "over a year ago";
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, string text, TMP_Text like, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (like != null)
            {
                tmp.font = like.font;
                tmp.fontSharedMaterial = like.fontSharedMaterial;
                tmp.color = like.color;
            }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.enableAutoSizing = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>
        /// A button the way Jotunn's GUIManager.CreateButton makes one: the game's "button"
        /// sprite from its UI atlas, sliced, the AveriaSerif bold font in Valheim orange with a
        /// black outline, and Jotunn's hover and press tints. Anchored to the parent's top-right
        /// with the position measured to the button's center, like Jotunn's.
        /// </summary>
        private static Button MakeValheimButton(Transform parent, string name, string text, Vector2 position, float width, float height,
            UnityEngine.Events.UnityAction onClick, bool framed = true, Color? textColor = null, int fontSize = 16)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, height);

            var image = go.GetComponent<Image>();
            Sprite sprite = framed ? FindSprite("button") : null;
            if (!framed)
            {
                // No frame: an invisible plate that still takes the click, with the text tinted.
                image.color = Color.clear;
            }
            else if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 2f;   // what Jotunn uses for the start menu
            }
            else
            {
                image.color = new Color(0.25f, 0.2f, 0.15f, 0.9f);
            }

            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor      = new Color(0.824f, 0.824f, 0.824f, 1f),
                highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f),
                pressedColor     = new Color(0.537f, 0.556f, 0.556f, 1f),
                selectedColor    = new Color(0.824f, 0.824f, 0.824f, 1f),
                disabledColor    = new Color(0.566f, 0.566f, 0.566f, 0.502f),
                colorMultiplier  = 1f,
                fadeDuration     = 0.1f,
            };
            button.onClick.AddListener(onClick);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.transform as RectTransform);
            var label = textGo.GetComponent<Text>();
            label.text = text;
            label.font = FindFont("AveriaSerifLibre-Bold") ?? FindFont("AveriaSerifLibre-Regular") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = fontSize;
            label.color = textColor ?? new Color(1f, 0.631f, 0.235f, 1f);   // Jotunn's ValheimOrange
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            textGo.GetComponent<Outline>().effectColor = Color.black;
            // The frame takes the hover tint when there is one; otherwise the text does.
            button.targetGraphic = framed ? (Graphic)image : label;
            return button;
        }

        private static Sprite FindSprite(string name)
        {
            foreach (var atlas in Resources.FindObjectsOfTypeAll<UnityEngine.U2D.SpriteAtlas>())
            {
                if (atlas == null || atlas.name != "UIAtlas") continue;
                Sprite sprite = atlas.GetSprite(name);
                if (sprite != null) return sprite;
            }
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && sprite.name == name) return sprite;
            OneClickLaunchMod.Log.LogInfo($"Sprite {name} not found; the close button gets a plain background.");
            return null;
        }

        private static Font FindFont(string name)
        {
            foreach (Font font in Resources.FindObjectsOfTypeAll<Font>())
                if (font != null && font.name == name) return font;
            return null;
        }

        private static void SetFontSize(Button button, float size)
        {
            foreach (TMP_Text t in button.GetComponentsInChildren<TMP_Text>(true))
            {
                t.enableAutoSizing = false;
                t.fontSize = size;
            }
        }

        /// <summary>Borrow the look of a vanilla panel for the window background.</summary>
        private static void CopyBackground(Image target, FejdStartup fs)
        {
            Image source = null;
            if (fs.m_startGamePanel != null)
            {
                source = fs.m_startGamePanel.GetComponent<Image>();
                if (source == null || source.sprite == null)
                {
                    Image first = null;
                    foreach (Image img in fs.m_startGamePanel.GetComponentsInChildren<Image>(true))
                    {
                        if (img.sprite == null) continue;
                        string n = img.name.ToLowerInvariant();
                        if (n.Contains("bkg") || n.Contains("background") || n.Contains("panel"))
                        {
                            source = img;
                            break;
                        }
                        if (first == null) first = img;
                    }
                    if (source == null || source.sprite == null) source = first;
                }
            }
            if (source != null && source.sprite != null)
            {
                target.sprite = source.sprite;
                target.type = source.type;
                target.material = source.material;
                target.color = source.color;
                target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
                OneClickLaunchMod.Log.LogInfo($"History panel background borrowed from {source.transform.parent?.name}/{source.name} ({source.sprite.name}).");
            }
            else
            {
                target.sprite = null;
                target.color = new Color(0.09f, 0.07f, 0.05f, 0.96f);
                OneClickLaunchMod.Log.LogInfo("History panel background: no vanilla panel image found, using a plain one.");
            }
        }

        /// <summary>Runs an action on a left click; used for the blocker that closes the panel.</summary>
        private sealed class ClickRelay : MonoBehaviour, IPointerClickHandler
        {
            public Action OnClick;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left) OnClick?.Invoke();
            }
        }

        private sealed class EscapeCloses : MonoBehaviour
        {
            private void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape)) HistoryPanel.Close();
            }
        }
    }

    /// <summary>
    /// Our color while the button rests, the game's own while it is hovered or selected. The
    /// tag is applied as rich text, which the button's hover animation cannot override, so it
    /// has to come off for the animation's color to show, and go back on afterwards.
    /// </summary>
    internal sealed class RestingTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public TMP_Text Label;
        public string Plain;
        public string Colored;

        private bool _hovered;
        private bool _selected;

        public void Apply(bool hovered)
        {
            if (Label != null) Label.text = hovered ? Plain : Colored;
        }

        private void Refresh() => Apply(_hovered || _selected);

        public void OnPointerEnter(PointerEventData eventData) { _hovered = true;  Refresh(); }
        public void OnPointerExit(PointerEventData eventData)  { _hovered = false; Refresh(); }
        public void OnSelect(BaseEventData eventData)          { _selected = true;  Refresh(); }
        public void OnDeselect(BaseEventData eventData)        { _selected = false; Refresh(); }
    }

    /// <summary>A right click on a menu button; Button.onClick only fires for the left one.</summary>
    internal sealed class RightClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public Action OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke();
        }
    }
}
