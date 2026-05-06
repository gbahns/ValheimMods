using System.Collections.Generic;
using System.Text;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GrabMaterials
{
	public static class MaterialsPanel
	{
		public struct ItemStatus
		{
			public string Name;
			public int Needed;     // original request
			public int Available;  // amount actually grabbed (success) or amount in containers (shortage)
			public int Had;        // amount already in player inventory at request time (only > 0 with GrabDelta)
		}

		public struct InventoryItem
		{
			public string Name;
			public int Count;
			public Sprite Icon;
			public string SharedName; // m_shared.m_name — used to match the item back to chest contents on click
		}

		public struct InventoryGroup
		{
			public string CategoryName;
			public List<InventoryItem> Items;
		}

		public enum InventoryStyle
		{
			List,    // grouped by category (category as section header, optional underline). Counts right-aligned.
			Table,   // flat 3-column table with "Category | Item | Count" header row.
		}

		public enum InventoryShape
		{
			MaxHeight,    // single column, never splits
			VeryTall,
			Tall,
			SlightlyTall,
			Square,
			SlightlyWide,
			Wide,
			VeryWide,
			MaxWidth,     // fan out as many columns as fit on the screen
		}

		private const float DefaultPanelWidth = 480f;
		private const float MinPanelWidth = 280f;

		// Dynamic max — at high aspect-ratio targets and large inventories the panel
		// can grow up to (almost) the full screen width; that lets the column-search
		// algorithm pick more columns instead of being capped by a hardcoded 1200px.
		private static float MaxPanelWidth => Mathf.Max(800f, Screen.width - 40f);
		// Leave room for the Valheim HUD (hotbar/health/stamina) at the bottom and
		// a small top margin. Used to keep a long inventory from running off-screen
		// even when the user picks a tall shape like MaxHeight.
		private static float MaxPanelHeight => Mathf.Max(400f, Screen.height - 100f);
		private const float PanelSidePadding = 20f;   // panel-to-content margin per side (so contentWidth = panel - 2×this)
		private const float TitleHeight = 50f;
		private const float LineHeight = 26f;
		private const float HeaderUnderlineHeight = 2f;
		private const float CategorySpacing = 8f;     // gap between groups within a column
		private const float ColumnSpacing = 20f;      // gap between columns when multi-column
		private const float BottomPadding = 20f;
		private const float TopMargin = 20f;
		private const float ArmDelaySeconds = 0.3f;
		private const int MaxColumns = 20;

		// Table column widths (data rows). Count column is fixed; name fills the rest.
		private const float CountColumnWidth = 60f;
		private const float CategoryColumnWidth = 130f;  // Table style only
		private const float ColumnGap = 8f;
		private const float ListRowPadding = 0f;      // List style: zero indent
		private const float TableRowPadding = 14f;    // Table style: standard indent

		private const int FontSize = 18;

		private static readonly Color CategoryColor = new Color(1f, 0.847f, 0.42f);          // #ffd86b
		private static readonly Color UnderlineColor = new Color(1f, 0.847f, 0.42f, 0.55f);

		private static float IdleTimeoutSeconds => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelIdleTimeout?.Value ?? 15f;
		private static float FadeDurationSeconds => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelFadeDuration?.Value ?? 3f;
		private static bool DismissOnMovement => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelDismissOnMovement?.Value ?? true;
		private static bool ShowCategoryUnderlines => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelCategoryUnderlines?.Value ?? true;
		private static bool ShowItemIcons => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelShowItemIcons?.Value ?? true;
		private static float IconSize => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelIconSize?.Value ?? 24f;
		private static float TargetAspectRatio => AspectRatioFor(GrabMaterialsMod.GrabMaterialsMod.Instance?.InventoryShape?.Value ?? InventoryShape.SlightlyWide);

		private static float AspectRatioFor(InventoryShape s)
		{
			// Bias every preset slightly wider than its name suggests — in practice
			// the title bar and category headers add visual height, so a literal
			// 1.0 ratio panel reads as taller-than-square. These numbers are what
			// "feels right" rather than what the math says.
			switch (s)
			{
				case InventoryShape.MaxHeight:    return 0.1f;   // extreme: never splits
				case InventoryShape.VeryTall:     return 0.6f;
				case InventoryShape.Tall:         return 0.85f;
				case InventoryShape.SlightlyTall: return 1.05f;
				case InventoryShape.Square:       return 1.25f;
				case InventoryShape.SlightlyWide: return 1.55f;
				case InventoryShape.Wide:         return 2.0f;
				case InventoryShape.VeryWide:     return 3.0f;
				case InventoryShape.MaxWidth:     return 10.0f;  // extreme: as wide as fits
				default:                          return 1.55f;
			}
		}

		private static GameObject _panel;
		private static RectTransform _panelRect;
		private static Text _titleText;
		private static Text _contentText;
		private static RectTransform _contentRect;
		private static GameObject _rowsContainer;          // outer (HorizontalLayoutGroup); holds 1+ column children
		private static RectTransform _rowsContainerRect;
		private static Transform _currentBuildParent;       // column GameObject the next BuildXxx call writes into
		private static CanvasGroup _canvasGroup;
		private static float _shownAt;
		private static float _fadeStart = -1f;
		private static bool _dismissArmed;
		// Hover tracking — while the pointer is over the panel (a row or the
		// wood background), Tick() keeps the panel alive instead of fading it.
		internal static int _rowsHovered;
		internal static bool _panelHovered;
		private static bool IsPanelHovered => _rowsHovered > 0 || _panelHovered;

		public static void Show(string title, List<ItemStatus> items)
		{
			var lines = new List<string>(items.Count);
			foreach (var item in items) lines.Add(FormatItemStatus(item));
			ShowLines(title, lines);
		}

		public static void ShowInventory(string title, List<InventoryItem> items)
		{
			var maxWidth = MaxCountWidth(items);
			var lines = new List<string>(items.Count);
			foreach (var item in items)
			{
				lines.Add($"{RightAlignCount(item.Count, maxWidth)} {item.Name}");
			}
			ShowLines(title, lines);
		}

		public static void ShowCategorizedInventory(string title, List<InventoryGroup> groups, InventoryStyle style = InventoryStyle.List)
		{
			switch (style)
			{
				case InventoryStyle.Table: ShowTable(title, groups); break;
				default:                   ShowList(title, groups); break;
			}
		}

		private static void ShowList(string title, List<InventoryGroup> groups)
		{
			EnsureCreated();
			if (_panel == null) return;

			var countCol = ComputeListCountColumnWidth(groups);
			var showUnderlines = ShowCategoryUnderlines;

			// Per-group height (header + optional underline + items).
			var groupHeights = new List<float>(groups.Count);
			float totalHeight = 0f;
			foreach (var group in groups)
			{
				var h = LineHeight + (showUnderlines ? HeaderUnderlineHeight : 0f) + group.Items.Count * LineHeight;
				groupHeights.Add(h);
				totalHeight += h;
			}
			// Inter-group spacing (n-1 spacers when laid out single-column).
			if (groups.Count > 1) totalHeight += (groups.Count - 1) * CategorySpacing;

			// Per-column width = whatever fits the contents (rows + headers).
			var maxNameWidth = 0f;
			var maxCategoryWidth = 0f;
			foreach (var group in groups)
			{
				maxCategoryWidth = Mathf.Max(maxCategoryWidth, EstimateTextWidth(group.CategoryName));
				foreach (var item in group.Items)
				{
					maxNameWidth = Mathf.Max(maxNameWidth, EstimateTextWidth(item.Name));
				}
			}
			var iconCol = ShowItemIcons ? IconSize + ColumnGap : 0f;
			var dataRowWidth = ListRowPadding * 2f + countCol + ColumnGap + iconCol + maxNameWidth;
			var headerRowWidth = ListRowPadding * 2f + maxCategoryWidth;
			var perColumnWidth = Mathf.Max(dataRowWidth, headerRowWidth);

			// Pick column count from the configured aspect-ratio target.
			var numColumns = ComputeColumnCount(perColumnWidth, totalHeight);
			var distribution = DistributeIntoColumns(groupHeights, numColumns);

			var totalContentWidth = numColumns * perColumnWidth + (numColumns - 1) * ColumnSpacing;
			var panelWidth = Mathf.Clamp(totalContentWidth + PanelSidePadding * 2f, MinPanelWidth, MaxPanelWidth);

			SwitchToRowsMode(title, panelWidth);

			// Build each column. Track the tallest column for panel sizing.
			float maxColHeight = 0f;
			for (int c = 0; c < numColumns; c++)
			{
				var col = CreateColumn(perColumnWidth);
				_currentBuildParent = col.transform;

				var colIndices = distribution[c];
				float colHeight = 0f;
				for (int i = 0; i < colIndices.Count; i++)
				{
					if (i > 0)
					{
						AddSpacer(CategorySpacing);
						colHeight += CategorySpacing;
					}
					var groupIdx = colIndices[i];
					var group = groups[groupIdx];
					BuildHeaderRow(group.CategoryName, group.Items, showUnderlines, ListRowPadding);
					colHeight += LineHeight + (showUnderlines ? HeaderUnderlineHeight : 0f);
					foreach (var item in group.Items)
					{
						BuildDataRow(item.Count.ToString(), item.Icon, item.Name, item.SharedName, ListRowPadding, countCol);
						colHeight += LineHeight;
					}
				}

				// ContentSizeFitter on the column handles the height; we just need
				// maxColHeight for sizing the outer panel.
				maxColHeight = Mathf.Max(maxColHeight, colHeight);
			}

			FinalizeRowsLayout(panelWidth, maxColHeight);
		}

		// Pick the column count whose resulting panel aspect ratio (W/H) comes
		// closest to the user-configured target. Skips column counts that would
		// produce a panel too tall to fit on screen, so MaxHeight (and any tall
		// shape) splits automatically when content overflows the screen.
		private static int ComputeColumnCount(float perColumnContentWidth, float totalContentHeight)
		{
			var target = TargetAspectRatio;
			var maxHeight = MaxPanelHeight;
			var bestN = -1;
			var bestDiff = float.MaxValue;
			for (int n = 1; n <= MaxColumns; n++)
			{
				var contentWidth = n * perColumnContentWidth + (n - 1) * ColumnSpacing;
				var panelWidth = Mathf.Clamp(contentWidth + PanelSidePadding * 2f, MinPanelWidth, MaxPanelWidth);
				var perColumnHeight = totalContentHeight / n;
				var panelHeight = TitleHeight + perColumnHeight + BottomPadding;
				if (panelHeight > maxHeight) continue;  // doesn't fit; try more columns
				var aspect = panelWidth / Mathf.Max(panelHeight, 1f);
				var diff = Mathf.Abs(aspect - target);
				if (diff < bestDiff)
				{
					bestDiff = diff;
					bestN = n;
				}
			}
			// Nothing fit — content is enormous. Use the most columns we can
			// (gives the shortest panel) so as much content as possible is visible.
			if (bestN < 0) bestN = MaxColumns;
			return bestN;
		}

		// Balanced partition: binary-search for the smallest "max column height"
		// such that a greedy left-to-right pack fits all groups in ≤ numColumns.
		// Newspaper-style — no column gets stuck with the leftovers.
		private static List<List<int>> DistributeIntoColumns(List<float> groupHeights, int numColumns)
		{
			if (numColumns <= 1)
			{
				var single = new List<int>(groupHeights.Count);
				for (int i = 0; i < groupHeights.Count; i++) single.Add(i);
				return new List<List<int>> { single };
			}

			float low = 0f;
			float high = 0f;
			foreach (var h in groupHeights)
			{
				if (h > low) low = h;       // a single group is the floor
				high += h + CategorySpacing; // worst case: everything in one column
			}

			for (int iter = 0; iter < 40 && high - low > 0.5f; iter++)
			{
				var mid = (low + high) * 0.5f;
				if (FitsInColumns(groupHeights, numColumns, mid)) high = mid;
				else low = mid;
			}

			return PackGreedy(groupHeights, numColumns, high);
		}

		private static bool FitsInColumns(List<float> heights, int numColumns, float maxH)
		{
			int colsUsed = 1;
			float currentHeight = 0f;
			foreach (var h in heights)
			{
				var extra = (currentHeight > 0f ? CategorySpacing : 0f) + h;
				if (currentHeight + extra > maxH && currentHeight > 0f)
				{
					colsUsed++;
					if (colsUsed > numColumns) return false;
					currentHeight = h;  // first group in the new column — no leading spacer
				}
				else
				{
					currentHeight += extra;
				}
			}
			return true;
		}

		private static List<List<int>> PackGreedy(List<float> heights, int numColumns, float maxH)
		{
			var columns = new List<List<int>>();
			columns.Add(new List<int>());
			float currentHeight = 0f;
			for (int i = 0; i < heights.Count; i++)
			{
				var extra = (currentHeight > 0f ? CategorySpacing : 0f) + heights[i];
				if (currentHeight + extra > maxH && currentHeight > 0f)
				{
					columns.Add(new List<int>());
					currentHeight = heights[i];
				}
				else
				{
					currentHeight += extra;
				}
				columns[columns.Count - 1].Add(i);
			}
			while (columns.Count < numColumns) columns.Add(new List<int>());
			return columns;
		}

		private static GameObject CreateColumn(float width)
		{
			var col = new GameObject("Column");
			col.transform.SetParent(_rowsContainer.transform, false);
			var rect = col.AddComponent<RectTransform>();
			// Top-left anchor + pivot so HLG's "UpperLeft" alignment lines up
			// the column's top edge with the rows container's top edge. With
			// the default (0.5, 0.5) pivot, half of each column ends up above
			// the layout container's top edge.
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			var vlg = col.AddComponent<VerticalLayoutGroup>();
			vlg.childControlWidth = true;
			vlg.childControlHeight = true;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.spacing = 0f;
			// Auto-size column height to its content. Outer HLG has
			// childControlHeight=false, so this is what determines the column's
			// final height — and no extra space is distributed inside.
			var fitter = col.AddComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			var le = col.AddComponent<LayoutElement>();
			le.preferredWidth = width;
			le.flexibleWidth = 0f;
			return col;
		}

		private static float ComputeListCountColumnWidth(List<InventoryGroup> groups)
		{
			float maxW = EstimateTextWidth("9"); // floor: at least one digit
			foreach (var group in groups)
			{
				foreach (var item in group.Items)
				{
					maxW = Mathf.Max(maxW, EstimateTextWidth(item.Count.ToString()));
				}
			}
			return maxW + 2f; // tiny breathing-room buffer
		}

		private static void ShowTable(string title, List<InventoryGroup> groups)
		{
			EnsureCreated();
			if (_panel == null) return;

			var panelWidth = ComputeTablePanelWidth(groups);
			SwitchToRowsMode(title, panelWidth);

			// Table is always one column wrapping the rows.
			var perColumnWidth = panelWidth - PanelSidePadding * 2f;
			var col = CreateColumn(perColumnWidth);
			_currentBuildParent = col.transform;

			BuildColumnHeadersRow();
			float totalHeight = LineHeight + HeaderUnderlineHeight;

			foreach (var group in groups)
			{
				foreach (var item in group.Items)
				{
					BuildFlatDataRow(group.CategoryName, item.Icon, item.Name, item.Count.ToString(), item.SharedName);
					totalHeight += LineHeight;
				}
			}

			FinalizeRowsLayout(panelWidth, totalHeight);
		}

		private static void SwitchToRowsMode(string title, float panelWidth)
		{
			if (_contentText != null) _contentText.gameObject.SetActive(false);
			_rowsContainer.SetActive(true);
			_titleText.text = title;
			// Apply width up-front so layout-group children can size correctly during build.
			ApplyPanelWidth(panelWidth);
			ClearRows();
		}

		private static void FinalizeRowsLayout(float panelWidth, float totalHeight)
		{
			var contentWidth = panelWidth - PanelSidePadding * 2f;
			_rowsContainerRect.sizeDelta = new Vector2(contentWidth, totalHeight);
			var panelHeight = TitleHeight + totalHeight + BottomPadding;
			_panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

			_panel.SetActive(true);
			_canvasGroup.alpha = 1f;
			_shownAt = Time.time;
			_fadeStart = -1f;
			_dismissArmed = false;
		}

		// =========================================================================
		// Panel width sizing
		// =========================================================================

		private static void ApplyPanelWidth(float panelWidth)
		{
			var contentWidth = panelWidth - PanelSidePadding * 2f;
			_panelRect.sizeDelta = new Vector2(panelWidth, _panelRect.sizeDelta.y);
			var titleRect = _titleText.GetComponent<RectTransform>();
			titleRect.sizeDelta = new Vector2(panelWidth - 60f, 30f);
			_contentRect.sizeDelta = new Vector2(contentWidth, _contentRect.sizeDelta.y);
			_rowsContainerRect.sizeDelta = new Vector2(contentWidth, _rowsContainerRect.sizeDelta.y);
		}

		private static float ComputeListPanelWidth(List<InventoryGroup> groups, float countCol)
		{
			float maxNameWidth = 0f;
			float maxCategoryWidth = 0f;
			foreach (var group in groups)
			{
				maxCategoryWidth = Mathf.Max(maxCategoryWidth, EstimateTextWidth(group.CategoryName));
				foreach (var item in group.Items)
				{
					maxNameWidth = Mathf.Max(maxNameWidth, EstimateTextWidth(item.Name));
				}
			}
			var dataRowWidth = ListRowPadding * 2f + countCol + ColumnGap + maxNameWidth;
			var headerWidth = ListRowPadding * 2f + maxCategoryWidth;
			var contentWidth = Mathf.Max(dataRowWidth, headerWidth);
			return Mathf.Clamp(contentWidth + PanelSidePadding * 2f, MinPanelWidth, MaxPanelWidth);
		}

		private static float ComputeTablePanelWidth(List<InventoryGroup> groups)
		{
			float maxNameWidth = EstimateTextWidth("Item"); // header label as a floor
			float maxCountWidth = EstimateTextWidth("Count");
			float maxCategoryWidth = EstimateTextWidth("Category");
			foreach (var group in groups)
			{
				maxCategoryWidth = Mathf.Max(maxCategoryWidth, EstimateTextWidth(group.CategoryName));
				foreach (var item in group.Items)
				{
					maxNameWidth = Mathf.Max(maxNameWidth, EstimateTextWidth(item.Name));
					maxCountWidth = Mathf.Max(maxCountWidth, EstimateTextWidth(item.Count.ToString()));
				}
			}
			var categoryCol = Mathf.Max(CategoryColumnWidth, maxCategoryWidth);
			var countCol = Mathf.Max(CountColumnWidth, maxCountWidth);
			var iconCol = ShowItemIcons ? IconSize + ColumnGap : 0f;
			var contentWidth = TableRowPadding * 2f + categoryCol + ColumnGap + iconCol + maxNameWidth + ColumnGap + countCol;
			return Mathf.Clamp(contentWidth + PanelSidePadding * 2f, MinPanelWidth, MaxPanelWidth);
		}

		// Rough proportional-font width estimate. Good enough for panel-sizing
		// decisions; real text rendering may differ by a few px per glyph.
		private static float EstimateTextWidth(string s)
		{
			if (string.IsNullOrEmpty(s)) return 0f;
			return s.Length * FontSize * 0.55f;
		}

		public static void Hide()
		{
			if (_panel != null) _panel.SetActive(false);
			if (_canvasGroup != null) _canvasGroup.alpha = 1f;
			_fadeStart = -1f;
			_dismissArmed = false;
			_rowsHovered = 0;
			_panelHovered = false;
		}

		// Reset the fade/dismiss timers as if the panel had just been opened.
		// Used when the user actively interacts with the panel (e.g. clicks a
		// row to highlight containers) so it doesn't immediately start fading
		// from the click being treated as gameplay input.
		public static void KeepAlive()
		{
			if (_panel == null || !_panel.activeSelf) return;
			_shownAt = Time.time;
			_fadeStart = -1f;
			_dismissArmed = false;
			if (_canvasGroup != null) _canvasGroup.alpha = 1f;
		}

		public static void Tick()
		{
			if (_panel == null || !_panel.activeSelf) return;

			var now = Time.time;

			// Active engagement: pointer is over the panel. Reset all timers
			// and ensure the panel stays fully opaque, even mid-fade.
			if (IsPanelHovered)
			{
				_shownAt = now;
				_fadeStart = -1f;
				_dismissArmed = false;
				if (_canvasGroup != null && _canvasGroup.alpha < 1f) _canvasGroup.alpha = 1f;
				return;
			}

			// Fade in progress: advance and possibly hide.
			if (_fadeStart >= 0f)
			{
				var progress = (now - _fadeStart) / FadeDurationSeconds;
				if (progress >= 1f)
				{
					Hide();
					return;
				}
				_canvasGroup.alpha = 1f - progress;
				return;
			}

			if (now - _shownAt >= IdleTimeoutSeconds)
			{
				_fadeStart = now;
				return;
			}

			if (!_dismissArmed && now - _shownAt >= ArmDelaySeconds)
			{
				_dismissArmed = true;
			}
			if (!_dismissArmed) return;

			if (DismissOnMovement && HasGameplayInput())
			{
				_fadeStart = now;
			}
		}

		// =========================================================================
		// Row-based table builders (used by ShowCategorizedInventory)
		// =========================================================================

		private static void ClearRows()
		{
			if (_rowsContainer == null) return;
			for (int i = _rowsContainer.transform.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.DestroyImmediate(_rowsContainer.transform.GetChild(i).gameObject);
			}
		}

		// List style: category label, optionally followed by a thin underline strip.
		// items is the items in this category — used to make the header
		// clickable so a click highlights every container holding any of them.
		private static void BuildHeaderRow(string categoryName, List<InventoryItem> items, bool drawUnderline, float sidePadding)
		{
			var hdr = MakeRow("CategoryHeader", sidePadding);
			MakeHeaderClickable(hdr, items);
			MakeCellText(hdr, categoryName, TextAnchor.MiddleLeft, CategoryColor, 0f, 1f);

			if (drawUnderline)
			{
				var line = new GameObject("CategoryUnderline");
				line.transform.SetParent(_currentBuildParent, false);
				line.AddComponent<RectTransform>();
				var img = line.AddComponent<Image>();
				img.color = UnderlineColor;
				img.raycastTarget = false;
				var lineLE = line.AddComponent<LayoutElement>();
				lineLE.preferredHeight = HeaderUnderlineHeight;
				lineLE.flexibleWidth = 1f;
			}
		}

		// List style: count + (icon) + name. countWidth is dynamic so the column
		// hugs the widest count string (eliminates the "indent" effect from a
		// fixed-width column). Icon column is omitted when ShowItemIcons is off.
		// sharedName drives click-to-highlight (#40); pass null to skip.
		private static void BuildDataRow(string countText, Sprite icon, string nameText, string sharedName, float sidePadding, float countWidth)
		{
			var row = MakeRow("DataRow", sidePadding);
			MakeRowClickable(row, sharedName);
			MakeCellText(row, countText, TextAnchor.MiddleRight, Color.white, countWidth, 0f);
			if (ShowItemIcons) MakeIconCell(row, icon);
			MakeCellText(row, nameText, TextAnchor.MiddleLeft, Color.white, 0f, 1f);
		}

		// Table style: header row "Category | (icon) | Item | Count" + thin underline.
		// The icon column gets an empty placeholder cell so the headers line up
		// with the data rows below. Spacer is omitted when ShowItemIcons is off.
		private static void BuildColumnHeadersRow()
		{
			var row = MakeRow("ColumnHeaders", TableRowPadding);
			MakeCellText(row, "Category", TextAnchor.MiddleLeft, CategoryColor, CategoryColumnWidth, 0f);
			if (ShowItemIcons) AddSpacerCell(row, IconSize);
			MakeCellText(row, "Item",     TextAnchor.MiddleLeft, CategoryColor, 0f, 1f);
			MakeCellText(row, "Count",    TextAnchor.MiddleRight, CategoryColor, CountColumnWidth, 0f);

			var line = new GameObject("ColumnHeadersUnderline");
			line.transform.SetParent(_currentBuildParent, false);
			line.AddComponent<RectTransform>();
			var img = line.AddComponent<Image>();
			img.color = UnderlineColor;
			img.raycastTarget = false;
			var lineLE = line.AddComponent<LayoutElement>();
			lineLE.preferredHeight = HeaderUnderlineHeight;
			lineLE.flexibleWidth = 1f;
		}

		// Table style: category + (icon) + item + count.
		private static void BuildFlatDataRow(string category, Sprite icon, string itemName, string countText, string sharedName)
		{
			var row = MakeRow("FlatDataRow", TableRowPadding);
			MakeRowClickable(row, sharedName);
			MakeCellText(row, category, TextAnchor.MiddleLeft, Color.white, CategoryColumnWidth, 0f);
			if (ShowItemIcons) MakeIconCell(row, icon);
			MakeCellText(row, itemName, TextAnchor.MiddleLeft, Color.white, 0f, 1f);
			MakeCellText(row, countText, TextAnchor.MiddleRight, Color.white, CountColumnWidth, 0f);
		}

		// Adds a transparent Image + Button to the row so clicking it triggers
		// onClick. The Image stays raycastable so clicks land on the row
		// instead of falling through to the panel-wide dismiss button. Hover
		// state tints the row faintly so users can see the row is clickable.
		private static readonly Color RowNormalColor = new Color(1f, 1f, 1f, 0f);
		private static readonly Color RowHoverColor = new Color(1f, 0.92f, 0.45f, 0.22f);
		private static readonly Color RowPressedColor = new Color(1f, 0.92f, 0.45f, 0.4f);

		private static void MakeClickable(GameObject row, UnityEngine.Events.UnityAction onClick)
		{
			if (onClick == null) return;
			var img = row.AddComponent<Image>();
			img.color = RowNormalColor;
			img.raycastTarget = true;
			img.maskable = false;
			var btn = row.AddComponent<Button>();
			btn.transition = Selectable.Transition.None;  // we drive the visuals ourselves
			btn.onClick.AddListener(onClick);
			// Selectable.ColorTint isn't applying in this Jotunn-panel context for
			// reasons unclear, so we toggle Image.color manually via pointer events.
			var hover = row.AddComponent<RowHoverTint>();
			hover.target = img;
			hover.normal = RowNormalColor;
			hover.hover = RowHoverColor;
			hover.pressed = RowPressedColor;
		}

		// Pointer event handler attached to clickable rows. Replaces the
		// Selectable-based ColorTint transition (which doesn't visually update
		// the targetGraphic in this panel context). Also bumps the hover
		// counter so MaterialsPanel.Tick() keeps the panel alive while hovered.
		internal class RowHoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
		{
			public Image target;
			public Color normal;
			public Color hover;
			public Color pressed;
			private bool _hovered;
			private bool _pressed;

			public void OnPointerEnter(PointerEventData eventData)
			{
				if (!_hovered) { _hovered = true; _rowsHovered++; }
				Apply();
			}

			public void OnPointerExit(PointerEventData eventData)
			{
				if (_hovered) { _hovered = false; _rowsHovered = Mathf.Max(0, _rowsHovered - 1); }
				_pressed = false;
				Apply();
			}

			public void OnPointerDown(PointerEventData eventData) { _pressed = true; Apply(); }
			public void OnPointerUp(PointerEventData eventData)   { _pressed = false; Apply(); }

			private void OnDisable()
			{
				// Row destroyed or panel deactivated while hovered — release the count.
				if (_hovered) { _hovered = false; _rowsHovered = Mathf.Max(0, _rowsHovered - 1); }
				_pressed = false;
			}

			private void Apply()
			{
				if (target == null) return;
				if (_pressed) target.color = pressed;
				else if (_hovered) target.color = hover;
				else target.color = normal;
			}
		}

		// Tracks pointer over the panel itself (the wood background between rows).
		internal class PanelHoverTracker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
		{
			public void OnPointerEnter(PointerEventData eventData) { _panelHovered = true; }
			public void OnPointerExit(PointerEventData eventData)  { _panelHovered = false; }
			private void OnDisable() { _panelHovered = false; }
		}

		// Click-to-highlight: a single item's containers (#40).
		private static void MakeRowClickable(GameObject row, string sharedName)
		{
			if (string.IsNullOrEmpty(sharedName)) return;
			MakeClickable(row, () =>
			{
				ConsoleCommands.HighlightContainersHolding(sharedName);
				KeepAlive();  // clicking is intentional engagement, not a "dismiss" signal
			});
		}

		// Click-to-highlight: every container holding any item in this category.
		private static void MakeHeaderClickable(GameObject row, List<InventoryItem> items)
		{
			if (items == null || items.Count == 0) return;
			var sharedNames = new HashSet<string>();
			foreach (var item in items)
			{
				if (!string.IsNullOrEmpty(item.SharedName)) sharedNames.Add(item.SharedName);
			}
			if (sharedNames.Count == 0) return;
			MakeClickable(row, () =>
			{
				ConsoleCommands.HighlightContainersHoldingAny(sharedNames);
				KeepAlive();
			});
		}

		// Common HLG row scaffolding shared by data rows + column header rows.
		private static GameObject MakeRow(string name, float sidePadding)
		{
			var row = new GameObject(name);
			row.transform.SetParent(_currentBuildParent, false);
			row.AddComponent<RectTransform>();
			var hlg = row.AddComponent<HorizontalLayoutGroup>();
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = true;
			hlg.spacing = ColumnGap;
			hlg.padding = new RectOffset((int)sidePadding, (int)sidePadding, 0, 0);
			var rowLE = row.AddComponent<LayoutElement>();
			rowLE.preferredHeight = LineHeight;
			return row;
		}

		private static Text MakeCellText(GameObject parent, string txt, TextAnchor alignment, Color color, float preferredWidth, float flexibleWidth)
		{
			var go = new GameObject("Cell");
			go.transform.SetParent(parent.transform, false);
			go.AddComponent<RectTransform>();
			var t = go.AddComponent<Text>();
			t.font = GUIManager.Instance.AveriaSerifBold;
			t.fontSize = 18;
			t.color = color;
			t.alignment = alignment;
			t.text = txt;
			t.supportRichText = false;
			t.raycastTarget = false;
			AddOutline(go);
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = preferredWidth;
			le.flexibleWidth = flexibleWidth;
			le.preferredHeight = LineHeight;
			return t;
		}

		// Square Image cell holding the item icon. Always created, even with a
		// null sprite — keeps row layouts aligned across rows where some items
		// happen to lack an icon.
		private static void MakeIconCell(GameObject parent, Sprite sprite)
		{
			var go = new GameObject("Icon");
			go.transform.SetParent(parent.transform, false);
			go.AddComponent<RectTransform>();
			var img = go.AddComponent<Image>();
			if (sprite != null)
			{
				img.sprite = sprite;
				img.preserveAspect = true;
			}
			else
			{
				img.color = new Color(0f, 0f, 0f, 0f);  // invisible placeholder
			}
			img.raycastTarget = false;
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = IconSize;
			le.preferredHeight = IconSize;
			le.flexibleWidth = 0f;
		}

		// Empty fixed-width cell — used in the Table-style header row to hold
		// space for the icon column so headers line up with data rows.
		private static void AddSpacerCell(GameObject parent, float width)
		{
			var go = new GameObject("SpacerCell");
			go.transform.SetParent(parent.transform, false);
			go.AddComponent<RectTransform>();
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = width;
			le.preferredHeight = LineHeight;
			le.flexibleWidth = 0f;
		}

		private static void AddSpacer(float height)
		{
			var spacer = new GameObject("Spacer");
			spacer.transform.SetParent(_currentBuildParent, false);
			spacer.AddComponent<RectTransform>();
			var le = spacer.AddComponent<LayoutElement>();
			le.preferredHeight = height;
			le.flexibleWidth = 1f;
		}

		private static void AddOutline(GameObject go)
		{
			var outline = go.AddComponent<Outline>();
			outline.effectColor = Color.black;
		}

		// =========================================================================
		// Text-based content (grab results + uncategorized inventory)
		// =========================================================================

		private static int MaxCountWidth(List<InventoryItem> items)
		{
			var maxW = 1;
			foreach (var item in items)
			{
				var w = item.Count.ToString().Length;
				if (w > maxW) maxW = w;
			}
			return maxW;
		}

		// Right-align a count by padding with U+2007 (figure space), which is
		// digit-width in fonts with tabular figures.
		private static string RightAlignCount(int count, int width)
		{
			var s = count.ToString();
			var pad = width - s.Length;
			return pad > 0 ? new string(' ', pad) + s : s;
		}

		private static string FormatItemStatus(ItemStatus item)
		{
			var totalCovered = item.Had + item.Available;
			if (totalCovered >= item.Needed)
			{
				if (item.Had >= item.Needed)
					return $"<color=#88ff88>✓ {item.Needed} {item.Name}</color> <color=#aaaaaa>(already had)</color>";
				if (item.Had > 0)
					return $"<color=#88ff88>✓ {item.Needed} {item.Name}</color> <color=#aaaaaa>(had {item.Had}, grabbed {item.Available})</color>";
				return $"<color=#88ff88>✓ {item.Needed} {item.Name}</color>";
			}
			var missing = item.Needed - totalCovered;
			if (item.Had > 0)
				return $"<color=#ff8888>✗ {totalCovered} of {item.Needed} {item.Name}</color> <color=#aaaaaa>(had {item.Had}, missing {missing})</color>";
			return $"<color=#ff8888>✗ {item.Available} of {item.Needed} {item.Name} (missing {missing})</color>";
		}

		private static void ShowLines(string title, List<string> lines)
		{
			EnsureCreated();
			if (_panel == null) return;

			// Switch to text-based content; hide the row-based table.
			if (_rowsContainer != null) _rowsContainer.SetActive(false);
			_contentText.gameObject.SetActive(true);

			_titleText.text = title;

			var sb = new StringBuilder();
			foreach (var line in lines) sb.AppendLine(line);
			_contentText.text = sb.ToString().TrimEnd();

			float contentHeight = LineHeight * lines.Count;
			float panelHeight = TitleHeight + contentHeight + BottomPadding;
			ApplyPanelWidth(DefaultPanelWidth);
			_panelRect.sizeDelta = new Vector2(DefaultPanelWidth, panelHeight);
			_contentRect.sizeDelta = new Vector2(_contentRect.sizeDelta.x, contentHeight);

			_panel.SetActive(true);
			_canvasGroup.alpha = 1f;
			_shownAt = Time.time;
			_fadeStart = -1f;
			_dismissArmed = false;
		}

		private static bool HasGameplayInput()
		{
			var player = Player.m_localPlayer;
			if (player != null && player.GetMoveDir().sqrMagnitude > 0.01f) return true;
			if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) return true;
			if (Input.GetKey(KeyCode.Space)) return true;
			return false;
		}

		private static void EnsureCreated()
		{
			if (_panel != null) return;
			if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;

			// Top-center anchored, pivot at top-center so resize grows downward.
			_panel = GUIManager.Instance.CreateWoodpanel(
				parent: GUIManager.CustomGUIFront.transform,
				anchorMin: new Vector2(0.5f, 1f),
				anchorMax: new Vector2(0.5f, 1f),
				position: new Vector2(0f, -TopMargin),
				width: DefaultPanelWidth,
				height: 200f,
				draggable: false);
			_panel.name = "GrabMaterials_MaterialsPanel";
			_panelRect = _panel.GetComponent<RectTransform>();
			_panelRect.pivot = new Vector2(0.5f, 1f);
			_panelRect.anchoredPosition = new Vector2(0f, -TopMargin);
			_canvasGroup = _panel.AddComponent<CanvasGroup>();

			var titleObj = GUIManager.Instance.CreateText(
				text: string.Empty,
				parent: _panel.transform,
				anchorMin: new Vector2(0.5f, 1f),
				anchorMax: new Vector2(0.5f, 1f),
				position: new Vector2(0f, -25f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 22,
				color: GUIManager.Instance.ValheimOrange,
				outline: true,
				outlineColor: Color.black,
				width: DefaultPanelWidth - 60f,
				height: 30f,
				addContentSizeFitter: false);
			_titleText = titleObj.GetComponent<Text>();
			_titleText.alignment = TextAnchor.UpperCenter;
			_titleText.raycastTarget = false;

			// Text-based content (used by grab results and the uncategorized /inventory path).
			var contentObj = GUIManager.Instance.CreateText(
				text: string.Empty,
				parent: _panel.transform,
				anchorMin: new Vector2(0.5f, 1f),
				anchorMax: new Vector2(0.5f, 1f),
				position: new Vector2(0f, -TitleHeight),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 18,
				color: Color.white,
				outline: true,
				outlineColor: Color.black,
				width: DefaultPanelWidth - 40f,
				height: 200f,
				addContentSizeFitter: false);
			_contentText = contentObj.GetComponent<Text>();
			_contentRect = _contentText.GetComponent<RectTransform>();
			_contentRect.pivot = new Vector2(0.5f, 1f);
			_contentRect.anchoredPosition = new Vector2(0f, -TitleHeight);
			_contentText.alignment = TextAnchor.UpperLeft;
			_contentText.supportRichText = true;
			_contentText.raycastTarget = false;

			// Row-based content (used by the categorized /inventory path).
			// Outer container is horizontal — each child is a column with its own VerticalLayoutGroup.
			// Single-column layouts have one child; multi-column has 2+ children side-by-side.
			_rowsContainer = new GameObject("RowsContainer");
			_rowsContainer.transform.SetParent(_panel.transform, false);
			_rowsContainerRect = _rowsContainer.AddComponent<RectTransform>();
			_rowsContainerRect.anchorMin = new Vector2(0.5f, 1f);
			_rowsContainerRect.anchorMax = new Vector2(0.5f, 1f);
			_rowsContainerRect.pivot = new Vector2(0.5f, 1f);
			_rowsContainerRect.anchoredPosition = new Vector2(0f, -TitleHeight);
			_rowsContainerRect.sizeDelta = new Vector2(DefaultPanelWidth - 40f, 200f);
			var hlg = _rowsContainer.AddComponent<HorizontalLayoutGroup>();
			hlg.childControlWidth = true;
			hlg.childControlHeight = false;     // each column ContentSizeFits its own height
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = false;
			hlg.spacing = ColumnSpacing;
			hlg.childAlignment = TextAnchor.UpperLeft;
			_rowsContainer.SetActive(false);

			// Click anywhere on panel dismisses (when the cursor is free, e.g. via Tab/Esc).
			var panelBtn = _panel.AddComponent<Button>();
			panelBtn.transition = Selectable.Transition.None;
			panelBtn.onClick.AddListener(Hide);

			// Track pointer-over-panel so Tick() can hold the panel open while
			// the user is actively browsing it.
			_panel.AddComponent<PanelHoverTracker>();
		}
	}
}
