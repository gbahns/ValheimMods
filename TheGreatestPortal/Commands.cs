using System.Text;

namespace TheGreatestPortal
{
    /// <summary>Console commands (F5 in game, or the dedicated server's console). Prefixed tgp_.</summary>
    internal static class Commands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("tgp_status", "The Greatest Portal: portal count, map picker state and favourites",
                (Terminal.ConsoleEvent)(args =>
                {
                    args.Context?.AddString(PortalNetwork.Status());
                    if (Player.m_localPlayer != null)
                        args.Context?.AddString($"picker: {MapPicker.Status()} | favourites: {Favorites.Count}, default: {DefaultName()}");
                }));

            new Terminal.ConsoleCommand("tgp_list", "The Greatest Portal: list every portal the server knows, with destinations",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (Catalog.Count == 0)
                    {
                        args.Context?.AddString(Catalog.HasSnapshot ? "No portals in the world." : "No portal list received from the server yet.");
                        return;
                    }
                    var sb = new StringBuilder();
                    foreach (var p in Catalog.All)
                    {
                        var t = Catalog.Get(p.TargetId);
                        string dest = p.TargetId == 0L ? "open (choose on the map)" : (t != null ? "-> " + t.DisplayName : "-> missing portal");
                        string fav = Favorites.IsFavorite(p.Id) ? " *" : "";
                        string def = Favorites.DefaultId == p.Id ? " (default)" : "";
                        sb.AppendLine($"{p.DisplayName}{fav}{def}  [{p.Pos.x:0},{p.Pos.z:0}]  {dest}");
                    }
                    args.Context?.AddString(sb.ToString().TrimEnd());
                }));

            new Terminal.ConsoleCommand("tgp_refresh", "The Greatest Portal: ask the server for the portal list again",
                (Terminal.ConsoleEvent)(args =>
                {
                    PortalNetwork.RequestCatalog();
                    args.Context?.AddString("Portal list requested.");
                }));

            new Terminal.ConsoleCommand("tgp_default", "The Greatest Portal: tgp_default <portal name> sets the default portal for portals you build; tgp_default clear removes it",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (Player.m_localPlayer == null) { args.Context?.AddString("Not in a game."); return; }
                    string name = args.Length > 1 ? string.Join(" ", args.Args, 1, args.Length - 1) : "";
                    if (string.IsNullOrEmpty(name))
                    {
                        args.Context?.AddString("Default portal: " + DefaultName());
                        return;
                    }
                    if (name.Equals("clear", System.StringComparison.OrdinalIgnoreCase))
                    {
                        Favorites.SetDefault(0L);
                        args.Context?.AddString("Default portal cleared.");
                        return;
                    }
                    var p = Catalog.ByName(name);
                    if (p == null) { args.Context?.AddString($"No portal named '{name}'."); return; }
                    Favorites.SetDefault(p.Id);
                    args.Context?.AddString($"New portals will connect to '{p.DisplayName}'.");
                }));
        }

        private static string DefaultName()
        {
            long id = Favorites.DefaultId;
            if (id == 0L) return "none";
            var p = Catalog.Get(id);
            return p != null ? p.DisplayName : "a portal that no longer exists";
        }
    }
}
