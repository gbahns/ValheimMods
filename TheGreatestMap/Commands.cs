using System.Collections.Generic;

namespace TheGreatestMap
{
    /// <summary>Console commands (F5). All are prefixed tgm_.</summary>
    internal static class Commands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("tgm_status", "The Greatest Map: shared marker counts and local state",
                (Terminal.ConsoleEvent)(args =>
                {
                    int auto = 0, manual = 0;
                    foreach (var pin in ClientPins.All) { if (pin.Auto) auto++; else manual++; }
                    args.Context.AddString($"Shared markers: {ClientPins.Count} ({auto} recorded, {manual} placed), erased spots: {ClientPins.SuppressionCount}, synced: {ClientPins.Synced}");
                    args.Context.AddString($"Found but not yet recorded: {DiscoveryLedger.PendingCount}, recorded this session: {DiscoveryLedger.RecordedCount}, map out: {PocketMap.IsOut}");
                    if (PinStore.IsServer) args.Context.AddString($"Server store: {PinStore.Count} markers, {PinStore.SuppressionCount} erased spots");
                }));

            new Terminal.ConsoleCommand("tgm_import", "Share your local map markers (the five standard icons) with everyone",
                (Terminal.ConsoleEvent)(args =>
                {
                    int n = ClientPins.ImportLocalPins();
                    args.Context.AddString($"Shared {n} local markers.");
                }));

            new Terminal.ConsoleCommand("tgm_clearlocal", "Delete your local (non-shared) player-placed markers, including stale ones imported from tables",
                (Terminal.ConsoleEvent)(args =>
                {
                    int n = ClientPins.ClearLocalPins();
                    args.Context.AddString($"Deleted {n} local markers.");
                }));

            new Terminal.ConsoleCommand("tgm_resync", "Re-download the shared markers from the server",
                (Terminal.ConsoleEvent)(args =>
                {
                    PinNetwork.SendRequestSync();
                    args.Context.AddString("Requested a full sync.");
                }));

            new Terminal.ConsoleCommand("tgm_sync", "Read and write the nearest cartography table now",
                (Terminal.ConsoleEvent)(args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) { args.Context.AddString("No player."); return; }
                    args.Context.AddString(TableSync.SyncNow(player, announceMissing: false) ? "Synced." : "No cartography table within reach.");
                }));

            new Terminal.ConsoleCommand("tgm_forget", "Forget everything found but not yet recorded (nothing is deleted from the map)",
                (Terminal.ConsoleEvent)(args =>
                {
                    DiscoveryLedger.Clear();
                    Recorder.Reset();
                    args.Context.AddString("Forgot pending discoveries.");
                }));

            new Terminal.ConsoleCommand("tgm_wipe", "Admin: erase shared markers for everyone. tgm_wipe auto (default) erases recorded ones only; tgm_wipe all erases every shared marker",
                (Terminal.ConsoleEvent)(args =>
                {
                    bool autoOnly = args.Args.Length < 2 || args.Args[1].ToLowerInvariant() != "all";
                    PinNetwork.SendWipe(autoOnly);
                    args.Context.AddString(autoOnly ? "Asked the server to erase recorded markers." : "Asked the server to erase all shared markers.");
                }));

            new Terminal.ConsoleCommand("tgm_unsuppress", "Admin: allow recording again at spots where recorded markers were erased",
                (Terminal.ConsoleEvent)(args =>
                {
                    PinNetwork.SendUnsuppress();
                    args.Context.AddString("Asked the server to clear erased spots.");
                }));

            new Terminal.ConsoleCommand("tgm_list", "List shared markers, nearest first (tgm_list 40 for more)",
                (Terminal.ConsoleEvent)(args =>
                {
                    var player = Player.m_localPlayer;
                    int limit = 15;
                    if (args.Args.Length >= 2) int.TryParse(args.Args[1], out limit);
                    var pins = new List<SharedPin>(ClientPins.All);
                    if (player != null)
                        pins.Sort((a, b) => Geo.FlatDistance(a.Pos, player.transform.position).CompareTo(Geo.FlatDistance(b.Pos, player.transform.position)));
                    int shown = 0;
                    foreach (var pin in pins)
                    {
                        if (shown++ >= limit) break;
                        string dist = player != null ? $"{Geo.FlatDistance(pin.Pos, player.transform.position):0}m" : "";
                        args.Context.AddString($"{dist,6} {(pin.Auto ? "[rec]" : "[pin]")} {pin.Name} ({pin.Icon}) by {pin.Author}");
                    }
                    if (pins.Count == 0) args.Context.AddString("No shared markers.");
                }));
        }
    }
}
