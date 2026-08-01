using System.Collections.Generic;
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

        // Track whether WE set Time.timeScale = 0 so Close() only restores it if we did.
        private static bool  _pausedGame;
        private static float _previousTimeScale;

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
            public Button      ClearButton;
            public Text        Summary;
            public GameObject  IconsContainer;  // re-populated on data change
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
        private const float IconStripPx    = IconSize + 6f; // icons + the 6px hgroup padding
        private const float RowPaddingPx   = 8f;         // VLG vertical padding (4 top + 4 bottom)
        private const float RowSpacingPx   = 2f;         // VLG spacing between adjacent visible children
        private const float ScrollListPadPx = 8f;        // content VLG vertical padding (4 top + 4 bottom)
        private const float ScrollListGapPx = 4f;        // content VLG spacing between rows

        // Track previous toggle values so Tick can detect a config flip and trigger a resize.
        private static bool? _lastShowSummary;
        private static bool? _lastShowIcons;

        // Tracked so ResizeMainPanel can update the scroll view too.
        private static RectTransform _scrollRT;

        // ── Public API ─────────────────────────────────────────────────────────────

        public static void Open(ArmoryRack rack)
        {
            if (_isOpen) Close();

            _rack = rack;
            _data = rack.GetData();
            LoadoutManager.EnsureSlots(_data, DefaultSlotCount);
            Jotunn.Logger.LogInfo($"[Armory] Open: rack instance {rack.GetInstanceID()}, slots loaded={_data.Slots.Count}");
            for (int i = 0; i < _data.Slots.Count; i++)
                Jotunn.Logger.LogInfo($"[Armory]   slot {i}: name='{_data.Slots[i].Name}' empty={_data.Slots[i].IsEmpty()}");
            _editingSlot = -1;
            _compareSlot = -1;
            _isOpen      = true;

            BuildUI();

            // Migrate items left out-of-bounds by a previous container size (e.g. the brief
            // 8×5 layout — anything at y=4 is now outside our 10×4 grid and would be hidden
            // from the vanilla chest UI).  Move them into the first empty in-bounds slot
            // before InventoryGui.Show so the player sees a clean grid with all their items.
            LoadoutManager.MigrateOutOfBoundsItems(rack.GetStorageInventory());

            // Show vanilla inventory + the rack's storage container alongside our window.
            // Passing the Container makes the standard chest-style two-panel layout appear:
            // player inv on the left, rack storage on the right.  Items can be drag/dropped
            // between them normally, and Load will pull from either.
            if (InventoryGui.instance != null)
                InventoryGui.instance.Show(rack.GetStorage());

            // Optionally pause the game while the panel is open (config-gated, default off).
            bool shouldPause = ArmoryMod.PauseGameWhileOpen != null && ArmoryMod.PauseGameWhileOpen.Value;
            Jotunn.Logger.LogInfo($"[Armory] Open: PauseGameWhileOpen={shouldPause}, currentTimeScale={Time.timeScale}");
            if (shouldPause)
            {
                _previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale     = 0f;
                _pausedGame        = true;
                Jotunn.Logger.LogInfo($"[Armory] Open: paused — previousTimeScale={_previousTimeScale}, newTimeScale={Time.timeScale}");
            }
        }

        public static void Close()
        {
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
            _slotsContent = null;
            _rows.Clear();
            _lastShowSummary = null;
            _lastShowIcons   = null;

            _rack = null;

            if (InventoryGui.instance != null)
                InventoryGui.instance.Hide();

            // Restore the time scale we changed in Open() (only if WE were the one who paused).
            if (_pausedGame)
            {
                Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
                _pausedGame    = false;
                Jotunn.Logger.LogInfo($"[Armory] Close: restored timeScale={Time.timeScale}");
            }
        }

        public static void Tick()
        {
            if (!_isOpen) return;

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

            // Refresh icon availability colors a few times a second (not every frame — rebuilding
            // 13×N icons per frame is wasteful and would flicker).  Triggers when items get
            // added/removed from rack or player inv while the panel is open.
            if (showIcons && Time.unscaledTime - _lastIconRefresh > 0.5f)
            {
                _lastIconRefresh = Time.unscaledTime;
                for (int i = 0; i < _rows.Count && i < _data.Slots.Count; i++)
                    RebuildIcons(_rows[i], _data.Slots[i]);
            }

            // Re-assert Time.timeScale = 0 each frame.  Some Valheim code (or other mods) may
            // reset timeScale during their own Update loop, undoing our pause.  Holding it at
            // 0 every frame defeats that.
            if (_pausedGame && Time.timeScale != 0f)
                Time.timeScale = 0f;
        }

        // ── UI construction ────────────────────────────────────────────────────────

        private static void BuildUI()
        {
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;
            var parent = GUIManager.CustomGUIFront.transform;

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
                width:     760f,
                height:    ComputePanelHeight(),
                draggable: true);

            BuildMainPanel(_mainPanel.transform);
        }

        private static void BuildMainPanel(Transform panel)
        {
            // Title text.
            GUIManager.Instance.CreateText(
                text: "Armory Rack",
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

            // Scrollable slot-list area.
            var scrollGo = GUIManager.Instance.CreateScrollView(
                parent:                      panel,
                showHorizontalScrollbar:     false,
                showVerticalScrollbar:       true,
                handleSize:                  10f,
                handleDistanceToBorder:      4f,
                handleColors:                new ColorBlock { normalColor = new Color(0.5f, 0.4f, 0.2f, 0.8f), highlightedColor = new Color(0.7f, 0.55f, 0.25f, 0.9f), pressedColor = new Color(0.4f, 0.3f, 0.15f, 1f), selectedColor = new Color(0.6f, 0.5f, 0.2f, 0.9f), disabledColor = new Color(0.3f, 0.25f, 0.15f, 0.5f), colorMultiplier = 1f, fadeDuration = 0.1f },
                slidingAreaBackgroundColor:  new Color(0.04f, 0.04f, 0.04f, 0.6f),
                width:                       700f,
                height:                      ComputePanelHeight() - TitleAreaPx - FooterAreaPx);
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRect.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRect.anchoredPosition = new Vector2(0f, 10f);
            _scrollRT = scrollRect;

            var scrollComp = scrollGo.GetComponentInChildren<ScrollRect>();
            if (scrollComp == null || scrollComp.content == null)
            {
                Jotunn.Logger.LogError("[Armory] CreateScrollView did not yield a ScrollRect with content.");
                return;
            }
            // Default ScrollRect.scrollSensitivity is ~1, which feels glacial with a real mouse
            // wheel.  Jotunn's ScrollView may nest multiple ScrollRects, so bump every one of
            // them — and set a much higher value so a single wheel notch moves several rows.
            var allScrollRects = scrollGo.GetComponentsInChildren<ScrollRect>(true);
            foreach (var sr in allScrollRects)
            {
                Jotunn.Logger.LogInfo($"[Armory] ScrollRect found on '{sr.name}': sensitivity was {sr.scrollSensitivity}");
                sr.scrollSensitivity = 100f;
            }
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

            RebuildSlotRows();
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
            var nameLabelPair = AddText(topRow.transform, slot.Name, 16, 200f, 44f, GUIManager.Instance.ValheimOrange, leftAlign: true);
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
            var saveGo = AddRowButton(topRow.transform, SaveIcon, 44f, () => OnSaveClicked(index));
            AttachTooltip(saveGo, "Save");
            var loadGo = AddRowButton(topRow.transform, LoadIcon, 44f, () => OnLoadClicked(index));
            AttachTooltip(loadGo, "Load");
            refs.LoadButton      = loadGo.GetComponent<Button>();
            refs.LoadButtonLabel = loadGo.GetComponentInChildren<Text>();
            refs.LoadButtonDefaultColor = refs.LoadButtonLabel != null ? refs.LoadButtonLabel.color : Color.white;
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

            // ── Bottom line: summary ──
            var summaryPair = AddText(row.transform, BuildSummary(slot), 12, 480f, 22f,
                                      new Color(0.78f, 0.72f, 0.58f), leftAlign: true, richText: true);
            refs.Summary = summaryPair.Txt;
            SetHeight(summaryPair.Go, 22f);

            // ── Icons strip: visual icons for all armor + hotbar slots.
            refs.IconsContainer = BuildIconsStrip();
            refs.IconsContainer.transform.SetParent(row.transform, false);
            PopulateIcons(refs.IconsContainer.transform, slot);

            refs.Root = row;
            UpdateRowEditState(refs, index);
            UpdateRowEmptyState(refs, _data.Slots[index]);
            return refs;
        }

        // Creates the empty icons strip container (HorizontalLayoutGroup).  PopulateIcons fills
        // it with image children for each saved item / empty placeholder.
        private static GameObject BuildIconsStrip()
        {
            var go = new GameObject("Icons", typeof(RectTransform));
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 3;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = true;   // respect each cell's LayoutElement.preferredWidth (30)
            hlg.childControlHeight     = true;   // respect each cell's LayoutElement.preferredHeight (30)
            hlg.padding                = new RectOffset(4, 4, 2, 2);
            hlg.childAlignment         = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = IconSize + 6f;
            return go;
        }

        private static void PopulateIcons(Transform parent, LoadoutSlot slot)
        {
            var playerInv = Player.m_localPlayer?.GetInventory();
            var rackInv   = _rack?.GetStorageInventory();
            AddIcon(parent, slot.Helmet,   playerInv, rackInv);
            AddIcon(parent, slot.Chest,    playerInv, rackInv);
            AddIcon(parent, slot.Legs,     playerInv, rackInv);
            AddIcon(parent, slot.Shoulder, playerInv, rackInv);
            AddIcon(parent, slot.Utility,  playerInv, rackInv);
            AddIconSpacer(parent, 10f);  // visual gap between armor and hotbar
            if (slot.Hotbar != null)
                for (int i = 0; i < 8 && i < slot.Hotbar.Length; i++)
                    AddIcon(parent, slot.Hotbar[i], playerInv, rackInv);
            // Extended-inventory items (Azu food/potion/trinket slots) — only render if the
            // loadout actually saved any, with a visual gap separating them from the hotbar.
            if (slot.Extended != null && slot.Extended.Count > 0)
            {
                AddIconSpacer(parent, 10f);
                foreach (var ext in slot.Extended)
                    AddIcon(parent, ext, playerInv, rackInv);
            }
        }

        private const float IconSize = 60f;

        // Each "icon cell" is a square with a background-color Image (state indicator) and,
        // when there's a saved item, a child Image with the actual sprite on top of it.
        // Background color: empty=dim grey, available=warm dark brown, missing=red.
        private static void AddIcon(Transform parent, SavedItem item, Inventory playerInv, Inventory rackInv)
        {
            var cell = new GameObject(item?.SharedName ?? "EmptySlot", typeof(RectTransform));
            cell.transform.SetParent(parent, false);
            var le = cell.AddComponent<LayoutElement>();
            le.preferredWidth  = IconSize;
            le.preferredHeight = IconSize;

            var bg = cell.AddComponent<Image>();
            bg.raycastTarget = true;  // needed for hover/tooltip detection

            if (item == null)
            {
                bg.color = new Color(0.10f, 0.10f, 0.10f, 0.35f);
            }
            else
            {
                bool available = LoadoutManager.IsItemAvailable(item, playerInv, rackInv);
                bg.color = available
                    ? new Color(0.15f, 0.12f, 0.08f, 0.80f)
                    : new Color(0.55f, 0.15f, 0.15f, 0.90f);  // red when not findable
            }

            // Icon sprite layered on top of the cell background.
            if (item != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(cell.transform, false);
                var rt = iconGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(2f, 2f);
                rt.offsetMax = new Vector2(-2f, -2f);

                var img = iconGo.AddComponent<Image>();
                img.raycastTarget = false;  // let bg receive the hover
                var sprite = LoadoutManager.GetIcon(item);
                if (sprite != null)
                {
                    img.sprite        = sprite;
                    img.preserveAspect = true;
                    img.color         = Color.white;
                }
                else
                {
                    img.color = new Color(0.5f, 0.4f, 0.3f, 0.7f);
                }

                // Hover tooltip — custom implementation since Valheim's UITooltip needs a
                // tooltip prefab the HUD wires up at game init and NREs on our Canvas.
                AttachTooltip(cell, LoadoutManager.GetItemDisplayName(item));
            }
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

        private static void AttachTooltip(GameObject host, string text)
        {
            var trigger = host.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowTooltip(text, Input.mousePosition));
            trigger.triggers.Add(enter);
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideTooltip());
            trigger.triggers.Add(exit);
        }

        private static void AddIconSpacer(Transform parent, float width)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = width;
        }

        private static void RebuildIcons(SlotRowRefs refs, LoadoutSlot slot)
        {
            if (refs?.IconsContainer == null) return;
            foreach (Transform child in refs.IconsContainer.transform)
                Object.Destroy(child.gameObject);
            PopulateIcons(refs.IconsContainer.transform, slot);
        }

        // Manages the per-row dynamic button states:
        //  - Load / Compare / Clear are disabled when the slot is empty.
        //  - Load becomes "Active" in green (and disabled) when the player is currently
        //    wearing exactly what this slot has saved.
        private static void UpdateRowEmptyState(SlotRowRefs refs, LoadoutSlot slot)
        {
            bool hasContent = !slot.IsEmpty();
            bool isActive   = hasContent && LoadoutManager.IsLoadoutActive(slot, Player.m_localPlayer);

            if (refs.CompareButton != null) refs.CompareButton.interactable = hasContent;
            if (refs.ClearButton   != null) refs.ClearButton.interactable   = true;  // Delete works on empty slots too
            if (refs.LoadButton    != null) refs.LoadButton.interactable    = hasContent && !isActive;
            if (refs.LoadButtonLabel != null)
            {
                // Green ● (U+25CF) when the slot's gear matches what the player is currently
                // wearing — replaces the prior "Active" text so the button can stay square.
                refs.LoadButtonLabel.text  = isActive ? "●" : LoadIcon;
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
        private static float ComputeRowHeight()
        {
            bool showSummary = ArmoryMod.ShowSummaryText?.Value ?? true;
            bool showIcons   = ArmoryMod.ShowIcons?.Value       ?? true;
            float h = TopRowPx + RowPaddingPx;     // always-present pieces
            int gaps = 0;                          // VLG only adds spacing between *visible* children
            if (showSummary) { h += SummaryRowPx;  gaps++; }
            if (showIcons)   { h += IconStripPx;   gaps++; }
            h += gaps * RowSpacingPx;
            return h;
        }

        // Compute desired panel height based on the current slot count, clamped to the screen.
        private static float ComputePanelHeight()
        {
            int   slotCount = _data?.Slots?.Count ?? DefaultSlotCount;
            float rowH      = ComputeRowHeight();
            float listH     = slotCount * rowH
                            + System.Math.Max(0, slotCount - 1) * ScrollListGapPx
                            + ScrollListPadPx;
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

            float newHeight = ComputePanelHeight();
            panelRT.sizeDelta = new Vector2(panelRT.sizeDelta.x, newHeight);

            if (_scrollRT != null)
                _scrollRT.sizeDelta = new Vector2(_scrollRT.sizeDelta.x, newHeight - TitleAreaPx - FooterAreaPx);
        }

        private static void OnSaveClicked(int index)
        {
            Jotunn.Logger.LogInfo($"[Armory] Click: Save slot {index}");
            CommitPendingEdit();
            var player = Player.m_localPlayer;
            if (player == null) { Jotunn.Logger.LogWarning("[Armory] OnSaveClicked: no local player"); return; }
            var name = _data.Slots[index].Name;
            var captured = LoadoutManager.CaptureCurrentEquipment(player);
            captured.Name = name;
            _data.Slots[index] = captured;
            _rack.SaveData(_data);
            player.Message(MessageHud.MessageType.Center, $"Saved: {name}");
            Jotunn.Logger.LogInfo($"[Armory] Saved slot {index} '{name}': {BuildSummary(captured)}");
            RefreshRowVisual(index);
        }

        private static void OnLoadClicked(int index)
        {
            Jotunn.Logger.LogInfo($"[Armory] Click: Load slot {index}");
            CommitPendingEdit();
            var player = Player.m_localPlayer;
            if (player == null) return;
            var slot = _data.Slots[index];
            if (slot.IsEmpty()) { Jotunn.Logger.LogInfo($"[Armory] Load slot {index}: slot is empty, ignoring"); return; }

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
            player.Message(MessageHud.MessageType.Center, msg);
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

            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Deleted: {name}");
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
            if (!SameSavedItem(a.RightHand, b.RightHand)) return false;
            if (!SameSavedItem(a.LeftHand,  b.LeftHand))  return false;
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
            AddCompareRow(grid.transform, "Helmet",  cur.Helmet,    slot.Helmet,    inv);
            AddCompareRow(grid.transform, "Chest",   cur.Chest,     slot.Chest,     inv);
            AddCompareRow(grid.transform, "Legs",    cur.Legs,      slot.Legs,      inv);
            AddCompareRow(grid.transform, "Cape",    cur.Shoulder,  slot.Shoulder,  inv);
            AddCompareRow(grid.transform, "Utility", cur.Utility,   slot.Utility,   inv);
            AddCompareRow(grid.transform, "Weapon",  cur.RightHand, slot.RightHand, inv);
            AddCompareRow(grid.transform, "Shield",  cur.LeftHand,  slot.LeftHand,  inv);
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

        private static void AddCompareRow(Transform parent, string label, SavedItem current, SavedItem saved, Inventory inv)
        {
            var row = new GameObject($"Row_{label}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childForceExpandWidth = false;
            hlg.childControlWidth = true;
            row.AddComponent<LayoutElement>().preferredHeight = 20f;

            AddText(row.transform, label, 12, 70f, 18f, new Color(0.75f, 0.7f, 0.55f));
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

        private static string BuildSummary(LoadoutSlot slot)
        {
            if (slot.IsEmpty()) return "<color=#666666>empty</color>";
            var playerInv = Player.m_localPlayer?.GetInventory();
            var rackInv   = _rack?.GetStorageInventory();

            var parts = new List<string>(8);
            Append(parts, "Helm",  slot.Helmet,    playerInv, rackInv);
            Append(parts, "Chest", slot.Chest,     playerInv, rackInv);
            Append(parts, "Legs",  slot.Legs,      playerInv, rackInv);
            Append(parts, "Cape",  slot.Shoulder,  playerInv, rackInv);
            Append(parts, "Wpn",   slot.RightHand, playerInv, rackInv);
            Append(parts, "Shld",  slot.LeftHand,  playerInv, rackInv);
            Append(parts, "Util",  slot.Utility,   playerInv, rackInv);

            // Append a compact hotbar summary like "Hotbar: 1=Axe 2=Club 5=Hammer".  Missing
            // hotbar items are individually red-tinted so the player can see which slot is short.
            if (slot.Hotbar != null)
            {
                var hotParts = new List<string>(8);
                for (int i = 0; i < slot.Hotbar.Length; i++)
                {
                    var h = slot.Hotbar[i];
                    if (h == null || string.IsNullOrEmpty(h.SharedName)) continue;
                    bool available = LoadoutManager.IsItemAvailable(h, playerInv, rackInv);
                    var entry = $"{i + 1}={LoadoutManager.GetItemDisplayName(h)}";
                    hotParts.Add(available ? entry : $"<color=#d8584a>{entry}</color>");
                }
                if (hotParts.Count > 0)
                    parts.Add($"<color=#b8943a>Hotbar:</color>{string.Join(" ", hotParts)}");
            }

            // Extended-inventory items (Azu food/potion/trinket slots).  Same color rules.
            if (slot.Extended != null && slot.Extended.Count > 0)
            {
                var extParts = new List<string>(slot.Extended.Count);
                foreach (var ext in slot.Extended)
                {
                    if (ext == null || string.IsNullOrEmpty(ext.SharedName)) continue;
                    bool available = LoadoutManager.IsItemAvailable(ext, playerInv, rackInv);
                    var entry = LoadoutManager.GetItemDisplayName(ext);
                    extParts.Add(available ? entry : $"<color=#d8584a>{entry}</color>");
                }
                if (extParts.Count > 0)
                    parts.Add($"<color=#b8943a>Extra:</color>{string.Join(" ", extParts)}");
            }
            return string.Join("  ", parts);
        }

        private static void Append(List<string> parts, string label, SavedItem item, Inventory playerInv, Inventory rackInv)
        {
            if (item == null) return;
            if (LoadoutManager.IsItemAvailable(item, playerInv, rackInv))
                parts.Add($"<color=#b8943a>{label}:</color>{LoadoutManager.GetItemDisplayName(item)}");
            else
                parts.Add($"<color=#d8584a>{label}:{LoadoutManager.GetItemDisplayName(item)}</color>");
        }
    }

}
