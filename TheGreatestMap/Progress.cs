using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace TheGreatestMap
{
    /// <summary>
    /// What this mod has already done once, kept in its own file rather than among the settings.
    ///
    /// <para>None of this is a setting. It is a list of one-time jobs -- catalog lines corrected,
    /// types cut or moved to another kind, the lines put into the order a version ships them in,
    /// which maps have had the catch-up passes run on them -- written down so each is done once
    /// and never again. It lived in the config file because that was the one place a mod could
    /// keep a string, and a mod manager cannot tell the difference: it showed six boxes with a
    /// blank default and a Default button beside them, under a heading a player had every reason
    /// to open. One did, found them "refilling by themselves", and asked what was wrong. Nothing
    /// was, but the answer took a paragraph and the next player will ask again.</para>
    ///
    /// <para>So it moves next to the map stores, the backups and the undo files, in the folder
    /// this mod already keeps for things that are its own business. A player who opens it finds
    /// plain words and a line saying not to edit it.</para>
    ///
    /// <para><b>Losing this is not harmless</b>, which is why the carry-over below refuses to
    /// tidy the config until it has read its own file back and found everything in it. An empty
    /// record does not mean no work was done, it means the work gets done again: every catch-up
    /// pass runs on every map at the next login, and lines a player has since edited themselves
    /// are left alone by the corrections but still cost the pass. Better to leave the old entries
    /// sitting in the config for another day than to drop them on the floor.</para>
    /// </summary>
    internal static class Progress
    {
        private const string FileName = "progress.txt";

        private static readonly string[] Keys =
        {
            "CatalogEntriesFixed", "CatalogMovesMade", "CatalogCutsMade",
            "CatalogIconsFixed", "CatalogOrdered", "MapsTidied",
        };

        /// <summary>What each was called while it lived in the config file, for the carry-over.</summary>
        private static readonly string[] WasCalled =
        {
            "Catalog Entries Fixed", "Catalog Moves Made", "Catalog Cuts Made",
            "Catalog Icons Fixed", "Catalog Ordered", "Maps Tidied",
        };

        private static readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        private static bool _loaded;

        internal static string Folder => Path.Combine(Paths.ConfigPath, "TheGreatestMap");
        private static string FilePath => Path.Combine(Folder, FileName);

        internal static string CatalogEntriesFixed { get { return Get("CatalogEntriesFixed"); } set { Set("CatalogEntriesFixed", value); } }
        internal static string CatalogMovesMade { get { return Get("CatalogMovesMade"); } set { Set("CatalogMovesMade", value); } }
        internal static string CatalogCutsMade { get { return Get("CatalogCutsMade"); } set { Set("CatalogCutsMade", value); } }
        internal static string CatalogIconsFixed { get { return Get("CatalogIconsFixed"); } set { Set("CatalogIconsFixed", value); } }
        internal static string CatalogOrdered { get { return Get("CatalogOrdered"); } set { Set("CatalogOrdered", value); } }
        internal static string MapsTidied { get { return Get("MapsTidied"); } set { Set("MapsTidied", value); } }

        private static string Get(string key)
        {
            string value;
            return _loaded && _values.TryGetValue(key, out value) ? value : "";
        }

        private static void Set(string key, string value)
        {
            _values[key] = value ?? "";
            Write();
        }

        /// <summary>
        /// Read the file, and the first time there is none, carry the six old settings across and
        /// take them out of the config so nobody is shown them as settings again.
        /// </summary>
        internal static void Load(ConfigFile config)
        {
            _loaded = true;
            _values.Clear();
            bool had = Read();
            // Looked for in the file's own text rather than asked of the config, because an entry
            // nothing has bound is orphaned: it is not among the settings to be asked about, and
            // it is written back out at every save. Reading the text is also what keeps this from
            // binding and saving on every launch -- which would rewrite the config file each time
            // and set the watcher off for nothing.
            if (!NeedsTidying(config)) return;

            // Bound only to be read and then taken out: a value still in the config file is the
            // record, and binding is the only way to ask for it by name.
            var carried = new List<ConfigDefinition>();
            var found = new Dictionary<string, string>();
            for (int i = 0; i < Keys.Length; i++)
            {
                var definition = new ConfigDefinition("Internal", WasCalled[i]);
                var entry = config.Bind(definition, "", new ConfigDescription("Moved out of the settings; see progress.txt."));
                found[Keys[i]] = entry.Value ?? "";
                carried.Add(definition);
            }

            if (!had)
            {
                foreach (var kv in found) _values[kv.Key] = kv.Value;
                Write();
            }
            if (!had && !ReadsBack())
            {
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Could not write {FilePath}, so the record of what this mod has already done " +
                    "stays in the settings for now. Nothing is lost and nothing will be done twice.");
                return;
            }

            foreach (var definition in carried) config.Remove(definition);
            config.Save();
            if (had) return;
            TheGreatestMapMod.Log.LogInfo(
                "[TheGreatestMap] The record of one-time changes already made has moved out of the settings and into " +
                $"{FilePath}. It was never a setting -- six boxes with a blank default that filled themselves in -- " +
                "and the Internal section is gone.");
        }

        /// <summary>Is there still an Internal section in the config file to carry over and clear out?</summary>
        private static bool NeedsTidying(ConfigFile config)
        {
            try
            {
                if (config == null || !File.Exists(config.ConfigFilePath)) return false;
                foreach (var line in File.ReadAllLines(config.ConfigFilePath))
                    if (line.Trim() == "[Internal]") return true;
                return false;
            }
            catch (Exception) { return false; }
        }

        private static bool Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    string one = line.Trim();
                    if (one.Length == 0 || one[0] == '#') continue;
                    // The first equals only: several of these values are full of them.
                    int eq = one.IndexOf('=');
                    if (eq <= 0) continue;
                    _values[one.Substring(0, eq).Trim()] = one.Substring(eq + 1);
                }
                return true;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read {FilePath}: {e.Message}");
                return false;
            }
        }

        /// <summary>Everything we meant to write is in the file. The one test worth doing here.</summary>
        private static bool ReadsBack()
        {
            var wanted = new Dictionary<string, string>(_values);
            _values.Clear();
            bool read = Read();
            foreach (var kv in wanted)
            {
                string got;
                if (!_values.TryGetValue(kv.Key, out got) || !string.Equals(got, kv.Value, StringComparison.Ordinal))
                {
                    foreach (var put in wanted) _values[put.Key] = put.Value;
                    return false;
                }
            }
            return read;
        }

        private static void Write()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                sb.AppendLine("# What The Greatest Map has already done once, so it does not do it again.");
                sb.AppendLine("# Written by the mod. Nothing here is a setting; the settings are in the config file.");
                sb.AppendLine("# Emptying a line makes that piece of work run again at the next login.");
                sb.AppendLine();
                for (int i = 0; i < Keys.Length; i++)
                {
                    string value;
                    sb.Append(Keys[i]).Append('=')
                      .AppendLine(_values.TryGetValue(Keys[i], out value) ? value : "");
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not write {FilePath}: {e.Message}");
            }
        }
    }
}
