using System.Collections.Generic;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// IMGUI panel for managing loadout slots on an Armory Rack.
    /// Call Open() when the player interacts with the rack, OnGUI() from the plugin's
    /// OnGUI(), and Tick() from Update() to handle escape/distance checks.
    /// </summary>
    internal static class ArmoryUI
    {
        // ── State ──────────────────────────────────────────────────────────────────

        private static ArmoryRack _rack;
        private static ArmoryData _data;
        private static bool       _isOpen;
        public  static bool       IsOpen => _isOpen;

        private const int DefaultSlotCount = 5;
        private const int MaxSlotCount     = 10;

        // Interaction range — player must stay within this distance.
        private const float MaxInteractDistance = 5f;

        // ── Window rects ───────────────────────────────────────────────────────────

        private static Rect _mainRect    = new Rect(100, 100, 580, 520);
        private static Rect _compareRect = new Rect(700, 100, 370, 320);
        private static Vector2 _scroll;

        // ── Per-slot editing ───────────────────────────────────────────────────────

        private static int    _editingSlot  = -1;
        private static string _editingName  = "";
        private static int    _compareSlot  = -1;

        // ── GUIStyles (initialised once inside OnGUI) ──────────────────────────────

        private static bool     _stylesReady;
        private static GUIStyle _styleTitle;
        private static GUIStyle _styleSlotName;
        private static GUIStyle _styleBody;
        private static GUIStyle _styleHeader;
        private static GUIStyle _styleWindowBg;

        // ── Public API ─────────────────────────────────────────────────────────────

        public static void Open(ArmoryRack rack)
        {
            _rack       = rack;
            _data       = rack.GetData();
            LoadoutManager.EnsureSlots(_data, DefaultSlotCount);
            _compareSlot = -1;
            _editingSlot = -1;
            _isOpen      = true;
        }

        public static void Close()
        {
            _isOpen      = false;
            _rack        = null;
            _compareSlot = -1;
            _editingSlot = -1;

            // Return cursor control to the game.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        public static void Tick()
        {
            if (!_isOpen) return;

            // Keep cursor unlocked while UI is open.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            // Close on Escape.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null || _rack == null)
            {
                Close();
                return;
            }

            // Close if player walks away from the rack.
            if (Vector3.Distance(player.transform.position, _rack.transform.position) > MaxInteractDistance)
            {
                player.Message(MessageHud.MessageType.Center, "Moved too far from the Armory Rack.");
                Close();
            }
        }

        public static void OnGUI()
        {
            if (!_isOpen) return;
            InitStyles();

            // Reposition windows to screen-centre on first open (when rect is at default).
            if (_mainRect.x == 100 && _mainRect.y == 100)
            {
                _mainRect.x = Screen.width  / 2f - _mainRect.width  / 2f;
                _mainRect.y = Screen.height / 2f - _mainRect.height / 2f;
            }

            GUI.backgroundColor = new Color(0.12f, 0.10f, 0.07f, 0.97f);
            _mainRect = GUILayout.Window(7_654_321, _mainRect, DrawMainWindow, GUIContent.none, _styleWindowBg);
            GUI.backgroundColor = Color.white;

            if (_compareSlot >= 0 && _compareSlot < _data.Slots.Count)
            {
                GUI.backgroundColor = new Color(0.08f, 0.08f, 0.13f, 0.97f);
                _compareRect = GUILayout.Window(7_654_322, _compareRect, DrawCompareWindow, GUIContent.none, _styleWindowBg);
                GUI.backgroundColor = Color.white;
            }
        }

        // ── Main loadout window ────────────────────────────────────────────────────

        private static void DrawMainWindow(int id)
        {
            var player = Player.m_localPlayer;
            if (player == null) { Close(); return; }

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            // ── Title bar ──
            GUILayout.BeginHorizontal();
            GUILayout.Label("Armory Rack", _styleTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(28), GUILayout.Height(24)))
                Close();
            GUILayout.EndHorizontal();

            DrawSeparator();

            // ── Slot rows ──
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _data.Slots.Count; i++)
                DrawSlotRow(i, player);
            GUILayout.EndScrollView();

            DrawSeparator();

            // ── Footer ──
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.enabled = _data.Slots.Count < MaxSlotCount;
            if (GUILayout.Button("+ Add Slot", GUILayout.Width(90)))
            {
                _data.Slots.Add(new LoadoutSlot { Name = $"Slot {_data.Slots.Count + 1}" });
                _rack.SaveData(_data);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, _mainRect.width, 36));
        }

        private static void DrawSlotRow(int i, Player player)
        {
            var slot = _data.Slots[i];

            // Alternating row tint.
            GUI.backgroundColor = (i % 2 == 0)
                ? new Color(0.22f, 0.18f, 0.12f, 1f)
                : new Color(0.18f, 0.15f, 0.10f, 1f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;

            // ── Name + buttons ──
            GUILayout.BeginHorizontal();

            if (_editingSlot == i)
            {
                // Inline name editor.
                _editingName = GUILayout.TextField(_editingName, GUILayout.Width(165));
                if (GUILayout.Button("OK", GUILayout.Width(32)))
                    CommitNameEdit(i, slot);
            }
            else
            {
                GUILayout.Label(slot.Name, _styleSlotName, GUILayout.Width(165));
                if (GUILayout.Button("✎", GUILayout.Width(28)))
                {
                    _editingSlot = i;
                    _editingName = slot.Name;
                }
            }

            GUILayout.FlexibleSpace();

            // Save current gear into this slot.
            if (GUILayout.Button("Save", GUILayout.Width(52)))
            {
                var captured  = LoadoutManager.CaptureCurrentEquipment(player);
                captured.Name = slot.Name;
                _data.Slots[i] = captured;
                _rack.SaveData(_data);
                player.Message(MessageHud.MessageType.Center, $"Saved: {slot.Name}");
            }

            // Load slot onto player (items must be in inventory).
            GUI.enabled = !slot.IsEmpty();
            if (GUILayout.Button("Load", GUILayout.Width(52)))
            {
                var (found, missing) = LoadoutManager.ApplyLoadout(player, slot);
                var msg = missing > 0
                    ? $"Loaded '{slot.Name}': {found} equipped, {missing} not found in inventory"
                    : $"Loaded '{slot.Name}': {found} item(s) equipped";
                player.Message(MessageHud.MessageType.Center, msg);
            }
            GUI.enabled = true;

            // Toggle comparison panel.
            var prevBg = GUI.backgroundColor;
            if (_compareSlot == i) GUI.backgroundColor = new Color(0.35f, 0.35f, 0.75f, 1f);
            if (GUILayout.Button("Cmp", GUILayout.Width(42)))
                _compareSlot = (_compareSlot == i) ? -1 : i;
            GUI.backgroundColor = prevBg;

            // Clear slot contents (keeps the name).
            GUI.backgroundColor = new Color(0.55f, 0.18f, 0.18f, 1f);
            if (GUILayout.Button("✕", GUILayout.Width(28)))
            {
                _data.Slots[i] = new LoadoutSlot { Name = slot.Name };
                _rack.SaveData(_data);
                if (_compareSlot == i) _compareSlot = -1;
                player.Message(MessageHud.MessageType.Center, $"Cleared: {slot.Name}");
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();

            // ── Item summary line ──
            GUILayout.Label(BuildSummary(slot), _styleBody);

            GUILayout.EndVertical();
            GUILayout.Space(3);
        }

        private static void CommitNameEdit(int i, LoadoutSlot slot)
        {
            slot.Name     = string.IsNullOrWhiteSpace(_editingName)
                            ? $"Slot {i + 1}"
                            : _editingName.Trim();
            _editingSlot  = -1;
            _rack.SaveData(_data);
        }

        // ── Comparison window ──────────────────────────────────────────────────────

        private static void DrawCompareWindow(int id)
        {
            if (_compareSlot < 0 || _compareSlot >= _data.Slots.Count) return;
            var player = Player.m_localPlayer;
            if (player == null) return;

            var slot      = _data.Slots[_compareSlot];
            var inventory = player.GetInventory();
            var current   = LoadoutManager.CaptureCurrentEquipment(player);

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            // Title bar.
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Compare: {slot.Name}", _styleTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(28), GUILayout.Height(24)))
                _compareSlot = -1;
            GUILayout.EndHorizontal();

            DrawSeparator();

            // Column headers.
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Slot</b>",      _styleHeader, GUILayout.Width(58));
            GUILayout.Label("<b>Equipped</b>",   _styleHeader, GUILayout.Width(130));
            GUILayout.Label("<b>Loadout</b>",    _styleHeader);
            GUILayout.EndHorizontal();

            DrawSeparator();

            DrawCompareRow("Helmet",  current.Helmet,    slot.Helmet,    inventory);
            DrawCompareRow("Chest",   current.Chest,     slot.Chest,     inventory);
            DrawCompareRow("Legs",    current.Legs,      slot.Legs,      inventory);
            DrawCompareRow("Cape",    current.Shoulder,  slot.Shoulder,  inventory);
            DrawCompareRow("Utility", current.Utility,   slot.Utility,   inventory);
            DrawCompareRow("Weapon",  current.RightHand, slot.RightHand, inventory);
            DrawCompareRow("Shield",  current.LeftHand,  slot.LeftHand,  inventory);

            DrawSeparator();

            // Legend.
            GUILayout.Label(
                "<color=#66cc66>■</color> same  " +
                "<color=#ccaa44>■</color> different  " +
                "<color=#cc4444>■</color> not in inventory",
                _styleBody);

            GUILayout.Space(4);
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, _compareRect.width, 36));
        }

        private static void DrawCompareRow(string label, SavedItem current, SavedItem saved, Inventory inventory)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#aaaaaa>{label}</color>", _styleBody, GUILayout.Width(58));

            GUILayout.Label(current != null
                ? LoadoutManager.GetItemDisplayName(current)
                : "<color=#555555>—</color>",
                _styleBody, GUILayout.Width(130));

            if (saved != null)
            {
                var name = LoadoutManager.GetItemDisplayName(saved);
                bool same        = current != null
                                   && current.SharedName == saved.SharedName
                                   && current.Quality    == saved.Quality;
                bool inInventory = LoadoutManager.IsInInventory(inventory, saved);
                var color = same ? "#66cc66" : (inInventory ? "#ccaa44" : "#cc4444");
                GUILayout.Label($"<color={color}>{name} (Q{saved.Quality})</color>", _styleBody);
            }
            else
            {
                GUILayout.Label("<color=#555555>—</color>", _styleBody);
            }

            GUILayout.EndHorizontal();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static string BuildSummary(LoadoutSlot slot)
        {
            if (slot.IsEmpty())
                return "<color=#666666>empty</color>";

            var parts = new List<string>(7);
            Append(parts, "Helm",   slot.Helmet);
            Append(parts, "Chest",  slot.Chest);
            Append(parts, "Legs",   slot.Legs);
            Append(parts, "Cape",   slot.Shoulder);
            Append(parts, "Wpn",    slot.RightHand);
            Append(parts, "Shld",   slot.LeftHand);
            Append(parts, "Util",   slot.Utility);
            return string.Join("  ", parts);
        }

        private static void Append(List<string> parts, string label, SavedItem item)
        {
            if (item == null) return;
            var name = LoadoutManager.GetItemDisplayName(item);
            parts.Add($"<color=#b8943a>{label}:</color>{name}");
        }

        private static void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(0.5f, 0.4f, 0.2f, 0.6f), 0, 0);
            GUILayout.Space(4);
        }

        // ── Style init ─────────────────────────────────────────────────────────────

        private static void InitStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _styleWindowBg = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(8, 8, 8, 8),
            };

            _styleTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 15,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.92f, 0.82f, 0.52f) },
            };

            _styleSlotName = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.88f, 0.78f, 0.52f) },
            };

            _styleBody = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 11,
                wordWrap  = true,
                richText  = true,
                normal    = { textColor = new Color(0.80f, 0.80f, 0.72f) },
            };

            _styleHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 12,
                fontStyle = FontStyle.Bold,
                richText  = true,
                normal    = { textColor = new Color(0.90f, 0.90f, 0.82f) },
            };
        }
    }
}
