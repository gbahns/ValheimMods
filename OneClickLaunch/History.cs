using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace OneClickLaunch
{
    /// <summary>
    /// One remembered launch: a character together with the world it was started in, or the
    /// server it joined.
    /// </summary>
    public class Entry
    {
        public const string KindWorld  = "world";
        public const string KindServer = "server";

        public const string ServerDedicated = "Dedicated";
        public const string ServerSteam     = "Steam";
        public const string ServerPlayFab   = "PlayFab";

        public string kind;

        // The character. profileFile is the save file name, which is what the game keys on;
        // profileName is only for the button label. profileSource is a FileHelpers.FileSource.
        public string profileFile;
        public int    profileSource;
        public string profileName;

        // A local world, and how it was hosted.
        public string worldName;
        public int    worldSource;
        public bool   openServer;
        public bool   publicServer;
        public bool   crossplay;

        // A server: serverAddress is host:port for a dedicated server, the Steam id of the host
        // for a friend's game over Steam, or the PlayFab id of the host for a crossplay game.
        public string serverType;
        public string serverAddress;
        public string serverName;

        // The server password (or the password a hosted world was opened with), when
        // Remember Passwords is on and one was used.
        public string password;

        public long lastPlayedUtcTicks;

        public bool IsWorld  => kind == KindWorld;
        public bool IsServer => kind == KindServer;

        /// <summary>What makes two launches the same pair: same character and same destination.</summary>
        public string Key => IsWorld
            ? $"world|{profileFile}|{worldName}"
            : $"server|{profileFile}|{serverType}|{serverAddress}";

        public string Label()
        {
            string format = IsWorld ? OneClickLaunchMod.WorldLabel.Value : OneClickLaunchMod.ServerLabel.Value;
            string server = string.IsNullOrEmpty(serverName) ? History.DisplayAddress(serverAddress) : serverName;
            return format
                .Replace("{character}", profileName ?? profileFile ?? "?")
                .Replace("{world}", worldName ?? "?")
                .Replace("{server}", server ?? "?");
        }

        internal Dictionary<string, object> ToJson()
        {
            var o = new Dictionary<string, object>
            {
                ["kind"]          = kind,
                ["profileFile"]   = profileFile,
                ["profileSource"] = profileSource,
                ["profileName"]   = profileName,
            };
            if (IsWorld)
            {
                o["worldName"]    = worldName;
                o["worldSource"]  = worldSource;
                o["openServer"]   = openServer;
                o["publicServer"] = publicServer;
                o["crossplay"]    = crossplay;
            }
            else
            {
                o["serverType"]    = serverType;
                o["serverAddress"] = serverAddress;
                o["serverName"]    = serverName;
            }
            if (password != null) o["password"] = PasswordVault.Protect(password);
            o["lastPlayedUtcTicks"] = lastPlayedUtcTicks;
            return o;
        }

        internal static Entry FromJson(Dictionary<string, object> o)
        {
            var e = new Entry
            {
                kind               = MiniJson.GetString(o, "kind"),
                profileFile        = MiniJson.GetString(o, "profileFile"),
                profileSource      = (int)MiniJson.GetLong(o, "profileSource"),
                profileName        = MiniJson.GetString(o, "profileName"),
                worldName          = MiniJson.GetString(o, "worldName"),
                worldSource        = (int)MiniJson.GetLong(o, "worldSource"),
                openServer         = MiniJson.GetBool(o, "openServer"),
                publicServer       = MiniJson.GetBool(o, "publicServer"),
                crossplay          = MiniJson.GetBool(o, "crossplay"),
                serverType         = MiniJson.GetString(o, "serverType"),
                serverAddress      = MiniJson.GetString(o, "serverAddress"),
                serverName         = MiniJson.GetString(o, "serverName"),
                password           = PasswordVault.Unprotect(MiniJson.GetString(o, "password")),
                lastPlayedUtcTicks = MiniJson.GetLong(o, "lastPlayedUtcTicks"),
            };
            if (string.IsNullOrEmpty(e.kind) || string.IsNullOrEmpty(e.profileFile)) return null;
            if (e.IsWorld && string.IsNullOrEmpty(e.worldName)) return null;
            if (e.IsServer && (string.IsNullOrEmpty(e.serverType) || string.IsNullOrEmpty(e.serverAddress))) return null;
            return e;
        }
    }

    /// <summary>
    /// The remembered pairs, most recent first, persisted next to the BepInEx configs so the
    /// mod manager keeps it with the profile.
    /// </summary>
    internal static class History
    {
        private const int FileVersion = 1;

        private static int MaxEntries => OneClickLaunchMod.HistorySize.Value;

        internal static List<Entry> Entries { get; private set; } = new List<Entry>();

        private static string FilePath => Path.Combine(Paths.ConfigPath, "DeathMonger.OneClickLaunch.history.json");

        internal static void Load()
        {
            Entries = new List<Entry>();
            try
            {
                if (!File.Exists(FilePath)) return;
                if (MiniJson.Read(File.ReadAllText(FilePath)) is Dictionary<string, object> file
                    && file.TryGetValue("entries", out object list) && list is List<object> items)
                {
                    foreach (object item in items)
                    {
                        Entry e = item is Dictionary<string, object> o ? Entry.FromJson(o) : null;
                        if (e != null) Entries.Add(e);
                    }
                }
                // Passwords written plain by an earlier build, or before protection was
                // available: rewrite the file so they are protected from now on.
                bool changed = PasswordVault.SawUnprotected && PasswordVault.Available;
                // A History Size lowered since the last run.
                while (Entries.Count > MaxEntries)
                {
                    Entries.RemoveAt(Entries.Count - 1);
                    changed = true;
                }
                // Passwords the player has since decided not to keep.
                if (!OneClickLaunchMod.RememberPasswords.Value)
                {
                    foreach (var e in Entries)
                    {
                        if (e.password == null) continue;
                        e.password = null;
                        changed = true;
                    }
                }
                if (changed) Save();
            }
            catch (Exception ex)
            {
                OneClickLaunchMod.Log.LogWarning($"Could not read {FilePath}: {ex.Message}. Starting with an empty history.");
            }
        }

        internal static void Save()
        {
            try
            {
                var items = new List<object>();
                foreach (var e in Entries) items.Add(e.ToJson());
                var file = new Dictionary<string, object>
                {
                    ["version"] = FileVersion,
                    ["entries"] = items,
                };
                File.WriteAllText(FilePath, MiniJson.Write(file));
            }
            catch (Exception ex)
            {
                OneClickLaunchMod.Log.LogWarning($"Could not write {FilePath}: {ex.Message}");
            }
        }

        internal static Entry Find(string key)
        {
            foreach (var e in Entries)
                if (e.Key == key) return e;
            return null;
        }

        /// <summary>
        /// Puts a launch at the top of the list, replacing an earlier launch of the same pair.
        /// A server's name carries over from the earlier launch when the new one does not know
        /// it yet, and so does its password when carryPassword is set (a server's password is
        /// only learned later, when the server asks; a hosted world's is known at launch, so a
        /// world launched without one really has none).
        /// </summary>
        internal static void Record(Entry entry, bool carryPassword)
        {
            var previous = Find(entry.Key);
            if (previous != null)
            {
                if (string.IsNullOrEmpty(entry.serverName)) entry.serverName = previous.serverName;
                if (carryPassword && string.IsNullOrEmpty(entry.password)) entry.password = previous.password;
                Entries.Remove(previous);
            }
            entry.lastPlayedUtcTicks = DateTime.UtcNow.Ticks;
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
            Save();
        }

        internal static void Remove(Entry entry)
        {
            if (Entries.Remove(entry)) Save();
        }

        internal static void SetPassword(string key, string password)
        {
            var e = Find(key);
            if (e == null || e.password == password) return;
            e.password = password;
            Save();
        }

        /// <summary>
        /// A dedicated server's address as a button reads it: 127.0.0.1 is "localhost", and the
        /// port is left off unless the history knows the same host on more than one port, in
        /// which case every button for that host shows its port so they can be told apart.
        /// </summary>
        internal static string DisplayAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return address;
            SplitAddress(address, out string host, out string port);
            string shownHost = host == "127.0.0.1" || host == "::1" ? "localhost" : host;
            if (port == null) return shownHost;

            var ports = new HashSet<string>();
            foreach (var e in Entries)
            {
                if (!e.IsServer || e.serverType != Entry.ServerDedicated || string.IsNullOrEmpty(e.serverAddress)) continue;
                SplitAddress(e.serverAddress, out string otherHost, out string otherPort);
                if (otherHost == host && otherPort != null) ports.Add(otherPort);
            }
            return ports.Count > 1 ? shownHost + ":" + port : shownHost;
        }

        private static void SplitAddress(string address, out string host, out string port)
        {
            int colon = address.LastIndexOf(':');
            if (colon > 0 && colon < address.Length - 1 && int.TryParse(address.Substring(colon + 1), out _))
            {
                host = address.Substring(0, colon);
                port = address.Substring(colon + 1);
            }
            else
            {
                host = address;
                port = null;
            }
        }

        /// <summary>Whether this character has been started in this local world through this mod.</summary>
        internal static bool HasPlayed(string profileFile, string worldName)
        {
            foreach (var e in Entries)
                if (e.IsWorld && e.profileFile == profileFile && e.worldName == worldName) return true;
            return false;
        }
    }
}
