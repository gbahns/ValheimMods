using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// The stats panel: a scoreboard of everyone online, and a breakdown for one player.
    ///
    /// The panel is built by cloning the game's own text prompt, so it carries Valheim's frame,
    /// font and buttons without an asset bundle or Jotunn. Drag the title to move it and the
    /// bottom-right corner to resize it; both are remembered in the config.
    /// </summary>
    internal static class StatsPanel
    {
        private const float W = 820f, H = 620f;
        private const float MinW = 620f, MaxW = 1800f, MinH = 400f, MaxH = 1400f;
        private const float HeaderHeight = 26f;
        private const float ListTop = 152f;

        private enum Tab { Scoreboard, Details }

        // ── widgets ─────────────────────────────────────────────────────────────────
        private static GameObject _root;
        private static RectTransform _content;
        private static TextMeshProUGUI _title;
        private static TextMeshProUGUI _subtitle;
        private static Button _tabScore, _tabDetails, _refreshButton, _closeButton;
        private static Button _expandAll, _collapseAll;
        private static Button _prevPlayer, _nextPlayer;
        private static TextMeshProUGUI _detailWho;
        private static RectTransform _headerRow;
        private static readonly List<TextMeshProUGUI> _headerLabels = new List<TextMeshProUGUI>();
        private static RectTransform _listContent;
        private static ScrollRect _scroll;
        private static RectTransform _grip, _mover;
        private static float _w = W, _h = H;

        // ── state ───────────────────────────────────────────────────────────────────
        private static Tab _tab = Tab.Scoreboard;
        private static long _detailPlayer;
        private static List<Snapshot> _roster = new List<Snapshot>();
        private static bool _dirty;
        private static bool _openedThisFrame;
        private static bool _buildFailed;
        private static int _closedFrame = -10;
        private static readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);
        private static bool _collapsedLoaded;
        private static bool _scrollReset = true;

        internal static bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>True on the frame the panel closed, so the key that closed it does not also act on the world.</summary>
        internal static bool JustClosed => Time.frameCount - _closedFrame <= 1;

        // ── the scoreboard's columns ────────────────────────────────────────────────

        private sealed class Column
        {
            internal string Key;
            internal string Header;
            internal float Start, End;                    // fractions of the row width
            internal Func<Snapshot, float> Sort;
            internal Func<Snapshot, string> Text;
            internal bool RightAlign = true;
        }

        private static readonly Column[] Columns =
        {
            new Column { Key = "Player",  Header = "Player", Start = 0.00f, End = 0.26f, RightAlign = false,
                         Sort = null, Text = s => s.Name },
            new Column { Key = "Kills",   Header = "Kills",  Start = 0.26f, End = 0.38f,
                         Sort = s => s.Kills, Text = s => StatGroups.Count(s.Kills) },
            new Column { Key = "Deaths",  Header = "Deaths", Start = 0.38f, End = 0.49f,
                         Sort = s => s.Deaths, Text = s => StatGroups.Count(s.Deaths) },
            new Column { Key = "K/D",     Header = "K/D",    Start = 0.49f, End = 0.59f,
                         Sort = s => s.KillDeath, Text = s => s.KillDeath.ToString("0.0") },
            new Column { Key = "Bosses",  Header = "Bosses", Start = 0.59f, End = 0.70f,
                         Sort = s => s.Bosses, Text = s => StatGroups.Count(s.Bosses) },
            new Column { Key = "Played",  Header = "Played", Start = 0.70f, End = 0.84f,
                         Sort = s => s.Played, Text = s => StatGroups.Duration(s.Played) },
            new Column { Key = "Skill",   Header = "Best skill", Start = 0.84f, End = 1.00f,
                         Sort = s => s.BestSkillLevel, Text = s => s.BestSkillText },
        };

        // ── open / close ────────────────────────────────────────────────────────────

        internal static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        internal static void Open()
        {
            if (Player.m_localPlayer == null) return;
            if (_root == null && !Build())
            {
                DudeWhatAreMyStatsMod.Message("Stats panel unavailable; try dwams_status in the console.");
                return;
            }
            LoadCollapsed();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            _openedThisFrame = true;
            StatsPause.Refresh();
            StatsNetwork.RequestNow();
            // Opening the board is a good moment to leave the server a fresh copy of our own
            // numbers, so they are current for whoever looks after we have logged off.
            StatsNetwork.PushLocal();
            _scrollReset = true;
            Populate();
            _dirty = false;   // Populate just ran; do not rebuild the whole list again next frame
        }

        internal static void Close()
        {
            if (_root != null && _root.activeSelf)
            {
                _root.SetActive(false);
                _closedFrame = Time.frameCount;
            }
            StatsPause.Refresh();
        }

        /// <summary>
        /// A player's stats arrived. The scoreboard shows everyone, so any answer changes it; the
        /// details tab shows one player, and rebuilding its two hundred rows because somebody else
        /// answered would throw away the reader's place in the list every few seconds.
        /// </summary>
        internal static void OnRosterChanged(long identity)
        {
            if (IsOpen && _tab == Tab.Details && identity != _detailPlayer) return;
            _dirty = true;
        }

        /// <summary>
        /// The whole stored roster arrived from the server, so who is on the board may have changed
        /// rather than just one player's numbers. Always worth a rebuild.
        /// </summary>
        internal static void OnRosterRebuilt() => _dirty = true;

        // ── per-frame ───────────────────────────────────────────────────────────────

        internal static void Update()
        {
            if (!IsOpen)
            {
                // The hotkey opens the panel only when nothing else owns the keyboard.
                if (Keys.IsDown(DwamsConfig.OpenKey.Value) && Keys.CanTakeInput()) Open();
                return;
            }

            if (_openedThisFrame)
            {
                _openedThisFrame = false;
                return;
            }

            // The console opens over the panel (its toggle ignores text prompts) and `dwams` in
            // the console is itself a way to get here, so while the console or chat has the
            // keyboard the panel must keep its hands off it: otherwise typing an "i" closes the
            // panel, Tab both completes and switches tabs, and the arrows move two things at once.
            if (Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()))
            {
                StatsPause.Refresh();
                return;
            }

            // While the panel is up it owns the keyboard: Escape and the hotkey both close it.
            if (ZInput.GetKeyDown(KeyCode.Escape) || Keys.IsDown(DwamsConfig.OpenKey.Value))
            {
                Close();
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Tab)) { SetTab(_tab == Tab.Scoreboard ? Tab.Details : Tab.Scoreboard); return; }
            if (_tab == Tab.Details)
            {
                if (ZInput.GetKeyDown(KeyCode.LeftArrow)) { StepPlayer(-1); return; }
                if (ZInput.GetKeyDown(KeyCode.RightArrow)) { StepPlayer(1); return; }
            }

            StatsPause.Refresh();
            if (_dirty)
            {
                _dirty = false;
                Populate();
            }
            SyncHeader();
        }

        // ── contents ────────────────────────────────────────────────────────────────

        private static void Populate()
        {
            if (_listContent == null) return;
            _roster = StatsNetwork.Roster();
            SortRoster();
            UiKit.ClearChildren(_listContent);

            if (_tab == Tab.Scoreboard) PopulateScoreboard();
            else PopulateDetails();

            UpdateChrome();
            SyncHeader();
            if (_scrollReset && _scroll != null)
            {
                _scrollReset = false;
                _scroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>
        /// Lines the column headers up with the rows below them. The rows sit inside the scroll
        /// view's padding and the layout group's padding, and the viewport narrows again whenever
        /// the scrollbar appears, so the header cannot simply share the scroll rect's width: it has
        /// to follow the width the rows actually get, which is only known once Unity has laid out.
        /// </summary>
        private static void SyncHeader()
        {
            if (_headerRow == null || _listContent == null) return;
            if (!_headerRow.gameObject.activeSelf) return;
            float pad = 2f * UiKit.ListPadding;
            float inner = _listContent.rect.width - pad;
            if (inner <= 0f) return;
            UiKit.Place(_headerRow, 30f + pad, ListTop - HeaderHeight - 2f, inner, HeaderHeight);
        }

        private static void PopulateScoreboard()
        {
            if (_roster.Count == 0)
            {
                UiKit.Row(_listContent, "No stats yet.", "", null, null, 17f, UiKit.Dim);
                return;
            }
            foreach (var snap in _roster)
            {
                var captured = snap;
                bool offline = !snap.IsLocal && !snap.Online;
                Color color = snap.IsLocal ? UiKit.Gold : (offline ? UiKit.Dim : UiKit.Body);
                // An offline row says when it was last true rather than just "offline", so nobody
                // argues over numbers that turn out to be a fortnight old.
                string age = snap.LastSeenText;
                string mark = snap.IsLocal ? "  (you)" : offline ? "  (" + (age.Length > 0 ? age : "offline") + ")" : "";
                string name = snap.Name + mark;
                TableRow(name, captured, color, () => ShowDetails(captured.Identity));
            }
        }

        /// <summary>One scoreboard row: every column placed by fraction so it follows the panel's width.</summary>
        private static void TableRow(string nameText, Snapshot snap, Color color, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                                    typeof(Button), typeof(LayoutElement), typeof(UiKit.Hover));
            go.transform.SetParent(_listContent, false);
            var img = go.GetComponent<Image>();
            img.color = UiKit.RowColor;
            img.raycastTarget = true;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = UiKit.RowHeight;
            le.minHeight = UiKit.RowHeight;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var hover = go.GetComponent<UiKit.Hover>();
            hover.OnHoverChanged = on => { if (img != null) img.color = on ? UiKit.RowHover : UiKit.RowColor; };

            for (int i = 0; i < Columns.Length; i++)
            {
                var col = Columns[i];
                string text = i == 0 ? nameText : col.Text(snap);
                var label = UiKit.Text(go.transform, col.Key, text, 17f,
                    col.RightAlign ? TextAlignmentOptions.Right : TextAlignmentOptions.Left, color);
                SpanColumn(label.rectTransform, col);
            }
        }

        /// <summary>Anchors a cell to its column's slice of the row, with a little breathing room.</summary>
        private static void SpanColumn(RectTransform rt, Column col)
        {
            rt.anchorMin = new Vector2(col.Start, 0f);
            rt.anchorMax = new Vector2(col.End, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(6f, 0f);
            rt.offsetMax = new Vector2(-6f, 0f);
        }

        private static void PopulateDetails()
        {
            var snap = Current();
            if (snap == null)
            {
                UiKit.Row(_listContent, "No stats yet.", "", null, null, 17f, UiKit.Dim);
                return;
            }

            // A one-line summary, then the sections.
            Section("Summary", () =>
            {
                UiKit.Row(_listContent, "Kills", StatGroups.Count(snap.Kills), null, null);
                UiKit.Row(_listContent, "Deaths", StatGroups.Count(snap.Deaths), null, null);
                UiKit.Row(_listContent, "Kills per death", snap.KillDeath.ToString("0.0"), null, null);
                UiKit.Row(_listContent, "Boss kills", StatGroups.Count(snap.Bosses), null, null);
                UiKit.Row(_listContent, "Time played", StatGroups.Duration(snap.Played), null, null);
                UiKit.Row(_listContent, "Skill levels total", StatGroups.Count(snap.SkillTotal), null, null);
            });

            if (snap.Skills.Count > 0)
                Section("Skills", () =>
                {
                    foreach (var s in snap.Skills)
                        UiKit.Row(_listContent, Snapshot.SkillName(s.Key), s.Value.ToString("0.0"), null, null);
                });

            // Every stat the game tracks, bucketed and sorted by name inside each bucket.
            bool showZero = DwamsConfig.ShowZeroStats != null && DwamsConfig.ShowZeroStats.Value;
            var buckets = new Dictionary<string, List<KeyValuePair<PlayerStatType, float>>>(StringComparer.Ordinal);
            foreach (PlayerStatType stat in Enum.GetValues(typeof(PlayerStatType)))
            {
                if (stat == PlayerStatType.Count || stat == PlayerStatType.None) continue;
                float value = snap.Stat(stat);
                if (value == 0f && !showZero) continue;
                string section = StatGroups.Section(stat);
                if (!buckets.TryGetValue(section, out var list)) buckets[section] = list = new List<KeyValuePair<PlayerStatType, float>>();
                list.Add(new KeyValuePair<PlayerStatType, float>(stat, value));
            }

            foreach (var name in StatGroups.Order)
            {
                if (!buckets.TryGetValue(name, out var list) || list.Count == 0) continue;
                list.Sort((a, b) => string.Compare(StatGroups.Label(a.Key), StatGroups.Label(b.Key), StringComparison.Ordinal));
                var captured = list;
                Section(name, () =>
                {
                    foreach (var kv in captured)
                        UiKit.Row(_listContent, StatGroups.Label(kv.Key), StatGroups.Format(kv.Key, kv.Value), null, null);
                });
            }

            int topCreatures = DwamsConfig.TopCreatureCount != null ? DwamsConfig.TopCreatureCount.Value : 15;
            if (topCreatures > 0 && snap.Creatures.Count > 0)
                Section("Creatures killed", () =>
                {
                    foreach (var kv in snap.Creatures)
                        UiKit.Row(_listContent, CreatureName(kv.Key), StatGroups.Count(kv.Value), null, null);
                });
        }

        /// <summary>A collapsible section header plus its rows, remembering the fold between sessions.</summary>
        private static void Section(string title, Action body)
        {
            bool collapsed = _collapsed.Contains(title);
            UiKit.SectionHeader(_listContent, title, () =>
            {
                if (!_collapsed.Remove(title)) _collapsed.Add(title);
                SaveCollapsed();
                _dirty = true;
            }, collapsed);
            if (!collapsed) body();
        }

        /// <summary>"$enemy_greydwarf" becomes "Greydwarf"; an unlocalized key is spaced out instead.</summary>
        private static string CreatureName(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(unknown)";
            if (key.StartsWith("$", StringComparison.Ordinal) && Localization.instance != null)
            {
                string localized = Localization.instance.Localize(key);
                if (!string.IsNullOrEmpty(localized) && localized != key && !localized.StartsWith("[", StringComparison.Ordinal))
                    return localized;
            }
            return key.TrimStart('$');
        }

        // ── sorting and selection ───────────────────────────────────────────────────

        private static Column SortColumn()
        {
            string key = DwamsConfig.SortColumn != null ? DwamsConfig.SortColumn.Value : "Kills";
            foreach (var c in Columns)
                if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) return c;
            return Columns[1];
        }

        private static void SortRoster()
        {
            var col = SortColumn();
            bool desc = DwamsConfig.SortDescending == null || DwamsConfig.SortDescending.Value;
            _roster.Sort((a, b) =>
            {
                int cmp = col.Sort == null
                    ? string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase)
                    : col.Sort(a).CompareTo(col.Sort(b));
                if (cmp == 0) cmp = string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
                return desc ? -cmp : cmp;
            });
        }

        private static void OnHeaderClicked(Column col)
        {
            if (DwamsConfig.SortColumn == null) return;
            if (string.Equals(DwamsConfig.SortColumn.Value, col.Key, StringComparison.OrdinalIgnoreCase))
                DwamsConfig.SortDescending.Value = !DwamsConfig.SortDescending.Value;
            else
            {
                DwamsConfig.SortColumn.Value = col.Key;
                // Names read best A to Z; every number reads best highest first.
                DwamsConfig.SortDescending.Value = col.Sort != null;
            }
            _scrollReset = true;
            _dirty = true;
        }

        private static Snapshot Current()
        {
            foreach (var s in _roster)
                if (s.Identity == _detailPlayer) return s;
            foreach (var s in _roster)
                if (s.IsLocal) return s;
            return _roster.Count > 0 ? _roster[0] : null;
        }

        private static void ShowDetails(long identity)
        {
            _detailPlayer = identity;
            _scrollReset = true;
            SetTab(Tab.Details);
        }

        private static void StepPlayer(int delta)
        {
            if (_roster.Count == 0) return;
            var current = Current();
            int index = current != null ? _roster.IndexOf(current) : 0;
            index = ((index + delta) % _roster.Count + _roster.Count) % _roster.Count;
            _detailPlayer = _roster[index].Identity;
            _scrollReset = true;
            _dirty = true;
        }

        private static void SetTab(Tab tab)
        {
            if (_tab != tab) _scrollReset = true;
            _tab = tab;
            if (tab == Tab.Details && _detailPlayer == 0L)
            {
                var local = Current();
                if (local != null) _detailPlayer = local.Identity;
            }
            _dirty = true;
        }

        // ── chrome that changes with the state ──────────────────────────────────────

        private static void UpdateChrome()
        {
            if (_subtitle != null)
            {
                int online = 0;
                foreach (var s in _roster) if (s.IsLocal || s.Online) online++;
                // Game.IsPaused reaches into ZNet without a null check of its own, so ask only while connected.
                string paused = StatsPause.Holding && ZNet.instance != null && Game.IsPaused() ? "  ·  game paused" : "";
                string asking = DwamsConfig.AskOtherPlayers != null && !DwamsConfig.AskOtherPlayers.Value
                    ? "  ·  not asking other players" : "";
                _subtitle.text = $"{online} player{(online == 1 ? "" : "s")}{paused}{asking}";
            }
            if (_tabScore != null) UiKit.SetLabel(_tabScore.gameObject, _tab == Tab.Scoreboard ? "<b>Scoreboard</b>" : "Scoreboard");
            if (_tabDetails != null) UiKit.SetLabel(_tabDetails.gameObject, _tab == Tab.Details ? "<b>Details</b>" : "Details");

            bool details = _tab == Tab.Details;
            if (_headerRow != null) _headerRow.gameObject.SetActive(!details);
            if (_expandAll != null) _expandAll.gameObject.SetActive(details);
            if (_collapseAll != null) _collapseAll.gameObject.SetActive(details);
            if (_prevPlayer != null) _prevPlayer.gameObject.SetActive(details && _roster.Count > 1);
            if (_nextPlayer != null) _nextPlayer.gameObject.SetActive(details && _roster.Count > 1);
            if (_detailWho != null)
            {
                _detailWho.gameObject.SetActive(details);
                var snap = Current();
                _detailWho.text = snap == null ? "" : snap.Name + (snap.IsLocal ? "  (you)" : snap.Online ? "" : "  (offline)");
            }

            if (!details)
            {
                var sorted = SortColumn();
                bool desc = DwamsConfig.SortDescending == null || DwamsConfig.SortDescending.Value;
                for (int i = 0; i < _headerLabels.Count && i < Columns.Length; i++)
                {
                    bool active = Columns[i] == sorted;
                    _headerLabels[i].text = Columns[i].Header + (active ? (desc ? "  ▼" : "  ▲") : "");
                    _headerLabels[i].color = active ? UiKit.Gold : UiKit.Header;
                }
            }
        }

        // ── building the panel ──────────────────────────────────────────────────────

        private static bool Build()
        {
            if (_buildFailed) return false;
            try
            {
                if (BuildInner()) return true;
                // The game's text prompt was not there to clone. That happens during a scene
                // change and fixes itself, so this is not latched: the next keypress tries again.
            }
            catch (Exception e)
            {
                // A real fault. Latch it, or every keypress repeats the same exception.
                DudeWhatAreMyStatsMod.Log.LogError($"[DudeWhatAreMyStats] Building the stats panel failed: {e}");
                _buildFailed = true;
            }
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            return false;
        }

        private static bool BuildInner()
        {
            var src = TextInput.instance;
            if (src == null || src.m_panel == null)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning("[DudeWhatAreMyStats] The game's text prompt is not available; cannot build the stats panel.");
                return false;
            }
            UiKit.EnsureFont();

            _root = UnityEngine.Object.Instantiate(src.m_panel, src.m_panel.transform.parent);
            _root.name = "DWAMS_StatsPanel";
            _root.SetActive(false);

            foreach (var c in _root.GetComponents<LayoutGroup>()) UnityEngine.Object.Destroy(c);
            foreach (var c in _root.GetComponents<ContentSizeFitter>()) UnityEngine.Object.Destroy(c);

            // Borrow one of the prompt's buttons as a template, then drop the prompt's own widgets.
            Button template = null;
            foreach (var b in _root.GetComponentsInChildren<Button>(true)) { template = b; break; }

            var content = new GameObject("DWAMS_Content", typeof(RectTransform));
            content.transform.SetParent(_root.transform, false);
            UiKit.Stretch(content.GetComponent<RectTransform>());
            _content = content.GetComponent<RectTransform>();

            if (template != null)
            {
                template = UnityEngine.Object.Instantiate(template, content.transform);
                template.gameObject.SetActive(false);
                template.name = "ButtonTemplate";
            }

            // Of what the prompt came with, keep only flat background art; everything else goes.
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

            _title = UiKit.Text(content.transform, "Title", "Dude, What Are My Stats?", 26f, TextAlignmentOptions.Center, UiKit.Gold);
            _subtitle = UiKit.Text(content.transform, "Subtitle", "", 15f, TextAlignmentOptions.Center, UiKit.Dim);

            _tabScore = MakeButton(template, content.transform, "TabScoreboard", "Scoreboard", () => SetTab(Tab.Scoreboard));
            _tabDetails = MakeButton(template, content.transform, "TabDetails", "Details", () => SetTab(Tab.Details));
            _refreshButton = MakeButton(template, content.transform, "Refresh", "Refresh", () => { StatsNetwork.RequestNow(); _dirty = true; });
            _closeButton = MakeButton(template, content.transform, "Close", "Close", Close);

            _prevPlayer = MakeButton(template, content.transform, "PrevPlayer", "<", () => StepPlayer(-1));
            _nextPlayer = MakeButton(template, content.transform, "NextPlayer", ">", () => StepPlayer(1));
            _detailWho = UiKit.Text(content.transform, "DetailWho", "", 19f, TextAlignmentOptions.Center, UiKit.Gold);

            _expandAll = UiKit.LinkButton(content.transform, "ExpandAll", "Expand all", () => { _collapsed.Clear(); SaveCollapsed(); _dirty = true; });
            _collapseAll = UiKit.LinkButton(content.transform, "CollapseAll", "Collapse all", () =>
            {
                _collapsed.Clear();
                _collapsed.Add("Summary");
                _collapsed.Add("Skills");
                _collapsed.Add("Creatures killed");
                foreach (var s in StatGroups.Order) _collapsed.Add(s);
                SaveCollapsed();
                _dirty = true;
            });

            // The scoreboard's column headers sit above the list and sort it when clicked.
            var headerGo = new GameObject("Headers", typeof(RectTransform));
            headerGo.transform.SetParent(content.transform, false);
            _headerRow = headerGo.GetComponent<RectTransform>();
            _headerLabels.Clear();
            foreach (var col in Columns)
            {
                var captured = col;
                var cell = new GameObject("H_" + col.Key, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                cell.transform.SetParent(headerGo.transform, false);
                var cimg = cell.GetComponent<Image>();
                cimg.color = new Color(1f, 1f, 1f, 0.04f);
                cimg.raycastTarget = true;
                var cbtn = cell.GetComponent<Button>();
                cbtn.transition = Selectable.Transition.None;
                cbtn.onClick.AddListener(() => OnHeaderClicked(captured));
                SpanColumn(cell.GetComponent<RectTransform>(), col);

                var label = UiKit.Text(cell.transform, "Label", col.Header, 16f,
                    col.RightAlign ? TextAlignmentOptions.Right : TextAlignmentOptions.Left, UiKit.Header);
                UiKit.Stretch(label.rectTransform);
                _headerLabels.Add(label);
            }

            _listContent = UiKit.ScrollList(content.transform, "List", out _scroll);

            foreach (var l in _root.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.Destroy(l);

            // Resize grip in the bottom-right corner; the size is remembered in the config.
            var gripGo = new GameObject("Grip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiKit.DragHandle));
            gripGo.transform.SetParent(content.transform, false);
            _grip = gripGo.GetComponent<RectTransform>();
            var gripImg = gripGo.GetComponent<Image>();
            gripImg.sprite = UiKit.Grip();
            gripImg.color = new Color(1f, 0.85f, 0.45f, 0.75f);
            gripImg.raycastTarget = true;
            var gripHandle = gripGo.GetComponent<UiKit.DragHandle>();
            gripHandle.OnDrag = OnGripDrag;
            gripHandle.OnEnd = SavePlacement;

            // The title area drags the whole panel; the position is remembered too.
            var moveGo = new GameObject("Mover", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiKit.DragHandle));
            moveGo.transform.SetParent(content.transform, false);
            _mover = moveGo.GetComponent<RectTransform>();
            var moveImg = moveGo.GetComponent<Image>();
            moveImg.color = new Color(0f, 0f, 0f, 0f);   // invisible, but it catches the pointer
            moveImg.raycastTarget = true;
            var mover = moveGo.GetComponent<UiKit.DragHandle>();
            mover.OnDrag = OnMoveDrag;
            mover.OnEnd = SavePlacement;
            // Left as the last sibling on purpose: a drag has to reach it rather than the panel's
            // background art, which is a plain graphic and would swallow the pointer without
            // handling the drag. It only covers the title strip, above every control.

            LoadPlacement();
            Layout();
            return true;
        }

        private static Button MakeButton(Button template, Transform parent, string name, string label, Action onClick)
        {
            return template != null
                ? UiKit.CloneButton(template, parent, name, label, onClick)
                : UiKit.SimpleButton(parent, name, label, onClick);
        }

        /// <summary>A child of the prompt that is pure background art: a graphic with no text or controls inside.</summary>
        private static bool IsDecoration(Transform child)
        {
            if (child.GetComponentInChildren<TMP_Text>(true) != null) return false;
            if (child.GetComponentInChildren<Selectable>(true) != null) return false;
            return child.GetComponentInChildren<Graphic>(true) != null && child.GetComponent<RectTransform>() != null;
        }

        // ── size and layout ─────────────────────────────────────────────────────────

        private static void Layout()
        {
            if (_root == null) return;
            float w = _w, h = _h;
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);

            if (_title != null) UiKit.Place(_title.rectTransform, 0f, 14f, w, 34f);
            if (_subtitle != null) UiKit.Place(_subtitle.rectTransform, 0f, 48f, w, 22f);

            float tabY = 76f, tabH = 34f;
            if (_tabScore != null) UiKit.Place(_tabScore.GetComponent<RectTransform>(), 30f, tabY, 140f, tabH);
            if (_tabDetails != null) UiKit.Place(_tabDetails.GetComponent<RectTransform>(), 178f, tabY, 140f, tabH);
            if (_refreshButton != null) UiKit.Place(_refreshButton.GetComponent<RectTransform>(), w - 290f, tabY, 120f, tabH);
            if (_closeButton != null) UiKit.Place(_closeButton.GetComponent<RectTransform>(), w - 160f, tabY, 120f, tabH);

            // The details tab's player stepper sits on the row below the tabs. The name field stops
            // well short of the fold links on the right so the two never overlap at any width.
            float whoY = 118f;
            float whoWidth = Mathf.Max(120f, w - 380f);
            if (_prevPlayer != null) UiKit.Place(_prevPlayer.GetComponent<RectTransform>(), 30f, whoY, 36f, 28f);
            if (_detailWho != null) UiKit.Place(_detailWho.rectTransform, 74f, whoY, whoWidth, 28f);
            if (_nextPlayer != null) UiKit.Place(_nextPlayer.GetComponent<RectTransform>(), 74f + whoWidth + 6f, whoY, 36f, 28f);
            if (_expandAll != null) UiKit.Place(_expandAll.GetComponent<RectTransform>(), w - 202f, whoY + 4f, 82f, 22f);
            if (_collapseAll != null) UiKit.Place(_collapseAll.GetComponent<RectTransform>(), w - 116f, whoY + 4f, 86f, 22f);

            if (_scroll != null) UiKit.Place(_scroll.GetComponent<RectTransform>(), 30f, ListTop, w - 60f, h - ListTop - 30f);
            SyncHeader();

            if (_mover != null) UiKit.Place(_mover, 0f, 0f, w, 70f);
            if (_grip != null)
            {
                _grip.anchorMin = new Vector2(1f, 0f);
                _grip.anchorMax = new Vector2(1f, 0f);
                _grip.pivot = new Vector2(1f, 0f);
                _grip.anchoredPosition = new Vector2(-5f, 5f);
                _grip.sizeDelta = new Vector2(18f, 18f);
            }
        }

        /// <summary>The grip follows the pointer: the bottom-right corner moves, the top-left corner stays.</summary>
        private static void OnGripDrag(Vector2 screenDelta)
        {
            if (_root == null) return;
            float scale = CanvasScale();
            float nw = Mathf.Clamp(_w + screenDelta.x / scale, MinW, MaxW);
            float nh = Mathf.Clamp(_h - screenDelta.y / scale, MinH, MaxH);
            var rt = _root.GetComponent<RectTransform>();
            rt.anchoredPosition += new Vector2((nw - _w) / 2f, -(nh - _h) / 2f);
            _w = nw;
            _h = nh;
            Layout();
        }

        private static void OnMoveDrag(Vector2 screenDelta)
        {
            if (_root == null) return;
            var rt = _root.GetComponent<RectTransform>();
            rt.anchoredPosition += screenDelta / CanvasScale();
            ClampToScreen(rt);
        }

        private static float CanvasScale()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            return scale <= 0f ? 1f : scale;
        }

        /// <summary>Keeps at least a corner of the panel on screen, so it can always be dragged back.</summary>
        private static void ClampToScreen(RectTransform rt)
        {
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float halfW = parent.rect.width / 2f, halfH = parent.rect.height / 2f;
            var p = rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -halfW, halfW);
            p.y = Mathf.Clamp(p.y, -halfH, halfH);
            rt.anchoredPosition = p;
        }

        private static void LoadPlacement()
        {
            _w = W;
            _h = H;
            if (TryPair(DwamsConfig.PanelSize != null ? DwamsConfig.PanelSize.Value : "", out float w, out float h))
            {
                _w = Mathf.Clamp(w, MinW, MaxW);
                _h = Mathf.Clamp(h, MinH, MaxH);
            }
            if (_root != null)
            {
                var rt = _root.GetComponent<RectTransform>();
                rt.anchoredPosition = TryPair(DwamsConfig.PanelPosition != null ? DwamsConfig.PanelPosition.Value : "", out float x, out float y)
                    ? new Vector2(x, y) : Vector2.zero;
                ClampToScreen(rt);
            }
        }

        private static void SavePlacement()
        {
            if (DwamsConfig.PanelSize != null) DwamsConfig.PanelSize.Value = $"{Mathf.RoundToInt(_w)},{Mathf.RoundToInt(_h)}";
            if (DwamsConfig.PanelPosition != null && _root != null)
            {
                var p = _root.GetComponent<RectTransform>().anchoredPosition;
                DwamsConfig.PanelPosition.Value = $"{Mathf.RoundToInt(p.x)},{Mathf.RoundToInt(p.y)}";
            }
        }

        private static bool TryPair(string text, out float a, out float b)
        {
            a = 0f;
            b = 0f;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(',');
            return parts.Length == 2
                && float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a)
                && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b);
        }

        // ── which sections are folded away ──────────────────────────────────────────

        private static void LoadCollapsed()
        {
            if (_collapsedLoaded) return;
            _collapsedLoaded = true;
            _collapsed.Clear();
            string raw = DwamsConfig.CollapsedGroups != null ? DwamsConfig.CollapsedGroups.Value : "";
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var part in raw.Split('|'))
            {
                string name = part.Trim();
                if (name.Length > 0) _collapsed.Add(name);
            }
        }

        private static void SaveCollapsed()
        {
            if (DwamsConfig.CollapsedGroups == null) return;
            DwamsConfig.CollapsedGroups.Value = string.Join("|", new List<string>(_collapsed).ToArray());
        }
    }
}
