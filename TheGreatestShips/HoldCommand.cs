using System.Globalization;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// <c>tgs_hold [right forward]</c>: shows or sets the Hold Fast Offset of the hull you are
    /// aboard -- the Karve's if the nearest of this mod's ships is a Karve hull, else the
    /// longship hulls' -- in meters, and applies it at once.  So the spot can be dialed in by
    /// eye while holding the mast, and the numbers read back for this mod's defaults.
    /// </summary>
    internal sealed class HoldCommand : ConsoleCommand
    {
        public override string Name => "tgs_hold";
        public override string Help => "tgs_hold [right forward]: show or set, in meters, where you stand to hold fast to the mast of the hull you are aboard (the Karve hulls' or the longship hulls'). Applies at once.";

        internal static void Register() => CommandManager.Instance.AddConsoleCommand(new HoldCommand());

        public override void Run(string[] args)
        {
            var def = Nearest();
            bool karve = def != null && def.BasePrefab == "Karve";
            var entry  = karve ? ShipConfig.HoldFastOffsetKarve : ShipConfig.HoldFastOffsetLongship;
            string hull = karve ? "the Karve hull" : "the longship hulls";
            string aboard = def != null ? def.DisplayName : "no ship of this mod near you";

            if (args.Length >= 2
                && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float right)
                && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float forward))
            {
                entry.Value = new Vector2(right, forward);   // its SettingChanged refits every ship at once
                Console.instance.Print($"Hold Fast Offset for {hull}: {right:0.000} right, {forward:0.000} forward ({aboard}).");
            }
            else if (args.Length == 0)
                Console.instance.Print($"Hold Fast Offset for {hull}: {entry.Value.x:0.000} right, {entry.Value.y:0.000} forward ({aboard}). " +
                                       "tgs_hold <right> <forward> sets it, in meters.");
            else
                Console.instance.Print("tgs_hold <right> <forward>, in meters: e.g. tgs_hold 0 -0.06");
        }

        // The nearest of this mod's ships within 15 m of the player, by hull center.
        private static ShipDefinition Nearest()
        {
            var player = Player.m_localPlayer;
            if (player == null) return null;
            Ship nearest = null;
            float best = 15f * 15f;
            foreach (var updater in Ship.Instances)
            {
                if (!(updater is Ship ship)) continue;
                float d = (ship.transform.position - player.transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = ship; }
            }
            if (nearest == null) return null;
            string prefab = Utils.GetPrefabName(nearest.gameObject);
            foreach (var def in ShipDefinitions.All)
                if (def.PrefabName == prefab) return def;
            return null;
        }
    }
}
