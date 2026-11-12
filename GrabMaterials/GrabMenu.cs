using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace GrabMaterials
{
	// The right-click menu on an inventory row: grab 1, 5, 10 or 20 of that item from the
	// nearby containers, or type an amount (#46).  Amounts the containers cannot cover are
	// greyed out.  The grab runs through the same owned-container path as `/g`, the result
	// arrives as a HUD message, and the inventory panel redraws with the new counts rather
	// than closing, so you can keep pulling.
	//
	// The menu sits on Jotunn's front canvas above the panel, with an invisible full-screen
	// click sink under it: a click anywhere else closes the menu and nothing more.
	internal static class GrabMenu
	{
		private static readonly int[] Amounts = { 1, 5, 10, 20 };
		private const float Width = 220f;
		private const float RowHeight = 30f;
		private const float Pad = 12f;
		private const float Gap = 6f;
		private const float AmountFieldWidth = 70f;

		private static GameObject _catcher;
		private static GameObject _menu;
		private static InputField _amountField;
		private static MaterialsPanel.InventoryItem _item;
		private static int _grabbable;

		// The amount last typed for each item, so the field opens on what you asked for before.
		private static readonly Dictionary<string, int> _lastAmounts = new Dictionary<string, int>();

		internal static bool IsOpen => _menu != null;
		internal static bool Typing => _amountField != null && _amountField.isFocused;

		internal static void Show(MaterialsPanel.InventoryItem item, Vector2 screenPos)
		{
			Close();
			var front = GUIManager.CustomGUIFront;
			if (GUIManager.Instance == null || front == null) return;

			_item = item;
			// What sits in containers; the rest of the count is in kilns, smelters, stacks or piles.
			_grabbable = Mathf.Max(0, item.Count - item.InProcess);

			// Click sink behind the menu.
			_catcher = new GameObject("GrabMaterials_GrabMenuCatcher", typeof(RectTransform), typeof(Image), typeof(Button));
			_catcher.transform.SetParent(front.transform, false);
			var catcherRect = _catcher.GetComponent<RectTransform>();
			catcherRect.anchorMin = Vector2.zero;
			catcherRect.anchorMax = Vector2.one;
			catcherRect.offsetMin = Vector2.zero;
			catcherRect.offsetMax = Vector2.zero;
			var catcherImage = _catcher.GetComponent<Image>();
			catcherImage.color = Color.clear;
			catcherImage.raycastTarget = true;
			var catcherButton = _catcher.GetComponent<Button>();
			catcherButton.transition = Selectable.Transition.None;
			catcherButton.onClick.AddListener(Close);

			var rows = 1 + Amounts.Length + 1;   // header, the fixed amounts, the typed amount
			var height = Pad * 2f + rows * RowHeight + (rows - 1) * Gap;

			_menu = GUIManager.Instance.CreateWoodpanel(
				parent: front.transform,
				anchorMin: new Vector2(0f, 1f),
				anchorMax: new Vector2(0f, 1f),
				position: Vector2.zero,
				width: Width,
				height: height,
				draggable: false);
			_menu.name = "GrabMaterials_GrabMenu";
			PlaceAt(_menu.GetComponent<RectTransform>(), front, screenPos, height);

			var y = -Pad;
			var header = GUIManager.Instance.CreateText(
				text: $"{item.Name}  <color=#aaaaaa>({_grabbable} nearby)</color>",
				parent: _menu.transform,
				anchorMin: new Vector2(0f, 1f),
				anchorMax: new Vector2(0f, 1f),
				position: new Vector2(Pad, y),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 16,
				color: GUIManager.Instance.ValheimOrange,
				outline: true,
				outlineColor: Color.black,
				width: Width - Pad * 2f,
				height: RowHeight,
				addContentSizeFitter: false);
			var headerRect = header.GetComponent<RectTransform>();
			headerRect.pivot = new Vector2(0f, 1f);
			headerRect.anchoredPosition = new Vector2(Pad, y);
			var headerText = header.GetComponent<Text>();
			headerText.alignment = TextAnchor.MiddleLeft;
			headerText.supportRichText = true;
			headerText.horizontalOverflow = HorizontalWrapMode.Overflow;
			headerText.raycastTarget = false;
			y -= RowHeight + Gap;

			foreach (var amount in Amounts)
			{
				var n = amount;
				var enabled = n <= _grabbable;
				AddButton($"Grab {n}", Pad, y, Width - Pad * 2f, enabled, () => Grab(n, remember: false));
				y -= RowHeight + Gap;
			}

			// Typed amount: a field with the last amount for this item, Enter or the button grabs.
			var fieldGo = GUIManager.Instance.CreateInputField(
				parent: _menu.transform,
				anchorMin: new Vector2(0f, 1f),
				anchorMax: new Vector2(0f, 1f),
				position: new Vector2(Pad, y),
				contentType: InputField.ContentType.IntegerNumber,
				placeholderText: "amount",
				fontSize: 16,
				width: AmountFieldWidth,
				height: RowHeight);
			var fieldRect = fieldGo.GetComponent<RectTransform>();
			fieldRect.pivot = new Vector2(0f, 1f);
			fieldRect.anchoredPosition = new Vector2(Pad, y);
			fieldRect.sizeDelta = new Vector2(AmountFieldWidth, RowHeight);
			_amountField = fieldGo.GetComponent<InputField>() ?? fieldGo.GetComponentInChildren<InputField>();
			if (_amountField != null)
			{
				if (!string.IsNullOrEmpty(item.SharedName) && _lastAmounts.TryGetValue(item.SharedName, out var last)) _amountField.text = last.ToString();
				_amountField.onEndEdit.AddListener(_ =>
				{
					// onEndEdit also fires when the field loses focus; only Enter grabs.
					if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) GrabTyped();
				});
			}
			AddButton("Grab", Pad + AmountFieldWidth + Gap, y, Width - Pad * 2f - AmountFieldWidth - Gap, _grabbable > 0, GrabTyped);

			MaterialsPanel.KeepAlive();
		}

		internal static void Close()
		{
			if (_menu != null) Object.Destroy(_menu);
			if (_catcher != null) Object.Destroy(_catcher);
			_menu = null;
			_catcher = null;
			_amountField = null;
		}

		internal static void Tick()
		{
			if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
		}

		private static void GrabTyped()
		{
			if (_amountField == null) return;
			if (!int.TryParse(_amountField.text, out var n) || n <= 0)
			{
				Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Type how many to grab");
				return;
			}
			Grab(n, remember: true);
		}

		private static void Grab(int count, bool remember)
		{
			var item = _item;
			Close();
			if (count <= 0 || string.IsNullOrEmpty(item.SharedName)) return;
			if (remember) _lastAmounts[item.SharedName] = count;
			ConsoleCommands.GrabFromPanel(item.SharedName, item.Name, count);
			MaterialsPanel.KeepAlive();
		}

		private static void AddButton(string label, float x, float y, float width, bool enabled, System.Action onClick)
		{
			var go = GUIManager.Instance.CreateButton(
				text: label,
				parent: _menu.transform,
				anchorMin: new Vector2(0f, 1f),
				anchorMax: new Vector2(0f, 1f),
				position: new Vector2(x, y),
				width: width,
				height: RowHeight);
			var rect = go.GetComponent<RectTransform>();
			rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = new Vector2(x, y);
			rect.sizeDelta = new Vector2(width, RowHeight);
			var button = go.GetComponent<Button>();
			if (button != null)
			{
				button.interactable = enabled;
				if (enabled) button.onClick.AddListener(() => onClick());
			}
			var text = go.GetComponentInChildren<Text>();
			if (text != null && !enabled) text.color = new Color(0.55f, 0.55f, 0.55f);
		}

		// Put the menu's top-left corner at the mouse, pulled back onto the screen when it
		// would hang off the right or bottom edge.
		private static void PlaceAt(RectTransform rect, GameObject front, Vector2 screenPos, float height)
		{
			var frontRect = front.GetComponent<RectTransform>();
			var canvas = front.GetComponentInParent<Canvas>();
			var cam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;
			var scale = canvas != null ? canvas.scaleFactor : 1f;

			var x = Mathf.Min(screenPos.x, Screen.width - Width * scale - 4f);
			var y = Mathf.Max(screenPos.y, height * scale + 4f);
			RectTransformUtility.ScreenPointToLocalPointInRectangle(frontRect, new Vector2(x, y), cam, out var local);

			// Anchor on the parent's pivot so the local point is the anchored position.
			rect.anchorMin = rect.anchorMax = frontRect.pivot;
			rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = local;
		}
	}
}
