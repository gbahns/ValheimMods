using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestPortal
{
    /// <summary>
    /// The portal configuration panel, opened with Use on a portal: name, a searchable
    /// destination list (optionally grouped by biome), favorite and default toggles, a button
    /// that points every portal in the world here, and buttons to pick or show the destination
    /// on the map. Up/Down move the selection, Enter confirms, Escape cancels.
    ///
    /// It is a copy of the game's own text prompt (the one vanilla uses for the portal tag),
    /// which supplies the wood background, the title, the input field and the OK/Cancel buttons
    /// in the game's style; the rest is laid out on top. While it is open, vanilla believes its
    /// text prompt is up, so movement, hotkeys and the mouse capture stay out of the way.
    /// </summary>
    internal static class PortalPanel
    {
        private const float W = 680f;
        private const float H = 600f;
        private const long HeaderId = long.MinValue;
        private const float ConfirmSeconds = 6f;

        private static GameObject _root;
        private static RectTransform _content;
        private static TMP_Text _title;
        private static TMP_InputField _name;
        private static TMP_InputField _search;
        private static Toggle _group;
        private static RectTransform _listContent;
        private static ScrollRect _scroll;
        private static Toggle _favorite;
        private static Toggle _default;
        private static Button _redirectAll;
        private static Button _expandAll;
        private static Button _collapseAll;
        private static readonly List<UiKit.RowHandle> _rows = new List<UiKit.RowHandle>();
        private static readonly List<long> _rowIds = new List<long>();
        private static List<ListEntry> _entries = new List<ListEntry>();

        // Renaming a portal in the list: one text box that moves onto the row being edited.
        private static TMP_InputField _renameField;
        private static bool _renaming;
        private static long _renameId;
        private static UiKit.RowHandle _renameRow;
        private static int _renameDoneFrame = -10;

        private static TeleportWorld _portal;
        private static ZDOID _zdo = ZDOID.None;
        private static long _id;
        private static long _selected;
        private static Vector3 _pos;
        private static string _query = "";
        private static float _confirmUntil = -1f;
        private static bool _openedThisFrame;
        private static bool _catalogDirty;
        private static bool _buildFailed;

        internal static bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>
        /// True for a frame after the panel closes. The Enter or Escape that closed it is still
        /// "down" for the rest of that frame, and vanilla (chat, the ESC menu) must not act on it.
        /// </summary>
        internal static bool JustClosed => Time.frameCount - _closedFrame <= 1;
        private static int _closedFrame = -10;

        static PortalPanel()
        {
            Catalog.Changed += () => _catalogDirty = true;
        }

        // ── hover text ──────────────────────────────────────────────────────────────

        internal static string HoverText(TeleportWorld portal)
        {
            var nview = portal != null ? portal.GetComponent<ZNetView>() : null;
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            string name = PortalData.CleanName(PortalData.GetName(zdo), 0);
            long to = PortalData.GetTarget(zdo);
            string status = null;
            if (to == 0L)
            {
                // An open portal needs no explanation on hover; an unconnected one (map travel off) does.
                if (!TgpConfig.UntargetedOpensMap.Value) status = "$piece_portal_unconnected";
            }
            else
            {
                var t = Catalog.Get(to);
                if (t == null) status = Catalog.HasSnapshot ? "Destination portal is gone" : "Destination unknown yet";
                else status = "To " + t.DisplayName + (PortalData.Connection(zdo) == ZDOID.None ? " (connecting)" : "");
            }
            string head = string.IsNullOrEmpty(name) ? "$piece_portal" : "$piece_portal \"" + name + "\"";
            if (status != null) head += $" [{status}]";
            return Localization.instance.Localize($"{head}\n[<color=yellow><b>$KEY_Use</b></color>] Configure");
        }

        // ── open / close ────────────────────────────────────────────────────────────

        internal static void Open(TeleportWorld portal)
        {
            if (portal == null) return;
            var nview = portal.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            if (_root == null && !Build())
            {
                // Last resort: vanilla's own prompt still lets the portal be named.
                TextInput.instance?.RequestText(portal, "$piece_portal_tag", TgpConfig.MaxNameLength.Value);
                return;
            }
            var zdo = nview.GetZDO();
            _portal = portal;
            _zdo = zdo.m_uid;
            _id = PortalData.GetId(zdo);
            _selected = PortalData.GetTarget(zdo);
            _pos = portal.transform.position;
            _catalogDirty = false;
            _confirmUntil = -1f;
            _query = "";

            _title.text = "Portal";
            _name.characterLimit = TgpConfig.MaxNameLength.Value;
            _name.text = PortalData.GetName(zdo);
            _search.text = "";
            UiKit.SetToggle(_group, TgpConfig.GroupByBiome.Value);
            UiKit.SetToggle(_favorite, Favorites.IsFavorite(_id));
            UiKit.SetToggle(_default, _id != 0L && Favorites.DefaultId == _id);
            _favorite.interactable = _id != 0L;
            _default.interactable = _id != 0L;
            _redirectAll.interactable = _id != 0L;
            UiKit.SetLabel(_redirectAll.gameObject, "All portals lead here");
            Populate();

            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            _name.ActivateInputField();
            _openedThisFrame = true;
            if (!Catalog.HasSnapshot) PortalNetwork.RequestCatalog();
        }

        internal static void Close()
        {
            CancelRename();
            UiKit.ContextMenu.Close();
            if (_root != null && _root.activeSelf)
            {
                if (_name != null) _name.DeactivateInputField();
                if (_search != null) _search.DeactivateInputField();
                _root.SetActive(false);
                _closedFrame = Time.frameCount;
            }
            _portal = null;
        }

        internal static void Update()
        {
            if (!IsOpen) return;
            if (_openedThisFrame)
            {
                _openedThisFrame = false;
                return;
            }
            if (UiKit.ContextMenu.IsOpen)
            {
                UiKit.ContextMenu.Update();   // the menu owns the keyboard and the mouse while it is up
                return;
            }
            // The keys that end a rename must not also act on the panel that frame.
            bool renameKeys = _renaming || Time.frameCount - _renameDoneFrame <= 1;
            if (ZInput.GetKeyDown(KeyCode.Escape))
            {
                if (_renaming) CancelRename();
                else if (!renameKeys) Close();
                return;
            }
            if (_portal == null)
            {
                // The portal was unloaded or destroyed under us.
                Close();
                return;
            }
            if (renameKeys) { }
            else if (ZInput.GetKeyDown(KeyCode.DownArrow)) MoveSelection(1);
            else if (ZInput.GetKeyDown(KeyCode.UpArrow)) MoveSelection(-1);
            else if ((ZInput.GetKeyDown(KeyCode.Return) || ZInput.GetKeyDown(KeyCode.KeypadEnter)) && !UiKit.IsFocused(_search))
            {
                Apply();
                return;
            }
            if (_confirmUntil > 0f && Time.time > _confirmUntil)
            {
                _confirmUntil = -1f;
                UiKit.SetLabel(_redirectAll.gameObject, "All portals lead here");
            }
            if (_catalogDirty)
            {
                _catalogDirty = false;
                Populate();
            }
        }

        // ── the list ────────────────────────────────────────────────────────────────

        private static void Populate()
        {
            if (_listContent == null) return;
            CancelRename();   // the row being edited is about to be rebuilt
            UiKit.ContextMenu.Close();
            UiKit.ClearChildren(_listContent);
            _rows.Clear();
            _rowIds.Clear();

            AddRow(0L, UiKit.Row(_listContent, "Open portal: choose where to go each time you step in", null,
                () => Choose(0L), null, 16f, null, () => { Choose(0L); Apply(); }));

            bool grouped = TgpConfig.GroupByBiome.Value;
            _entries = PortalList.Build(_id, _zdo, _query, grouped);
            if (_expandAll != null) _expandAll.gameObject.SetActive(grouped);
            if (_collapseAll != null) _collapseAll.gameObject.SetActive(grouped);
            if (_entries.Count == 0)
            {
                if (!Catalog.HasSnapshot)
                    AddRow(HeaderId, UiKit.Row(_listContent, "Waiting for the portal list from the server...", null, null, null, 15f, UiKit.Dim));
                else if (!string.IsNullOrEmpty(_query))
                    AddRow(HeaderId, UiKit.Row(_listContent, $"No portal matches \"{_query}\"", null, null, null, 15f, UiKit.Dim));
            }

            bool selectedPresent = _selected == 0L;
            foreach (var e in _entries)
            {
                if (e.IsHeader)
                {
                    string key = e.GroupKey;
                    AddRow(HeaderId, UiKit.SectionHeader(_listContent, e.Title, key == null ? null : (Action)(() => { PortalList.ToggleCollapsed(key); Populate(); })));
                    continue;
                }
                AddPortalRow(e.Portal, e.Favorite);
                if (e.Portal.Id == _selected) selectedPresent = true;
            }
            if (!selectedPresent)
            {
                var current = Catalog.Get(_selected);
                if (current != null)
                {
                    AddRow(HeaderId, UiKit.SectionHeader(_listContent, "Current destination (hidden above)"));
                    AddPortalRow(current, Favorites.IsFavorite(current.Id));
                }
                else
                {
                    string label = Catalog.HasSnapshot ? "(the destination portal no longer exists)" : "(current destination, details not received yet)";
                    AddRow(_selected, UiKit.Row(_listContent, label, null, () => UpdateSelection(), null, 15f, UiKit.Dim));
                }
            }
            UpdateSelection();
        }

        private static void AddPortalRow(PortalInfo p, bool fav)
        {
            var captured = p;
            string label = fav ? "★ " + p.DisplayName : p.DisplayName;
            string dist = TgpConfig.ShowDistances.Value ? UiKit.Distance(_pos, p.Pos) : null;
            UiKit.RowHandle row = null;
            row = UiKit.Row(_listContent, label, dist,
                () => Choose(captured.Id),
                () => ShowRowMenu(captured, row),
                16f, fav ? UiKit.Gold : (Color?)null,
                () => { Choose(captured.Id); Apply(); });   // double-click: choose and confirm
            AddRow(p.Id, row);
        }

        /// <summary>The right-click menu on a portal row.</summary>
        private static void ShowRowMenu(PortalInfo p, UiKit.RowHandle row)
        {
            if (p == null || row == null || _content == null) return;
            var items = new List<KeyValuePair<string, Action>>();
            if (_renameField != null && p.Id != 0L)
                items.Add(new KeyValuePair<string, Action>("Rename", () => BeginRename(p, row)));
            if (p.Id != 0L)
            {
                bool fav = Favorites.IsFavorite(p.Id);
                items.Add(new KeyValuePair<string, Action>(fav ? "Remove favorite" : "Add to favorites", () =>
                {
                    bool on = Favorites.Toggle(p.Id);
                    TheGreatestPortalMod.Message(on ? "Favorite: " + p.DisplayName : "No longer a favorite: " + p.DisplayName);
                    Populate();
                }));
            }
            items.Add(new KeyValuePair<string, Action>("Show on map", () =>
            {
                Vector3 center = p.Pos;
                Close();
                MapPicker.BeginBrowse(center);
            }));
            UiKit.ContextMenu.Show(_content, ZInput.pointerPosition, items);
        }

        // ── renaming in the list ────────────────────────────────────────────────────

        private static void BeginRename(PortalInfo p, UiKit.RowHandle row)
        {
            if (_renameField == null || p == null || row == null || row.Root == null) return;
            CancelRename();
            _renaming = true;
            _renameId = p.Id;
            _renameRow = row;
            row.Label.gameObject.SetActive(false);
            var rt = _renameField.GetComponent<RectTransform>();
            rt.SetParent(row.Root.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(6f, 2f);
            rt.offsetMax = new Vector2(row.Right != null ? -96f : -10f, -2f);
            _renameField.gameObject.SetActive(true);
            _renameField.characterLimit = TgpConfig.MaxNameLength.Value;
            _renameField.text = p.Name ?? "";
            _renameField.ActivateInputField();
        }

        private static void CommitRename(string text)
        {
            if (!_renaming) return;
            long id = _renameId;
            string name = PortalData.CleanName(text, TgpConfig.MaxNameLength.Value);
            var p = Catalog.Get(id);
            string old = p != null ? p.Name : "";
            EndRename();
            if (p == null || name == old) return;
            PortalNetwork.SendRename(id, name);
            p.Name = name;   // shown at once; the server's next portal list confirms it
            TheGreatestPortalMod.Message((string.IsNullOrEmpty(old) ? "Portal" : old) + " renamed to " + (string.IsNullOrEmpty(name) ? "(no name)" : name));
            Populate();
        }

        private static void CancelRename()
        {
            if (!_renaming) return;
            EndRename();
        }

        /// <summary>Puts the text box away and the row's label back.</summary>
        private static void EndRename()
        {
            _renaming = false;
            _renameDoneFrame = Time.frameCount;
            if (_renameRow != null && _renameRow.Label != null) _renameRow.Label.gameObject.SetActive(true);
            _renameRow = null;
            if (_renameField != null)
            {
                _renameField.DeactivateInputField();
                _renameField.gameObject.SetActive(false);
                if (_listContent != null) _renameField.transform.SetParent(_listContent.parent.parent, false);
            }
        }

        private static void AddRow(long id, UiKit.RowHandle row)
        {
            _rows.Add(row);
            _rowIds.Add(id);
        }

        private static void Choose(long id)
        {
            _selected = id;
            UpdateSelection();
        }

        private static void UpdateSelection()
        {
            for (int i = 0; i < _rows.Count; i++) _rows[i].SetSelected(_rowIds[i] != HeaderId && _rowIds[i] == _selected);
        }

        /// <summary>Up/Down: the next portal row in the list, skipping section headers.</summary>
        private static void MoveSelection(int delta)
        {
            if (_rowIds.Count == 0) return;
            int cur = _rowIds.IndexOf(_selected);
            int i = cur < 0 ? (delta > 0 ? -1 : _rowIds.Count) : cur;
            do { i += delta; } while (i >= 0 && i < _rowIds.Count && _rowIds[i] == HeaderId);
            if (i < 0 || i >= _rowIds.Count) return;
            _selected = _rowIds[i];
            UpdateSelection();
            UiKit.ScrollToRow(_scroll, i, _rowIds.Count);
        }

        private static void OnSearch(string text)
        {
            _query = (text ?? "").Trim();
            Populate();
        }

        private static void OnGroupToggle(bool on)
        {
            TgpConfig.GroupByBiome.Value = on;
            Populate();
        }

        // ── actions ─────────────────────────────────────────────────────────────────

        private static void Apply()
        {
            if (!IsOpen) return;
            string name = PortalData.CleanName(_name.text, TgpConfig.MaxNameLength.Value);
            long target = _selected;
            PortalNetwork.SendSetPortal(_zdo, name, target);
            ApplyToggles();
            var t = Catalog.Get(target);
            string shown = string.IsNullOrEmpty(name) ? "Portal" : name;
            TheGreatestPortalMod.Message(target == 0L ? shown + " is an open portal" : (t != null ? shown + " leads to " + t.DisplayName : shown + " saved"));
            Close();
        }

        private static void ApplyToggles()
        {
            if (_id == 0L) return;
            Favorites.SetFavorite(_id, _favorite.isOn);
            if (_default.isOn) Favorites.SetDefault(_id);
            else if (Favorites.DefaultId == _id) Favorites.SetDefault(0L);
        }

        /// <summary>Saves the name and toggles, then lets the player click the destination on the map.</summary>
        private static void PickOnMap()
        {
            if (!IsOpen) return;
            string name = PortalData.CleanName(_name.text, TgpConfig.MaxNameLength.Value);
            ZDOID zdo = _zdo;
            long id = _id;
            long current = _selected;
            PortalNetwork.SendSetPortal(zdo, name, current);
            ApplyToggles();
            Vector3 center = _pos;
            Close();
            MapPicker.BeginPick(zdo, id, center, picked =>
            {
                PortalNetwork.SendSetPortal(zdo, name, picked.Id);
                TheGreatestPortalMod.Message((string.IsNullOrEmpty(name) ? "Portal" : name) + " leads to " + picked.DisplayName);
            });
        }

        private static void ShowOnMap()
        {
            if (!IsOpen) return;
            var t = Catalog.Get(_selected);
            Vector3 center = t != null ? t.Pos : _pos;
            Close();
            MapPicker.BeginBrowse(center);
        }

        /// <summary>Two clicks within a few seconds: every portal in the world gets this one as its destination.</summary>
        private static void RedirectAll()
        {
            if (!IsOpen || _id == 0L) return;
            if (_confirmUntil < 0f || Time.time > _confirmUntil)
            {
                _confirmUntil = Time.time + ConfirmSeconds;
                UiKit.SetLabel(_redirectAll.gameObject, "Click again to confirm");
                TheGreatestPortalMod.Message("This points every portal in the world here. Click again to confirm.", always: true);
                return;
            }
            _confirmUntil = -1f;
            UiKit.SetLabel(_redirectAll.gameObject, "All portals lead here");
            string name = PortalData.CleanName(_name.text, TgpConfig.MaxNameLength.Value);
            PortalNetwork.SendSetPortal(_zdo, name, _selected);
            ApplyToggles();
            PortalNetwork.SendSetAll(_id);
        }

        // ── building the panel ──────────────────────────────────────────────────────

        private static bool Build()
        {
            if (_buildFailed) return false;
            try
            {
                if (BuildInner()) return true;
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogError($"[TheGreatestPortal] Building the portal panel failed: {e}");
            }
            _buildFailed = true;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            return false;
        }

        private static bool BuildInner()
        {
            var src = TextInput.instance;
            if (src == null || src.m_panel == null)
            {
                TheGreatestPortalMod.Log.LogWarning("[TheGreatestPortal] The game's text prompt is not available; cannot build the portal panel.");
                return false;
            }
            UiKit.EnsureFont();

            string topicPath = src.m_topic != null ? UiKit.PathBelow(src.m_topic.transform, src.m_panel.transform) : null;

            _root = UnityEngine.Object.Instantiate(src.m_panel, src.m_panel.transform.parent);
            _root.name = "TGP_PortalPanel";
            _root.SetActive(false);
            TheGreatestPortalMod.Log.LogInfo("[TheGreatestPortal] Cloned the text prompt for the portal panel:\n" + UiKit.Describe(_root.transform));

            foreach (var c in _root.GetComponents<LayoutGroup>()) UnityEngine.Object.Destroy(c);
            foreach (var c in _root.GetComponents<ContentSizeFitter>()) UnityEngine.Object.Destroy(c);

            var input = _root.GetComponentInChildren<TMP_InputField>(true);
            if (input == null)
            {
                TheGreatestPortalMod.Log.LogWarning("[TheGreatestPortal] The text prompt has no input field; cannot build the portal panel.");
                return false;
            }
            TMP_Text topic = null;
            if (topicPath != null)
            {
                var tt = _root.transform.Find(topicPath);
                if (tt != null) topic = tt.GetComponent<TMP_Text>();
            }
            if (topic == null)
            {
                foreach (var t in _root.GetComponentsInChildren<TMP_Text>(true))
                    if (t.GetComponentInParent<TMP_InputField>() == null && t.GetComponentInParent<Button>() == null) { topic = t; break; }
            }
            Button ok = null, cancel = null;
            foreach (var b in _root.GetComponentsInChildren<Button>(true))
            {
                string methods = UiKit.PersistentMethods(b.onClick);
                if (ok == null && (methods.Contains("OnEnter") || methods.Contains("OnInput"))) ok = b;
                else if (cancel == null && methods.Contains("OnCancel")) cancel = b;
            }

            // Everything we keep moves into a plain container; the rest of the prompt goes.
            var content = new GameObject("TGP_Content", typeof(RectTransform));
            content.transform.SetParent(_root.transform, false);
            UiKit.Stretch(content.GetComponent<RectTransform>());
            _content = content.GetComponent<RectTransform>();

            var keep = new List<Transform> { input.transform };
            if (topic != null) keep.Add(topic.transform);
            if (ok != null) keep.Add(ok.transform);
            if (cancel != null) keep.Add(cancel.transform);
            foreach (var t in keep) t.SetParent(content.transform, false);
            // Of what is left, background art (graphics with no text or controls inside, now that the
            // controls have moved out) stays, stretched over the panel; everything else goes.
            for (int i = _root.transform.childCount - 1; i >= 0; i--)
            {
                var child = _root.transform.GetChild(i);
                if (child == content.transform) continue;
                if (IsDecoration(child))
                {
                    child.SetParent(content.transform, false);
                    child.SetAsFirstSibling();
                    UiKit.Stretch(child.GetComponent<RectTransform>());
                }
                else UnityEngine.Object.Destroy(child.gameObject);
            }

            var rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(W, H);

            // Title
            if (topic != null)
            {
                _title = topic;
                UiKit.Place(topic.rectTransform, 0f, 16f, W, 40f);
                topic.alignment = TextAlignmentOptions.Center;
                foreach (var l in topic.GetComponents<Localize>()) UnityEngine.Object.Destroy(l);
            }
            else
            {
                _title = UiKit.Text(content.transform, "Title", "Portal", 26f, TextAlignmentOptions.Center, UiKit.Gold);
                UiKit.Place(_title.rectTransform, 0f, 16f, W, 40f);
            }

            // Name
            var nameLabel = UiKit.Text(content.transform, "NameLabel", "Name", 18f, TextAlignmentOptions.Left);
            UiKit.Place(nameLabel.rectTransform, 30f, 72f, 100f, 36f);
            _name = input;
            UiKit.PrepareInput(input, "", TgpConfig.MaxNameLength.Value, null, _ => Apply());
            UiKit.Place(input.GetComponent<RectTransform>(), 130f, 72f, 420f, 36f);

            // Destination: label, search box, biome grouping
            var destLabel = UiKit.Text(content.transform, "DestinationLabel", "Destination", 18f, TextAlignmentOptions.Left);
            UiKit.Place(destLabel.rectTransform, 30f, 120f, 140f, 30f);
            _search = UiKit.CloneInputField(content.transform, "Search", "Search...", 40, OnSearch, null);
            if (_search != null) UiKit.Place(_search.GetComponent<RectTransform>(), 175f, 120f, 215f, 30f);
            _group = UiKit.SimpleToggle(content.transform, "GroupByBiome", "Group by biome", TgpConfig.GroupByBiome.Value, OnGroupToggle);
            UiKit.Place(_group.GetComponent<RectTransform>(), 410f, 120f, 240f, 30f);
            var hint = UiKit.Text(content.transform, "Hint", "Click a portal, pick one on the map, or use Up/Down and Enter. Right-click a portal to rename it or mark a favorite.", 13f, TextAlignmentOptions.Left, UiKit.Dim);
            UiKit.Place(hint.rectTransform, 30f, 152f, 440f, 20f);
            _expandAll = UiKit.LinkButton(content.transform, "ExpandAll", "Expand all", () => { PortalList.ExpandAll(); Populate(); });
            UiKit.Place(_expandAll.GetComponent<RectTransform>(), 478f, 150f, 82f, 22f);
            _collapseAll = UiKit.LinkButton(content.transform, "CollapseAll", "Collapse all", () => { PortalList.CollapseAll(_entries); Populate(); });
            UiKit.Place(_collapseAll.GetComponent<RectTransform>(), 564f, 150f, 86f, 22f);
            _listContent = UiKit.ScrollList(content.transform, "Destinations", out _scroll);
            UiKit.Place(_scroll.GetComponent<RectTransform>(), 30f, 176f, W - 60f, 230f);
            _renameField = UiKit.CloneInputField(_scroll.transform, "Rename", "", TgpConfig.MaxNameLength.Value, null, CommitRename);
            if (_renameField != null)
            {
                _renameField.onDeselect.AddListener(_ => CancelRename());   // clicking away cancels
                _renameField.gameObject.SetActive(false);
            }

            // Toggles and the redirect-all button
            _favorite = UiKit.SimpleToggle(content.transform, "Favorite", "Favorite (listed first)", false, null);
            UiKit.Place(_favorite.GetComponent<RectTransform>(), 30f, 418f, 250f, 30f);
            _default = UiKit.SimpleToggle(content.transform, "Default", "Default: new portals lead here", false, null);
            UiKit.Place(_default.GetComponent<RectTransform>(), 290f, 418f, 360f, 30f);
            var allLabel = UiKit.Text(content.transform, "AllLabel", "Every portal in the world:", 16f, TextAlignmentOptions.Left, UiKit.Dim);
            UiKit.Place(allLabel.rectTransform, 30f, 458f, 250f, 34f);
            _redirectAll = ok != null ? UiKit.CloneButton(ok, content.transform, "RedirectAll", "All portals lead here", RedirectAll)
                                      : UiKit.SimpleButton(content.transform, "RedirectAll", "All portals lead here", RedirectAll);
            UiKit.Place(_redirectAll.GetComponent<RectTransform>(), 290f, 456f, 250f, 36f);

            // Buttons
            float by = 508f, bh = 44f;
            Button pick = ok != null ? UiKit.CloneButton(ok, content.transform, "PickOnMap", "Pick on map", PickOnMap) : UiKit.SimpleButton(content.transform, "PickOnMap", "Pick on map", PickOnMap);
            UiKit.Place(pick.GetComponent<RectTransform>(), 30f, by, 160f, bh);
            Button show = ok != null ? UiKit.CloneButton(ok, content.transform, "ShowOnMap", "Show on map", ShowOnMap) : UiKit.SimpleButton(content.transform, "ShowOnMap", "Show on map", ShowOnMap);
            UiKit.Place(show.GetComponent<RectTransform>(), 200f, by, 160f, bh);
            if (ok != null)
            {
                UiKit.KillPersistent(ok.onClick);
                ok.onClick.RemoveAllListeners();
                ok.onClick.AddListener(Apply);
                ok.interactable = true;
            }
            else ok = UiKit.SimpleButton(content.transform, "OK", "OK", Apply);
            UiKit.Place(ok.GetComponent<RectTransform>(), 390f, by, 120f, bh);
            if (cancel != null)
            {
                UiKit.KillPersistent(cancel.onClick);
                cancel.onClick.RemoveAllListeners();
                cancel.onClick.AddListener(Close);
                cancel.interactable = true;
            }
            else cancel = UiKit.SimpleButton(content.transform, "Cancel", "Cancel", Close);
            UiKit.Place(cancel.GetComponent<RectTransform>(), 520f, by, 120f, bh);
            foreach (var l in _root.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.Destroy(l);

            if (_search == null)
            {
                // No field to clone: the list still works, only the filter is missing.
                TheGreatestPortalMod.Log.LogWarning("[TheGreatestPortal] Could not clone a text field for the search box.");
                _search = input;   // keeps the null checks simple; typing a name never filters
            }
            return true;
        }

        /// <summary>A graphic with no text and no controls inside: background art worth keeping.</summary>
        private static bool IsDecoration(Transform t)
        {
            if (t.GetComponent<Graphic>() == null) return false;
            if (t.GetComponentInChildren<Selectable>(true) != null) return false;
            if (t.GetComponentInChildren<TMP_Text>(true) != null) return false;
            if (t.GetComponentInChildren<Text>(true) != null) return false;
            return true;
        }
    }
}
