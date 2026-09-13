using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestPortal
{
    /// <summary>
    /// The portal configuration panel, opened with Use on a portal: name, destination list,
    /// favourite and default toggles, and buttons to pick or show the destination on the map.
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

        private static GameObject _root;
        private static TMP_Text _title;
        private static TMP_InputField _name;
        private static RectTransform _listContent;
        private static Toggle _favorite;
        private static Toggle _default;
        private static readonly Dictionary<long, UiKit.RowHandle> _rows = new Dictionary<long, UiKit.RowHandle>();

        private static TeleportWorld _portal;
        private static ZDOID _zdo = ZDOID.None;
        private static long _id;
        private static long _selected;
        private static Vector3 _pos;
        private static bool _openedThisFrame;
        private static bool _catalogDirty;
        private static bool _buildFailed;

        internal static bool IsOpen => _root != null && _root.activeSelf;

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
            string status;
            if (to == 0L)
            {
                status = TgpConfig.UntargetedOpensMap.Value ? "Open: choose the destination when you step in" : "$piece_portal_unconnected";
            }
            else
            {
                var t = Catalog.Get(to);
                if (t == null) status = Catalog.HasSnapshot ? "Destination portal is gone" : "Destination unknown yet";
                else status = "To " + t.DisplayName + (PortalData.Connection(zdo) == ZDOID.None ? " (connecting)" : "");
            }
            string head = string.IsNullOrEmpty(name) ? "$piece_portal" : "$piece_portal \"" + name + "\"";
            return Localization.instance.Localize($"{head} [{status}]\n[<color=yellow><b>$KEY_Use</b></color>] Configure");
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

            _title.text = "Portal";
            _name.characterLimit = TgpConfig.MaxNameLength.Value;
            _name.text = PortalData.GetName(zdo);
            _favorite.SetIsOnWithoutNotify(Favorites.IsFavorite(_id));
            _default.SetIsOnWithoutNotify(_id != 0L && Favorites.DefaultId == _id);
            _favorite.interactable = _id != 0L;
            _default.interactable = _id != 0L;
            SetCheck(_favorite);
            SetCheck(_default);
            Populate();

            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            _name.ActivateInputField();
            _openedThisFrame = true;
            if (!Catalog.HasSnapshot) PortalNetwork.RequestCatalog();
        }

        internal static void Close()
        {
            if (_root != null && _root.activeSelf)
            {
                if (_name != null) _name.DeactivateInputField();
                _root.SetActive(false);
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
            if (ZInput.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            if (_portal == null)
            {
                // The portal was unloaded or destroyed under us.
                Close();
                return;
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
            UiKit.ClearChildren(_listContent);
            _rows.Clear();

            var none = UiKit.Row(_listContent, "Open portal: choose where to go each time you step in", null,
                () => { _selected = 0L; UpdateSelection(); }, null, 16f);
            _rows[0L] = none;

            var portals = Favorites.Sorted(_id, _zdo);
            if (portals.Count == 0 && !Catalog.HasSnapshot)
                UiKit.Row(_listContent, "Waiting for the portal list from the server...", null, null, null, 15f, UiKit.Dim);

            bool selectedPresent = _selected == 0L;
            foreach (var p in portals)
            {
                var captured = p;
                bool fav = Favorites.IsFavorite(p.Id);
                string label = fav ? "★ " + p.DisplayName : p.DisplayName;
                string dist = TgpConfig.ShowDistances.Value ? UiKit.Distance(_pos, p.Pos) : null;
                var row = UiKit.Row(_listContent, label, dist,
                    () => { _selected = captured.Id; UpdateSelection(); },
                    () =>
                    {
                        if (captured.Id == 0L) return;
                        bool on = Favorites.Toggle(captured.Id);
                        TheGreatestPortalMod.Message(on ? "Favourite: " + captured.DisplayName : "No longer a favourite: " + captured.DisplayName);
                        Populate();
                    },
                    16f, fav ? UiKit.Gold : (Color?)null);
                _rows[p.Id] = row;
                if (p.Id == _selected) selectedPresent = true;
            }
            if (!selectedPresent)
            {
                string label = Catalog.HasSnapshot ? "(the destination portal no longer exists)" : "(current destination, details not received yet)";
                var missing = UiKit.Row(_listContent, label, null, () => UpdateSelection(), null, 15f, UiKit.Dim);
                _rows[_selected] = missing;
            }
            UpdateSelection();
        }

        private static void UpdateSelection()
        {
            foreach (var kv in _rows) kv.Value.SetSelected(kv.Key == _selected);
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
            UiKit.KillPersistent(input.onSubmit);
            UiKit.KillPersistent(input.onEndEdit);
            UiKit.KillPersistent(input.onValueChanged);
            UiKit.KillPersistent(input.onDeselect);
            input.onSubmit.RemoveAllListeners();
            input.onSubmit.AddListener(_ => Apply());
            input.lineType = TMP_InputField.LineType.SingleLine;
            UiKit.Place(input.GetComponent<RectTransform>(), 130f, 72f, 420f, 36f);

            // Destination
            var destLabel = UiKit.Text(content.transform, "DestinationLabel", "Destination", 18f, TextAlignmentOptions.Left);
            UiKit.Place(destLabel.rectTransform, 30f, 122f, 300f, 30f);
            var hint = UiKit.Text(content.transform, "Hint", "Click a portal below, or pick one on the map. Right-click a portal to mark it as a favourite.", 13f, TextAlignmentOptions.Left, UiKit.Dim);
            UiKit.Place(hint.rectTransform, 30f, 150f, W - 60f, 20f);
            _listContent = UiKit.ScrollList(content.transform, "Destinations", out _);
            UiKit.Place(_listContent.parent.parent as RectTransform, 30f, 174f, W - 60f, 236f);

            // Toggles
            _favorite = UiKit.SimpleToggle(content.transform, "Favorite", "Favourite (listed first)", false, on => SetCheck(_favorite));
            UiKit.Place(_favorite.GetComponent<RectTransform>(), 30f, 424f, 250f, 30f);
            _default = UiKit.SimpleToggle(content.transform, "Default", "Default: new portals I build lead here", false, on => SetCheck(_default));
            UiKit.Place(_default.GetComponent<RectTransform>(), 300f, 424f, 350f, 30f);

            // Buttons
            float by = 500f, bh = 44f;
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

        private static void SetCheck(Toggle toggle)
        {
            if (toggle == null || toggle.graphic == null) return;
            toggle.graphic.gameObject.SetActive(toggle.isOn);
        }
    }
}
