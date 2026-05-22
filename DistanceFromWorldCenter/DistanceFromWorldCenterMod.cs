using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DistanceFromWorldCenter
{
	[BepInPlugin(ModGuid, "Distance From World Center", "1.0.0")]
	[BepInProcess("valheim.exe")]
	[BepInDependency(Jotunn.Main.ModGuid)]
	public class DistanceFromWorldCenterMod : BaseUnityPlugin
	{
		const string ModGuid = "DeathMonger.DistanceFromWorldCenter";
		public static DistanceFromWorldCenterMod Instance;

		public ConfigEntry<bool> ShowDistanceHud;
		public ConfigEntry<bool> ShowDistanceCoords;
		public ConfigEntry<float> DistanceHudOffsetX;
		public ConfigEntry<float> DistanceHudOffsetY;

		private void Awake()
		{
			Instance = this;

			ShowDistanceHud = Config.Bind("Distance HUD", "Enabled", true, new ConfigDescription("Show a small always-on widget displaying the player's horizontal distance from the world center."));
			ShowDistanceCoords = Config.Bind("Distance HUD", "Show Coordinates", false, new ConfigDescription("Also include the player's (X, Z) coordinates in the distance widget."));
			DistanceHudOffsetX = Config.Bind("Distance HUD", "Offset X (px)", 10f, new ConfigDescription("Horizontal offset from the upper-left corner of the screen."));
			DistanceHudOffsetY = Config.Bind("Distance HUD", "Offset Y (px)", 10f, new ConfigDescription("Vertical offset from the top of the screen."));
		}

		private void Update()
		{
			DistanceHud.Tick();
		}
	}
}
