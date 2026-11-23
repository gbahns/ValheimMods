using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace GrabMaterials
{
	// Small always-on HUD listing every grab pack that has items, with its hotkey —
	// the same idea as AzuExtendedPlayerInventory's quick-slot labels, for people
	// (the author included) who can never remember what's on Shift+G vs Ctrl+G.
	//
	// Toggled with the "Pack HUD Toggle Key"; the on/off state is written back to
	// the "Show Pack HUD" config value so it persists across sessions.  Content is
	// rebuilt twice a second so config edits (rename a pack, change a key) show up
	// live without a restart.
	//
	// With "Show Pack Contents" on, each pack line carries the icon of every piece or item in
	// the pack after its name, with a count on the icon when the pack asks for more than one,
	// so "Explore" reads as workbench, portal, two chests without opening the config (#81).
	//
	// Every line is its own row of fixed height, holding its key, its name and its icons.
	// Until 2.11.0 the names were one multi-line Text and the icons were placed at a fixed
	// pitch beside it; the text advanced by the font's own line height, which Unity rounds to
	// whole pixels at the canvas scale, so on some screens the icons drifted half a pixel per
	// line and sat on the wrong name ten rows down (#91).  Rows cannot accumulate anything.
	// The box is sized to its widest row, so anchored to a right-hand corner the long names
	// and their icons stay on the screen.
	public static class PackHud
	{
		public enum Anchor
		{
			TopLeft,
			TopRight,
			BottomLeft,
			BottomRight,
		}

		private const float RefreshInterval = 0.5f;
		private const float MinKeysWidth = 40f;
		private const float Gap        = 10f;
		private const int   FontSize   = 16;
		private const float LineHeight = 21f;
		private const float IconSize   = 18f;
		private const float IconGap    = 3f;
		private const float IconIndent = 8f;   // between the end of the name and the first icon
		private const int   MaxIcons   = 8;    // per line; a pack longer than that shows its first eight

		private class Row
		{
			public GameObject Root;
			public RectTransform Rect;
			public Text Keys;
			public Text Name;
			public readonly List<GameObject> Icons = new List<GameObject>();
		}

		private static GameObject    _root;
		private static RectTransform _rect;
		private static readonly List<Row> _rows = new List<Row>();
		private static readonly Dictionary<string, List<ConsoleCommands.PackEntry>> _entryCache = new Dictionary<string, List<ConsoleCommands.PackEntry>>();
		private static float         _lastRefresh;

		private static readonly Color NameColor = new Color(0.95f, 0.90f, 0.78f);

		private static GrabMaterialsMod.GrabMaterialsMod Mod => GrabMaterialsMod.GrabMaterialsMod.Instance;
		private static bool   Enabled  => Mod?.ShowPackHud?.Value ?? true;
		private static Anchor Corner   => Mod?.PackHudAnchor?.Value ?? Anchor.BottomLeft;
		private static float  OffsetX  => Mod?.PackHudOffsetX?.Value ?? 10f;
		private static float  OffsetY  => Mod?.PackHudOffsetY?.Value ?? 300f;
		private static bool   ShowBuildPieceKey => Mod?.PackHudShowBuildPieceKey?.Value ?? true;
		private static bool   HideUndiscovered  => Mod?.PackHudHideUndiscovered?.Value ?? true;
		private static bool   ShowContents      => Mod?.PackHudShowContents?.Value ?? true;

		public static void Tick()
		{
			EnsureCreated();
			if (_root == null) return;

			var player = Player.m_localPlayer;
			var shouldShow = Enabled && player != null;
			if (_root.activeSelf != shouldShow) _root.SetActive(shouldShow);
			if (!shouldShow) return;

			ApplyAnchor();

			var now = Time.time;
			if (now - _lastRefresh < RefreshInterval) return;
			_lastRefresh = now;
			Rebuild();
		}

		// Called on toggle so the list is correct the instant it appears.
		public static void Refresh()
		{
			_lastRefresh = 0f;
		}

		private struct Line
		{
			public string Key;
			public string Name;
			public List<ConsoleCommands.PackEntry> Entries;   // null on lines that are not a pack
		}

		private static void Rebuild()
		{
			if (Mod?.GrabPacks == null) return;

			var lines = new List<Line>();
			var toggle = Mod.PackHudToggleKey != null
				? GrabMaterialsMod.GrabMaterialsMod.FormatShortcut(Mod.PackHudToggleKey.Value)
				: "";
			lines.Add(new Line { Key = toggle, Name = "Grab Packs" });

			foreach (var pack in Mod.GrabPacks)
			{
				if (string.IsNullOrWhiteSpace(pack.Items.Value)) continue;
				if (HideUndiscovered && !ConsoleCommands.AreAllPackMaterialsKnown(pack.Items.Value, Player.m_localPlayer)) continue;
				lines.Add(new Line
				{
					Key = GrabMaterialsMod.GrabMaterialsMod.FormatShortcut(pack.Key.Value),
					Name = pack.Name.Value,
					Entries = ShowContents ? EntriesFor(pack.Items.Value) : null,
				});
			}

			if (ShowBuildPieceKey)
				lines.Add(new Line { Key = Mod.GrabSelectedPieceKey.ToString(), Name = "Build piece under cursor" });

			// Pass one: the texts, and the widest key, which sets where the names start.
			var keysWidth = MinKeysWidth;
			for (int i = 0; i < lines.Count; i++)
			{
				var row = i < _rows.Count ? _rows[i] : MakeRow();
				row.Root.SetActive(true);
				row.Keys.text = lines[i].Key;
				row.Name.text = lines[i].Name;
				keysWidth = Mathf.Max(keysWidth, row.Keys.preferredWidth);
			}
			for (int i = lines.Count; i < _rows.Count; i++) _rows[i].Root.SetActive(false);

			// Pass two: place each row's parts and find the widest row.
			var width = 0f;
			for (int i = 0; i < lines.Count; i++)
			{
				var row = _rows[i];
				row.Rect.anchoredPosition = new Vector2(0f, -i * LineHeight);
				row.Keys.rectTransform.sizeDelta = new Vector2(keysWidth, LineHeight);
				var nameX = keysWidth + Gap;
				var nameWidth = row.Name.preferredWidth;
				row.Name.rectTransform.anchoredPosition = new Vector2(nameX, 0f);
				row.Name.rectTransform.sizeDelta = new Vector2(nameWidth, LineHeight);
				var right = nameX + nameWidth;

				var x = right + IconIndent;
				var drawn = 0;
				var entries = lines[i].Entries;
				if (entries != null)
					foreach (var entry in entries)
					{
						if (entry.Icon == null || drawn >= MaxIcons) continue;
						var icon = drawn < row.Icons.Count ? row.Icons[drawn] : MakeIcon(row);
						icon.SetActive(true);
						icon.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, -(LineHeight - IconSize) / 2f);
						icon.GetComponent<Image>().sprite = entry.Icon;
						var count = icon.transform.Find("Count").GetComponent<Text>();
						count.text = entry.Count > 1 ? entry.Count.ToString() : string.Empty;
						x += IconSize + IconGap;
						drawn++;
					}
				for (int k = drawn; k < row.Icons.Count; k++) row.Icons[k].SetActive(false);
				if (drawn > 0) right = x - IconGap;

				row.Rect.sizeDelta = new Vector2(right, LineHeight);
				width = Mathf.Max(width, right);
			}

			_rect.sizeDelta = new Vector2(width, lines.Count * LineHeight);
		}

		// A pack's entries, parsed once per distinct item list.  Nothing is cached before the
		// game's object database is up, since pieces would resolve as plain names until then.
		private static List<ConsoleCommands.PackEntry> EntriesFor(string itemsString)
		{
			if (_entryCache.TryGetValue(itemsString, out var cached)) return cached;
			if (ObjectDB.instance == null || !ZNetScene.instance) return null;
			var entries = ConsoleCommands.ParsePack(itemsString, out _);
			_entryCache[itemsString] = entries;
			return entries;
		}

		private static Row MakeRow()
		{
			var row = new Row();
			row.Root = new GameObject("GrabMaterials_PackHud_Row", typeof(RectTransform));
			row.Root.transform.SetParent(_root.transform, false);
			row.Rect = row.Root.GetComponent<RectTransform>();
			row.Rect.anchorMin = new Vector2(0f, 1f);
			row.Rect.anchorMax = new Vector2(0f, 1f);
			row.Rect.pivot = new Vector2(0f, 1f);
			row.Rect.sizeDelta = new Vector2(0f, LineHeight);
			row.Keys = MakeText(row.Root, "Keys", TextAnchor.MiddleRight, GUIManager.Instance.ValheimOrange);
			row.Name = MakeText(row.Root, "Name", TextAnchor.MiddleLeft, NameColor);
			_rows.Add(row);
			return row;
		}

		private static GameObject MakeIcon(Row row)
		{
			var go = new GameObject("GrabMaterials_PackHud_Icon", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(row.Root.transform, false);
			var rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			rt.sizeDelta = new Vector2(IconSize, IconSize);
			var image = go.GetComponent<Image>();
			image.preserveAspect = true;
			image.raycastTarget = false;

			// The count sits in the icon's bottom-right corner, the way stack sizes do in the inventory.
			var countObj = new GameObject("Count", typeof(RectTransform), typeof(Text), typeof(Outline));
			countObj.transform.SetParent(go.transform, false);
			var countRect = countObj.GetComponent<RectTransform>();
			countRect.anchorMin = Vector2.zero;
			countRect.anchorMax = Vector2.one;
			countRect.offsetMin = new Vector2(0f, -3f);
			countRect.offsetMax = new Vector2(4f, 0f);
			var count = countObj.GetComponent<Text>();
			count.font = GUIManager.Instance.AveriaSerifBold;
			count.fontSize = 12;
			count.color = Color.white;
			count.alignment = TextAnchor.LowerRight;
			count.horizontalOverflow = HorizontalWrapMode.Overflow;
			count.verticalOverflow = VerticalWrapMode.Overflow;
			count.raycastTarget = false;
			var outline = countObj.GetComponent<Outline>();
			outline.effectColor = Color.black;
			outline.effectDistance = new Vector2(1f, -1f);

			row.Icons.Add(go);
			return go;
		}

		private static void ApplyAnchor()
		{
			Vector2 anchor;
			Vector2 pos;
			switch (Corner)
			{
				case Anchor.TopLeft:     anchor = new Vector2(0f, 1f); pos = new Vector2( OffsetX, -OffsetY); break;
				case Anchor.TopRight:    anchor = new Vector2(1f, 1f); pos = new Vector2(-OffsetX, -OffsetY); break;
				case Anchor.BottomRight: anchor = new Vector2(1f, 0f); pos = new Vector2(-OffsetX,  OffsetY); break;
				default:                 anchor = new Vector2(0f, 0f); pos = new Vector2( OffsetX,  OffsetY); break;
			}
			_rect.anchorMin = anchor;
			_rect.anchorMax = anchor;
			_rect.pivot     = anchor;
			_rect.anchoredPosition = pos;
		}

		private static void EnsureCreated()
		{
			if (_root != null) return;
			if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;

			_root = new GameObject("GrabMaterials_PackHud", typeof(RectTransform));
			_root.transform.SetParent(GUIManager.CustomGUIFront.transform, false);
			_rect = _root.GetComponent<RectTransform>();
			_rect.sizeDelta = new Vector2(0f, LineHeight);
			_rows.Clear();
			ApplyAnchor();
		}

		// One cell of a row: a single line of text, centered on the row's height, so the font's
		// own line height never decides where anything sits.
		private static Text MakeText(GameObject row, string name, TextAnchor align, Color color)
		{
			var go = GUIManager.Instance.CreateText(
				text: string.Empty,
				parent: row.transform,
				anchorMin: new Vector2(0f, 1f),
				anchorMax: new Vector2(0f, 1f),
				position: Vector2.zero,
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: FontSize,
				color: color,
				outline: true,
				outlineColor: Color.black,
				width: 0f,
				height: LineHeight,
				addContentSizeFitter: false);
			go.name = "GrabMaterials_PackHud_" + name;
			var rt = go.GetComponent<RectTransform>();
			rt.pivot = new Vector2(0f, 1f);
			rt.anchoredPosition = Vector2.zero;
			var text = go.GetComponent<Text>();
			text.alignment = align;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow   = VerticalWrapMode.Overflow;
			text.raycastTarget = false;
			return text;
		}
	}
}
