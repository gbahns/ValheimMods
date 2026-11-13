using System.Collections.Generic;
using System.Text;
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
		private const float KeysWidth  = 120f;
		private const float Gap        = 10f;
		private const float NamesWidth = 280f;
		private const int   FontSize   = 16;
		private const float LineHeight = 21f;
		private const float IconSize   = 18f;
		private const float IconGap    = 3f;
		private const float IconIndent = 8f;   // between the end of the name and the first icon
		private const int   MaxIcons   = 8;    // per line; a pack longer than that shows its first eight

		private static GameObject    _root;
		private static RectTransform _rect;
		private static Text          _keys;
		private static Text          _names;
		private static RectTransform _icons;
		private static readonly List<GameObject> _iconPool = new List<GameObject>();
		private static readonly Dictionary<string, List<ConsoleCommands.PackEntry>> _entryCache = new Dictionary<string, List<ConsoleCommands.PackEntry>>();
		private static float         _lastRefresh;
		private static int           _lineCount;

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

		private static void Rebuild()
		{
			if (Mod?.GrabPacks == null) return;

			var keys  = new StringBuilder();
			var names = new StringBuilder();
			var lines = 0;
			// Per line: the name as drawn (to measure where its icons start) and the pack's
			// entries, null on lines that are not a pack.
			var lineNames = new List<string>();
			var lineEntries = new List<List<ConsoleCommands.PackEntry>>();

			var toggle = Mod.PackHudToggleKey != null
				? GrabMaterialsMod.GrabMaterialsMod.FormatShortcut(Mod.PackHudToggleKey.Value)
				: "";
			keys.AppendLine(toggle);
			names.AppendLine("Grab Packs");
			lineNames.Add("Grab Packs");
			lineEntries.Add(null);
			lines++;

			foreach (var pack in Mod.GrabPacks)
			{
				if (string.IsNullOrWhiteSpace(pack.Items.Value)) continue;
				if (HideUndiscovered && !ConsoleCommands.AreAllPackMaterialsKnown(pack.Items.Value, Player.m_localPlayer)) continue;
				keys.AppendLine(GrabMaterialsMod.GrabMaterialsMod.FormatShortcut(pack.Key.Value));
				names.AppendLine(pack.Name.Value);
				lineNames.Add(pack.Name.Value);
				lineEntries.Add(ShowContents ? EntriesFor(pack.Items.Value) : null);
				lines++;
			}

			if (ShowBuildPieceKey)
			{
				keys.AppendLine(Mod.GrabSelectedPieceKey.ToString());
				names.AppendLine("Build piece under cursor");
				lineNames.Add("Build piece under cursor");
				lineEntries.Add(null);
				lines++;
			}

			_keys.text  = keys.ToString().TrimEnd();
			_names.text = names.ToString().TrimEnd();
			LayoutIcons(lineNames, lineEntries);

			if (lines != _lineCount)
			{
				_lineCount = lines;
				var h = lines * LineHeight;
				_rect.sizeDelta = new Vector2(KeysWidth + Gap + NamesWidth, h);
				_keys.rectTransform.sizeDelta  = new Vector2(KeysWidth, h);
				_names.rectTransform.sizeDelta = new Vector2(NamesWidth, h);
			}
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

		// Place one icon per entry after each line's name, reusing the pooled icon objects.
		private static void LayoutIcons(List<string> lineNames, List<List<ConsoleCommands.PackEntry>> lineEntries)
		{
			var used = 0;
			var settings = _names.GetGenerationSettings(Vector2.zero);
			var generator = _names.cachedTextGeneratorForLayout;
			for (int line = 0; line < lineEntries.Count; line++)
			{
				var entries = lineEntries[line];
				if (entries == null || entries.Count == 0) continue;
				var x = generator.GetPreferredWidth(lineNames[line], settings) / _names.pixelsPerUnit + IconIndent;
				var y = -(line * LineHeight + (LineHeight - IconSize) / 2f);
				var drawn = 0;
				foreach (var entry in entries)
				{
					if (entry.Icon == null || drawn >= MaxIcons) continue;
					var icon = used < _iconPool.Count ? _iconPool[used] : MakeIcon();
					used++;
					icon.SetActive(true);
					icon.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y);
					icon.GetComponent<Image>().sprite = entry.Icon;
					var count = icon.transform.Find("Count").GetComponent<Text>();
					count.text = entry.Count > 1 ? entry.Count.ToString() : string.Empty;
					x += IconSize + IconGap;
					drawn++;
				}
			}
			for (int i = used; i < _iconPool.Count; i++) _iconPool[i].SetActive(false);
		}

		private static GameObject MakeIcon()
		{
			var go = new GameObject("GrabMaterials_PackHud_Icon", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(_icons, false);
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

			_iconPool.Add(go);
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
			_rect.sizeDelta = new Vector2(KeysWidth + Gap + NamesWidth, LineHeight);

			_keys  = MakeColumn("Keys",  0f,               KeysWidth,  TextAnchor.UpperRight, GUIManager.Instance.ValheimOrange);
			_names = MakeColumn("Names", KeysWidth + Gap,  NamesWidth, TextAnchor.UpperLeft,  new Color(0.95f, 0.90f, 0.78f));

			// Icons share the names column's origin, so an icon at x sits x pixels past the names' left edge.
			var iconsObj = new GameObject("GrabMaterials_PackHud_Icons", typeof(RectTransform));
			iconsObj.transform.SetParent(_root.transform, false);
			_icons = iconsObj.GetComponent<RectTransform>();
			_icons.anchorMin = new Vector2(0f, 1f);
			_icons.anchorMax = new Vector2(0f, 1f);
			_icons.pivot = new Vector2(0f, 1f);
			_icons.anchoredPosition = new Vector2(KeysWidth + Gap, 0f);
			_icons.sizeDelta = Vector2.zero;
			_iconPool.Clear();
			ApplyAnchor();
		}

		private static Text MakeColumn(string name, float x, float width, TextAnchor align, Color color)
		{
			var go = GUIManager.Instance.CreateText(
				text: string.Empty,
				parent: _root.transform,
				anchorMin: new Vector2(0f, 1f),
				anchorMax: new Vector2(0f, 1f),
				position: new Vector2(x, 0f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: FontSize,
				color: color,
				outline: true,
				outlineColor: Color.black,
				width: width,
				height: LineHeight,
				addContentSizeFitter: false);
			go.name = "GrabMaterials_PackHud_" + name;
			var rt = go.GetComponent<RectTransform>();
			rt.pivot = new Vector2(0f, 1f);
			rt.anchoredPosition = new Vector2(x, 0f);
			var text = go.GetComponent<Text>();
			text.alignment = align;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow   = VerticalWrapMode.Overflow;
			text.lineSpacing = 1f;
			text.raycastTarget = false;
			return text;
		}
	}
}
