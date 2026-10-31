using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace TheGreatestMap
{
    /// <summary>
    /// A copy of the map as it was, taken the moment this mod first changes how a map is stored.
    ///
    /// Reading an old map and writing a new one is the one operation here that cannot be undone
    /// by playing on: the old shape is gone from disk as soon as the new one is saved over it. It
    /// has been got right so far, but "so far" is doing a lot of work in that sentence, and the
    /// person it goes wrong for is not the one who can read the code.
    ///
    /// So the map is copied aside first, once per format, before anything writes. Both maps are
    /// covered, and they are quite different problems:
    ///
    /// - The <b>shared map</b> is a file the server owns, so the copy is a copy of that file and
    ///   restoring it is putting it back.
    /// - A <b>personal map</b> lives inside the character file, compressed into a field Valheim
    ///   owns, where it cannot be copied as a file and would double the character's size if kept
    ///   beside itself. It is written out here as a file of its own, which <c>tgm_restore</c>
    ///   reads back.
    ///
    /// <para><b>What a backup can and cannot undo.</b> It holds markers. It does not hold the
    /// fact that an erasure has already travelled: a tombstone that reached the server has been
    /// seen by everyone, and putting an old copy back on one machine only means the next merge
    /// erases those markers again. Rolling a shared map back properly means restoring the
    /// server's copy too, with nobody connected. The file names say which is which.</para>
    /// </summary>
    internal static class MapBackup
    {
        /// <summary>Beside the maps themselves, so a player looking for one finds both together.</summary>
        internal static string Folder => Path.Combine(Paths.ConfigPath, "TheGreatestMap", "backups");

        /// <summary>
        /// Copy a map aside before it is written in a newer format. Does nothing if a backup of
        /// this map at this format already exists, so a map that is loaded and saved a hundred
        /// times leaves one file, not a hundred.
        /// </summary>
        /// <param name="label">What map this is, for the file name: a world, or a character and world.</param>
        /// <param name="fromVersion">The format it was read out of.</param>
        /// <param name="bytes">The map exactly as it was stored.</param>
        /// <returns>The path written, or null if there was nothing to do or it failed.</returns>
        internal static string Keep(string label, int fromVersion, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"{Clean(label)}.format{fromVersion}.pins.bin");
                // One per map per format. A second run has nothing to add: the file already holds
                // the last state before this version ever touched it, which is the thing worth
                // keeping. Overwriting it with a map this version has already been editing would
                // quietly turn the backup into a copy of the problem.
                if (File.Exists(path)) return path;
                File.WriteAllBytes(path, bytes);
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Kept a copy of this map as it was before version {TheGreatestMapMod.ModVersion} " +
                    $"changed how maps are stored: {path} ({bytes.Length:n0} bytes, format {fromVersion}). " +
                    "Nothing needs to be done with it; it is there in case something is wrong.");
                return path;
            }
            catch (Exception e)
            {
                // Never fatal. Failing to keep a copy is a worse map than we wanted, not a broken
                // one, and refusing to load because the copy failed would be the larger harm.
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not keep a copy of the map before converting it: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// A copy of the map as it is right now, before something writes a great deal of it at
        /// once. Always written and never over an earlier one, which is the difference between
        /// this and the copy above: that one is a once-ever record of what a map was before this
        /// version of the mod existed, and this one is "what it was a minute before I pressed
        /// the button". Both are put back with tgm_restore.
        /// </summary>
        internal static string Snapshot(string label, string why, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder,
                    $"{Clean(label)}.{Clean(why)}.{DateTime.Now:yyyyMMdd-HHmmss}.pins.bin");
                File.WriteAllBytes(path, bytes);
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Copied this map aside before {why}: {path} ({bytes.Length:n0} bytes).");
                return path;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not copy the map aside before {why}: {e.Message}");
                return null;
            }
        }

        /// <summary>Every backup on this machine, newest first.</summary>
        internal static List<string> All()
        {
            var found = new List<string>();
            try
            {
                if (!Directory.Exists(Folder)) return found;
                found.AddRange(Directory.GetFiles(Folder, "*.pins.bin"));
                found.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            }
            catch (Exception) { /* a listing nobody can read is an empty listing */ }
            return found;
        }

        private static string Clean(string s)
        {
            // The shared map's name already ends in ".pins", which would otherwise come out as
            // TestWorld.pins.format2.pins.bin.
            if (s != null && s.EndsWith(".pins", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 5);
            var bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "map" : sb.ToString();
        }
    }
}
