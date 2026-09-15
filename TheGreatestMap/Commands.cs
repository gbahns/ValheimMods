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
                    args.Context.AddString($"Personal map: {ClientPins.Count} markers ({auto} recorded, {manual} placed), {ClientPins.TombstoneCount} erased, {ClientPins.SuppressionCount} erased spots; sharing mode: {TgmConfig.SharingMode.Value}");
                    args.Context.AddString($"Found but not yet recorded: {DiscoveryLedger.PendingCount}, recorded this session: {DiscoveryLedger.RecordedCount}, map out: {PocketMap.IsOut}");
                    if (PinStore.IsServer) args.Context.AddString($"Shared map (this is the server): {PinStore.Count} markers, {PinStore.TombstoneCount} erased, {PinStore.SuppressionCount} erased spots");
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

            new Terminal.ConsoleCommand("tgm_resync", "Merge your personal map with the shared map now, without a cartography table",
                (Terminal.ConsoleEvent)(args =>
                {
                    SyncEngine.SyncWithServer(announceNothing: true);
                    args.Context.AddString("Merging with the shared map...");
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

            new Terminal.ConsoleCommand("tgm_relabel", "Apply the label rules to existing recorded markers for everyone (tgm_relabel Structures for one kind); labels are only removed, never added",
                (Terminal.ConsoleEvent)(args =>
                {
                    Category? only = null;
                    if (args.Args.Length >= 2)
                    {
                        if (System.Enum.TryParse(args.Args[1], true, out Category parsed)) only = parsed;
                        else { args.Context.AddString("Unknown kind '" + args.Args[1] + "'. Kinds: " + string.Join(", ", System.Array.ConvertAll(Categories.All, c => c.ToString()))); return; }
                    }
                    int n = ClientPins.ApplyLabelRules(only);
                    args.Context.AddString($"Removed labels from {n} recorded markers.");
                }));

            new Terminal.ConsoleCommand("tgm_reicon", "Repair dungeon markers that got the swamp crypt key because their cave was not in the catalog; a real sunken crypt keeps it. Carries to everyone at the next merge",
                (Terminal.ConsoleEvent)(args =>
                {
                    int n = ClientPins.RepairDungeonIcons();
                    ClientPins.Restyle();
                    args.Context.AddString(n == 0 ? "No dungeon markers needed repairing." : $"Repaired {n} dungeon marker(s).");
                }));

            new Terminal.ConsoleCommand("tgm_show", "Show every hidden marker again: clears markers hidden one by one, the Hidden Icons list and switches every Show <Kind> back on",
                (Terminal.ConsoleEvent)(args =>
                {
                    int single = ViewPrefs.Count, icons = TgmConfig.HiddenIconCount(), kinds = TgmConfig.HiddenKindCount();
                    ViewPrefs.Clear();
                    TgmConfig.ShowEverything();
                    ClientPins.Restyle();
                    args.Context.AddString($"Showing everything again ({single} single markers, {icons} icons and {kinds} kinds were hidden).");
                }));

            new Terminal.ConsoleCommand("tgm_erase","Erase recorded markers of one kind near you: tgm_erase Structures [radius, default 50]. Works even when erasing by click is off; carries to everyone at the next merge",
                (Terminal.ConsoleEvent)(args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) { args.Context.AddString("No player."); return; }
                    if (args.Args.Length < 2 || !System.Enum.TryParse(args.Args[1], true, out Category kind))
                    {
                        args.Context.AddString("Usage: tgm_erase <kind> [radius]. Kinds: " + string.Join(", ", System.Array.ConvertAll(Categories.All, c => c.ToString())));
                        return;
                    }
                    float radius = 50f;
                    if (args.Args.Length >= 3) float.TryParse(args.Args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
                    int n = ClientPins.EraseKindNear(kind, player.transform.position, radius);
                    args.Context.AddString($"Erased {n} recorded {kind} markers within {radius:0} m.");
                }));

            new Terminal.ConsoleCommand("tgm_look", "Report what the crosshair hits and every reason it would or would not be recorded (also written to the log)",
                (Terminal.ConsoleEvent)(args =>
                {
                    foreach (var line in Diagnostics.Describe())
                    {
                        args.Context.AddString(line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_look: " + line);
                    }
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
