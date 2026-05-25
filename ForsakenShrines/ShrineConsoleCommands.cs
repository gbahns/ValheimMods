using Jotunn.Entities;
using Jotunn.Managers;
using System.Collections.Generic;

namespace ForsakenShrines
{
    internal static class ShrineConsoleCommands
    {
        internal static void Register()
        {
            CommandManager.Instance.AddConsoleCommand(new ShrineSpawnAllCommand());
            CommandManager.Instance.AddConsoleCommand(new ShrineSpawnTrophiesCommand());
        }

        // Aggregate item totals across all shrine requirements, optionally filtering
        // each requirement.  Returns a dict of itemPrefab → total amount.
        internal static Dictionary<string, int> AggregateRequirements(
            System.Func<Piece.Requirement, bool> filter = null)
        {
            var totals = new Dictionary<string, int>();
            foreach (var def in ShrineDefinitions.All)
            {
                foreach (var req in ShrineConfig.BuildRequirements(def.PieceName))
                {
                    if (filter != null && !filter(req)) continue;
                    string key = req.m_resItem.name;
                    totals[key] = totals.TryGetValue(key, out int existing)
                        ? existing + req.m_amount
                        : req.m_amount;
                }
            }
            return totals;
        }

        internal static int GiveToPlayer(Dictionary<string, int> totals)
        {
            var player = Player.m_localPlayer;
            if (player == null) return -1;
            // Use the public GetInventory() accessor — direct m_inventory access compiles against
            // the publicized assembly but throws FieldAccessException against the shipped DLL.
            var inv = player.GetInventory();
            if (inv == null) return -1;
            int added = 0;
            foreach (var kvp in totals)
            {
                inv.AddItem(kvp.Key, kvp.Value, 1, 0, 0L, "");
                added++;
            }
            return added;
        }
    }

    /// <summary>
    /// ShrineSpawnAll — gives every material needed to build all Forsaken Shrines.
    /// Stacks are aggregated across all shrines so you get one batch per item type.
    /// Requires devcommands to be active.
    /// </summary>
    internal class ShrineSpawnAllCommand : ConsoleCommand
    {
        public override string Name => "ShrineSpawnAll";
        public override string Help => "Gives all materials needed to build every Forsaken Shrine.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            var totals = ShrineConsoleCommands.AggregateRequirements();
            int added = ShrineConsoleCommands.GiveToPlayer(totals);
            if (added < 0)
            {
                Console.instance.Print("No local player found.");
                return;
            }
            Console.instance.Print(
                $"[ForsakenShrines] Added {added} item types for all {ShrineDefinitions.All.Length} shrines.");
        }
    }

    /// <summary>
    /// ShrineSpawnTrophies — gives just the trophy items needed across all Forsaken Shrines
    /// (skips bulk materials like Stone, Chain, Iron).  Useful when you already have the
    /// base materials but need the boss/sub-boss trophies.
    /// Requires devcommands to be active.
    /// </summary>
    internal class ShrineSpawnTrophiesCommand : ConsoleCommand
    {
        public override string Name => "ShrineSpawnTrophies";
        public override string Help => "Gives all trophy items needed to build every Forsaken Shrine.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            var totals = ShrineConsoleCommands.AggregateRequirements(
                req => req.m_resItem.name.StartsWith("Trophy"));
            int added = ShrineConsoleCommands.GiveToPlayer(totals);
            if (added < 0)
            {
                Console.instance.Print("No local player found.");
                return;
            }
            Console.instance.Print(
                $"[ForsakenShrines] Added {added} trophy types for all {ShrineDefinitions.All.Length} shrines.");
        }
    }
}
