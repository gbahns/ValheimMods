using System.Collections.Generic;
using System.Text;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace GrabMaterials
{
	internal static class MissingMaterialsPanel
	{
		public struct ItemStatus
		{
			public string Name;
			public int Needed;     // original request
			public int Available;  // amount actually grabbed (success) or amount in containers (shortage)
			public int Had;        // amount already in player inventory at request time (only > 0 with GrabDelta)
		}

		private const float PanelWidth = 480f;
		private const float TitleHeight = 50f;
		private const float LineHeight = 26f;
		private const float BottomPadding = 20f;
		private const float TopMargin = 20f;
		private const float ArmDelaySeconds = 0.3f;

		private static float IdleTimeoutSeconds => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelIdleTimeout?.Value ?? 15f;
		private static float FadeDurationSeconds => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelFadeDuration?.Value ?? 3f;
		private static bool DismissOnMovement => GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelDismissOnMovement?.Value ?? true;

		private static GameObject _panel;
		private static RectTransform _panelRect;
		private static Text _titleText;
		private static Text _contentText;
		private static RectTransform _contentRect;
		private static CanvasGroup _canvasGroup;
		private static float _shownAt;
		private static float _fadeStart = -1f;
		private static bool _dismissArmed;

		public static void Show(string title, List<ItemStatus> items)
		{
			EnsureCreated();
			if (_panel == null) return;

			_titleText.text = title;

			var sb = new StringBuilder();
			foreach (var item in items)
			{
				var totalCovered = item.Had + item.Available;
				if (totalCovered >= item.Needed)
				{
					if (item.Had >= item.Needed)
					{
						sb.AppendLine($"<color=#88ff88>✓ {item.Needed} {item.Name}</color> <color=#aaaaaa>(already had)</color>");
					}
					else if (item.Had > 0)
					{
						sb.AppendLine($"<color=#88ff88>✓ {item.Needed} {item.Name}</color> <color=#aaaaaa>(had {item.Had}, grabbed {item.Available})</color>");
					}
					else
					{
						sb.AppendLine($"<color=#88ff88>✓ {item.Needed} {item.Name}</color>");
					}
				}
				else
				{
					var missing = item.Needed - totalCovered;
					if (item.Had > 0)
					{
						sb.AppendLine($"<color=#ff8888>✗ {totalCovered} of {item.Needed} {item.Name}</color> <color=#aaaaaa>(had {item.Had}, missing {missing})</color>");
					}
					else
					{
						sb.AppendLine($"<color=#ff8888>✗ {item.Available} of {item.Needed} {item.Name} (missing {missing})</color>");
					}
				}
			}
			_contentText.text = sb.ToString().TrimEnd();

			// Auto-size to fit content
			float contentHeight = LineHeight * items.Count;
			float panelHeight = TitleHeight + contentHeight + BottomPadding;
			_panelRect.sizeDelta = new Vector2(PanelWidth, panelHeight);
			_contentRect.sizeDelta = new Vector2(_contentRect.sizeDelta.x, contentHeight);

			_panel.SetActive(true);
			_canvasGroup.alpha = 1f;
			_shownAt = Time.time;
			_fadeStart = -1f;
			_dismissArmed = false;
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

			// Idle timeout fires regardless of arm state.
			if (now - _shownAt >= IdleTimeoutSeconds)
			{
				_fadeStart = now;
				return;
			}

			// Grace period: ignore input briefly so a triggered-while-running grab
			// doesn't dismiss instantly.
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
				width: PanelWidth,
				height: 200f,
				draggable: false);
			_panel.name = "GrabMaterials_MissingPanel";
			_panelRect = _panel.GetComponent<RectTransform>();
			_panelRect.pivot = new Vector2(0.5f, 1f);
			_panelRect.anchoredPosition = new Vector2(0f, -TopMargin);
			_canvasGroup = _panel.AddComponent<CanvasGroup>();

			var titleObj = GUIManager.Instance.CreateText(
				text: "Missing materials",
				parent: _panel.transform,
				anchorMin: new Vector2(0.5f, 1f),
				anchorMax: new Vector2(0.5f, 1f),
				position: new Vector2(0f, -25f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 22,
				color: GUIManager.Instance.ValheimOrange,
				outline: true,
				outlineColor: Color.black,
				width: PanelWidth - 60f,
				height: 30f,
				addContentSizeFitter: false);
			_titleText = titleObj.GetComponent<Text>();
			_titleText.alignment = TextAnchor.UpperCenter;
			_titleText.raycastTarget = false;

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
				width: PanelWidth - 40f,
				height: 200f,
				addContentSizeFitter: false);
			_contentText = contentObj.GetComponent<Text>();
			_contentRect = _contentText.GetComponent<RectTransform>();
			_contentRect.pivot = new Vector2(0.5f, 1f);
			_contentRect.anchoredPosition = new Vector2(0f, -TitleHeight);
			_contentText.alignment = TextAnchor.UpperLeft;
			_contentText.supportRichText = true;
			_contentText.raycastTarget = false;

			// Click anywhere on panel dismisses (when the cursor is free, e.g. via Tab/Esc).
			var panelBtn = _panel.AddComponent<Button>();
			panelBtn.transition = Selectable.Transition.None;
			panelBtn.onClick.AddListener(Hide);
		}
	}
}
