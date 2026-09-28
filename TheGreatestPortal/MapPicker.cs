using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// The map is the one the M key gives: every marker you normally see is still there, with the
    /// portals added on top as pins and, on the left, a searchable list with
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
        private static readonly RowRename _rename = new RowRename();
        private static bool _panning;
        private static Vector3 _panFrom, _panTo, _panLast;
        private static float _panStart;
        private static Toggle _group;
        private static Button _expandAll;
        private static Button _collapseAll;
        private static RectTransform _listRt;
        private static RectTransform _listMover;
        private static float _listW, _listH;
        private static Vector2 _listPos;
        private static List<ListEntry> _entries = new List<ListEntry>();
        private static readonly List<UiKit.RowHandle> _rows = new List<UiKit.RowHandle>();
        private static readonly List<long> _rowIds = new List<long>();
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
            bool had = Active || _list != null || PortalPins.Count > 0;
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
            DestroyGlow();
            if (!had) return;
            PortalPins.Clear();
            DestroyList();
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
            UpdatePan(map);
            if (UiKit.ContextMenu.IsOpen)
            {
                UiKit.ContextMenu.Update();   // it owns the keyboard and the mouse while it is up
                return;
            }
            // Escape while typing only leaves the box; the next Escape closes the map.
            if (_rename.Active && ZInput.GetKeyDown(KeyCode.Escape)) _rename.Cancel();
            else if (SearchFocused && ZInput.GetKeyDown(KeyCode.Escape)) _search.DeactivateInputField();
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
        private static GameObject _glow;
        private static Image _glowImage;

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
            // Vanilla repaints the pins whenever it feels like it, so both the portal color and
            // the highlight are reapplied every frame.
            var color = Access.PortalPinColor();
            PortalPins.Tint(color);
            if (_hoverId != 0L) Decorate(map, _hoverId, true, color);
            else HideGlow();
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
            HideGlow();
        }

        private static void Decorate(Minimap map, long id, bool on, Color? tint = null)
        {
            if (id == 0L || map == null) return;
            var pin = PortalPins.PinFor(id);
            if (pin == null || pin.m_uiElement == null) { if (on) HideGlow(); return; }
            Color color = tint ?? Access.PortalPinColor();
            pin.m_uiElement.localScale = on ? Vector3.one * HoverScale : Vector3.one;
            if (pin.m_iconElement != null) pin.m_iconElement.color = color;
            if (on) ShowGlow(map, pin, color); else HideGlow();
            var namePin = pin.m_NamePinData;
            if (namePin == null || namePin.PinNameGameObject == null) return;
            if (namePin.PinNameText != null) namePin.PinNameText.color = on ? UiKit.Gold : Color.white;
            // Hovering always names the portal; otherwise vanilla shows names only when zoomed in.
            namePin.PinNameGameObject.SetActive(on || (!string.IsNullOrEmpty(pin.m_name) && map.LargeZoom < map.m_showNamesZoom));
        }

        /// <summary>
        /// The halo behind the hovered portal. One object, moved to whichever pin is hovered and
        /// tinted to that portal's own color, pulsing gently so it reads as lit rather than
        /// painted. It sits first among the pins so every pin draws over it.
        /// </summary>
        private static void ShowGlow(Minimap map, Minimap.PinData pin, Color color)
        {
            var parent = pin.m_uiElement.parent as RectTransform;
            if (parent == null) { HideGlow(); return; }
            if (_glow == null)
            {
                _glow = new GameObject("TGP_PinGlow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                _glowImage = _glow.GetComponent<Image>();
                _glowImage.sprite = UiKit.Glow();
                _glowImage.raycastTarget = false;
            }
            var rt = (RectTransform)_glow.transform;
            if (rt.parent != parent)
            {
                rt.SetParent(parent, false);
                rt.SetAsFirstSibling();
            }
            rt.anchorMin = pin.m_uiElement.anchorMin;
            rt.anchorMax = pin.m_uiElement.anchorMax;
            rt.pivot = pin.m_uiElement.pivot;
            rt.anchoredPosition = pin.m_uiElement.anchoredPosition;
            float size = map.m_pinSizeLarge * 2.6f;
            rt.sizeDelta = new Vector2(size, size);
            // Unscaled, so it keeps breathing while the map is up with the game paused.
            float t = (Mathf.Sin(Time.unscaledTime * 3.2f) + 1f) * 0.5f;
            color.a = Mathf.Lerp(0.32f, 0.62f, t);
            _glowImage.color = color;
            if (!_glow.activeSelf) _glow.SetActive(true);
        }

        private static void HideGlow()
        {
            if (_glow != null && _glow.activeSelf) _glow.SetActive(false);
        }

        private static void DestroyGlow()
        {
            if (_glow != null) UnityEngine.Object.Destroy(_glow);
            _glow = null;
            _glowImage = null;
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
                RowMenu(map, p);   // a pin offers what its row offers
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
                    if (Travel.Go(p, _sourceAllowAll, _sourceId))
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
                    CenterOn(p);
                    break;
            }
        }

        // ── renaming in the list ────────────────────────────────────────────────────

        private static UiKit.RowHandle RowFor(long id)
        {
            for (int i = 0; i < _rowIds.Count; i++)
                if (_rowIds[i] == id) return _rows[i];
            return null;
        }

        /// <summary>
        /// Puts the map over a portal and marks its row, without choosing it. This is what the pin
        /// button on a row does: while travelling, a click on the row itself sends you there, so
        /// looking at where a portal actually is needs a way of its own.
        /// </summary>
        private static void CenterOn(PortalInfo p)
        {
            if (p == null) return;
            _highlightId = p.Id;
            PanTo(p.Pos);
            for (int i = 0; i < _rows.Count; i++) _rows[i].SetSelected(_rowIds[i] == p.Id);
        }

        // ── panning ─────────────────────────────────────────────────────────────────

        /// <summary>How long the map takes to slide to a portal. Long enough to follow, short
        /// enough not to be waited on.</summary>
        private const float PanSeconds = 0.35f;

        /// <summary>
        /// Slides the map to a place rather than cutting to it, so the player can see where it is
        /// in relation to where they were: a jump tells you where a portal is, a pan tells you
        /// which way and how far. Falls back to the jump if the map's own centring cannot be
        /// reached.
        /// </summary>
        private static void PanTo(Vector3 worldPos)
        {
            var map = Minimap.instance;
            var player = Player.m_localPlayer;
            if (map == null || player == null || !Access.CanPan)
            {
                OpenMapAt(worldPos);
                return;
            }
            _panFrom = Access.MapOffset(map);
            _panTo = worldPos - player.transform.position;
            if ((_panTo - _panFrom).sqrMagnitude < 0.01f) return;   // already there
            _panLast = _panFrom;
            _panStart = Time.unscaledTime;
            _panning = true;
            Access.MoveInertia(map) = Vector3.zero;   // a fling still running would fight it
        }

        private static void UpdatePan(Minimap map)
        {
            if (!_panning) return;
            var player = Player.m_localPlayer;
            if (player == null)
            {
                _panning = false;
                return;
            }
            // Anything else moving the map -- a drag, a zoom -- wins: stop rather than fight it.
            if ((Access.MapOffset(map) - _panLast).sqrMagnitude > 0.01f)
            {
                _panning = false;
                return;
            }
            float t = Mathf.Clamp01((Time.unscaledTime - _panStart) / PanSeconds);
            float eased = t * t * (3f - 2f * t);   // slow at both ends, so it reads as movement
            Vector3 offset = Vector3.Lerp(_panFrom, _panTo, eased);
            Access.MapOffset(map) = offset;
            _panLast = offset;
            Access.CenterMap(map, player.transform.position + offset);
            if (t >= 1f) _panning = false;
        }

        /// <summary>
        /// The right-click menu for a row. Right-clicking used to favorite outright, which left no
        /// way to ask for anything else and was easy to do by accident; the star does that now.
        /// </summary>
        private static void RowMenu(Minimap map, PortalInfo p)
        {
            if (p == null || map == null || map.m_largeRoot == null) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;
            string pick = Current == Mode.Travel ? "Travel here" : Current == Mode.Pick ? "Set as destination" : "Center the map here";
            var items = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>(pick, () => Select(p)),
                new KeyValuePair<string, Action>("Center the map here", () => CenterOn(p)),
                PortalMenu.FavoriteItem(p, Refresh),
            };
            if (_rename.Ready && p.Id != 0L)
            {
                var row = RowFor(p.Id);
                if (row != null) items.Add(PortalMenu.RenameItem(p, _rename, row));
            }
            if (Current == Mode.Browse) items.RemoveAt(0);   // the first two would be the same act
            UiKit.ContextMenu.Show(parent, ZInput.pointerPosition, items);
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
                            ? "This trip only; the portal keeps the destination it is set to. The pin on a row shows where it is. Esc stays here."
                            : $"This trip only; the portal still leads to {_usualDestination}. The pin on a row shows where it is. Esc stays here.")
                        : "Click a portal here or on the map to travel. The pin on a row shows where it is. The star marks a favorite; right-click for a menu. Esc stays here.";
                    break;
                case Mode.Pick:
                    _header.text = "Choose the destination";
                    _hint.text = "Click a portal here or on the map. The pin on a row shows where it is. The star marks a favorite; right-click for a menu. Esc keeps the old destination.";
                    break;
                default:
                    _header.text = "Portals";
                    _hint.text = $"Click a portal to center the map on it. The star marks a favorite; right-click for a menu. {TgpConfig.TogglePinsKey.Value.MainKey} hides them.";
                    break;
            }

            _rename.Cancel();   // the row it is sitting on is about to go
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
                bool fav = e.Favorite;
                string dist = TgpConfig.ShowDistances.Value && player != null ? UiKit.Distance(from, captured.Pos) : null;
                // The star says whether it is a favorite and flips it; the name no longer has to.
                var icons = new[]
                {
                    new UiKit.RowIcon(fav ? UiKit.Star() : UiKit.StarOutline(), () => PortalMenu.ToggleFavorite(captured, Refresh), fav ? UiKit.Gold : (Color?)null),
                    new UiKit.RowIcon(UiKit.Pin(), () => CenterOn(captured)),
                };
                var row = UiKit.Row(_listContent, captured.DisplayName, dist, () => Select(captured), () => RowMenu(map, captured), 16f, fav ? UiKit.Gold : (Color?)null, null, PortalList.DestinationText(captured), icons);
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
            _listRt = rt;
            LoadListPlacement();
            rt.anchoredPosition = _listPos;
            var bg = _list.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = true;
            _listHover = _list.GetComponent<UiKit.Hover>();

            _header = UiKit.Text(_list.transform, "Header", "Portals", 22f, TextAlignmentOptions.Left, UiKit.Gold);
            _hint = UiKit.Text(_list.transform, "Hint", "", 13f, TextAlignmentOptions.TopLeft, UiKit.Dim);
            _hint.textWrappingMode = TextWrappingModes.Normal;
            _hint.overflowMode = TextOverflowModes.Truncate;

            _search = UiKit.CloneInputField(_list.transform, "Search", "Search...", 40, text => { _query = (text ?? "").Trim(); RefreshList(map); }, null);
            _rename.Build(_list.transform);
            _rename.Refresh = Refresh;
            _rename.Renamed = (p, name) =>
            {
                Spelling.Judge(name, p.Pos);
                var me = Player.m_localPlayer;
                if (me == null || me.IsDead()) End();
            };
            _group = UiKit.SimpleToggle(_list.transform, "GroupByBiome", "By biome", TgpConfig.GroupByBiome.Value, on => { TgpConfig.GroupByBiome.Value = on; RefreshList(map); });
            _expandAll = UiKit.LinkButton(_list.transform, "ExpandAll", "Expand all", () => { PortalList.ExpandAll(); RefreshList(map); });
            _collapseAll = UiKit.LinkButton(_list.transform, "CollapseAll", "Collapse all", () => { PortalList.CollapseAll(_entries); RefreshList(map); });

            _listContent = UiKit.ScrollList(_list.transform, "List", out _scroll);
            var lrt = _scroll.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(8f, 8f);
            lrt.offsetMax = new Vector2(-8f, -160f);

            // Resize grip in the bottom-right corner, the same as the portal panel has.
            var gripGo = new GameObject("Grip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiKit.DragHandle));
            gripGo.transform.SetParent(_list.transform, false);
            var grt = gripGo.GetComponent<RectTransform>();
            grt.anchorMin = new Vector2(1f, 0f);
            grt.anchorMax = new Vector2(1f, 0f);
            grt.pivot = new Vector2(1f, 0f);
            grt.anchoredPosition = new Vector2(-2f, 2f);
            grt.sizeDelta = new Vector2(18f, 18f);
            var gimg = gripGo.GetComponent<Image>();
            gimg.sprite = UiKit.Grip();
            gimg.color = new Color(1f, 0.85f, 0.45f, 0.75f);
            gimg.raycastTarget = true;
            var handle = gripGo.GetComponent<UiKit.DragHandle>();
            handle.OnDrag = OnListGripDrag;
            handle.OnEnd = SaveListPlacement;

            // The heading strip drags the whole list, as the panel's title does.
            var moveGo = new GameObject("Mover", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiKit.DragHandle));
            moveGo.transform.SetParent(_list.transform, false);
            moveGo.transform.SetAsFirstSibling();   // behind the heading, which does not take the pointer anyway
            _listMover = moveGo.GetComponent<RectTransform>();
            var moveImg = moveGo.GetComponent<Image>();
            moveImg.color = new Color(0f, 0f, 0f, 0f);   // invisible, but it catches the pointer
            moveImg.raycastTarget = true;
            var mover = moveGo.GetComponent<UiKit.DragHandle>();
            mover.OnDrag = OnListMoveDrag;
            mover.OnEnd = SaveListPlacement;

            LayoutList();
        }

        // ── the list's own size ─────────────────────────────────────────────────────

        private const float ListW = 380f, ListH = 660f;
        private const float MinListW = 300f, MaxListW = 900f;
        private const float MinListH = 240f, MaxListH = 1400f;

        /// <summary>
        /// Lays the list's own furniture out for the current width. The rows below stretch by
        /// themselves, so a wider list is what gives a portal's destination room to be read.
        /// </summary>
        private static void LayoutList()
        {
            if (_listRt == null) return;
            _listRt.sizeDelta = new Vector2(_listW, _listH);
            float inner = _listW - 24f;
            if (_listMover != null) UiKit.Place(_listMover, 0f, 0f, _listW, 38f);
            if (_header != null) UiKit.Place(_header.rectTransform, 12f, 8f, inner, 30f);
            if (_hint != null) UiKit.Place(_hint.rectTransform, 12f, 40f, inner, 54f);
            if (_search != null) UiKit.Place(_search.GetComponent<RectTransform>(), 8f, 98f, _listW - 130f, 28f);
            if (_group != null) UiKit.Place(_group.GetComponent<RectTransform>(), _listW - 122f, 98f, 114f, 28f);
            if (_expandAll != null) UiKit.Place(_expandAll.GetComponent<RectTransform>(), 8f, 132f, 100f, 22f);
            if (_collapseAll != null) UiKit.Place(_collapseAll.GetComponent<RectTransform>(), 114f, 132f, 100f, 22f);
        }

        private static float CanvasScale()
        {
            var canvas = _list != null ? _list.GetComponentInParent<Canvas>() : null;
            return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        }

        private static void OnListGripDrag(Vector2 screenDelta)
        {
            if (_list == null) return;
            float scale = CanvasScale();
            _listW = Mathf.Clamp(_listW + screenDelta.x / scale, MinListW, MaxListW);
            _listH = Mathf.Clamp(_listH - screenDelta.y / scale, MinListH, MaxListH);   // it hangs from its top
            LayoutList();
            ClampListToMap();
        }

        private static void OnListMoveDrag(Vector2 screenDelta)
        {
            if (_listRt == null) return;
            float scale = CanvasScale();
            _listPos += new Vector2(screenDelta.x / scale, screenDelta.y / scale);
            ClampListToMap();
        }

        /// <summary>Keeps the list on the map: the heading has to stay reachable to drag it back.</summary>
        private static void ClampListToMap()
        {
            if (_listRt == null) return;
            var parent = _listRt.parent as RectTransform;
            if (parent != null)
            {
                float w = parent.rect.width, h = parent.rect.height;
                if (w > 0f && h > 0f)
                {
                    _listPos.x = Mathf.Clamp(_listPos.x, 0f, Mathf.Max(0f, w - _listW));
                    _listPos.y = Mathf.Clamp(_listPos.y, -Mathf.Max(0f, h - _listH), 0f);
                }
            }
            _listRt.anchoredPosition = _listPos;
        }

        private static void SaveListPlacement()
        {
            if (TgpConfig.MapListSize != null) TgpConfig.MapListSize.Value = $"{Mathf.RoundToInt(_listW)},{Mathf.RoundToInt(_listH)}";
            if (TgpConfig.MapListPosition != null) TgpConfig.MapListPosition.Value = $"{Mathf.RoundToInt(_listPos.x)},{Mathf.RoundToInt(_listPos.y)}";
        }

        private static void LoadListPlacement()
        {
            _listW = ListW;
            _listH = ListH;
            if (TryPair(TgpConfig.MapListSize != null ? TgpConfig.MapListSize.Value : "", out float w, out float h))
            {
                _listW = Mathf.Clamp(w, MinListW, MaxListW);
                _listH = Mathf.Clamp(h, MinListH, MaxListH);
            }
            _listPos = new Vector2(20f, -12f);   // hard against the top of the map view
            if (TryPair(TgpConfig.MapListPosition != null ? TgpConfig.MapListPosition.Value : "", out float x, out float y))
                _listPos = new Vector2(x, y);
        }

        private static bool TryPair(string raw, out float a, out float b)
        {
            a = 0f;
            b = 0f;
            var parts = (raw ?? "").Split(',');
            return parts.Length == 2
                && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out b);
        }

        private static void DestroyList()
        {
            _panning = false;
            UiKit.ContextMenu.Close();
            if (_list != null) UnityEngine.Object.Destroy(_list);
            _list = null;
            _listRt = null;
            _listMover = null;
            _listHover = null;
            _listContent = null;
            _scroll = null;
            _header = null;
            _hint = null;
            _search = null;
            _rename.Clear();
            _group = null;
            _expandAll = null;
            _collapseAll = null;
            _rows.Clear();
            _rowIds.Clear();
        }

        internal static string Status()
        {
            return $"{Current}, {PortalPins.Count} pin(s){(Current == Mode.Travel ? ", from " + _sourceId : "")}";
        }
    }
}
