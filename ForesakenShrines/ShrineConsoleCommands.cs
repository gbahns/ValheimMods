using Jotunn.Entities;
using Jotunn.Managers;
using System.Collections.Generic;

namespace ForesakenShrines
{
    internal static class ShrineConsoleCommands
    {
        internal static void Register()
        {
            CommandManager.Instance.AddConsoleCommand(new GiveShrineMaterialsCommand());
        }
    }

    /// <summary>
    /// shrine_give — gives every material needed to build all seven Forsaken Shrines.
    /// Stacks are aggregated across all shrines so you get one batch per item type.
    /// Requires devcommands to be active.
    /// </summary>
    internal class GiveShrineMaterialsCommand : ConsoleCommand
    {
        public override string Name => "shrine_give";
        public override string Help => "Gives all materials needed to build every Forsaken Shrine.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                Console.instance.Print("No local player found.");
                return;
            }

            // Aggregate totals across all shrines so each item type is one batch.
            var totals = new Dictionary<string, int>();
            foreach (var def in ShrineDefinitions.All)
            {
                foreach (var req in ShrineConfig.BuildRequirements(def.PieceName))
                {
                    string key = req.m_resItem.name;
                    totals[key] = totals.TryGetValue(key, out int existing)
                        ? existing + req.m_amount
                        : req.m_amount;
                }
            }

            int added = 0;
            foreach (var kvp in totals)
            {
                player.m_inventory.AddItem(kvp.Key, kvp.Value, 1, 0, 0L, "");
                added++;
            }

            Console.instance.Print(
                $"[ForesakenShrines] Added {added} item types for all {ShrineDefinitions.All.Length} shrines.");
        }
    }
}
