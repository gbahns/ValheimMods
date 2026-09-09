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

		private static GameObject    _root;
		private static RectTransform _rect;
		private static Text          _keys;
		private static Text          _names;
		private static float         _lastRefresh;
		private static int           _lineCount;

		private static GrabMaterialsMod.GrabMaterialsMod Mod => GrabMaterialsMod.GrabMaterialsMod.Instance;
		private static bool   Enabled  => Mod?.ShowPackHud?.Value ?? true;
		private static Anchor Corner   => Mod?.PackHudAnchor?.Value ?? Anchor.BottomLeft;
		private static float  OffsetX  => Mod?.PackHudOffsetX?.Value ?? 10f;
		private static float  OffsetY  => Mod?.PackHudOffsetY?.Value ?? 300f;
		private static bool   ShowBuildPieceKey => Mod?.PackHudShowBuildPieceKey?.Value ?? true;
		private static bool   HideUndiscovered  => Mod?.PackHudHideUndiscovered?.Value ?? true;

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

			var toggle = Mod.PackHudToggleKey != null
				? GrabMaterialsMod.GrabMaterialsMod.FormatShortcut(Mod.PackHudToggleKey.Value)
				: "";
			keys.AppendLine(toggle);
			names.AppendLine("Grab Packs");
			lines++;

			foreach (var pack in Mod.GrabPacks)
			{
				if (string.IsNullOrWhiteSpace(pack.Items.Value)) continue;
				if (HideUndiscovered && !ConsoleCommands.AreAllPackMaterialsKnown(pack.Items.Value, Player.m_localPlayer)) continue;
				keys.AppendLine(GrabMaterialsMod.GrabMaterialsMod.FormatShortcut(pack.Key.Value));
				names.AppendLine(pack.Name.Value);
				lines++;
			}

			if (ShowBuildPieceKey)
			{
				keys.AppendLine(Mod.GrabSelectedPieceKey.ToString());
				names.AppendLine("Build piece under cursor");
				lines++;
			}

			_keys.text  = keys.ToString().TrimEnd();
			_names.text = names.ToString().TrimEnd();

			if (lines != _lineCount)
			{
				_lineCount = lines;
				var h = lines * LineHeight;
				_rect.sizeDelta = new Vector2(KeysWidth + Gap + NamesWidth, h);
				_keys.rectTransform.sizeDelta  = new Vector2(KeysWidth, h);
				_names.rectTransform.sizeDelta = new Vector2(NamesWidth, h);
			}
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
