using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace DistanceFromWorldCenter
{
	// Always-on small HUD widget showing the player's horizontal distance from
	// world center (X/Z plane). Position is upper-left, below the player's
	// status icons area.
	public static class DistanceHud
	{
		private const float UpdateInterval = 0.2f;  // ~5x/sec
		private const float Width = 320f;
		private const float Height = 30f;

		private static GameObject _go;
		private static Text _text;
		private static RectTransform _rect;
		private static float _lastUpdate;

		private static bool Enabled => DistanceFromWorldCenterMod.Instance?.ShowDistanceHud?.Value ?? true;
		private static bool ShowCoords => DistanceFromWorldCenterMod.Instance?.ShowDistanceCoords?.Value ?? false;
		// Config X/Y are in screen pixels from the top-left (Y grows downward,
		// matching what users expect). Unity's anchoredPosition has Y growing
		// upward from the anchor, so we flip the sign of Y when applying.
		private static float OffsetX => DistanceFromWorldCenterMod.Instance?.DistanceHudOffsetX?.Value ?? 10f;
		private static float OffsetY => DistanceFromWorldCenterMod.Instance?.DistanceHudOffsetY?.Value ?? 10f;

		public static void Tick()
		{
			EnsureCreated();
			if (_text == null) return;

			// Hide entirely when disabled or there's no player.
			var player = Player.m_localPlayer;
			var shouldShow = Enabled && player != null;
			if (_go.activeSelf != shouldShow) _go.SetActive(shouldShow);
			if (!shouldShow) return;

			// Apply configured position each tick so config edits take effect live.
			_rect.anchoredPosition = new Vector2(OffsetX, -OffsetY);

			var now = Time.time;
			if (now - _lastUpdate < UpdateInterval) return;
			_lastUpdate = now;

			var pos = player.transform.position;
			var distance = Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z);
			_text.text = ShowCoords
				? $"World Center: {distance:F0}m  ({pos.x:F0}, {pos.z:F0})"
				: $"World Center: {distance:F0}m";
		}

		private static void EnsureCreated()
		{
			if (_go != null) return;
			if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;

			_go = GUIManager.Instance.CreateText(
				text: string.Empty,
				parent: GUIManager.CustomGUIFront.transform,
				anchorMin: new Vector2(0f, 1f),
				anchorMax: new Vector2(0f, 1f),
				position: new Vector2(OffsetX, -OffsetY),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 16,
				color: GUIManager.Instance.ValheimOrange,
				outline: true,
				outlineColor: Color.black,
				width: Width,
				height: Height,
				addContentSizeFitter: false);
			_go.name = "DistanceFromWorldCenter_DistanceHud";
			_rect = _go.GetComponent<RectTransform>();
			_rect.pivot = new Vector2(0f, 1f);
			_rect.anchoredPosition = new Vector2(OffsetX, -OffsetY);
			_text = _go.GetComponent<Text>();
			_text.alignment = TextAnchor.UpperLeft;
			_text.raycastTarget = false;
		}
	}
}
