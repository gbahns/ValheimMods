using System;
using System.IO;
using TheGreatestMap;

// Merges two of TheGreatestMap's pin stores using the mod's own MapStore.Merge, so the result
// obeys the same rules the game does: later change wins, and a tombstone beats an older pin, so
// nothing anyone deliberately erased comes back.
//
//   pinmerge <base.bin> <incoming.bin> <out.bin>
//
// <base> is merged INTO, <incoming> is merged FROM. Merge is symmetric for pins but tombstones
// take the later timestamp either way, so the direction only decides which copy is reported as
// "added" versus "already here".
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            System.Console.Error.WriteLine("usage: pinmerge <base.bin> <incoming.bin> <out.bin>");
            return 2;
        }

        var a = MapStore.FromBytes(File.ReadAllBytes(args[0]));
        var b = MapStore.FromBytes(File.ReadAllBytes(args[1]));

        System.Console.WriteLine($"base     {Path.GetFileName(args[0])}: {a.Pins.Count} pins, {a.Tombstones.Count} erased, {a.Suppressions.Count} erased spots");
        System.Console.WriteLine($"incoming {Path.GetFileName(args[1])}: {b.Pins.Count} pins, {b.Tombstones.Count} erased, {b.Suppressions.Count} erased spots");

        // Which ids each side has that the other does not - the actual question being asked.
        int onlyA = 0, onlyB = 0, both = 0;
        foreach (var id in a.Pins.Keys) { if (b.Pins.ContainsKey(id)) both++; else onlyA++; }
        foreach (var id in b.Pins.Keys) if (!a.Pins.ContainsKey(id)) onlyB++;
        System.Console.WriteLine($"shared {both}, only in base {onlyA}, only in incoming {onlyB}");

        var r = a.Merge(b);
        System.Console.WriteLine($"merged: added {r.Added}, updated {r.Updated}, deleted by tombstone {r.Deleted}, suppressed {r.Suppressed}");
        System.Console.WriteLine($"result   : {a.Pins.Count} pins, {a.Tombstones.Count} erased, {a.Suppressions.Count} erased spots");

        File.WriteAllBytes(args[2], a.ToBytes());
        System.Console.WriteLine($"wrote {args[2]} ({new FileInfo(args[2]).Length} bytes)");

        // Read it back: a store that cannot be reloaded is not a merge, it is a corrupt file.
        var check = MapStore.FromBytes(File.ReadAllBytes(args[2]));
        if (check.Pins.Count != a.Pins.Count || check.Tombstones.Count != a.Tombstones.Count)
        {
            System.Console.Error.WriteLine("re-read disagrees with what was written; not usable.");
            return 1;
        }
        System.Console.WriteLine("re-read OK");
        return 0;
    }
}
