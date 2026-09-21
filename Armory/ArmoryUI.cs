using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Armory
{
    /// <summary>
    /// Canvas-based loadout management UI for the Armory Rack.
    ///
    /// Replaces the prior IMGUI implementation so the panel reads as a native Valheim UI —
    /// wood-panel background, Valheim's serif font, gold accent text, real buttons that
    /// hover and click like the build menu / inventory.
    /// </summary>
    internal static class ArmoryUI
    {
        // ── State ──────────────────────────────────────────────────────────────────

        private static ArmoryRack _rack;
        private static ArmoryData _data;
        private static bool       _isOpen;
        public  static bool       IsOpen      => _isOpen;
        public  static ArmoryRack CurrentRack => _rack;

        // True while an in-place name edit is active.  Used by Chat.HasFocus patch so Valheim
        // routes keystrokes only to our InputField and not to game shortcuts.
        public static bool IsEditing => _isOpen && _editingSlot >= 0;

        // Remembered panel positions across opens (within the game session).
        private static Vector2? _savedMainPos;
        private static Vector2? _savedComparePos;

        // Vanilla slides the inventory + container panels into place with an Animator on the
        // InventoryGui object (the "visible" bool it sets in Show/Hide).  An Animator left on
        // its default update mode advances on Time.deltaTime, so pausing freezes that slide on
        // its first frame: the player grid stays parked off the top of the screen and the
        // container panel lands on top of it.  While we hold the pause, run that one Animator
        // on unscaled time so the panels still reach their resting position.
        private static Animator           _invAnimator;
        private static AnimatorUpdateMode _invAnimatorMode;

        // Throttle icon-availability re-checks (rebuilding the strip on every frame is wasteful).
        private static float _lastIconRefresh;

        private const int   DefaultSlotCount    = 5;
        private const int   MaxSlotCount        = 10;
        private const float MaxInteractDistance = 5f;

        private static int _editingSlot = -1;
        private static int _compareSlot = -1;

        // ── UI roots ───────────────────────────────────────────────────────────────

        private static GameObject _mainPanel;
        private static GameObject _comparePanel;
        private static GameObject _confirmModal;
        private static RectTransform _slotsContent;
        private static readonly List<SlotRowRefs> _rows = new List<SlotRowRefs>();

        // Rows showing every saved cell, in the loadout or not, so cells can be added to it.
        // By default a row shows only what it loads.  Session state, keyed by the slot object.
        private static readonly HashSet<LoadoutSlot> _expanded = new HashSet<LoadoutSlot>();

        private class SlotRowRefs
        {
            public GameObject  Root;
            public GameObject  NameLabelGo;   // always set — even if Text lookup failed
            public Text        NameLabel;
            public GameObject  NameInputGo;
            public InputField  NameInput;
            public Button      LoadButton;
            public Text        LoadButtonLabel;
            public Color       LoadButtonDefaultColor;
            public Button      CompareButton;
            public Button      SlotsButton;       // "+" / "-": show every saved cell, or only the loaded ones
            public Text        SlotsButtonLabel;
            public Button      MenuButton;        // menu mode: the one button that holds the others
            public Button      ClearButton;
            public Text        Summary;
            public GameObject  IconsContainer;  // re-populated on data change
            public readonly List<IconCell> IconCells = new List<IconCell>();  // recolored in place as items move
        }

        // One cell of a row's icon strip: enough to recolor it in place when items move between
        // the player and the rack, without rebuilding the strip (and the toggle buttons in it)
        // under the cursor.
        private class IconCell
        {
            public Image     Bg;
            public SavedItem Item;
            public bool      Included;
        }

        // Save/Load button glyphs.  BMP arrows (U+2193 / U+2191) — these are in AveriaSerifBold,
        // unlike the floppy emoji (U+1F4BE) which renders as an empty rectangle.  Down = storing
        // current gear into the slot; up = lifting saved gear out onto the player.
        private const string SaveIcon = "↓";
        private const string LoadIcon = "↑";

        // Panel-sizing constants used by ComputePanelHeight + ResizeMainPanel.
        private const float TitleAreaPx  = 70f;   // title + close button strip at top
        private const float FooterAreaPx = 70f;   // add-slot button + margin at bottom

        // Per-row sub-section heights (in pixels) — must mirror what BuildSlotRow actually
        // creates, otherwise ComputePanelHeight under/over-counts and the scroll view shows.
        private const float TopRowPx       = 48f;        // name + buttons strip
        private const float SummaryRowPx   = 22f;        // single line of summary text
        private const float IconHeaderPx   = 16f;        // category label (and group switch) above each icon group
        private const float IconLinePx     = IconHeaderPx + 2f + IconSize + 4f;  // header + gap + cells + line padding
        private const float IconLineGapPx  = 2f;         // between the worn line and the consumables line
        private const float RowPaddingPx   = 8f;         // VLG vertical padding (4 top + 4 bottom)
        private const float RowSpacingPx   = 2f;         // VLG spacing between adjacent visible children
        private const float ScrollListPadPx = 8f;        // content VLG vertical padding (4 top + 4 bottom)
        private const float ScrollListGapPx = 4f;        // content VLG spacing between rows

        // Track previous toggle values so Tick can detect a config flip and trigger a resize.
        private static bool? _lastShowSummary;
        private static bool? _lastShowIcons;
        private static bool? _lastShowButtons;

        // Row controls: five buttons on every row, or one menu button that holds them.  The
        // menu takes a quarter of the room, which is what lets the panel go narrow.
        private static bool ShowRowButtons => ArmoryMod.ShowRowButtons?.Value ?? false;

        // Tracked so ResizeMainPanel and the grip can update the scroll view too.  Jotunn builds
        // it as fixed-size layers — root, "Scroll View", viewport, content, scrollbar — none of
        // which stretch with its parent, so each has to be resized by hand.
        private static RectTransform _scrollRT;
        private static ScrollRect    _scrollRect;
        private static RectTransform _scrollViewRT;
        private static RectTransform _viewportRT;
        private static RectTransform _vScrollbarRT;
        private static RectTransform _vSlidingAreaRT;
        private const  float ScrollHandlePx = 10f;   // handleSize passed to CreateScrollView
        private const  float ScrollBorderPx = 4f;    // handleDistanceToBorder passed to CreateScrollView

        // Personal/Shared toggle in the title bar; label and interactability follow the rack.
        private static Button _privacyButton;
        private static Text   _privacyLabel;

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Called from ArmoryOpenPatch once vanilla has opened the rack's container, never from
        /// the keypress.  By this point the ownership exchange has happened and InventoryGui is
        /// already up; all that is left is to put the loadout panel beside it.
        /// </summary>
        public static void Open(ArmoryRack rack)
        {
            // Tear our own panel down without hiding the inventory — vanilla has just shown it.
            if (_isOpen) CloseInternal(hideInventory: false);

            _rack = rack;
            _data = rack.GetData();
            // A rack that has never been saved starts with a few empty slots to fill.  One that
            // has keeps exactly the slots it was left with — deleting the last three is a
            // choice, not something to undo on the next open.
            if (!_data.Saved) LoadoutManager.EnsureSlots(_data, DefaultSlotCount);
            Jotunn.Logger.LogInfo($"[Armory] Open: rack instance {rack.GetInstanceID()}, slots loaded={_data.Slots.Count}");
            for (int i = 0; i < _data.Slots.Count; i++)
                Jotunn.Logger.LogInfo($"[Armory]   slot {i}: name='{_data.Slots[i].Name}' empty={_data.Slots[i].IsEmpty()}");
            _editingSlot = -1;
            _compareSlot = -1;
            _isOpen      = true;

            BuildUI();

            // Migrate items left out-of-bounds by a previous container size (e.g. the brief
            // 8×5 layout — anything at y=4 is now outside our 10×4 grid and would be hidden
            // from the vanilla chest UI).  Move them into the first empty in-bounds slot.  The
            // grid is rebuilt from the inventory every frame in InventoryGui.UpdateContainer,
            // which has not run yet this frame, so the player still sees a clean grid.
            LoadoutManager.MigrateOutOfBoundsItems(rack.GetStorageInventory());

            // The optional pause (config-gated, default off) is ArmoryPause's business: the mod's
            // Update calls its Refresh, which picks the panel up on the next frame and asks
            // vanilla for the pause rather than writing Time.timeScale behind its back.
        }

        /// <summary>Close the panel and the inventory with it — the close button and Tick.</summary>
        public static void Close() => CloseInternal(hideInventory: true);

        private static void CloseInternal(bool hideInventory)
        {
            CloseRowMenu();
            _isOpen      = false;
            _editingSlot = -1;
            _compareSlot = -1;

            // Capture panel positions before destroying so the next Open() can restore them.
            // Also write the main panel position to config so it persists across game sessions.
            if (_mainPanel != null)
            {
                var rt = _mainPanel.GetComponent<RectTransform>();
                if (rt != null)
                {
                    _savedMainPos = rt.anchoredPosition;
                    if (ArmoryMod.PanelPosX != null) ArmoryMod.PanelPosX.Value = rt.anchoredPosition.x;
                    if (ArmoryMod.PanelPosY != null) ArmoryMod.PanelPosY.Value = rt.anchoredPosition.y;
                }
                Object.Destroy(_mainPanel);
                _mainPanel = null;
            }
            if (_tooltipGo != null) { Object.Destroy(_tooltipGo); _tooltipGo = null; _tooltipText = null; _tooltipRT = null; }
            CloseConfirmModal();
            if (_comparePanel != null)
            {
                var rt = _comparePanel.GetComponent<RectTransform>();
                if (rt != null) _savedComparePos = rt.anchoredPosition;
                Object.Destroy(_comparePanel);
                _comparePanel = null;
            }
            _slotsContent  = null;
            _privacyButton = null;
            _privacyLabel  = null;
            _rows.Clear();
            _lastShowSummary = null;
            _lastShowIcons   = null;

            _rack = null;

            if (hideInventory && InventoryGui.instance != null)
                InventoryGui.instance.Hide();

            // Let go of the pause here rather than waiting for the next Refresh, so the world
            // resumes on the same frame the panel goes away.
            ArmoryPause.Release();
        }

        /// <summary>
        /// Switch InventoryGui's Animator between unscaled and normal time.  Vanilla caches it
        /// with GetComponent&lt;Animator&gt;() on the same object, so we can reach it the same way
        /// rather than through the private m_animator field.  Restores the mode we found rather
        /// than assuming Normal, in case another mod got there first.
        /// </summary>
        internal static void SetInventoryGuiUnscaled(bool unscaled)
        {
            if (unscaled)
            {
                _invAnimator = InventoryGui.instance != null
                    ? InventoryGui.instance.GetComponent<Animator>()
                    : null;
                if (_invAnimator == null)
                {
                    Jotunn.Logger.LogWarning("[Armory] No Animator on InventoryGui — the inventory panels may sit in the wrong place while paused.");
                    return;
                }
                _invAnimatorMode          = _invAnimator.updateMode;
                _invAnimator.updateMode   = AnimatorUpdateMode.UnscaledTime;
            }
            else if (_invAnimator != null)
            {
                _invAnimator.updateMode = _invAnimatorMode;
                _invAnimator            = null;
            }
        }

        public static void Tick()
        {
            TickToast();
            if (!_isOpen) return;

            TickWheel();

            // An open row menu closes on Esc or on a click anywhere outside it.
            if (_menuGo != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { CloseRowMenu(); return; }
                if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                    && !RectTransformUtility.RectangleContainsScreenPoint(_menuRT, Input.mousePosition, MenuCamera()))
                    CloseRowMenu();
            }

            // Delete-confirm modal eats Esc first.  Doesn't interact with the InventoryGui
            // watch below — the modal is one of our own GameObjects.
            if (_confirmModal != null && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseConfirmModal();
                return;
            }

            // Mirror the vanilla InventoryGui's visibility.  When the player presses Tab (the
            // inventory-toggle hotkey) or Esc, Valheim closes InventoryGui itself — if we kept
            // our panel up we'd be orphaned, AND if we *also* handled Esc here we'd race vanilla
            // and end up opening the pause menu the same frame.  Letting vanilla own Esc and just
            // following IGui closes both UIs together with the right key semantics for free.
            if (InventoryGui.instance == null || !InventoryGui.IsVisible())
            {
                Close();
                return;
            }

            // Enter while editing a slot name commits the rename.
            if (_editingSlot >= 0 &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                CommitPendingEdit();
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null || _rack == null) { Close(); return; }

            if (Vector3.Distance(player.transform.position, _rack.transform.position) > MaxInteractDistance)
            {
                player.Message(MessageHud.MessageType.Center, "Moved too far from the Armory Rack.");
                Close();
                return;
            }

            // Refresh per-row state (Active button, empty disabled state, summary text colors)
            // so the UI updates live when the player equips/unequips items via the inventory
            // or drags items in/out of the rack while the panel is open.  Also re-evaluate
            // each row's icon availability colors and apply the show/hide config toggles.
            bool showSummary = ArmoryMod.ShowSummaryText?.Value ?? true;
            bool showIcons   = ArmoryMod.ShowIcons?.Value       ?? true;

            // If either toggle flipped since last frame, the per-row preferred height changed —
            // re-run the panel sizing so the scroll view either stops scrolling or reclaims
            // the now-unused vertical space.
            if (_lastShowSummary != showSummary || _lastShowIcons != showIcons)
            {
                _lastShowSummary = showSummary;
                _lastShowIcons   = showIcons;
                ResizeMainPanel();
            }
            if (_lastShowButtons != ShowRowButtons)
            {
                _lastShowButtons = ShowRowButtons;
                RebuildSlotRows();
            }

            for (int i = 0; i < _rows.Count && i < _data.Slots.Count; i++)
            {
                var refs = _rows[i];
                var slot = _data.Slots[i];
                UpdateRowEmptyState(refs, slot);
                if (refs.Summary != null)
                {
                    refs.Summary.text = BuildSummary(slot);
                    if (refs.Summary.gameObject.activeSelf != showSummary)
                        refs.Summary.gameObject.SetActive(showSummary);
                }
                if (refs.IconsContainer != null && refs.IconsContainer.activeSelf != showIcons)
                    refs.IconsContainer.SetActive(showIcons);
            }

            // Refresh icon availability colors a few times a second (not every frame).  Only the
            // cell colors change — the strip itself is rebuilt on data changes, never on a timer,
            // so the switches in it stay put under the cursor between press and release.
            if (showIcons && Time.unscaledTime - _lastIconRefresh > 0.5f)
            {
                _lastIconRefresh = Time.unscaledTime;
                for (int i = 0; i < _rows.Count && i < _data.Slots.Count; i++)
                    RefreshIconColors(_rows[i]);
            }
        }

        // ── UI construction ────────────────────────────────────────────────────────

        private static void BuildUI()
        {
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;
            var parent = GUIManager.CustomGUIFront.transform;
            _lastShowButtons = ShowRowButtons;

            // Main panel — right of screen center so the vanilla inventory can occupy the left.
            // Use the remembered position if the user dragged it earlier this session; else fall
            // back to the persisted config (which survives across game sessions).
            var mainPos = _savedMainPos ?? new Vector2(
                ArmoryMod.PanelPosX?.Value ?? 380f,
                ArmoryMod.PanelPosY?.Value ?? 0f);
            _mainPanel = GUIManager.Instance.CreateWoodpanel(
                parent:    parent,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position:  mainPos,
                width:     PanelWidth,
                height:    PanelHeightOrAuto(),
                draggable: true);

            BuildMainPanel(_mainPanel.transform);
        }

        private static void BuildMainPanel(Transform panel)
        {
            // Title text, centered over the panel.
            var titleGo = GUIManager.Instance.CreateText(
                text: _rack != null ? _rack.RackName : ArmoryRack.DefaultName,
                parent: panel,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position:  new Vector2(0f, -32f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 24,
                color: GUIManager.Instance.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: 400f,
                height: 40f,
                addContentSizeFitter: false);
            var titleTxt = titleGo.GetComponent<Text>();
            if (titleTxt != null) titleTxt.alignment = TextAnchor.MiddleCenter;

            // Close (X) button — top-right.
            var closeBtn = GUIManager.Instance.CreateButton(
                text:      "x",
                parent:    panel,
                anchorMin: new Vector2(1f, 1f),
                anchorMax: new Vector2(1f, 1f),
                position:  new Vector2(-32f, -32f),
                width:     36f,
                height:    36f);
            closeBtn.GetComponent<Button>().onClick.AddListener(Close);

            // Personal/Shared toggle — top-left, mirroring the close button.  Only the player who
            // built the rack may change it; everyone else sees which it is and cannot flip it.
            var privacyBtn = GUIManager.Instance.CreateButton(
                text:      "Shared",
                parent:    panel,
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                position:  new Vector2(66f, -32f),
                width:     100f,
                height:    36f);
            _privacyButton = privacyBtn.GetComponent<Button>();
            _privacyLabel  = privacyBtn.GetComponentInChildren<Text>();
            _privacyButton.onClick.AddListener(OnPrivacyClicked);
            AttachTooltip(privacyBtn, PrivacyTooltip);
            RefreshPrivacyButton();

            // Scrollable slot-list area.
            var scrollGo = GUIManager.Instance.CreateScrollView(
                parent:                      panel,
                showHorizontalScrollbar:     false,
                showVerticalScrollbar:       true,
                handleSize:                  ScrollHandlePx,
                handleDistanceToBorder:      ScrollBorderPx,
                handleColors:                new ColorBlock { normalColor = new Color(0.5f, 0.4f, 0.2f, 0.8f), highlightedColor = new Color(0.7f, 0.55f, 0.25f, 0.9f), pressedColor = new Color(0.4f, 0.3f, 0.15f, 1f), selectedColor = new Color(0.6f, 0.5f, 0.2f, 0.9f), disabledColor = new Color(0.3f, 0.25f, 0.15f, 0.5f), colorMultiplier = 1f, fadeDuration = 0.1f },
                slidingAreaBackgroundColor:  new Color(0.04f, 0.04f, 0.04f, 0.6f),
                width:                       PanelWidth - 60f,
                height:                      PanelHeightOrAuto() - TitleAreaPx - FooterAreaPx);
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRect.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRect.anchoredPosition = new Vector2(0f, 10f);
            _scrollRT       = scrollRect;
            _scrollViewRT   = scrollRect.Find("Scroll View") as RectTransform;
            _viewportRT     = _scrollViewRT?.Find("Viewport") as RectTransform;
            _vScrollbarRT   = _scrollViewRT?.Find("Scrollbar Vertical") as RectTransform;
            _vSlidingAreaRT = _vScrollbarRT?.Find("Sliding Area") as RectTransform;

            var scrollComp = scrollGo.GetComponentInChildren<ScrollRect>();
            if (scrollComp == null || scrollComp.content == null)
            {
                Jotunn.Logger.LogError("[Armory] CreateScrollView did not yield a ScrollRect with content.");
                return;
            }
            // The wheel is handled in Tick, one row per notch, so the ScrollRect's own
            // handling is switched off rather than tuned: its step is the wheel delta times a
            // sensitivity, and the deltas this input stack reports are tiny.
            foreach (var sr in scrollGo.GetComponentsInChildren<ScrollRect>(true))
                sr.scrollSensitivity = 0f;
            _scrollRect = scrollComp;
            var content = scrollComp.content;

            // Jotunn's CreateScrollView already attaches a layout group to the content.
            // LayoutGroup has [DisallowMultipleComponent], so trying to AddComponent another
            // returns null and breaks our setup — strip any existing one first.
            foreach (var lg in content.GetComponents<LayoutGroup>())
                Object.DestroyImmediate(lg);

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 4;
            vlg.padding                = new RectOffset(4, 4, 4, 4);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;  // size each row to its preferred height — otherwise rows stay at sizeDelta=0 and their content overlaps

            if (content.GetComponent<ContentSizeFitter>() == null)
            {
                var csf = content.gameObject.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            _slotsContent = content;

            // Footer "+ Add Slot" button.
            var addBtn = GUIManager.Instance.CreateButton(
                text:      "+ Add Slot",
                parent:    panel,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position:  new Vector2(0f, 36f),
                width:     180f,
                height:    40f);
            addBtn.GetComponent<Button>().onClick.AddListener(OnAddSlotClicked);

            BuildResizeGrip(panel);

            RebuildSlotRows();
        }

        private static string PrivacyTooltip()
        {
            if (_rack == null) return string.Empty;
            if (_rack.CreatorId == 0L)
                return "Valheim has no record of who built this rack, and its personal setting " +
                       "is enforced by comparing against the builder — so it cannot be made " +
                       "personal without locking everyone out. Build a new rack to use this.";
            if (!_rack.IsBuilder) return "Only the player who built this rack can change this.";
            return _rack.IsPrivate
                ? "Only you can open this rack. Click to share it with everyone."
                : "Anyone can open this rack. Click to make it yours alone.";
        }

        private static void OnPrivacyClicked()
        {
            if (_rack == null) return;
            // Logged unconditionally: a refused click used to leave no trace at all, which is
            // exactly the case that needed explaining when the switch appeared to do nothing.
            Jotunn.Logger.LogInfo($"[Armory] Privacy click: creator={_rack.CreatorId}, me={ArmoryRack.LocalPlayerId}, " +
                                  $"isBuilder={_rack.IsBuilder}, canChange={_rack.CanChangePrivacy}, isPrivate={_rack.IsPrivate}");
            if (!_rack.CanChangePrivacy) return;
            _rack.SetPrivate(!_rack.IsPrivate);
            RefreshPrivacyButton();
        }

        /// <summary>
        /// Label and interactability both come off the rack, so a rack someone else built reads
        /// as what it is rather than offering a control that would be refused.
        /// </summary>
        private static void RefreshPrivacyButton()
        {
            if (_privacyButton == null || _rack == null) return;
            Jotunn.Logger.LogInfo($"[Armory] Privacy state: private={_rack.IsPrivate}, creator={_rack.CreatorId}, me={ArmoryRack.LocalPlayerId}, canChange={_rack.CanChangePrivacy}");
            bool isPrivate = _rack.IsPrivate;
            bool mine      = _rack.CanChangePrivacy;

            if (_privacyLabel != null)
            {
                _privacyLabel.text  = isPrivate ? "Personal" : "Shared";
                _privacyLabel.color = isPrivate ? GUIManager.Instance.ValheimOrange : Color.white;
            }
            _privacyButton.interactable = mine;
        }

        private static void RebuildSlotRows()
        {
            if (_slotsContent == null) return;
            foreach (Transform child in _slotsContent) Object.Destroy(child.gameObject);
            _rows.Clear();
            for (int i = 0; i < _data.Slots.Count; i++)
                _rows.Add(BuildSlotRow(i));
        }

        private static SlotRowRefs BuildSlotRow(int index)
        {
            var slot   = _data.Slots[index];
            var refs   = new SlotRowRefs();

            // Row container — vertical layout (header line above summary line).
            var row = new GameObject($"SlotRow_{index}", typeof(RectTransform));
            row.transform.SetParent(_slotsContent, false);
            var vlg = row.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2;
            vlg.padding = new RectOffset(6, 6, 4, 4);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            var bg = row.AddComponent<Image>();
            bg.color = (index % 2 == 0) ? new Color(0.20f, 0.15f, 0.08f, 0.55f)
                                        : new Color(0.13f, 0.10f, 0.06f, 0.55f);

            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.minHeight = 60f;   // floor when both summary + icons are hidden
            // No explicit preferredHeight — leaving it at the default (-1) lets the row's own
            // VerticalLayoutGroup compute height from its (active) children.  Unity layout
            // groups skip inactive children, so hiding the summary or icons shrinks the row
            // automatically.

            // ── Top line: name + buttons ──
            var topRow = new GameObject("Top", typeof(RectTransform));
            topRow.transform.SetParent(row.transform, false);
            var hlg = topRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;
            topRow.AddComponent<LayoutElement>().preferredHeight = 48f;

            // Name label — clickable, enters rename mode when tapped.  No separate Rename button.
            var nameLabelPair = AddText(topRow.transform, slot.Name, 16, 200f, 44f, GUIManager.Instance.ValheimOrange, leftAlign: true, richText: true);
            refs.NameLabel   = nameLabelPair.Txt;
            refs.NameLabelGo = nameLabelPair.Go;
            if (refs.NameLabel != null) refs.NameLabel.raycastTarget = true;  // Button needs a raycast-able Graphic
            var nameBtn = nameLabelPair.Go.AddComponent<Button>();
            int capturedNameIdx = index;
            nameBtn.onClick.AddListener(() => OnNameClicked(capturedNameIdx));
            // Subtle hover tint so the user discovers the label is clickable.
            var nameColors = nameBtn.colors;
            nameColors.highlightedColor = new Color(1f, 0.95f, 0.75f, 1f);
            nameColors.pressedColor     = new Color(1f, 0.85f, 0.55f, 1f);
            nameBtn.colors = nameColors;

            // Name input (visible when editing).
            var inputGo = GUIManager.Instance.CreateInputField(
                parent:        topRow.transform,
                anchorMin:     new Vector2(0f, 0.5f),
                anchorMax:     new Vector2(0f, 0.5f),
                position:      Vector2.zero,
                contentType:   InputField.ContentType.Standard,
                placeholderText: "",
                fontSize:      14,
                width:         200f,
                height:        44f);
            refs.NameInput   = inputGo.GetComponent<InputField>();
            refs.NameInputGo = inputGo;
            var inputLE = inputGo.GetComponent<LayoutElement>() ?? inputGo.AddComponent<LayoutElement>();
            inputLE.preferredWidth  = 200f;
            inputLE.preferredHeight = 44f;  // match the row's other controls — without this the
                                            //  HorizontalLayoutGroup collapses the field's height
            inputGo.SetActive(false);
            // (Enter-to-commit is handled in Tick() — the runtime Unity InputField in this Valheim
            //  build doesn't expose onEndEdit via the API our reference assembly claims, so we
            //  poll the key directly.)

            // (No separate Rename button — clicking the name label above starts rename mode.)

            // Flexible spacer absorbs remaining row width so the action buttons sit flush right.
            var spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(topRow.transform, false);
            var spacerLE = spacer.AddComponent<LayoutElement>();
            spacerLE.flexibleWidth = 1f;
            spacerLE.minWidth      = 0f;

            // Save / Load / Cmp / Clear buttons.  Save & Load are icon-only.  Load's label
            // switches to the literal text "Active" (in green) when the loadout matches the
            // player's current equipment — see UpdateRowEmptyState.
            if (ShowRowButtons)
            {
            var saveGo = AddRowButton(topRow.transform, SaveIcon, 44f, () => OnSaveClicked(index));
            AttachTooltip(saveGo, "Save");
            var loadGo = AddRowButton(topRow.transform, LoadIcon, 44f, () => OnLoadClicked(index));
            AttachTooltip(loadGo, "Load");
            refs.LoadButton      = loadGo.GetComponent<Button>();
            refs.LoadButtonLabel = loadGo.GetComponentInChildren<Text>();
            refs.LoadButtonDefaultColor = refs.LoadButtonLabel != null ? refs.LoadButtonLabel.color : Color.white;
            var slotsGo = AddRowButton(topRow.transform, "+", 44f, () => OnSlotsToggled(index));
            AttachTooltip(slotsGo, () => SlotsTooltip(index));
            refs.SlotsButton      = slotsGo.GetComponent<Button>();
            refs.SlotsButtonLabel = slotsGo.GetComponentInChildren<Text>();
            var cmpGo = AddRowButton(topRow.transform, "Cmp", 75f, () => OnCompareClicked(index));
            refs.CompareButton = cmpGo.GetComponent<Button>();
            var clearGo = AddRowButton(topRow.transform, "x", 50f, () => OnClearClicked(index));
            refs.ClearButton = clearGo.GetComponent<Button>();

            // Tint the X label red so it reads as "destructive", and make the button background
            // flash red on hover/press to reinforce the affordance.
            var clearLabel = clearGo.GetComponentInChildren<Text>();
            if (clearLabel != null) clearLabel.color = new Color(0.95f, 0.35f, 0.35f);
            var clearColors = refs.ClearButton.colors;
            clearColors.highlightedColor = new Color(0.85f, 0.20f, 0.20f, 1f);
            clearColors.pressedColor     = new Color(0.65f, 0.12f, 0.12f, 1f);
            refs.ClearButton.colors      = clearColors;
            }
            else
            {
                // Equip out in the open, since it is the one thing a row is for; the rest in
                // the menu behind the three-bar button beside it.
                var equipGo = AddRowButton(topRow.transform, "Equip", 76f, () => OnLoadClicked(index));
                AttachTooltip(equipGo, "Load this loadout");
                refs.LoadButton      = equipGo.GetComponent<Button>();
                refs.LoadButtonLabel = equipGo.GetComponentInChildren<Text>();
                refs.LoadButtonDefaultColor = refs.LoadButtonLabel != null ? refs.LoadButtonLabel.color : Color.white;

                var menuGo = AddRowButton(topRow.transform, "", 44f, () => OnMenuClicked(index));
                AttachTooltip(menuGo, "Save, Compare, Delete, show all cells");
                var bars = new GameObject("Bars", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                bars.transform.SetParent(menuGo.transform, false);
                var brt = bars.GetComponent<RectTransform>();
                brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
                brt.offsetMin = new Vector2(11f, 11f); brt.offsetMax = new Vector2(-11f, -11f);
                var bimg = bars.GetComponent<Image>();
                bimg.sprite        = MenuSprite();
                bimg.color         = GUIManager.Instance.ValheimOrange;
                bimg.raycastTarget = false;
                refs.MenuButton = menuGo.GetComponent<Button>();
            }

            // ── Bottom line: summary ──
            var summaryPair = AddText(row.transform, BuildSummary(slot), 12, 480f, 22f,
                                      new Color(0.78f, 0.72f, 0.58f), leftAlign: true, richText: true);
            refs.Summary = summaryPair.Txt;
            SetHeight(summaryPair.Go, 22f);
            if (refs.Summary != null)
            {
                // A long summary is cut at the row's edge rather than run out past the panel.
                refs.Summary.horizontalOverflow = HorizontalWrapMode.Wrap;
                refs.Summary.verticalOverflow   = VerticalWrapMode.Truncate;
            }

            // ── Icons strip: category groups of clickable cells, each headed by a group switch.
            refs.IconsContainer = BuildIconsStrip();
            refs.IconsContainer.transform.SetParent(row.transform, false);
            PopulateIcons(refs, slot, index);

            refs.Root = row;
            UpdateRowEditState(refs, index);
            UpdateRowEmptyState(refs, _data.Slots[index]);
            return refs;
        }

        // ── Icons strip ────────────────────────────────────────────────────────────
        // Category groups of icons, each a small header — a button that toggles the category
        // in and out of the loadout — over a row of cells that each toggle alone.  The groups sit on one
        // line when they fit the panel's width and wrap onto more only when they don't; the
        // panel's width is the player's to drag.  The worn categories always show, so an empty
        // one can still be toggled: an included empty Cape means "take the cape off".  Ammo,
        // Food and Meads appear only when the loadout saved any; with nothing to place, their
        // toggles would mean nothing.

        private const float IconSize       = 48f;
        private const float CellSpacingPx  = 3f;    // between cells in a group
        private const float GroupSpacingPx = 10f;   // between groups on a line
        private static float MinPanelWidth => ShowRowButtons ? 520f : 440f;

        private static float PanelWidth
        {
            get
            {
                float w = ArmoryMod.PanelWidth?.Value ?? 760f;
                return Mathf.Clamp(w, MinPanelWidth, Mathf.Max(MinPanelWidth, Screen.width));
            }
        }

        // What the strip can use: the panel minus the scroll view's margins, its scrollbar, and
        // the row and line padding.  An estimate, deliberately — planning from the configured
        // width keeps the row heights (needed before layout runs) and the lines in agreement.
        private static float IconStripAvailableWidth => PanelWidth - 104f;

        private static float GroupWidth(int cells)
        {
            cells = Mathf.Max(1, cells);
            return cells * IconSize + (cells - 1) * CellSpacingPx;
        }

        // Which cells a row shows: every saved cell when the row is expanded, otherwise only
        // the ones in the loadout.  Worn slots count even when empty — an included empty slot
        // is "wear nothing there", and has to be visible to be taken back out.
        private static List<LoadoutCategories.Entry> VisibleEntries(LoadoutSlot slot, List<LoadoutCategories.Entry> entries)
        {
            bool expanded = _expanded.Contains(slot);
            return entries.Where(e => (e.Item != null || e.Worn) && (expanded || e.Included)).ToList();
        }

        // Lays the groups out in lines: each takes as many groups as fit, in category order.
        private static List<List<LoadoutCategory>> PlanIconLines(LoadoutSlot slot)
        {
            var lines = new List<List<LoadoutCategory>>();
            if (slot == null)
            {
                lines.Add(new List<LoadoutCategory> { LoadoutCategory.Armor, LoadoutCategory.Cape });
                return lines;
            }
            var visible = VisibleEntries(slot, LoadoutCategories.Entries(slot));
            var cur  = new List<LoadoutCategory>();
            float curW = 0f, avail = IconStripAvailableWidth;
            foreach (var cat in LoadoutCategories.Order)
            {
                int cells = visible.Count(e => e.Category == cat);
                if (cells == 0) continue;
                float w = GroupWidth(cells);
                if (cur.Count > 0 && curW + GroupSpacingPx + w > avail)
                {
                    lines.Add(cur);
                    cur = new List<LoadoutCategory>();
                    curW = 0f;
                }
                curW += (cur.Count > 0 ? GroupSpacingPx : 0f) + w;
                cur.Add(cat);
            }
            if (cur.Count > 0) lines.Add(cur);
            if (lines.Count == 0) lines.Add(new List<LoadoutCategory>());   // one line, for the "nothing" note
            return lines;
        }

        private static GameObject BuildIconsStrip()
        {
            var go = new GameObject("Icons", typeof(RectTransform));
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = IconLineGapPx;
            vlg.padding                = new RectOffset(0, 0, 0, 0);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childAlignment         = TextAnchor.UpperLeft;
            go.AddComponent<LayoutElement>().preferredHeight = IconLinePx;
            return go;
        }

        private static float IconStripHeight(LoadoutSlot slot)
        {
            int lines = PlanIconLines(slot).Count;
            return lines * IconLinePx + (lines - 1) * IconLineGapPx;
        }

        private static void PopulateIcons(SlotRowRefs refs, LoadoutSlot slot, int index)
        {
            if (refs?.IconsContainer == null) return;
            refs.IconCells.Clear();
            var parent    = refs.IconsContainer.transform;
            var playerInv = Player.m_localPlayer?.GetInventory();
            var rackInv   = _rack?.GetStorageInventory();
            var visible   = VisibleEntries(slot, LoadoutCategories.Entries(slot));

            var le = refs.IconsContainer.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = IconStripHeight(slot);

            foreach (var line in PlanIconLines(slot))
                AddIconLine(parent, line, visible, slot, index, refs, playerInv, rackInv);
        }

        private static void AddIconLine(Transform parent, List<LoadoutCategory> categories, List<LoadoutCategories.Entry> visible,
                                        LoadoutSlot slot, int index, SlotRowRefs refs,
                                        Inventory playerInv, Inventory rackInv)
        {
            var line = new GameObject("IconLine", typeof(RectTransform));
            line.transform.SetParent(parent, false);
            var hlg = line.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = GroupSpacingPx;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.padding                = new RectOffset(4, 4, 2, 2);
            hlg.childAlignment         = TextAnchor.LowerLeft;
            line.AddComponent<LayoutElement>().preferredHeight = IconLinePx;

            if (categories.Count == 0)
            {
                var note = AddText(line.transform, "nothing in this loadout — press + to add cells", 12, 0f, IconSize,
                                   new Color(0.45f, 0.42f, 0.38f), leftAlign: true);
                var noteLE = note.Go.GetComponent<LayoutElement>() ?? note.Go.AddComponent<LayoutElement>();
                noteLE.preferredHeight = IconSize;
                return;
            }

            foreach (var cat in categories)
                AddIconGroup(line.transform, cat, visible.Where(e => e.Category == cat).ToList(),
                             slot, index, refs, playerInv, rackInv);
        }

        private static void AddIconGroup(Transform parent, LoadoutCategory cat, List<LoadoutCategories.Entry> members,
                                         LoadoutSlot slot, int index, SlotRowRefs refs,
                                         Inventory playerInv, Inventory rackInv)
        {
            var (total, included) = LoadoutCategories.CategoryState(slot, cat);

            var group = new GameObject($"Group_{cat}", typeof(RectTransform));
            group.transform.SetParent(parent, false);
            var vlg = group.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 2;
            vlg.padding                = new RectOffset(0, 0, 0, 0);
            vlg.childForceExpandWidth  = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childAlignment         = TextAnchor.UpperLeft;

            // Header: the category's name, doubling as the switch for the whole group.  Orange
            // when every cell is in the loadout, grey when none is, in between when mixed.
            var headerColor = included == total ? GUIManager.Instance.ValheimOrange
                            : included == 0     ? new Color(0.45f, 0.42f, 0.38f)
                                                : new Color(0.80f, 0.62f, 0.35f);
            var headerPair = AddText(group.transform, LoadoutCategories.Label(cat), 11, 0f, IconHeaderPx, headerColor, leftAlign: true);
            var headerLE = headerPair.Go.GetComponent<LayoutElement>() ?? headerPair.Go.AddComponent<LayoutElement>();
            headerLE.preferredHeight = IconHeaderPx;
            headerLE.minWidth        = IconSize;
            if (headerPair.Txt != null) headerPair.Txt.raycastTarget = true;  // Button needs a raycast-able Graphic
            var headerBtn = headerPair.Go.AddComponent<Button>();
            headerBtn.onClick.AddListener(() => OnCategoryToggled(index, cat));
            var hc = headerBtn.colors;
            hc.highlightedColor = new Color(1f, 0.95f, 0.75f, 1f);
            hc.pressedColor     = new Color(1f, 0.85f, 0.55f, 1f);
            headerBtn.colors = hc;
            AttachTooltip(headerPair.Go, () => CategoryTooltip(index, cat));

            var cells = new GameObject("Cells", typeof(RectTransform));
            cells.transform.SetParent(group.transform, false);
            var hlg = cells.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = CellSpacingPx;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;
            cells.AddComponent<LayoutElement>().preferredHeight = IconSize;

            foreach (var m in members)
                AddIcon(cells.transform, m, index, refs, playerInv, rackInv);
        }

        private static string CategoryTooltip(int index, LoadoutCategory cat)
        {
            if (index < 0 || index >= _data.Slots.Count) return string.Empty;
            var (total, included) = LoadoutCategories.CategoryState(_data.Slots[index], cat);
            var label = LoadoutCategories.Label(cat);
            if (included == total) return $"{label}: all in this loadout.\nClick to leave the whole group out.";
            if (included == 0)     return $"{label}: none in this loadout.\nClick to add the whole group.";
            return $"{label}: {included} of {total} in this loadout.\nClick to add the rest.";
        }

        // Each icon cell is a square with a background-color Image (state indicator) and, when
        // there's a saved item, a child Image with the actual sprite on top of it.  Background
        // color: empty=dim grey, available=warm dark brown, missing=red; everything fades when
        // the cell is not in the loadout.  Clicking the cell toggles that.
        private static void AddIcon(Transform parent, LoadoutCategories.Entry entry, int index, SlotRowRefs refs,
                                    Inventory playerInv, Inventory rackInv)
        {
            var item = entry.Item;
            bool included = entry.Included;
            var cell = new GameObject(item?.SharedName ?? "EmptySlot", typeof(RectTransform));
            cell.transform.SetParent(parent, false);
            var le = cell.AddComponent<LayoutElement>();
            le.preferredWidth  = IconSize;
            le.preferredHeight = IconSize;

            var bg = cell.AddComponent<Image>();
            bg.raycastTarget = true;  // needed for hover/tooltip detection and the click

            var cellRef = new IconCell { Bg = bg, Item = item, Included = included };
            refs.IconCells.Add(cellRef);
            ApplyIconColor(cellRef, playerInv, rackInv);

            var btn = cell.AddComponent<Button>();
            btn.targetGraphic = bg;
            var bc = btn.colors;
            bc.highlightedColor = new Color(1.4f, 1.3f, 1.1f, 1f);
            bc.pressedColor     = new Color(1.6f, 1.4f, 1.0f, 1f);
            btn.colors = bc;
            var captured = entry;
            btn.onClick.AddListener(() => OnCellToggled(index, captured));

            string how = included ? "in this loadout — click to leave it out" : "not in this loadout — click to add it";
            if (item == null)
            {
                AttachTooltip(cell, included
                    ? $"{entry.Label}: nothing — Load takes off whatever is worn here.\nClick to leave the slot alone."
                    : $"{entry.Label}: nothing — {how}.");
                return;
            }

            // Icon sprite layered on top of the cell background.
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(cell.transform, false);
            var rt = iconGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(2f, 2f);
            rt.offsetMax = new Vector2(-2f, -2f);

            var img = iconGo.AddComponent<Image>();
            img.raycastTarget = false;  // let bg receive the hover and the click
            var sprite = LoadoutManager.GetIcon(item);
            if (sprite != null)
            {
                img.sprite         = sprite;
                img.preserveAspect = true;
                img.color          = included ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            }
            else
            {
                img.color = new Color(0.5f, 0.4f, 0.3f, included ? 0.7f : 0.25f);
            }

            // Hover tooltip — custom implementation since Valheim's UITooltip needs a tooltip
            // prefab the HUD wires up at game init and NREs on our Canvas.
            AttachTooltip(cell, $"{entry.Describe()}\n{how}");
        }

        private static void ApplyIconColor(IconCell cell, Inventory playerInv, Inventory rackInv)
        {
            if (cell?.Bg == null) return;
            float fade = cell.Included ? 1f : 0.4f;
            if (cell.Item == null)
            {
                cell.Bg.color = new Color(0.10f, 0.10f, 0.10f, 0.35f * fade);
                return;
            }
            bool available = LoadoutManager.IsItemAvailable(cell.Item, playerInv, rackInv);
            cell.Bg.color = available
                ? new Color(0.15f, 0.12f, 0.08f, 0.80f * fade)
                : new Color(0.55f, 0.15f, 0.15f, 0.90f * fade);  // red when not findable
        }

        private static void RefreshIconColors(SlotRowRefs refs)
        {
            if (refs == null || refs.IconCells.Count == 0) return;
            var playerInv = Player.m_localPlayer?.GetInventory();
            var rackInv   = _rack?.GetStorageInventory();
            foreach (var cell in refs.IconCells)
                ApplyIconColor(cell, playerInv, rackInv);
        }

        // ── Toast ──────────────────────────────────────────────────────────────────
        // Vanilla's center message draws on the HUD canvas, which sits beneath Jotunn's front
        // canvas — and so beneath this panel, where "Saved" went unread.  Messages about the
        // panel's own actions are shown here instead: on the panel's canvas, last in draw
        // order, so they sit above everything the panel puts up.

        private static GameObject _toastGo;
        private static Text       _toastText;
        private static float      _toastUntil;
        private const  float      ToastSeconds = 2.5f;

        private static void Notify(string text)
        {
            Jotunn.Logger.LogInfo($"[Armory] {text}");
            var canvas = GUIManager.CustomGUIFront;
            if (!_isOpen || canvas == null)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, text);
                return;
            }
            if (_toastGo == null)
            {
                _toastGo = new GameObject("ArmoryToast", typeof(RectTransform));
                _toastGo.transform.SetParent(canvas.transform, false);
                var rt = _toastGo.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot            = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 220f);   // about where vanilla's center message sits
                rt.sizeDelta        = new Vector2(900f, 44f);

                _toastText = _toastGo.AddComponent<Text>();
                _toastText.fontSize           = 26;
                _toastText.alignment          = TextAnchor.MiddleCenter;
                _toastText.raycastTarget      = false;
                _toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
                try { GUIManager.Instance.ApplyTextStyle(_toastText, GUIManager.Instance.ValheimOrange); } catch { }
                _toastText.fontSize = 26;
                var outline = _toastGo.GetComponent<Outline>() ?? _toastGo.AddComponent<Outline>();
                outline.effectColor    = Color.black;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
            _toastText.text  = text;
            _toastText.color = GUIManager.Instance.ValheimOrange;
            _toastGo.transform.SetAsLastSibling();
            _toastGo.SetActive(true);
            _toastUntil = Time.unscaledTime + ToastSeconds;
        }

        // Runs every frame, open or not, so a toast raised just before the panel closed still
        // fades out on time.  Unscaled time: the panel may be holding the game paused.
        private static void TickToast()
        {
            if (_toastGo == null || !_toastGo.activeSelf) return;
            float left = _toastUntil - Time.unscaledTime;
            if (left <= 0f) { _toastGo.SetActive(false); return; }
            var c = _toastText.color;
            c.a = Mathf.Clamp01(left / 0.6f);   // fade over the last 0.6 s
            _toastText.color = c;
        }

        // ── Resizing ───────────────────────────────────────────────────────────────
        // A grip in the panel's bottom-right corner, drawn the way TheGreatestPortal draws
        // its own: three diagonal lines tucked into the corner.  Dragging it moves that corner
        // while the top-left corner stays; the rows follow the width as it changes, wrapping
        // their icon groups when there is no longer room for them on one line; and the size is
        // remembered in config beside the position.  A height the player has set is theirs
        // until they set it again; until then the panel fits its loadouts.

        private const float MinPanelHeight = 320f;
        private static float _lastReflow;

        private static float PanelHeightOrAuto()
        {
            float h = ArmoryMod.PanelHeight?.Value ?? 0f;
            return h > 0f ? Mathf.Clamp(h, MinPanelHeight, Screen.height * 0.95f) : ComputePanelHeight();
        }

        // Reports pointer drags in screen pixels, for the grip.
        private sealed class DragHandle : MonoBehaviour,
            UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IEndDragHandler
        {
            public System.Action<Vector2> OnDrag;
            public System.Action OnEnd;
            void UnityEngine.EventSystems.IDragHandler.OnDrag(UnityEngine.EventSystems.PointerEventData e) { OnDrag?.Invoke(e.delta); }
            void UnityEngine.EventSystems.IEndDragHandler.OnEndDrag(UnityEngine.EventSystems.PointerEventData e) { OnEnd?.Invoke(); }
        }

        private static Sprite _gripSprite;

        // Three diagonal lines tucked into the lower-right corner: the usual resize grip.  Same
        // drawing as TheGreatestPortal's UiKit.Grip, so the two panels read alike.
        private static Sprite GripSprite()
        {
            if (_gripSprite != null) return _gripSprite;
            const int n = 24;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int k = x + (n - 1 - y);        // grows towards the bottom-right corner
                    bool on = k >= n - 3 && (k - (n - 3)) % 6 < 2;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            _gripSprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _gripSprite.hideFlags = HideFlags.HideAndDontSave;
            return _gripSprite;
        }

        private static void BuildResizeGrip(Transform panel)
        {
            var grip = new GameObject("Grip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DragHandle));
            grip.transform.SetParent(panel, false);
            var rt = grip.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-5f, 5f);
            rt.sizeDelta        = new Vector2(18f, 18f);

            var img = grip.GetComponent<Image>();
            img.sprite        = GripSprite();
            img.color         = new Color(1f, 0.85f, 0.45f, 0.75f);
            img.raycastTarget = true;

            var handle = grip.GetComponent<DragHandle>();
            handle.OnDrag = OnResizeDrag;
            handle.OnEnd  = OnResizeEnd;
        }

        // The grip follows the pointer: the bottom-right corner moves, the top-left corner
        // stays.  The panel is anchored at its center, so it is shifted by half the change.
        private static void OnResizeDrag(Vector2 screenDelta)
        {
            if (_mainPanel == null) return;
            var rt = _mainPanel.GetComponent<RectTransform>();
            var canvas = GUIManager.CustomGUIFront?.GetComponent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            float oldW = rt.sizeDelta.x, oldH = rt.sizeDelta.y;
            float newW = Mathf.Clamp(oldW + screenDelta.x / scale, MinPanelWidth,  Mathf.Max(MinPanelWidth,  Screen.width  / scale));
            float newH = Mathf.Clamp(oldH - screenDelta.y / scale, MinPanelHeight, Mathf.Max(MinPanelHeight, Screen.height / scale));
            if (Mathf.Approximately(newW, oldW) && Mathf.Approximately(newH, oldH)) return;

            rt.sizeDelta         = new Vector2(newW, newH);
            rt.anchoredPosition += new Vector2((newW - oldW) / 2f, -(newH - oldH) / 2f);
            ApplyScrollViewSize(newW - 60f, newH - TitleAreaPx - FooterAreaPx);
            if (ArmoryMod.PanelWidth  != null) ArmoryMod.PanelWidth.Value  = newW;
            if (ArmoryMod.PanelHeight != null) ArmoryMod.PanelHeight.Value = newH;

            // The rows follow the width as it changes, a few times a second rather than every
            // frame: each reflow rebuilds them.
            if (!Mathf.Approximately(newW, oldW) && Time.unscaledTime - _lastReflow > 0.08f)
            {
                _lastReflow = Time.unscaledTime;
                RebuildSlotRows();
            }
        }

        private static void OnResizeEnd()
        {
            if (_mainPanel == null) return;
            var rt = _mainPanel.GetComponent<RectTransform>();
            _savedMainPos = rt.anchoredPosition;
            if (ArmoryMod.PanelPosX != null) ArmoryMod.PanelPosX.Value = rt.anchoredPosition.x;
            if (ArmoryMod.PanelPosY != null) ArmoryMod.PanelPosY.Value = rt.anchoredPosition.y;
            Jotunn.Logger.LogInfo($"[Armory] Panel resized to {rt.sizeDelta.x:0} x {rt.sizeDelta.y:0}");
            RebuildSlotRows();
        }

        // ── Row menu ───────────────────────────────────────────────────────────────
        // One shared popup, built fresh each time it opens so its entries reflect the row:
        // the cells entry flips between showing all and showing only the loaded ones, Compare
        // reads "Hide compare" while its window is up, and so on.  Equip is not in it — that
        // is the one thing a row is for, so it has a button of its own beside the menu.  It
        // lives on the panel's canvas rather than in the row, so the scroll view's mask
        // cannot clip it, and it closes on a choice, on Esc, or on a click anywhere else.

        private static GameObject    _menuGo;
        private static RectTransform _menuRT;
        private static int           _menuSlot = -1;
        private static Sprite        _menuSprite;

        private static Camera MenuCamera()
        {
            var canvas = GUIManager.CustomGUIFront?.GetComponentInParent<Canvas>()?.rootCanvas;
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }

        // Three horizontal bars: the usual menu glyph, drawn rather than typed since the
        // game's font has no such character.
        private static Sprite MenuSprite()
        {
            if (_menuSprite != null) return _menuSprite;
            const int n = 24;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool bar = (y >= 4 && y <= 6) || (y >= 10 && y <= 12) || (y >= 16 && y <= 18);
                    bool on  = bar && x >= 2 && x <= 21;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            _menuSprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _menuSprite.hideFlags = HideFlags.HideAndDontSave;
            return _menuSprite;
        }

        private static void OnMenuClicked(int index)
        {
            Jotunn.Logger.LogInfo($"[Armory] Click: menu slot {index}");
            CommitPendingEdit();
            if (_menuGo != null && _menuSlot == index) { CloseRowMenu(); return; }
            if (index < 0 || index >= _rows.Count || _rows[index].MenuButton == null) return;
            OpenRowMenu(index, _rows[index].MenuButton.GetComponent<RectTransform>());
        }

        private static void OpenRowMenu(int index, RectTransform anchor)
        {
            CloseRowMenu();
            var canvasGo = GUIManager.CustomGUIFront;
            if (canvasGo == null || index < 0 || index >= _data.Slots.Count) return;
            var slot = _data.Slots[index];
            bool hasContent = !slot.IsEmpty();
            bool expanded   = _expanded.Contains(slot);

            _menuGo = new GameObject("ArmoryRowMenu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _menuGo.transform.SetParent(canvasGo.transform, false);
            _menuRT = _menuGo.GetComponent<RectTransform>();
            _menuRT.anchorMin = _menuRT.anchorMax = new Vector2(0.5f, 0.5f);
            _menuGo.GetComponent<Image>().color = new Color(0.07f, 0.055f, 0.04f, 0.97f);

            var vlg = _menuGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 2;
            vlg.padding                = new RectOffset(6, 6, 6, 6);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            const float itemW = 210f, itemH = 36f;
            const int   items = 4;
            float menuH = items * itemH + (items - 1) * vlg.spacing + vlg.padding.vertical;
            _menuRT.sizeDelta = new Vector2(itemW + vlg.padding.horizontal, menuH);

            AddMenuItem("Save",                                         true,                 () => OnSaveClicked(index));
            AddMenuItem(expanded ? "Show only loaded cells" : "Show all cells", true,        () => OnSlotsToggled(index));
            AddMenuItem(_compareSlot == index ? "Hide compare" : "Compare", hasContent,       () => OnCompareClicked(index));
            AddMenuItem("Delete",                                       true,                 () => OnClearClicked(index), danger: true);

            // Hang it off the button's bottom-right corner; open upward instead when that
            // would run off the bottom of the screen.
            var canvasRT = canvasGo.GetComponent<RectTransform>();
            var cam = MenuCamera();
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, RectTransformUtility.WorldToScreenPoint(cam, corners[3]), cam, out var below);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, RectTransformUtility.WorldToScreenPoint(cam, corners[2]), cam, out var above);
            bool fitsBelow = below.y - menuH > -canvasRT.rect.height / 2f;
            _menuRT.pivot            = fitsBelow ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
            _menuRT.anchoredPosition = fitsBelow ? below : above;

            _menuGo.transform.SetAsLastSibling();
            _menuSlot = index;
        }

        private static void AddMenuItem(string label, bool enabled, System.Action action, bool danger = false)
        {
            var go = GUIManager.Instance.CreateButton(label, _menuGo.transform, Vector2.zero, Vector2.zero, Vector2.zero, 210f, 36f);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            var btn = go.GetComponent<Button>();
            btn.interactable = enabled;
            btn.onClick.AddListener(() => { CloseRowMenu(); action(); });
            var txt = go.GetComponentInChildren<Text>();
            if (txt != null)
            {
                txt.alignment = TextAnchor.MiddleLeft;
                var trt = txt.GetComponent<RectTransform>();
                if (trt != null) { trt.offsetMin = new Vector2(12f, trt.offsetMin.y); }
                if (danger) txt.color = new Color(0.95f, 0.35f, 0.35f);
                else if (!enabled) txt.color = new Color(0.55f, 0.5f, 0.45f);
            }
            if (danger)
            {
                var c = btn.colors;
                c.highlightedColor = new Color(0.85f, 0.20f, 0.20f, 1f);
                c.pressedColor     = new Color(0.65f, 0.12f, 0.12f, 1f);
                btn.colors = c;
            }
        }

        private static void CloseRowMenu()
        {
            if (_menuGo != null) Object.Destroy(_menuGo);
            _menuGo   = null;
            _menuRT   = null;
            _menuSlot = -1;
        }

        // ── Custom hover tooltip ───────────────────────────────────────────────────────
        // Single shared tooltip GameObject (text + background) shown on icon hover.

        private static GameObject _tooltipGo;
        private static Text _tooltipText;
        private static RectTransform _tooltipRT;

        private static void EnsureTooltip()
        {
            if (_tooltipGo != null) return;
            var canvas = GUIManager.CustomGUIFront;
            if (canvas == null) return;

            _tooltipGo = new GameObject("ArmoryTooltip", typeof(RectTransform));
            _tooltipGo.transform.SetParent(canvas.transform, false);
            _tooltipRT = _tooltipGo.GetComponent<RectTransform>();
            _tooltipRT.pivot = new Vector2(0f, 0f);
            // sizeDelta is set per-show in ShowTooltip so the box hugs the current text.

            var bg = _tooltipGo.AddComponent<Image>();
            bg.color         = new Color(0.04f, 0.04f, 0.04f, 0.92f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_tooltipGo.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 4f);
            trt.offsetMax = new Vector2(-8f, -4f);

            _tooltipText = textGo.AddComponent<Text>();
            _tooltipText.color         = GUIManager.Instance.ValheimOrange;
            _tooltipText.fontSize      = 14;
            _tooltipText.alignment     = TextAnchor.MiddleLeft;
            _tooltipText.raycastTarget = false;
            _tooltipText.horizontalOverflow = HorizontalWrapMode.Overflow;
            try { GUIManager.Instance.ApplyTextStyle(_tooltipText, _tooltipText.color); } catch { }
            _tooltipText.color = GUIManager.Instance.ValheimOrange;  // restore in case ApplyTextStyle stomps it

            _tooltipGo.SetActive(false);
        }

        private static void ShowTooltip(string text, Vector2 screenPos)
        {
            EnsureTooltip();
            if (_tooltipGo == null) return;
            _tooltipText.text = text;
            // Size the box to wrap the text snugly.  preferredWidth/Height read directly from
            // the Text's current font + fontSize.  Add the inner text's 8/8 horizontal and
            // 4/4 vertical padding back in (set via the inner RectTransform offsets).
            float w = _tooltipText.preferredWidth  + 16f;
            float h = _tooltipText.preferredHeight + 8f;
            _tooltipRT.sizeDelta = new Vector2(w, h);
            _tooltipRT.position  = new Vector3(screenPos.x + 12f, screenPos.y + 12f, 0f);
            _tooltipGo.SetActive(true);
            _tooltipGo.transform.SetAsLastSibling();  // render above other UI on the same canvas
        }

        private static void HideTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.SetActive(false);
        }

        private static void AttachTooltip(GameObject host, string text) =>
            AttachTooltip(host, () => text);

        /// <summary>
        /// Deferred variant, for a tooltip whose wording depends on state that changes while the
        /// panel is up.  Attaching once and reading the text on hover keeps a single EventTrigger
        /// on the host; re-attaching on every refresh would stack them.
        /// </summary>
        private static void AttachTooltip(GameObject host, System.Func<string> text)
        {
            var trigger = host.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowTooltip(text(), Input.mousePosition));
            trigger.triggers.Add(enter);
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideTooltip());
            trigger.triggers.Add(exit);
        }

        private static void RebuildIcons(SlotRowRefs refs, LoadoutSlot slot)
        {
            if (refs?.IconsContainer == null) return;
            foreach (Transform child in refs.IconsContainer.transform)
                Object.Destroy(child.gameObject);
            PopulateIcons(refs, slot, _rows.IndexOf(refs));
        }

        // Manages the per-row dynamic button states:
        //  - Load / Compare / Clear are disabled when the slot is empty.
        //  - Load becomes "Active" in green (and disabled) when the player is currently
        //    wearing exactly what this slot has saved.
        private static void UpdateRowEmptyState(SlotRowRefs refs, LoadoutSlot slot)
        {
            bool hasContent = !slot.IsEmpty();
            bool loadable   = hasContent && LoadoutCategories.HasIncludedContent(slot);
            UpdateSlotsButton(refs, slot);
            bool isActive   = loadable && LoadoutManager.IsLoadoutActive(slot, Player.m_localPlayer);

            if (refs.CompareButton != null) refs.CompareButton.interactable = hasContent;
            if (refs.ClearButton   != null) refs.ClearButton.interactable   = true;  // Delete works on empty slots too
            if (refs.LoadButton    != null) refs.LoadButton.interactable    = loadable && !isActive;
            if (refs.LoadButtonLabel != null)
            {
                // Green when the slot's gear matches what the player is currently wearing: a
                // ● (U+25CF) on the square arrow button, the word on the Equip button.
                refs.LoadButtonLabel.text  = ShowRowButtons ? (isActive ? "●" : LoadIcon)
                                                            : (isActive ? "Wearing" : "Equip");
                refs.LoadButtonLabel.color = isActive ? new Color(0.35f, 0.85f, 0.35f)
                                                     : refs.LoadButtonDefaultColor;
            }
        }

        // ── Event handlers ─────────────────────────────────────────────────────────

        private static void OnAddSlotClicked()
        {
            Jotunn.Logger.LogInfo("[Armory] Click: AddSlot");
            CommitPendingEdit();
            if (_data.Slots.Count >= MaxSlotCount) return;
            _data.Slots.Add(new LoadoutSlot { Name = $"Slot {_data.Slots.Count + 1}" });
            _rack.SaveData(_data);
            RebuildSlotRows();
            ResizeMainPanel();
        }

        // Per-row preferred height, accounting for whether Summary and Icons are visible.
        // VerticalLayoutGroup skips inactive children, so the row collapses naturally — this
        // mirrors that math so ComputePanelHeight stays in sync with what's rendered.
        // A row's height depends on its slot: a loadout with consumables saved gets a second
        // icon line.  Pass null for the height of a row with one line.
        private static float ComputeRowHeight(LoadoutSlot slot)
        {
            bool showSummary = ArmoryMod.ShowSummaryText?.Value ?? true;
            bool showIcons   = ArmoryMod.ShowIcons?.Value       ?? true;
            float h = TopRowPx + RowPaddingPx;     // always-present pieces
            int gaps = 0;                          // VLG only adds spacing between *visible* children
            if (showSummary) { h += SummaryRowPx;           gaps++; }
            if (showIcons)   { h += IconStripHeight(slot);  gaps++; }
            h += gaps * RowSpacingPx;
            return h;
        }

        // Compute desired panel height based on the current slots, clamped to the screen.
        private static float ComputePanelHeight()
        {
            float rowH  = ComputeRowHeight(null);
            float listH = ScrollListPadPx;
            var slots   = _data?.Slots;
            int slotCount = slots?.Count ?? 0;
            if (slotCount == 0)
            {
                slotCount = DefaultSlotCount;
                listH += slotCount * rowH;
            }
            else
            {
                foreach (var slot in slots) listH += ComputeRowHeight(slot);
            }
            listH += System.Math.Max(0, slotCount - 1) * ScrollListGapPx;
            float desired   = TitleAreaPx + FooterAreaPx + listH;
            float min       = TitleAreaPx + FooterAreaPx + (3 * rowH + 2 * ScrollListGapPx + ScrollListPadPx);
            float max       = Screen.height * 0.9f;
            return Mathf.Clamp(desired, min, max);
        }

        // Resize the main panel and the scroll view inside it to fit the current slot count.
        // The scroll view will still scroll if the desired size exceeds the screen.
        private static void ResizeMainPanel()
        {
            if (_mainPanel == null) return;
            var panelRT = _mainPanel.GetComponent<RectTransform>();
            if (panelRT == null) return;

            float newHeight = PanelHeightOrAuto();
            panelRT.sizeDelta = new Vector2(panelRT.sizeDelta.x, newHeight);

            ApplyScrollViewSize(panelRT.sizeDelta.x - 60f, newHeight - TitleAreaPx - FooterAreaPx);
        }

        // Mouse wheel over the list: one row per notch.  Done here instead of by the ScrollRect
        // because its own step, wheel delta times sensitivity, comes out at a few pixels with
        // the deltas this input stack reports, however high the sensitivity is set.
        private static void TickWheel()
        {
            if (_scrollRect == null || _scrollRect.content == null || _scrollRect.viewport == null) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(wheel, 0f)) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_scrollRT, Input.mousePosition, MenuCamera())) return;

            float scrollable = _scrollRect.content.rect.height - _scrollRect.viewport.rect.height;
            if (scrollable <= 0f) return;
            float step = ComputeRowHeight(null) + ScrollListGapPx;
            float pos  = _scrollRect.verticalNormalizedPosition + Mathf.Sign(wheel) * step / scrollable;
            _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(pos);
        }

        // Size every layer of the scroll view the way CreateScrollView sized it at build time.
        private static void ApplyScrollViewSize(float w, float h)
        {
            void Size(RectTransform rt, float width, float height)
            {
                if (rt == null) return;
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   height);
            }
            float innerW = w - 2f * ScrollBorderPx - ScrollHandlePx;
            float innerH = h - 2f * ScrollBorderPx - ScrollHandlePx;
            Size(_scrollRT,     w, h);
            Size(_scrollViewRT, w, h);
            Size(_viewportRT,   w, h);
            if (_slotsContent != null)
                _slotsContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, innerW);   // height is the fitter's
            Size(_vScrollbarRT,   ScrollHandlePx, innerH);
            Size(_vSlidingAreaRT, ScrollHandlePx, innerH);
        }

        private static void OnSaveClicked(int index)
        {
            Jotunn.Logger.LogInfo($"[Armory] Click: Save slot {index}");
            CommitPendingEdit();
            var player = Player.m_localPlayer;
            if (player == null) { Jotunn.Logger.LogWarning("[Armory] OnSaveClicked: no local player"); return; }
            var name = _data.Slots[index].Name;
            var captured = LoadoutManager.CaptureCurrentEquipment(player);
            captured.Name  = name;
            var previous = _data.Slots[index];
            captured.CopyInclusionFrom(previous);   // which cells to load is the slot's choice, not the snapshot's
            if (_expanded.Remove(previous)) _expanded.Add(captured);
            _data.Slots[index] = captured;
            _rack.SaveData(_data);
            Notify($"Saved: {name}");
            Jotunn.Logger.LogInfo($"[Armory] Saved slot {index} '{name}': {BuildSummary(captured)}");
            RefreshRowVisual(index);
            ResizeMainPanel();   // the row grows a line when consumables first appear in it
        }

        private static void OnLoadClicked(int index)
        {
            Jotunn.Logger.LogInfo($"[Armory] Click: Load slot {index}");
            CommitPendingEdit();
            var player = Player.m_localPlayer;
            if (player == null) return;
            var slot = _data.Slots[index];
            if (slot.IsEmpty()) { Jotunn.Logger.LogInfo($"[Armory] Load slot {index}: slot is empty, ignoring"); return; }
            if (!LoadoutCategories.HasIncludedContent(slot))
            {
                Notify($"'{slot.Name}': nothing in this loadout — press + to add cells");
                return;
            }

            // Also let the loadout pull items from this rack's storage container.
            var container = _rack?.GetStorage();
            var rackInv   = _rack?.GetStorageInventory();
            Jotunn.Logger.LogInfo($"[Armory] Load: container={container?.GetType().Name ?? "null"}, rackInv={(rackInv == null ? "null" : $"size {rackInv.GetWidth()}x{rackInv.GetHeight()}, items={rackInv.GetAllItems().Count}")}");
            if (rackInv != null)
                foreach (var it in rackInv.GetAllItems())
                    Jotunn.Logger.LogInfo($"[Armory]   rack item: '{it.m_shared?.m_name}' qty={it.m_stack} quality={it.m_quality} at ({it.m_gridPos.x},{it.m_gridPos.y})");

            var (found, missing) = LoadoutManager.ApplyLoadout(player, slot, rackInv);
            var msg = missing > 0
                ? $"Loaded '{slot.Name}': {found} equipped, {missing} not in inventory or rack"
                : $"Loaded '{slot.Name}': {found} item(s) equipped";
            Notify(msg);
        }

        // A category header was clicked: if any of its cells are out of the loadout, bring
        // them all in; if all are in, take them all out.  The data is what changes; the row
        // redraws from it.
        private static void OnCategoryToggled(int index, LoadoutCategory cat)
        {
            if (index < 0 || index >= _data.Slots.Count) return;
            CommitPendingEdit();
            var slot = _data.Slots[index];
            var (total, included) = LoadoutCategories.CategoryState(slot, cat);
            bool on = included < total;
            LoadoutCategories.SetCategory(slot, cat, on);
            Jotunn.Logger.LogInfo($"[Armory] Click: slot {index} {cat} → {(on ? "all in" : "all out")}");
            AfterInclusionChanged(index);
        }

        // One cell was clicked: flip it.
        private static void OnCellToggled(int index, LoadoutCategories.Entry entry)
        {
            if (index < 0 || index >= _data.Slots.Count) return;
            CommitPendingEdit();
            var slot = _data.Slots[index];
            LoadoutCategories.Toggle(slot, entry);
            Jotunn.Logger.LogInfo($"[Armory] Click: slot {index} cell {entry.Describe()} → {(entry.Included ? "out" : "in")}");
            AfterInclusionChanged(index);
        }

        private static void AfterInclusionChanged(int index)
        {
            _rack.SaveData(_data);
            RefreshRowVisual(index);
            ResizeMainPanel();   // a row's line count follows what it shows
            if (_compareSlot == index) BuildCompareWindow();
        }

        // "+" / "-" on the row: show every saved cell, or only the ones in the loadout.
        private static void OnSlotsToggled(int index)
        {
            if (index < 0 || index >= _data.Slots.Count) return;
            CommitPendingEdit();
            var slot = _data.Slots[index];
            if (!_expanded.Add(slot)) _expanded.Remove(slot);
            RefreshRowVisual(index);
            ResizeMainPanel();
        }

        private static string SlotsTooltip(int index)
        {
            if (index < 0 || index >= _data.Slots.Count) return string.Empty;
            return _expanded.Contains(_data.Slots[index])
                ? "Showing every saved cell.\nClick to show only what this loadout loads."
                : "Show every saved cell, to add some to this loadout or take some out.";
        }

        private static void UpdateSlotsButton(SlotRowRefs refs, LoadoutSlot slot)
        {
            if (refs?.SlotsButtonLabel != null) refs.SlotsButtonLabel.text = _expanded.Contains(slot) ? "-" : "+";
        }

        private static void OnClearClicked(int index)
        {
            Jotunn.Logger.LogInfo($"[Armory] Click: Delete slot {index}");
            CommitPendingEdit();
            if (index < 0 || index >= _data.Slots.Count) return;

            // Confirm only when this would actually lose data — empty slots and slots whose
            // saved items match another slot can be removed without asking.
            var slot = _data.Slots[index];
            if (!slot.IsEmpty() && !HasDuplicateElsewhere(index))
            {
                ShowDeleteConfirm(index);
                return;
            }
            DeleteSlot(index);
        }

        private static void DeleteSlot(int index)
        {
            if (index < 0 || index >= _data.Slots.Count) return;

            var name = _data.Slots[index].Name;
            _expanded.Remove(_data.Slots[index]);
            _data.Slots.RemoveAt(index);
            _rack.SaveData(_data);

            // Close the compare window if it was pinned to the deleted slot, and shift
            // _compareSlot down by one if it referenced a slot after the deleted index.
            if (_compareSlot == index) ToggleCompareOff();
            else if (_compareSlot > index) _compareSlot--;

            // Same correction for an in-progress rename: cancel it if its slot just vanished,
            // otherwise track the new index of the slot being edited.
            if (_editingSlot == index) _editingSlot = -1;
            else if (_editingSlot > index) _editingSlot--;

            Notify($"Deleted: {name}");
            RebuildSlotRows();
            ResizeMainPanel();
        }

        // True if any other slot in the list saves the exact same items as `index` (so deleting
        // this slot wouldn't actually lose any loadout data — just a duplicate name).
        private static bool HasDuplicateElsewhere(int index)
        {
            if (index < 0 || index >= _data.Slots.Count) return false;
            var slot = _data.Slots[index];
            for (int i = 0; i < _data.Slots.Count; i++)
            {
                if (i == index) continue;
                if (SlotsHaveSameItems(slot, _data.Slots[i])) return true;
            }
            return false;
        }

        private static bool SlotsHaveSameItems(LoadoutSlot a, LoadoutSlot b)
        {
            if (!SameSavedItem(a.Helmet,    b.Helmet))    return false;
            if (!SameSavedItem(a.Chest,     b.Chest))     return false;
            if (!SameSavedItem(a.Legs,      b.Legs))      return false;
            if (!SameSavedItem(a.Shoulder,  b.Shoulder))  return false;
            if (!SameSavedItem(a.Utility,   b.Utility))   return false;
            if (!SameSavedItem(a.Trinket,   b.Trinket))   return false;
            int ah = a.Hotbar?.Length ?? 0;
            int bh = b.Hotbar?.Length ?? 0;
            int n  = System.Math.Max(ah, bh);
            for (int i = 0; i < n; i++)
            {
                var ai = i < ah ? a.Hotbar[i] : null;
                var bi = i < bh ? b.Hotbar[i] : null;
                if (!SameSavedItem(ai, bi)) return false;
            }
            // Extended slots: each must match by (GridX, GridY) — same item in the same slot.
            int extA = a.Extended?.Count ?? 0;
            int extB = b.Extended?.Count ?? 0;
            if (extA != extB) return false;
            for (int i = 0; i < extA; i++)
            {
                var ai = a.Extended[i];
                SavedItem matched = null;
                for (int j = 0; j < extB; j++)
                {
                    var bi = b.Extended[j];
                    if (bi != null && bi.GridX == ai.GridX && bi.GridY == ai.GridY)
                    {
                        matched = bi;
                        break;
                    }
                }
                if (!SameSavedItem(ai, matched)) return false;
            }
            return true;
        }

        private static bool SameSavedItem(SavedItem a, SavedItem b)
        {
            bool aEmpty = a == null || string.IsNullOrEmpty(a.SharedName);
            bool bEmpty = b == null || string.IsNullOrEmpty(b.SharedName);
            if (aEmpty && bEmpty) return true;
            if (aEmpty || bEmpty) return false;
            return a.SharedName == b.SharedName
                && a.Quality    == b.Quality
                && a.Variant    == b.Variant;
        }

        private static void ShowDeleteConfirm(int index)
        {
            CloseConfirmModal();

            var parent = GUIManager.CustomGUIFront.transform;
            _confirmModal = GUIManager.Instance.CreateWoodpanel(
                parent:    parent,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position:  Vector2.zero,
                width:     420f,
                height:    180f,
                draggable: false);
            _confirmModal.transform.SetAsLastSibling();

            var slotName = _data.Slots[index].Name;
            GUIManager.Instance.CreateText(
                text:      $"Delete '{slotName}'?",
                parent:    _confirmModal.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position:  new Vector2(0f, -42f),
                font:      GUIManager.Instance.AveriaSerifBold,
                fontSize:  20,
                color:     GUIManager.Instance.ValheimOrange,
                outline:   true,
                outlineColor: Color.black,
                width:     380f,
                height:    32f,
                addContentSizeFitter: false);

            GUIManager.Instance.CreateText(
                text:      "This loadout will be removed.",
                parent:    _confirmModal.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position:  new Vector2(0f, -80f),
                font:      GUIManager.Instance.AveriaSerifBold,
                fontSize:  14,
                color:     new Color(0.85f, 0.80f, 0.70f),
                outline:   true,
                outlineColor: Color.black,
                width:     380f,
                height:    24f,
                addContentSizeFitter: false);

            // Delete (red) and Cancel buttons.
            var delGo = GUIManager.Instance.CreateButton(
                text:      "Delete",
                parent:    _confirmModal.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position:  new Vector2(-75f, 36f),
                width:     130f,
                height:    40f);
            var delLabel = delGo.GetComponentInChildren<Text>();
            if (delLabel != null) delLabel.color = new Color(0.95f, 0.35f, 0.35f);
            var delBtn    = delGo.GetComponent<Button>();
            var delColors = delBtn.colors;
            delColors.highlightedColor = new Color(0.85f, 0.20f, 0.20f, 1f);
            delColors.pressedColor     = new Color(0.65f, 0.12f, 0.12f, 1f);
            delBtn.colors              = delColors;
            int captured = index;
            delBtn.onClick.AddListener(() => { CloseConfirmModal(); DeleteSlot(captured); });

            var cancelGo = GUIManager.Instance.CreateButton(
                text:      "Cancel",
                parent:    _confirmModal.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position:  new Vector2(75f, 36f),
                width:     130f,
                height:    40f);
            cancelGo.GetComponent<Button>().onClick.AddListener(CloseConfirmModal);
        }

        private static void CloseConfirmModal()
        {
            if (_confirmModal != null) { Object.Destroy(_confirmModal); _confirmModal = null; }
        }

        private static void OnCompareClicked(int index)
        {
            Jotunn.Logger.LogInfo($"[Armory] Click: Compare slot {index}");
            CommitPendingEdit();
            if (_compareSlot == index) { ToggleCompareOff(); return; }
            _compareSlot = index;
            BuildCompareWindow();
        }

        private static void UpdateRowEditState(SlotRowRefs refs, int index)
        {
            bool editing = _editingSlot == index;
            if (refs.NameLabelGo != null) refs.NameLabelGo.SetActive(!editing);
            if (refs.NameInputGo != null) refs.NameInputGo.SetActive(editing);
            if (!editing && refs.NameLabel != null) refs.NameLabel.text = _data.Slots[index].Name;
        }

        // Clicking the slot's name label enters rename mode.  If we're already editing another
        // row, commit that one first so its changes aren't lost.
        private static void OnNameClicked(int index)
        {
            Jotunn.Logger.LogInfo($"[Armory] Click: Name slot {index} (currently editing={_editingSlot})");
            if (_editingSlot == index) return;  // already editing this one
            if (_editingSlot >= 0) CommitPendingEdit();

            _editingSlot = index;
            if (_rows[index].NameInput != null)
                _rows[index].NameInput.text = _data.Slots[index].Name;
            UpdateRowEditState(_rows[index], index);

            // Focus the input field so keystrokes are routed to it.
            var inp = _rows[index].NameInput;
            if (inp != null)
            {
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(inp.gameObject);
                inp.ActivateInputField();
                inp.caretPosition = inp.text.Length;
            }
        }

        // Commits an in-progress rename (if any).  Called at the start of other click handlers
        // so the user's typed name is saved before Save/Load/Clear/etc. read slot data.
        private static void CommitPendingEdit()
        {
            if (_editingSlot < 0) return;
            int index = _editingSlot;
            var newName = _rows[index].NameInput.text.Trim();
            if (string.IsNullOrEmpty(newName)) newName = $"Slot {index + 1}";
            _data.Slots[index].Name = newName;
            _rack.SaveData(_data);
            _editingSlot = -1;
            UpdateRowEditState(_rows[index], index);
            RefreshRowVisual(index);
        }

        private static void RefreshRowVisual(int index)
        {
            if (index < 0 || index >= _rows.Count) return;
            var refs = _rows[index];
            if (refs.NameLabel != null) refs.NameLabel.text = _data.Slots[index].Name;
            if (refs.Summary   != null) refs.Summary.text   = BuildSummary(_data.Slots[index]);
            RebuildIcons(refs, _data.Slots[index]);
            UpdateRowEmptyState(refs, _data.Slots[index]);
        }

        // ── Compare window ─────────────────────────────────────────────────────────

        private static void ToggleCompareOff()
        {
            _compareSlot = -1;
            if (_comparePanel != null)
            {
                var rt = _comparePanel.GetComponent<RectTransform>();
                if (rt != null) _savedComparePos = rt.anchoredPosition;
                Object.Destroy(_comparePanel);
                _comparePanel = null;
            }
        }

        private static void BuildCompareWindow()
        {
            if (_comparePanel != null) { Object.Destroy(_comparePanel); _comparePanel = null; }
            if (_compareSlot < 0 || _compareSlot >= _data.Slots.Count) return;
            var slot   = _data.Slots[_compareSlot];
            var player = Player.m_localPlayer;
            if (player == null) return;

            var parent = GUIManager.CustomGUIFront.transform;
            var comparePos = _savedComparePos ?? new Vector2(300f, -610f);  // below the main panel by default
            _comparePanel = GUIManager.Instance.CreateWoodpanel(
                parent: parent,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position:  comparePos,
                width:  560f,
                height: 360f,
                draggable: true);

            // Title.
            GUIManager.Instance.CreateText(
                text:      $"Compare: {slot.Name}",
                parent:    _comparePanel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position:  new Vector2(0f, -28f),
                font:      GUIManager.Instance.AveriaSerifBold,
                fontSize:  20,
                color:     GUIManager.Instance.ValheimOrange,
                outline:   true,
                outlineColor: Color.black,
                width:     400f,
                height:    40f,
                addContentSizeFitter: false);

            // Close button.
            var closeBtn = GUIManager.Instance.CreateButton("x", _comparePanel.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -28f), 36f, 36f);
            closeBtn.GetComponent<Button>().onClick.AddListener(ToggleCompareOff);

            // Vertical grid of rows.
            var grid = new GameObject("CompareGrid", typeof(RectTransform));
            grid.transform.SetParent(_comparePanel.transform, false);
            var gridRT = grid.GetComponent<RectTransform>();
            gridRT.anchorMin = new Vector2(0f, 0f);
            gridRT.anchorMax = new Vector2(1f, 1f);
            gridRT.offsetMin = new Vector2(20f, 30f);
            gridRT.offsetMax = new Vector2(-20f, -60f);
            var vlg = grid.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childForceExpandWidth = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;

            // Header row.
            AddCompareHeader(grid.transform);

            var inv = player.GetInventory();
            var cur = LoadoutManager.CaptureCurrentEquipment(player);
            AddCompareRow(grid.transform, "Helmet",  cur.Helmet,   slot.Helmet,   inv, slot.IncludesWorn(WornSlot.Helmet));
            AddCompareRow(grid.transform, "Chest",   cur.Chest,    slot.Chest,    inv, slot.IncludesWorn(WornSlot.Chest));
            AddCompareRow(grid.transform, "Legs",    cur.Legs,     slot.Legs,     inv, slot.IncludesWorn(WornSlot.Legs));
            AddCompareRow(grid.transform, "Cape",    cur.Shoulder, slot.Shoulder, inv, slot.IncludesWorn(WornSlot.Cape));
            AddCompareRow(grid.transform, "Belt",    cur.Utility,  slot.Utility,  inv, slot.IncludesWorn(WornSlot.Belt));
            AddCompareRow(grid.transform, "Trinket", cur.Trinket,  slot.Trinket,  inv, slot.IncludesWorn(WornSlot.Trinket));
        }

        private static void AddCompareHeader(Transform parent)
        {
            var row = new GameObject("Header", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childForceExpandWidth = false;
            hlg.childControlWidth = true;
            row.AddComponent<LayoutElement>().preferredHeight = 22f;

            AddText(row.transform, "Slot",     14, 70f,  20f, GUIManager.Instance.ValheimOrange);
            AddText(row.transform, "Equipped", 14, 200f, 20f, GUIManager.Instance.ValheimOrange);
            AddText(row.transform, "Loadout",  14, 200f, 20f, GUIManager.Instance.ValheimOrange);
        }

        private static void AddCompareRow(Transform parent, string label, SavedItem current, SavedItem saved, Inventory inv,
                                          bool included = true)
        {
            var row = new GameObject($"Row_{label}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childForceExpandWidth = false;
            hlg.childControlWidth = true;
            row.AddComponent<LayoutElement>().preferredHeight = 20f;

            AddText(row.transform, label, 12, 70f, 18f, included ? new Color(0.75f, 0.7f, 0.55f) : new Color(0.45f, 0.45f, 0.45f));
            AddText(row.transform, current != null ? LoadoutManager.GetItemDisplayName(current) : "—",
                    12, 200f, 18f, Color.white);

            string savedText;
            Color  savedColor;
            if (saved == null)
            {
                savedText  = "—";
                savedColor = new Color(0.45f, 0.45f, 0.45f);
            }
            else
            {
                var name        = LoadoutManager.GetItemDisplayName(saved);
                bool same       = current != null && current.SharedName == saved.SharedName && current.Quality == saved.Quality;
                bool inInv      = LoadoutManager.IsInInventory(inv, saved);
                savedColor      = same   ? new Color(0.4f, 0.8f, 0.4f)
                                : inInv  ? new Color(0.85f, 0.7f, 0.3f)
                                         : new Color(0.85f, 0.3f, 0.3f);
                savedText       = $"{name} (Q{saved.Quality})";
            }
            if (!included) { savedText += "  (not loaded)"; savedColor = new Color(0.45f, 0.45f, 0.45f); }
            AddText(row.transform, savedText, 12, 200f, 18f, savedColor);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        // Manually create the legacy UI.Text rather than going through Jotunn's CreateText —
        // CreateText in this Jotunn version returns a GO whose Text component isn't reliably
        // discoverable from our caller, which was breaking everything downstream.  Doing it
        // ourselves with ApplyTextStyle for the Valheim font gives a known-good (GO, Text) pair.
        private static (GameObject Go, Text Txt) AddText(Transform parent, string text, int fontSize,
                                                         float width, float height, Color color,
                                                         bool leftAlign = false, bool richText = false)
        {
            var go = new GameObject("ArmoryText", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var txt = go.AddComponent<Text>();
            txt.text            = text;
            txt.fontSize        = fontSize;
            txt.color           = color;
            txt.supportRichText = richText;
            txt.alignment       = leftAlign ? TextAnchor.MiddleLeft : TextAnchor.MiddleLeft;
            txt.raycastTarget   = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow   = VerticalWrapMode.Overflow;

            // Apply Valheim's font + outline.  ApplyTextStyle(Text, Color) is the legacy-Text
            // overload; passing the explicit color so it isn't stomped by the helper.
            try { GUIManager.Instance.ApplyTextStyle(txt, color); }
            catch { /* fall through with default rendering */ }
            txt.color    = color;     // re-apply in case ApplyTextStyle overrode
            txt.fontSize = fontSize;  // ditto for size

            if (width > 0f)
            {
                var le = go.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                if (height > 0f) le.preferredHeight = height;
            }
            return (go, txt);
        }

        private static void SetHeight(GameObject go, float height)
        {
            if (go == null) return;
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }

        private static GameObject AddRowButton(Transform parent, string text, float width, UnityEngine.Events.UnityAction onClick)
        {
            var go = GUIManager.Instance.CreateButton(text, parent, Vector2.zero, Vector2.zero, Vector2.zero, width, 44f);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth  = width;
            le.preferredHeight = 44f;
            return go;
        }

        // One line per loadout: what it loads, by category, hotbar items keyed by their number.
        // Missing items read red; an included empty worn slot reads as "no helm" and so on.
        private static string BuildSummary(LoadoutSlot slot)
        {
            if (slot.IsEmpty()) return "<color=#666666>empty</color>";
            var playerInv = Player.m_localPlayer?.GetInventory();
            var rackInv   = _rack?.GetStorageInventory();
            var entries   = LoadoutCategories.Entries(slot);

            var parts = new List<string>(8);
            foreach (var cat in LoadoutCategories.Order)
            {
                var names = new List<string>();
                foreach (var e in entries)
                {
                    if (e.Category != cat || !e.Included) continue;
                    if (e.Item == null)
                    {
                        if (e.Worn) names.Add($"<color=#777777>no {e.Label.ToLower()}</color>");
                        continue;
                    }
                    var name = LoadoutManager.GetItemDisplayName(e.Item);
                    if (e.HotbarIndex >= 0) name = $"{e.Label}={name}";
                    bool available = LoadoutManager.IsItemAvailable(e.Item, playerInv, rackInv);
                    names.Add(available ? name : $"<color=#d8584a>{name}</color>");
                }
                if (names.Count == 0) continue;
                parts.Add($"<color=#b8943a>{LoadoutCategories.Label(cat)}:</color>{string.Join(" ", names)}");
            }
            return parts.Count > 0
                ? string.Join("  ", parts)
                : "<color=#666666>nothing in this loadout — press + to add cells</color>";
        }
    }

}
