using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpreadTheLoad
{
    /// <summary>
    /// Works out which connected players the admin meant.
    ///
    /// A character name is the obvious thing to type and the wrong thing to match on: it is not
    /// an identity. The same person on a second character stops matching, a renamed character
    /// stops matching, and somebody else who picks the same name starts matching. The stable
    /// identity is the network id behind the socket - the Steam id on a Steam server - which the
    /// admin has no easy way to look up and would not want to type anyway.
    ///
    /// So a name is treated as a way of *finding* someone once, not as the rule. The first time a
    /// configured name matches a connected player, that player's network id is written down and
    /// used from then on, and the name becomes irrelevant. Names are the door; ids are the lock.
    ///
    /// The learned ids live in a plain text file beside the config, one per line with the name
    /// that found them as a comment, so an admin can read it, correct it, or delete a line that
    /// was learned from the wrong person.
    /// </summary>
    internal static class PlayerIds
    {
        /// <summary>Ids the admin typed directly, plus every id learned from a name.</summary>
        private static readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Names to watch for, until each one has taught us an id.</summary>
        private static readonly List<string> _names = new List<string>();

        /// <summary>Learned id -> the name it was learned from, for the file and the log.</summary>
        private static readonly Dictionary<string, string> _learned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string _lastRaw = null;
        private static bool _loaded;
        private static bool _dirty;

        /// <summary>
        /// A network id is whatever the socket calls the far end. On Steam that is the 17-digit
        /// Steam id the server already prints as "Got connection SteamID ...". It is treated as an
        /// opaque string rather than parsed, so a crossplay or PlayFab server - where the value is
        /// not a Steam id at all - still gets a stable identity rather than nothing.
        /// </summary>
        private static bool LooksLikeId(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 6) return false;
            foreach (char c in token) if (c < '0' || c > '9') return false;
            return true;
        }

        internal static void Configure(string raw)
        {
            EnsureLoaded();
            if (raw == _lastRaw) return;
            _lastRaw = raw;

            // Rebuild from the configured text plus whatever has been learned. Learned ids survive
            // a config edit; the file is where you go to forget one.
            _ids.Clear();
            _names.Clear();
            foreach (var id in _learned.Keys) _ids.Add(id);

            if (string.IsNullOrEmpty(raw)) return;
            foreach (var part in raw.Split(','))
            {
                string t = part.Trim();
                if (t.Length == 0) continue;
                if (LooksLikeId(t)) _ids.Add(t);
                else _names.Add(t);
            }
        }

        /// <summary>True if this peer is one the admin asked to steer work away from.</summary>
        internal static bool Matches(long uid, string playerName, string networkId)
        {
            if (!string.IsNullOrEmpty(networkId) && _ids.Contains(networkId)) return true;

            if (_names.Count == 0 || string.IsNullOrEmpty(playerName)) return false;
            string name = playerName.Trim();
            for (int i = 0; i < _names.Count; i++)
            {
                if (!string.Equals(_names[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                Learn(networkId, name);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Remember the id behind a name the first time we see them, so the setting keeps working
        /// when they next log in on a different character.
        /// </summary>
        private static void Learn(string networkId, string name)
        {
            if (!SpreadTheLoadMod.RememberIds.Value) return;
            if (string.IsNullOrEmpty(networkId)) return;
            if (_learned.ContainsKey(networkId)) return;

            _learned[networkId] = name;
            _ids.Add(networkId);
            _dirty = true;
            SpreadTheLoadMod.Log.LogInfo(
                $"[SpreadTheLoad] learned {networkId} from the name \"{name}\"; that player will now be " +
                $"matched by id whatever they call their character. Remove the line from {FileName} to forget it.");
            Save();
        }

        private const string FileName = "SpreadTheLoad-known-ids.txt";

        private static string Path()
        {
            try { return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, FileName); }
            catch { return null; }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string p = Path();
                if (p == null || !File.Exists(p)) return;
                foreach (var line in File.ReadAllLines(p))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s[0] == '#') continue;
                    // "<id> # <name>" - the name is a comment, the id is the data.
                    int hash = s.IndexOf('#');
                    string id = (hash >= 0 ? s.Substring(0, hash) : s).Trim();
                    string name = hash >= 0 ? s.Substring(hash + 1).Trim() : "";
                    if (id.Length > 0) _learned[id] = name;
                }
                if (_learned.Count > 0)
                    SpreadTheLoadMod.Log.LogInfo($"[SpreadTheLoad] remembered {_learned.Count} player id(s) from {FileName}.");
            }
            catch (Exception e)
            {
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] could not read {FileName}: {e.Message}");
            }
        }

        private static void Save()
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                string p = Path();
                if (p == null) return;
                var sb = new StringBuilder();
                sb.AppendLine("# Players this mod steers object ownership away from, by network id.");
                sb.AppendLine("# Learned automatically the first time a name in the \"Yield Players\" setting matched");
                sb.AppendLine("# somebody connected. The name after the # is only a reminder of who it was.");
                sb.AppendLine("# Delete a line to forget that player. One id per line.");
                foreach (var kv in _learned) sb.AppendLine($"{kv.Key}  # {kv.Value}");
                File.WriteAllText(p, sb.ToString());
            }
            catch (Exception e)
            {
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] could not write {FileName}: {e.Message}");
            }
        }

        /// <summary>Nothing configured and nothing learned means the mod has no work to do.</summary>
        internal static bool Empty => _ids.Count == 0 && _names.Count == 0;
    }
}
