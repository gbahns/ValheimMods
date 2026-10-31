using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// What an admin tool did, written down so that it can be taken back.
    ///
    /// <para>tgm_adopt writes hundreds of markers and removes hundreds of hand-placed pins on one
    /// press, and until this neither half could be undone. A recorded marker needs the server's
    /// permission to erase and is erased one at a time; a vanilla pin that has been removed is
    /// gone from the character file at its next save, and nothing else in the game remembers it.
    /// A tool that does that much at once has to keep a record of what it did.</para>
    ///
    /// <para>Two things are kept, because they are two different problems. The markers written are
    /// kept as keys, so that undoing removes exactly those and leaves anything recorded since
    /// where it is -- which is the one thing a copy of the whole map cannot do. The pins removed
    /// are kept whole: picture, name, position, crossed off or not.</para>
    ///
    /// <para>No tombstone is left behind when one of these markers is removed. This is a tool
    /// taking back its own writing, not a player saying a thing is not there, and a suppression
    /// would stop the spot ever being recorded again.</para>
    /// </summary>
    internal static class AdminUndo
    {
        /// <summary>Beside the map backups, which are the other half of taking something back.</summary>
        internal static string Folder => Path.Combine(Paths.ConfigPath, "TheGreatestMap", "undo");

        /// <summary>A hand-placed pin as it was, with everything needed to put it back.</summary>
        internal struct Gone
        {
            public int Type;
            public string Name;
            public Vector3 Pos;
            public bool Checked;
        }

        /// <summary>One run of one tool: what it wrote, and what it took away.</summary>
        internal sealed class Entry
        {
            public string Tool = "";
            public string When = "";
            public readonly List<PinKey> Wrote = new List<PinKey>();
            public readonly List<Gone> Removed = new List<Gone>();
            /// <summary>Markers that were already there and were crossed off to match a pin.</summary>
            public readonly List<PinKey> Crossed = new List<PinKey>();

            public bool Nothing => Wrote.Count == 0 && Removed.Count == 0 && Crossed.Count == 0;
        }

        /// <summary>
        /// Write the record of a run. Never fatal: a run that happened and was not written down is
        /// worse than one that was, but refusing to do the work because the note failed would be
        /// the larger harm -- and the map backup taken at the same moment is still there.
        /// </summary>
        internal static string Write(Entry entry)
        {
            if (entry == null || entry.Nothing) return null;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder,
                    $"{Clean(entry.Tool)}.{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.undo");
                using (var writer = new StreamWriter(path, false))
                {
                    writer.WriteLine("tool\t" + entry.Tool);
                    writer.WriteLine("when\t" + DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
                    foreach (var key in entry.Wrote) writer.WriteLine("wrote\t" + key.Encode());
                    foreach (var key in entry.Crossed) writer.WriteLine("crossed\t" + key.Encode());
                    // The name goes last, so that a tab or anything else somebody typed into it
                    // cannot be read as another field.
                    foreach (var gone in entry.Removed)
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "gone\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}",
                            gone.Type, gone.Pos.x, gone.Pos.y, gone.Pos.z, gone.Checked ? 1 : 0,
                            (gone.Name ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")));
                }
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] {entry.Tool}: wrote down what it did, so it can be taken back: " +
                    $"{path} ({entry.Wrote.Count} marker(s) written, {entry.Removed.Count} pin(s) removed).");
                return path;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Could not write down what {entry.Tool} did, so it cannot be taken back " +
                    $"by itself: {e.Message}. The copy of the map taken at the same time is still there.");
                return null;
            }
        }

        /// <summary>Every record on this machine that has not been used, newest first.</summary>
        internal static List<string> All()
        {
            var found = new List<string>();
            try
            {
                if (!Directory.Exists(Folder)) return found;
                found.AddRange(Directory.GetFiles(Folder, "*.undo"));
                found.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            }
            catch (Exception) { /* a listing nobody can read is an empty listing */ }
            return found;
        }

        /// <summary>Read one back. Anything unreadable in it is dropped rather than argued with.</summary>
        internal static Entry Read(string path)
        {
            var entry = new Entry();
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                switch (parts[0])
                {
                    case "tool": entry.Tool = parts[1]; break;
                    case "when": entry.When = parts[1]; break;
                    case "wrote":
                        if (PinKey.TryDecode(parts[1], out var key)) entry.Wrote.Add(key);
                        break;
                    case "crossed":
                        if (PinKey.TryDecode(parts[1], out var was)) entry.Crossed.Add(was);
                        break;
                    case "gone":
                        if (parts.Length < 7) break;
                        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)) break;
                        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) break;
                        if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) break;
                        if (!float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) break;
                        entry.Removed.Add(new Gone
                        {
                            Type = type,
                            Pos = new Vector3(x, y, z),
                            Checked = parts[5] == "1",
                            Name = parts[6],
                        });
                        break;
                }
            }
            return entry;
        }

        /// <summary>
        /// Put it back: the markers it wrote go, and the pins it removed return. Counted rather
        /// than assumed -- a marker already gone and a pin already back are both fine, and both
        /// are worth saying out loud, because they mean somebody has been working on this map
        /// since and the undo is not the whole story.
        /// </summary>
        internal static void Apply(Entry entry, out int removed, out int restored, out int uncrossed)
        {
            removed = 0;
            restored = 0;
            uncrossed = 0;
            if (entry == null) return;

            // The markers it crossed off go back to not being crossed off. These are markers that
            // were already on the map, so this is the one part of an undo that touches something
            // the tool did not write.
            foreach (var key in entry.Crossed)
            {
                if (!PersonalMap.Loaded || !PersonalMap.Store.Pins.ContainsKey(key)) continue;
                ClientPins.SetChecked(key, false);
                uncrossed++;
            }

            foreach (var key in entry.Wrote)
            {
                if (!PersonalMap.Loaded || !PersonalMap.Store.Pins.ContainsKey(key)) continue;
                // Without a tombstone, for the reason in the class note above.
                ClientPins.RemoveStale(key);
                removed++;
            }

            var map = Minimap.instance;
            if (map != null)
                foreach (var gone in entry.Removed)
                {
                    if (map.AddPin(gone.Pos, (Minimap.PinType)gone.Type, gone.Name ?? "", true, gone.Checked) == null) continue;
                    restored++;
                }
            if (removed > 0 || uncrossed > 0) ClientPins.Restyle();
        }

        /// <summary>Mark a record used, so the same run cannot be undone twice.</summary>
        internal static void Done(string path)
        {
            try { File.Move(path, path + ".done"); }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Undid {Path.GetFileName(path)} but could not set it aside: {e.Message}. " +
                    "Move or delete it by hand, or it will be offered again.");
            }
        }

        private static string Clean(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "run" : sb.ToString();
        }
    }
}
