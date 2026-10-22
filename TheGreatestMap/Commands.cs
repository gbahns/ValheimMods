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

            new Terminal.ConsoleCommand("tgm_show", "Show every hidden marker again: clears markers hidden one by one, the Hidden Icons list and the hidden crossed-off markers, and switches every Show <Kind> back on",
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

            new Terminal.ConsoleCommand("tgm_backups", "List the copies kept of your map from before a format change, and how to put one back",
                (Terminal.ConsoleEvent)(args =>
                {
                    var files = MapBackup.All();
                    if (files.Count == 0)
                    {
                        args.Context.AddString("No backups. One is kept automatically the first time a map is read in an older format.");
                        args.Context.AddString("They live in " + MapBackup.Folder);
                        return;
                    }
                    args.Context.AddString($"{files.Count} backup(s) in {MapBackup.Folder}:");
                    foreach (var f in files)
                    {
                        var info = new System.IO.FileInfo(f);
                        args.Context.AddString($"  {info.Name}  {info.Length:n0} bytes  {info.LastWriteTime:yyyy-MM-dd HH:mm}");
                        TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] tgm_backups: {f} ({info.Length:n0} bytes)");
                    }
                    args.Context.AddString("Put one back with: tgm_restore <name> confirm");
                }));

            new Terminal.ConsoleCommand("tgm_restore", "Put a backup of your own map back: tgm_restore <name> confirm. Replaces every marker you hold with the ones in that copy",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (Player.m_localPlayer == null) { args.Context.AddString("No player."); return; }
                    if (args.Args.Length < 2)
                    {
                        args.Context.AddString("Usage: tgm_restore <name> confirm   (tgm_backups lists the names)");
                        return;
                    }
                    string name = args.Args[1];
                    string path = null;
                    foreach (var f in MapBackup.All())
                        if (System.IO.Path.GetFileName(f).IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0) { path = f; break; }
                    if (path == null) { args.Context.AddString($"No backup matching \"{name}\". Run tgm_backups."); return; }

                    MapStore restored;
                    try { restored = MapStore.FromBytes(System.IO.File.ReadAllBytes(path)); }
                    catch (System.Exception e) { args.Context.AddString("Could not read that backup: " + e.Message); return; }

                    // Said before it is done, because the thing it cannot undo is the part people
                    // get wrong: an erasure that already reached the server has been seen by
                    // everyone, and the next merge will apply it again whatever is restored here.
                    if (args.Args.Length < 3 || !string.Equals(args.Args[2], "confirm", System.StringComparison.OrdinalIgnoreCase))
                    {
                        args.Context.AddString($"{System.IO.Path.GetFileName(path)} holds {restored.Pins.Count} markers (format {restored.ReadVersion}); you hold {ClientPins.Count}.");
                        args.Context.AddString("This replaces your markers with those. Erasures that already reached the server will still be applied at the next merge;");
                        args.Context.AddString("rolling the shared map back as well means restoring the server's copy with nobody connected.");
                        args.Context.AddString("Run again with: tgm_restore " + name + " confirm");
                        return;
                    }
                    int was = ClientPins.Count;
                    ClientPins.ReplaceAll(restored);
                    args.Context.AddString($"Restored {restored.Pins.Count} markers from {System.IO.Path.GetFileName(path)} (you had {was}).");
                    TheGreatestMapMod.Log.LogWarning(
                        $"[TheGreatestMap] tgm_restore: replaced {was} markers with {restored.Pins.Count} from {path}.");
                }));

            new Terminal.ConsoleCommand("tgm_admin", "Ask the server whether it treats you as an admin, and what player id it sees for you",
                (Terminal.ConsoleEvent)(args =>
                {
                    PinNetwork.AskAmIAdmin();
                    args.Context.AddString("Asked the server; the answer appears on screen and in the log.");
                }));

            new Terminal.ConsoleCommand("tgm_portals", "What the portal system believes right now, and why a portal near you is or is not drawn",
                (Terminal.ConsoleEvent)(args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) { args.Context.AddString("No player."); return; }
                    foreach (var line in Portals.Describe(player.transform.position))
                    {
                        args.Context.AddString(line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_portals: " + line);
                    }
                    Portals.AskForList();
                    args.Context.AddString("Asked the server for a fresh list; run it again to see what came back.");
                }));

            new Terminal.ConsoleCommand("tgm_deposits", "List every deposit prefab in the game, what it yields, and whether the catalog knows it (also written to the log)",
                (Terminal.ConsoleEvent)(args =>
                {
                    foreach (var line in Catalog.Deposits())
                    {
                        args.Context.AddString(line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_deposits: " + line);
                    }
                }));

            new Terminal.ConsoleCommand("tgm_items", "List the game's items whose name contains a word, for choosing a marker icon: tgm_items bear",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (args.Args.Length < 2) { args.Context.AddString("Usage: tgm_items <word>"); return; }
                    var found = IconRegistry.ItemsLike(args.Args[1]);
                    if (found.Count == 0) { args.Context.AddString($"No item name contains '{args.Args[1]}'."); return; }
                    args.Context.AddString($"{found.Count} item(s) matching '{args.Args[1]}':");
                    foreach (var name in found) args.Context.AddString("  " + name);
                }));

            new Terminal.ConsoleCommand("tgm_legend", "Open the legend: every marker this mod can draw and the thing it stands for",
                (Terminal.ConsoleEvent)(args =>
                {
                    LegendPanel.Toggle();
                    args.Context.AddString(LegendPanel.IsOpen ? "Legend opened." : "Legend closed.");
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

            new Terminal.ConsoleCommand("tgm_mapui", "Measure the large map's own layout: how much screen it uses and how its parts are anchored (also written to the log)",
                (Terminal.ConsoleEvent)(args =>
                {
                    foreach (var line in MapLayout.Describe())
                    {
                        args.Context.AddString(line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_mapui: " + line);
                    }
                }));

            new Terminal.ConsoleCommand("tgm_identify", "Tell markers written by older versions what they are, so their icons follow the catalog; lists first, tgm_identify apply writes it",
                (Terminal.ConsoleEvent)(args =>
                {
                    bool apply = args.Args.Length >= 2 && args.Args[1].ToLowerInvariant() == "apply";
                    var result = Identify.Run(apply);
                    foreach (var line in Identify.Describe(result, apply))
                    {
                        args.Context.AddString(line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_identify: " + line);
                    }
                    if (!apply && result.Matched > 0)
                        args.Context.AddString("Run tgm_identify apply to write it. The change travels like any other edit.");
                    // The ones no icon can settle are put to the server, which holds the list of
                    // every building the world generator placed. The answer comes back a moment
                    // later, so it prints itself rather than joining the report above.
                    var ctx = args.Context;
                    if (Identify.AskTheWorld(result, apply, lines =>
                        {
                            foreach (var line in lines)
                            {
                                if (ctx != null) ctx.AddString(line);
                                TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_identify: " + line);
                            }
                        }))
                        args.Context.AddString($"Asking the world about {result.Ambiguous + result.Lost} more...");
                }));

            new Terminal.ConsoleCommand("tgm_counts", "What the map is made of: markers by kind, then by icon, commonest first",
                (Terminal.ConsoleEvent)(args =>
                {
                    int total = ClientPins.Count;
                    if (total == 0) { args.Context.AddString("No markers on your map."); return; }
                    var kinds = new Dictionary<string, int>();
                    var icons = new Dictionary<string, int>();
                    int crossed = 0, placed = 0;
                    foreach (var pin in ClientPins.All)
                    {
                        var kind = ClientPins.KindOf(pin);
                        string k = kind.HasValue ? Categories.Label(kind.Value) : "No kind";
                        kinds.TryGetValue(k, out int kn); kinds[k] = kn + 1;
                        string icon = ClientPins.IconOf(pin) ?? "";
                        icons.TryGetValue(icon, out int ino); icons[icon] = ino + 1;
                        if (pin.Checked) crossed++;
                        if (!pin.Auto) placed++;
                    }
                    var byKind = new List<KeyValuePair<string, int>>(kinds);
                    byKind.Sort((a, b) => b.Value.CompareTo(a.Value));
                    var byIcon = new List<KeyValuePair<string, int>>(icons);
                    byIcon.Sort((a, b) => b.Value.CompareTo(a.Value));

                    void Say(string line)
                    {
                        args.Context.AddString(line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_counts: " + line);
                    }

                    Say($"{total} markers: {crossed} crossed off, {placed} placed by hand.");
                    foreach (var kv in byKind)
                        Say($"  {kv.Key}: {kv.Value} ({100f * kv.Value / total:0.#}%)");
                    Say("By icon, the busiest first:");
                    int shown = 0;
                    foreach (var kv in byIcon)
                    {
                        if (shown++ >= 15) break;
                        Say($"  {IconRegistry.DisplayName(kv.Key)} ({kv.Key}): {kv.Value}");
                    }
                }));

            new Terminal.ConsoleCommand("tgm_order", "With the large map open, list the pins under the pointer in the order they are drawn (last is on top)",
                (Terminal.ConsoleEvent)(args =>
                {
                    var map = Minimap.instance;
                    if (map == null) { args.Context.AddString("No map."); return; }
                    float radius = 20f;
                    if (args.Args.Length >= 2) float.TryParse(args.Args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
                    // The pointer only means something with the large map open and pointing at
                    // the ground you mean. Shut, it converts a cursor position against the
                    // minimap, which is how the same command found a farm once and never again.
                    bool large = map.m_mode == Minimap.MapMode.Large;
                    var player = Player.m_localPlayer;
                    UnityEngine.Vector3 world = large || player == null
                        ? Access.ScreenToWorldPoint(map, ZInput.pointerPosition)
                        : player.transform.position;
                    args.Context.AddString(large
                        ? $"Looking where the pointer is: {world.x:0}, {world.z:0} (within {radius:0} m)."
                        : $"The large map is shut, so looking where you stand: {world.x:0}, {world.z:0} (within {radius:0} m).");
                    foreach (var line in ClientPins.DescribeOrder(world, radius))
                    {
                        args.Context.AddString(line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_order: " + line);
                    }
                }));

            new Terminal.ConsoleCommand("tgm_transports", "What the mod knows about ships and carts, and why one near you is or is not drawn",
                (Terminal.ConsoleEvent)(args =>
                {
                    foreach (var line in Transports.Describe())
                    {
                        args.Context.AddString(line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_transports: " + line);
                    }
                }));

            new Terminal.ConsoleCommand("tgm_find", "Show only the markers mentioning some text (tgm_find frost); tgm_find on its own shows everything again",
                (Terminal.ConsoleEvent)(args =>
                {
                    string text = args.Args.Length >= 2 ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim() : "";
                    if (text.Length == 0)
                    {
                        bool was = Focus.On;
                        Focus.Clear();
                        args.Context.AddString(was ? "Showing every marker again." : "Nothing was being singled out.");
                        return;
                    }
                    Focus.Search(text);
                    Focus.Tally(out int shown, out int all);
                    args.Context.AddString($"Showing {shown} of {all} markers mentioning \"{text}\".");
                    foreach (var match in Focus.Types(text))
                        args.Context.AddString($"  {match.Label} ({match.Count})");
                    if (shown == 0) args.Context.AddString("  nothing matches -- tgm_find on its own puts the map back.");
                }));

            new Terminal.ConsoleCommand("tgm_dedupe", "Markers standing on one another: lists them, and tgm_dedupe apply folds each group into one",
                (Terminal.ConsoleEvent)(args =>
                {
                    bool apply = args.Args.Length >= 2 && args.Args[1].ToLowerInvariant() == "apply";
                    var groups = Duplicates.Find();
                    if (groups.Count == 0) { args.Context.AddString("No markers are standing on one another."); return; }
                    int extra = 0;
                    foreach (var g in groups)
                    {
                        extra += g.Drop.Count;
                        string name = string.IsNullOrEmpty(g.Keep.Name) ? IconRegistry.DisplayName(g.Keep.Icon) : g.Keep.Name;
                        float worst = 0f;
                        foreach (var d in g.Drop) { float gap = Geo.FlatDistance(g.Keep.Pos, d.Pos); if (gap > worst) worst = gap; }
                        var authors = new List<string>();
                        foreach (var p in g.Members)
                        {
                            string by = string.IsNullOrEmpty(p.Author) ? "?" : p.Author;
                            if (!authors.Contains(by)) authors.Add(by);
                        }
                        // Who wrote them is the whole story of how the pair came about: one marker
                        // each from several players is the crew recording the same find at once,
                        // while several from one player is a marker being written down twice.
                        string line = $"{name} ({g.Keep.Icon}): {g.Drop.Count + 1} markers within {worst:0.#} m" +
                                      (g.Keep.Checked || g.Drop.Exists(d => d.Checked) ? ", one crossed off" : "") +
                                      ", by " + string.Join(", ", authors.ToArray());
                        args.Context.AddString("  " + line);
                        TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] tgm_dedupe: " + line);
                    }
                    if (!apply)
                    {
                        args.Context.AddString($"{groups.Count} group(s), {extra} marker(s) would go. Run tgm_dedupe apply to fold them.");
                        return;
                    }
                    int gone = Duplicates.Fold();
                    args.Context.AddString($"Folded {groups.Count} group(s); {gone} marker(s) erased. The erasures travel like any other.");
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
                        args.Context.AddString($"       {ClientPins.DrawnState(pin)}");
                        TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] tgm_list: {dist} {(pin.Auto ? "[rec]" : "[pin]")} {pin.Name} ({pin.Icon}) - {ClientPins.DrawnState(pin)}");
                    }
                    if (pins.Count == 0) args.Context.AddString("No shared markers.");
                }));
        }
    }
}
