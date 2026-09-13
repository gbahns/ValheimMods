using HarmonyLib;
using UnityEngine;

namespace GrabMaterials
{
	/// <summary>
	/// Option: pause the game while the /inventory panel is up, the way the ESC menu does.
	/// It goes through vanilla's own Game.Pause / Game.Unpause, the two calls the menu makes,
	/// so alone in a solo or hosted game the game freezes by itself, and on a dedicated server
	/// it is Pause My Server, if installed, that decides. Nothing here knows about that mod.
	///
	/// Because the request can be refused, nothing in the UI claims the game is paused: the
	/// panel's button reads Game.IsPaused() and reports what actually happened.
	/// </summary>
	internal static class PanelPause
	{
		private static bool _holding;

		internal static bool Holding => _holding;

		/// <summary>Hold the pause exactly while the option is on and a pausable panel is up; let go otherwise.</summary>
		internal static void Refresh()
		{
			var cfg = GrabMaterialsMod.GrabMaterialsMod.Instance?.PanelPauseWhileOpen;
			var want = cfg != null && cfg.Value
				&& MaterialsPanel.PausablePanelVisible
				&& Player.m_localPlayer != null;
			if (want == _holding) return;
			_holding = want;
			if (want) Game.Pause();
			else Game.Unpause();
		}

		internal static void Release()
		{
			if (!_holding) return;
			_holding = false;
			Game.Unpause();
		}
	}

	/// <summary>
	/// Escape closes the panel instead of opening the game menu. Only when a panel of ours is
	/// actually up and the menu is not already open, so Escape still closes the menu normally,
	/// and the keypress is swallowed so it does not do both at once.
	/// </summary>
	[HarmonyPatch(typeof(Menu), "Update")]
	internal static class Menu_Update_EscapeClosesPanel
	{
		private static bool Prefix()
		{
			if (!MaterialsPanel.IsVisible) return true;
			if (Menu.IsVisible()) return true;
			if (!Input.GetKeyDown(KeyCode.Escape)) return true;

			MaterialsPanel.Hide();
			return false;
		}
	}
}
