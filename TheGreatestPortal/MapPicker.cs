using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestPortal
{
    /// <summary>
    /// Choosing a portal on the large map. Three ways in:
    ///  * Travel: you stepped into an open portal. Click a portal (pin or list) and you go there.
    ///  * Pick: the panel's "Pick on map" button. Click a portal and it becomes the destination.
    ///  * Browse: the toggle key on the map, or "Show on map". Click a portal to center on it.
    /// In all three the map shows every portal as a pin and, on the left, a searchable list with
    /// favorites on top, optionally grouped by biome. Right-clicking a portal (pin or row) marks
    /// it as a favorite. Closing the map ends it.
    /// </summary>
    internal static class MapPicker
    {
        internal enum Mode { None, Travel, Pick, Browse }

        internal static Mode Current { get; private set; }
        internal static bool Active => Current != Mode.None;
        internal static bool IsSelecting => Current == Mode.Travel || Current == Mode.Pick;
        internal static bool ListPointerOver => _list != null && _listHover != null && _listHover.Over;
        internal static bool SearchFocused => UiKit.IsFocused(_search);

        private static TeleportWorld _source;
        private static ZDOID _sourceZdo = ZDOID.None;
        private static long _sourceId;
        private static bool _sourceAllowAll;
        private static Collider _trigger;
        private static Collider _playerCollider;
        private static float _lostAt = -1f;
        private static Action<PortalInfo> _onPicked;
        private static long _highlightId;
        private static string _query = "";
        private static bool _detour;
        private static long _hoverId;
        private static bool _hoverFromRow;
        private static string _usualDestination = "";

        private static GameObject _list;
        private static UiKit.Hover _listHover;
        private static RectTransform _listContent;
        private static ScrollRect _scroll;
        private static TextMeshProUGUI _header;
        private static TextMeshProUGUI _hint;
        private static TMP_InputField _search;
        private static Toggle _group;
        private static Button _expandAll;
        private static Button _collapseAll;
        private static List<ListEntry> _entries = new List<ListEntry>();
        private static readonly List<UiKit.RowHandle> _rows = new List<UiKit.RowHandle>();
        private static readonly List<long> _rowIds = new List<long>();
        private static readonly List<Minimap.PinData> _hidden = new List<Minimap.PinData>();
        private static bool _catalogDirty;

        static MapPicker()
        {
            Catalog.Changed += () => _catalogDirty = true;
        }

        // ── what kind of portal is this ─────────────────────────────────────────────

        private static ZDO ZdoOf(TeleportWorld portal)
        {
            if (portal == null) return null;
            var nview = portal.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        /// <summary>An open portal: no destination of its own, so stepping in shows the map.</summary>
        internal static bool IsOpenPortal(TeleportWorld portal)
        {
            if (TgpConfig.UntargetedOpensMap == null || !TgpConfig.UntargetedOpensMap.Value) return false;
            return PortalData.IsOpen(ZdoOf(portal));
        }

        /// <summary>Why a portal with a destination cannot be used right now, or null when vanilla may teleport.</summary>
        internal static string BlockedReason(TeleportWorld portal)
        {
            var zdo = ZdoOf(portal);
            if (zdo == null) return null;
            long to = PortalData.GetTarget(zdo);
            if (to == 0L || PortalData.Connection(zdo) != ZDOID.None) return null;
            if (Catalog.HasSnapshot && Catalog.Get(to) == null) return "The destination portal is gone";
            return "The portal is still connecting; step in again in a moment";
        }

        // ── entry points ────────────────────────────────────────────────────────────

        /// <summary>
        /// The destination map for a trip out of <paramref name="portal"/>. A detour is the same
        /// trip taken from a portal that already has a destination: it is chosen for this trip
        /// only and nothing about the portal changes.
        /// </summary>
        internal static void BeginTravel(TeleportWorld portal, Collider trigger, Collider playerCollider, bool detour = false)
        {
            var player = Player.m_localPlayer;
            if (player == null || Minimap.instance == null || portal == null) return;
            if (Active) End();
            if (!Travel.CanTeleport(player, portal.m_allowAllItems)) return;
            var zdo = ZdoOf(portal);
            _detour = detour;
            _usualDestination = "";
            if (detour)
            {
                var usual = Catalog.Get(PortalData.GetTarget(zdo));
                _usualDestination = usual != null ? usual.DisplayName : "";
            }
            _source = portal;
            _sourceZdo = zdo != null ? zdo.m_uid : ZDOID.None;
            _sourceId = PortalData.GetId(zdo);
            _sourceAllowAll = portal.m_allowAllItems;
            _trigger = trigger;
            _playerCollider = playerCollider;
            _lostAt = -1f;
            _onPicked = null;
            _highlightId = 0L;
            _query = "";
            Current = Mode.Travel;
            if (!Catalog.HasSnapshot) PortalNetwork.RequestCatalog();
            OpenMapAt(portal.transform.position);
            Refresh();
            if (Catalog.HasSnapshot && Catalog.Count <= 1) TheGreatestPortalMod.Message("No other portals to travel to", always: true);
        }

        internal static void BeginPick(ZDOID sourceZdo, long sourceId, Vector3 center, Action<PortalInfo> onPicked)
        {
            if (Minimap.instance == null) return;
            if (Active) End();
            _source = null;
            _sourceZdo = sourceZdo;
            _sourceId = sourceId;
            _sourceAllowAll = false;
            _onPicked = onPicked;
            _highlightId = 0L;
            _query = "";
            _detour = false;
            Current = Mode.Pick;
            OpenMapAt(center);
            Refresh();
        }

        internal static void BeginBrowse(Vector3? center)
        {
            var map = Minimap.instance;
            if (map == null) return;
            if (Active) End();
            _source = null;
            _sourceZdo = ZDOID.None;
            _sourceId = 0L;
            _onPicked = null;
            _highlightId = 0L;
            _query = "";
            _detour = false;
            Current = Mode.Browse;
            if (center.HasValue) OpenMapAt(center.Value);
            else if (map.m_mode != Minimap.MapMode.Large) map.SetMapMode(Minimap.MapMode.Large);
            Refresh();
        }

        internal static void End()
        {
            bool had = Active || _list != null || PortalPins.Count > 0 || _hidden.Count > 0;
            Current = Mode.None;
            _source = null;
            _sourceZdo = ZDOID.None;
            _sourceId = 0L;
            _trigger = null;
            _playerCollider = null;
            _lostAt = -1f;
            _onPicked = null;
            _highlightId = 0L;
            _query = "";
            _detour = false;
            _usualDestination = "";
            ClearHover();
            if (!had) return;
            PortalPins.Clear();
            DestroyList();
            RestoreHiddenPins();
        }

        // ── per frame ───────────────────────────────────────────────────────────────

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null)
            {
                if (Active) End();
                return;
            }
            if (map.m_mode == Minimap.MapMode.Large && Keys.CanTakeInput() && Keys.IsDown(TgpConfig.TogglePinsKey.Value))
            {
                if (Current == Mode.Browse) End();
                else if (Current == Mode.None) BeginBrowse(null);
            }
            if (!Active) return;
            if (map.m_mode != Minimap.MapMode.Large)
            {
                End();
                return;
            }
            // Escape while typing in the search box only leaves the box; the next Escape closes the map.
            if (SearchFocused && ZInput.GetKeyDown(KeyCode.Escape)) _search.DeactivateInputField();
            if (_catalogDirty)
            {
                _catalogDirty = false;
                Refresh();
            }
            UpdateHover(map);
            if (Current == Mode.Travel) UpdateAutoClose(map);
        }

        private static void UpdateAutoClose(Minimap map)
        {
            float grace = TgpConfig.AutoCloseGraceSeconds.Value;
            if (grace <= 0f || Player.m_localPlayer == null) return;
            if (InsideSource()) { _lostAt = -1f; return; }
            if (_lostAt < 0f) { _lostAt = Time.time; return; }
            if (Time.time - _lostAt < grace) return;
            map.SetMapMode(Minimap.MapMode.Small);
            End();
        }

        private static bool InsideSource()
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;
            Vector3 pos = player.transform.position;
            if (_trigger != null && _playerCollider != null && _trigger.enabled && _playerCollider.enabled)
            {
                if (_trigger.bounds.Intersects(_playerCollider.bounds)) return true;
                return Vector3.Distance(_trigger.ClosestPoint(pos), pos) <= 0.05f;
            }
            return _source != null && Vector3.Distance(_source.transform.position, pos) < 3f;
        }

        internal static void OnMapModeChanged(Minimap.MapMode mode)
        {
            if (mode == Minimap.MapMode.Large)
            {
                if (!Active && TgpConfig.ShowPinsOnMap != null && TgpConfig.ShowPinsOnMap.Value) BeginBrowse(null);
                return;
            }
            End();
        }

        // ── hover feedback ──────────────────────────────────────────────────────────

        private const float HoverScale = 1.6f;

        /// <summary>
        /// The portal under the pointer grows, brightens and shows its name, so it is obvious
        /// which one a click would take. Hovering a row in the list lights up its pin the same way.
        /// </summary>
        private static void UpdateHover(Minimap map)
        {
            if (ListPointerOver)
            {
                // The pointer is over the list, not the map: only a row can say what is hovered.
                if (!_hoverFromRow) SetHover(map, 0L, false);
            }
            else
            {
                var p = PortalUnderPointer(map);
                SetHover(map, p != null ? p.Id : 0L, false);
            }
            // Vanilla repaints the pins whenever it feels like it, so the highlight is reapplied.
            if (_hoverId != 0L) Decorate(map, _hoverId, true);
        }

        private static void SetHover(Minimap map, long id, bool fromRow)
        {
            if (_hoverId != id)
            {
                Decorate(map, _hoverId, false);
                _hoverId = id;
            }
            _hoverFromRow = id != 0L && fromRow;
        }

        private static void ClearHover()
        {
            _hoverId = 0L;
            _hoverFromRow = false;
        }

        private static void Decorate(Minimap map, long id, bool on)
        {
            if (id == 0L || map == null) return;
            var pin = PortalPins.PinFor(id);
            if (pin == null || pin.m_uiElement == null) return;
            pin.m_uiElement.localScale = on ? Vector3.one * HoverScale : Vector3.one;
            if (pin.m_iconElement != null) pin.m_iconElement.color = on ? UiKit.Gold : Color.white;
            var namePin = pin.m_NamePinData;
            if (namePin == null || namePin.PinNameGameObject == null) return;
            if (namePin.PinNameText != null) namePin.PinNameText.color = on ? UiKit.Gold : Color.white;
            // Hovering always names the portal; otherwise vanilla shows names only when zoomed in.
            namePin.PinNameGameObject.SetActive(on || (!string.IsNullOrEmpty(pin.m_name) && map.LargeZoom < map.m_showNamesZoom));
        }

        // ── clicks on the map ───────────────────────────────────────────────────────

        /// <summary>Returns true when the click was ours (vanilla must not see it).</summary>
        internal static bool HandleLeftClick(Minimap map)
        {
            if (!Active || map == null) return false;
            var p = PortalUnderPointer(map);
            if (p != null)
            {
                Select(p);
                return true;
            }
            return IsSelecting;
        }

        internal static bool HandleRightClick(Minimap map)
        {
            if (!Active || map == null) return false;
            var p = PortalUnderPointer(map);
            if (p != null)
            {
                ToggleFavorite(p);
                return true;
            }
            return IsSelecting;
        }

        private static PortalInfo PortalUnderPointer(Minimap map)
        {
            Vector3 world = Access.ScreenToWorldPoint(map, ZInput.pointerPosition);
            return PortalPins.Closest(world, Access.PinInteractRadius(map));
        }

        private static void Select(PortalInfo p)
        {
            var map = Minimap.instance;
            if (p == null || map == null) return;
            switch (Current)
            {
                case Mode.Travel:
                    if (p.Id == _sourceId || p.ZdoId == _sourceZdo)
                    {
                        TheGreatestPortalMod.Message("You are standing in that portal", always: true);
                        return;
                    }
                    if (Travel.Go(p, _sourceAllowAll))
                    {
                        map.SetMapMode(Minimap.MapMode.Small);
                        End();
                    }
                    break;
                case Mode.Pick:
                {
                    if (p.Id == _sourceId || p.ZdoId == _sourceZdo)
                    {
                        TheGreatestPortalMod.Message("A portal cannot lead to itself", always: true);
                        return;
                    }
                    var cb = _onPicked;
                    map.SetMapMode(Minimap.MapMode.Small);
                    End();
                    cb?.Invoke(p);
                    break;
                }
                default:
                    _highlightId = p.Id;
                    OpenMapAt(p.Pos);
                    for (int i = 0; i < _rows.Count; i++) _rows[i].SetSelected(_rowIds[i] == p.Id);
                    break;
            }
        }

        private static void ToggleFavorite(PortalInfo p)
        {
            if (p == null) return;
            if (p.Id == 0L)
            {
                TheGreatestPortalMod.Message("That portal has no id yet; try again in a moment", always: true);
                return;
            }
            bool on = Favorites.Toggle(p.Id);
            TheGreatestPortalMod.Message(on ? "Favorite: " + p.DisplayName : "No longer a favorite: " + p.DisplayName);
            Refresh();
        }

        private static void OpenMapAt(Vector3 position)
        {
            var map = Minimap.instance;
            if (map == null) return;
            bool noMap = Game.m_noMap;
            Game.m_noMap = false;
            map.ShowPointOnMap(position);
            Game.m_noMap = noMap;
        }

        // ── pins and the list ───────────────────────────────────────────────────────

        private static void Refresh()
        {
            var map = Minimap.instance;
            if (map == null || !Active) return;
            ClearHover();
            PortalPins.Show(PinLabel);
            RefreshList(map);
            Access.PinUpdateRequired(map) = true;
        }

        private static string PinLabel(PortalInfo p)
        {
            string s = Favorites.IsFavorite(p.Id) ? "★ " + p.DisplayName : p.DisplayName;
            if (IsSelecting && (p.Id == _sourceId && _sourceId != 0L || p.ZdoId == _sourceZdo)) s += Current == Mode.Travel ? " (you are here)" : " (this portal)";
            return s;
        }

        private static void RefreshList(Minimap map)
        {
            if (TgpConfig.ShowPortalListOnMap == null || !TgpConfig.ShowPortalListOnMap.Value || map.m_largeRoot == null)
            {
                DestroyList();
                return;
            }
            if (_list == null) BuildList(map);
            if (_list == null) return;

            switch (Current)
            {
                case Mode.Travel:
                    _header.text = _detour ? "Where to this time?" : "Where to?";
                    _hint.text = _detour
                        ? (string.IsNullOrEmpty(_usualDestination)
                            ? "This trip only; the portal keeps the destination it is set to. Esc stays here."
                            : $"This trip only; the portal still leads to {_usualDestination}. Esc stays here.")
                        : "Click a portal here or on the map to travel. Right-click marks a favorite. Esc stays here.";
                    break;
                case Mode.Pick:
                    _header.text = "Choose the destination";
                    _hint.text = "Click a portal here or on the map. Right-click marks a favorite. Esc keeps the old destination.";
                    break;
                default:
                    _header.text = "Portals";
                    _hint.text = $"Click a portal to center the map on it. Right-click marks a favorite. {TgpConfig.TogglePinsKey.Value.MainKey} hides them.";
                    break;
            }

            UiKit.ClearChildren(_listContent);
            _rows.Clear();
            _rowIds.Clear();
            var player = Player.m_localPlayer;
            Vector3 from = player != null ? player.transform.position : Vector3.zero;
            bool grouped = TgpConfig.GroupByBiome.Value;
            _entries = PortalList.Build(IsSelecting ? _sourceId : 0L, IsSelecting ? _sourceZdo : ZDOID.None, _query, grouped);
            if (_expandAll != null) _expandAll.gameObject.SetActive(grouped);
            if (_collapseAll != null) _collapseAll.gameObject.SetActive(grouped);
            if (_entries.Count == 0)
            {
                string text = !Catalog.HasSnapshot ? "Waiting for the portal list..." : (string.IsNullOrEmpty(_query) ? "No other portals yet." : $"No portal matches \"{_query}\"");
                UiKit.Row(_listContent, text, null, null, null, 16f, UiKit.Dim);
                return;
            }
            foreach (var e in _entries)
            {
                if (e.IsHeader)
                {
                    string key = e.GroupKey;
                    _rows.Add(UiKit.SectionHeader(_listContent, e.Title, key == null ? null : (Action)(() => { PortalList.ToggleCollapsed(key); RefreshList(map); }), key == null ? (bool?)null : e.Collapsed));
                    _rowIds.Add(long.MinValue);
                    continue;
                }
                var captured = e.Portal;
                string label = e.Favorite ? "★ " + captured.DisplayName : captured.DisplayName;
                string dist = TgpConfig.ShowDistances.Value && player != null ? UiKit.Distance(from, captured.Pos) : null;
                var row = UiKit.Row(_listContent, label, dist, () => Select(captured), () => ToggleFavorite(captured), 16f, e.Favorite ? UiKit.Gold : (Color?)null, null, PortalList.DestinationText(captured));
                row.SetSelected(captured.Id == _highlightId && _highlightId != 0L);
                long rowId = captured.Id;
                var rowHover = row.Hover.OnHoverChanged;
                row.Hover.OnHoverChanged = over =>
                {
                    rowHover?.Invoke(over);
                    if (over) SetHover(Minimap.instance, rowId, true);
                    else if (_hoverId == rowId) SetHover(Minimap.instance, 0L, false);
                };
                _rows.Add(row);
                _rowIds.Add(captured.Id);
            }
        }

        private static void BuildList(Minimap map)
        {
            UiKit.EnsureFont();
            _list = new GameObject("TGP_PortalList", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiKit.Hover));
            _list.transform.SetParent(map.m_largeRoot.transform, false);
            _list.transform.SetAsLastSibling();
            var rt = _list.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(20f, -60f);
            rt.sizeDelta = new Vector2(330f, 660f);
            var bg = _list.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = true;
            _listHover = _list.GetComponent<UiKit.Hover>();

            _header = UiKit.Text(_list.transform, "Header", "Portals", 22f, TextAlignmentOptions.Left, UiKit.Gold);
            UiKit.Place(_header.rectTransform, 12f, 8f, 306f, 30f);
            _hint = UiKit.Text(_list.transform, "Hint", "", 13f, TextAlignmentOptions.TopLeft, UiKit.Dim);
            _hint.textWrappingMode = TextWrappingModes.Normal;
            _hint.overflowMode = TextOverflowModes.Truncate;
            UiKit.Place(_hint.rectTransform, 12f, 40f, 306f, 54f);

            _search = UiKit.CloneInputField(_list.transform, "Search", "Search...", 40, text => { _query = (text ?? "").Trim(); RefreshList(map); }, null);
            if (_search != null) UiKit.Place(_search.GetComponent<RectTransform>(), 8f, 98f, 196f, 28f);
            _group = UiKit.SimpleToggle(_list.transform, "GroupByBiome", "By biome", TgpConfig.GroupByBiome.Value, on => { TgpConfig.GroupByBiome.Value = on; RefreshList(map); });
            UiKit.Place(_group.GetComponent<RectTransform>(), 212f, 98f, 114f, 28f);
            _expandAll = UiKit.LinkButton(_list.transform, "ExpandAll", "Expand all", () => { PortalList.ExpandAll(); RefreshList(map); });
            UiKit.Place(_expandAll.GetComponent<RectTransform>(), 8f, 132f, 100f, 22f);
            _collapseAll = UiKit.LinkButton(_list.transform, "CollapseAll", "Collapse all", () => { PortalList.CollapseAll(_entries); RefreshList(map); });
            UiKit.Place(_collapseAll.GetComponent<RectTransform>(), 114f, 132f, 100f, 22f);

            _listContent = UiKit.ScrollList(_list.transform, "List", out _scroll);
            var lrt = _scroll.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(8f, 8f);
            lrt.offsetMax = new Vector2(-8f, -160f);
        }

        private static void DestroyList()
        {
            if (_list != null) UnityEngine.Object.Destroy(_list);
            _list = null;
            _listHover = null;
            _listContent = null;
            _scroll = null;
            _header = null;
            _hint = null;
            _search = null;
            _group = null;
            _expandAll = null;
            _collapseAll = null;
            _rows.Clear();
            _rowIds.Clear();
        }

        /// <summary>After vanilla has laid out the pins: hide the ones that would clutter a destination choice.</summary>
        internal static void OnPinsUpdated(Minimap map)
        {
            if (!IsSelecting || map == null) return;
            if (TgpConfig.HideOtherPinsWhileChoosing == null || !TgpConfig.HideOtherPinsWhileChoosing.Value) return;
            var pins = Access.Pins(map);
            if (pins == null) return;
            foreach (var pin in pins)
            {
                if (PortalPins.IsOurs(pin) || !pin.m_save || pin.m_type == Minimap.PinType.Death) continue;
                bool any = false;
                if (pin.m_uiElement != null && pin.m_uiElement.gameObject.activeSelf)
                {
                    pin.m_uiElement.gameObject.SetActive(false);
                    any = true;
                }
                var nameObj = pin.m_NamePinData != null ? pin.m_NamePinData.PinNameGameObject : null;
                if (nameObj != null && nameObj.activeSelf)
                {
                    nameObj.SetActive(false);
                    any = true;
                }
                if (any && !_hidden.Contains(pin)) _hidden.Add(pin);
            }
        }

        private static void RestoreHiddenPins()
        {
            foreach (var pin in _hidden)
            {
                if (pin.m_uiElement != null) pin.m_uiElement.gameObject.SetActive(true);
                var nameObj = pin.m_NamePinData != null ? pin.m_NamePinData.PinNameGameObject : null;
                if (nameObj != null) nameObj.SetActive(true);
            }
            _hidden.Clear();
            if (Minimap.instance != null) Access.PinUpdateRequired(Minimap.instance) = true;
        }

        internal static string Status()
        {
            return $"{Current}, {PortalPins.Count} pin(s){(Current == Mode.Travel ? ", from " + _sourceId : "")}";
        }
    }
}
