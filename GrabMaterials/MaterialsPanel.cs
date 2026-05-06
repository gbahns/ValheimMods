using System.Collections.Generic;
using System.Text;
using Jotunn.Managers;
using UnityEngine;
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

		private const float DefaultPanelWidth = 480f;
		private const float MinPanelWidth = 280f;
		private const float MaxPanelWidth = 1200f;    // allow wide layouts when multi-column kicks in
		private const float PanelSidePadding = 20f;   // panel-to-content margin per side (so contentWidth = panel - 2×this)
		private const float TitleHeight = 50f;
		private const float LineHeight = 26f;
		private const float HeaderUnderlineHeight = 2f;
		private const float CategorySpacing = 8f;     // gap between groups within a column
		private const float ColumnSpacing = 20f;      // gap between columns when multi-column
		private const float BottomPadding = 20f;
		private const float TopMargin = 20f;
		private const float ArmDelaySeconds = 0.3f;
		private const float MaxSingleColumnHeight = 600f;  // List: above this, fan out to multi-column
		private const int MaxColumns = 3;

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

			var numColumns = ComputeColumnCount(totalHeight);
			var distribution = DistributeIntoColumns(groupHeights, numColumns);

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
			var dataRowWidth = ListRowPadding * 2f + countCol + ColumnGap + maxNameWidth;
			var headerRowWidth = ListRowPadding * 2f + maxCategoryWidth;
			var perColumnWidth = Mathf.Max(dataRowWidth, headerRowWidth);

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
					BuildHeaderRow(group.CategoryName, showUnderlines, ListRowPadding);
					colHeight += LineHeight + (showUnderlines ? HeaderUnderlineHeight : 0f);
					foreach (var item in group.Items)
					{
						BuildDataRow(item.Count.ToString(), item.Name, ListRowPadding, countCol);
						colHeight += LineHeight;
					}
				}

				// Tell the layout group how tall the column is.
				var colLE = col.GetComponent<LayoutElement>();
				colLE.preferredHeight = colHeight;
				maxColHeight = Mathf.Max(maxColHeight, colHeight);
			}

			FinalizeRowsLayout(panelWidth, maxColHeight);
		}

		private static int ComputeColumnCount(float totalHeight)
		{
			if (totalHeight <= MaxSingleColumnHeight) return 1;
			var needed = Mathf.CeilToInt(totalHeight / MaxSingleColumnHeight);
			return Mathf.Min(needed, MaxColumns);
		}

		// Greedy distribution: walk groups in order, advance to next column when the
		// running height of the current column would exceed the target. Keeps each
		// category whole; later columns may be slightly shorter than earlier ones.
		private static List<List<int>> DistributeIntoColumns(List<float> groupHeights, int numColumns)
		{
			var columns = new List<List<int>>(numColumns);
			for (int i = 0; i < numColumns; i++) columns.Add(new List<int>());

			if (numColumns <= 1)
			{
				for (int i = 0; i < groupHeights.Count; i++) columns[0].Add(i);
				return columns;
			}

			float total = 0f;
			foreach (var h in groupHeights) total += h;
			var target = total / numColumns;

			int currentCol = 0;
			float currentHeight = 0f;
			for (int i = 0; i < groupHeights.Count; i++)
			{
				if (currentHeight > 0f && currentHeight + groupHeights[i] > target && currentCol < numColumns - 1)
				{
					currentCol++;
					currentHeight = 0f;
				}
				columns[currentCol].Add(i);
				currentHeight += groupHeights[i];
				if (i < groupHeights.Count - 1) currentHeight += CategorySpacing;
			}

			return columns;
		}

		private static GameObject CreateColumn(float width)
		{
			var col = new GameObject("Column");
			col.transform.SetParent(_rowsContainer.transform, false);
			col.AddComponent<RectTransform>();
			var vlg = col.AddComponent<VerticalLayoutGroup>();
			vlg.childControlWidth = true;
			vlg.childControlHeight = true;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			vlg.spacing = 0f;
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
					BuildFlatDataRow(group.CategoryName, item.Name, item.Count.ToString());
					totalHeight += LineHeight;
				}
			}

			col.GetComponent<LayoutElement>().preferredHeight = totalHeight;
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
			var contentWidth = TableRowPadding * 2f + categoryCol + ColumnGap + maxNameWidth + ColumnGap + countCol;
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
		}

		public static void Tick()
		{
			if (_panel == null || !_panel.activeSelf) return;

			var now = Time.time;

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
		private static void BuildHeaderRow(string categoryName, bool drawUnderline, float sidePadding)
		{
			var hdr = MakeRow("CategoryHeader", sidePadding);
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

		// List style: count + name. countWidth is dynamic so the column hugs the
		// widest count string (eliminates the "indent" effect from a fixed-width column).
		private static void BuildDataRow(string countText, string nameText, float sidePadding, float countWidth)
		{
			var row = MakeRow("DataRow", sidePadding);
			MakeCellText(row, countText, TextAnchor.MiddleRight, Color.white, countWidth, 0f);
			MakeCellText(row, nameText, TextAnchor.MiddleLeft, Color.white, 0f, 1f);
		}

		// Table style: header row "Category | Item | Count" + a thin underline strip.
		private static void BuildColumnHeadersRow()
		{
			var row = MakeRow("ColumnHeaders", TableRowPadding);
			MakeCellText(row, "Category", TextAnchor.MiddleLeft, CategoryColor, CategoryColumnWidth, 0f);
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

		// Table style: a single data row with three columns.
		private static void BuildFlatDataRow(string category, string itemName, string countText)
		{
			var row = MakeRow("FlatDataRow", TableRowPadding);
			MakeCellText(row, category, TextAnchor.MiddleLeft, Color.white, CategoryColumnWidth, 0f);
			MakeCellText(row, itemName, TextAnchor.MiddleLeft, Color.white, 0f, 1f);
			MakeCellText(row, countText, TextAnchor.MiddleRight, Color.white, CountColumnWidth, 0f);
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
			hlg.childControlHeight = true;       // respect column LayoutElement.preferredHeight
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = false;
			hlg.spacing = ColumnSpacing;
			hlg.childAlignment = TextAnchor.UpperLeft;
			_rowsContainer.SetActive(false);

			// Click anywhere on panel dismisses (when the cursor is free, e.g. via Tab/Esc).
			var panelBtn = _panel.AddComponent<Button>();
			panelBtn.transition = Selectable.Transition.None;
			panelBtn.onClick.AddListener(Hide);
		}
	}
}
