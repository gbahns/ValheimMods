using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>One species' line in the world's ledger.</summary>
    internal sealed class WorldKillRow
    {
        internal string Key = "";

        /// <summary>Deaths of a wild one that at least one player had a hand in.</summary>
        internal int Kills;

        /// <summary>Deaths of somebody's pet, kept apart: killing your own boar is not a trophy.</summary>
        internal int Tame;

        /// <summary>Of <see cref="Kills"/>, the ones two or more players had a hand in.</summary>
        internal int Shared;

        /// <summary>Of <see cref="Kills"/>, the ones carrying at least one star.</summary>
        internal int Starred;

        /// <summary>The most stars anything of this species has died with. 1 means plain.</summary>
        internal int BestLevel = 1;

        /// <summary>When the last one died, for sorting and for saying how fresh a row is.</summary>
        internal long LastUtc;

        internal int Total => Kills + Tame;
    }

    /// <summary>
    /// How many of each creature this world has lost, counted once however many people swung at it.
    ///
    /// <para>The thing worth writing down is that the obvious way is wrong. Valheim credits a kill
    /// to <em>every</em> player who damaged the creature -- Character.OnDeath walks the player list
    /// and calls RegisterKill for each one whose attacker flag is set -- so three people who bring
    /// down a troll together each get a troll, and adding up the scoreboard says the world lost
    /// three of them. No amount of comparing those tallies afterwards recovers the real number: the
    /// overlap is exactly what was thrown away.</para>
    ///
    /// <para>So this counts the death instead of the credits. The same OnDeath has an owner guard
    /// immediately above the crediting block, and everything past it runs on precisely one machine,
    /// which is where <see cref="KillWatch"/> already stands. One death, one report, no overlap to
    /// undo -- not a de-duplication pass that might miss one, but a different thing counted.</para>
    ///
    /// <para>The owner can be anybody's game, so the reports go to the server and the ledger is kept
    /// there, one file per world beside the stats store rather than inside it: that file's version
    /// check discards what it cannot read, and a world tally has no business putting everyone's
    /// stored stats at risk. A server without this mod never answers, the same way it never answers
    /// for offline players, and the panel says so rather than showing a zero.</para>
    ///
    /// <para>It starts at nothing. Valheim records nowhere what a world has killed, so the count
    /// runs from the day it was installed and the panel dates it. What the history can still be
    /// squeezed for is a range, and that lives in <see cref="WorldEstimate"/>.</para>
    /// </summary>
    internal static class WorldKills
    {
        /// <summary>Bumped when the file's shape changes. An older file is kept aside, not guessed at.</summary>
        private const int FileSchema = 1;

        /// <summary>
        /// A ceiling on the species table, which is the only part that can grow. Vanilla has well
        /// under two hundred creatures and a heavily modded world is nowhere near this, so it is
        /// here to bound a stream of junk keys rather than to turn anybody's game away.
        /// </summary>
        internal const int MaxSpecies = 500;

        private const float SaveDebounce = 10f;
        private const float SaveDeadline = 60f;
        private const float SaveRetry = 30f;

        /// <summary>
        /// How long a reporting client holds kills before sending them. A busy fight kills a dozen
        /// things in a few seconds and each one is a handful of bytes, so they travel together;
        /// nothing here is wanted in a hurry.
        /// </summary>
        private const float FlushSeconds = 3f;

        /// <summary>A bound on the outbox, for a client that somehow cannot send for a long time.</summary>
        private const int MaxOutbox = 512;

        private static readonly Dictionary<string, WorldKillRow> _rows = new Dictionary<string, WorldKillRow>(StringComparer.Ordinal);
        private static string _path;
        private static bool _loaded;
        private static bool _dirty;
        private static float _saveAt;
        private static float _saveBy;
        private static long _startedUtc;
        private static bool _full;
        private static bool _warnedFull;

        /// <summary>Set once a client has waited long enough for a server that keeps no ledger.</summary>
        private static bool _gaveUp;

        /// <summary>Kills seen here but not yet sent on, for a client that does not own the ledger.</summary>
        private static readonly List<WorldKillRow> _outbox = new List<WorldKillRow>();
        private static float _flushAt;

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();
        internal static bool Loaded => _loaded;
        internal static string Path => _path;
        internal static int Species => _rows.Count;
        internal static bool Full => _full;

        /// <summary>When this world's ledger started counting, which is not when the world started.</summary>
        internal static long StartedUtc => _startedUtc;

        internal static bool Recording =>
            DwamsConfig.RecordWorldKills == null || DwamsConfig.RecordWorldKills.Value;

        // ── lifetime ────────────────────────────────────────────────────────────────

        internal static void Load()
        {
            Unload();
            if (!IsServer || !Recording) return;

            _path = BuildPath();
            if (_path == null) return;
            _loaded = true;
            _startedUtc = Snapshot.NowUtc();
            try
            {
                if (File.Exists(_path))
                {
                    FromBytes(File.ReadAllBytes(_path));
                    DudeWhatAreMyStatsMod.Log.LogInfo(
                        $"[DudeWhatAreMyStats] World kills: {_rows.Count} species, {TotalKills()} kill(s), from {_path}");
                }
                else
                {
                    DudeWhatAreMyStatsMod.Log.LogInfo(
                        $"[DudeWhatAreMyStats] No world kill ledger yet ({_path}); counting from now.");
                    MarkDirty();
                }
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogError($"[DudeWhatAreMyStats] Could not read {_path}: {e}");
                Preserve("corrupt");
                _rows.Clear();
            }
        }

        internal static void Unload()
        {
            SaveIfDirty();
            _rows.Clear();
            _outbox.Clear();
            _path = null;
            _loaded = false;
            _dirty = false;
            _full = false;
            _warnedFull = false;
            _gaveUp = false;
            _startedUtc = 0L;
        }

        /// <summary>Sends what we have seen, and writes the ledger once things go quiet.</summary>
        internal static void Update()
        {
            float now = Time.unscaledTime;
            if (_outbox.Count > 0 && now >= _flushAt) Flush();
            if (!_loaded || !_dirty) return;
            if (now >= _saveAt || now >= _saveBy) Save();
        }

        internal static void SaveIfDirty()
        {
            if (_loaded && _dirty) Save();
        }

        private static void MarkDirty()
        {
            float now = Time.unscaledTime;
            if (!_dirty) _saveBy = now + SaveDeadline;   // set by the first change, not the last
            _dirty = true;
            _saveAt = now + SaveDebounce;
        }

        // ── seeing one ──────────────────────────────────────────────────────────────

        /// <summary>
        /// One creature died here. Called from the machine that owned it and nowhere else, so this
        /// is the only time this death will ever be counted.
        /// </summary>
        /// <param name="attackers">How many players had damaged it, from the game's own count.</param>
        internal static void Report(string key, bool tame, int level, int attackers)
        {
            if (!Recording || string.IsNullOrEmpty(key)) return;
            var row = new WorldKillRow
            {
                Key = key,
                Kills = tame ? 0 : 1,
                Tame = tame ? 1 : 0,
                Shared = !tame && attackers >= 2 ? 1 : 0,
                Starred = !tame && level >= 2 ? 1 : 0,
                BestLevel = Mathf.Max(1, level),
                LastUtc = Snapshot.NowUtc(),
            };
            // The host of a listen server is both halves of this, and there the ledger is written
            // to on the spot rather than sent to itself.
            if (IsServer) { Take(row); return; }
            if (_gaveUp) return;
            if (_outbox.Count == 0) _flushAt = Time.unscaledTime + FlushSeconds;
            if (_outbox.Count < MaxOutbox) _outbox.Add(row);
        }

        /// <summary>
        /// Sends what we have seen, once there is somewhere for it to go.
        ///
        /// Nothing leaves until the server has shown it keeps a ledger, because a routed RPC under
        /// a name the receiver never registered is a line in <em>its</em> log, and a fight would
        /// write one every few seconds. The ask that settles the question runs on its own timer
        /// from the moment we join, so the wait is twenty seconds rather than until somebody opens
        /// the panel; kills made meanwhile are held, and dropped if the answer never comes.
        /// </summary>
        private static void Flush()
        {
            if (!WorldView.Answered)
            {
                if (_outbox.Count < MaxOutbox)
                {
                    _flushAt = Time.unscaledTime + FlushSeconds;
                    return;
                }
                _outbox.Clear();
                if (_gaveUp) return;
                _gaveUp = true;
                DudeWhatAreMyStatsMod.Log.LogInfo(
                    "[DudeWhatAreMyStats] The server keeps no world kill ledger, so kills seen here are not " +
                    "being counted towards one. Everything else is unaffected.");
                return;
            }
            var batch = new List<WorldKillRow>(_outbox);
            _outbox.Clear();
            StatsNetwork.SendWorldKills(batch);
        }

        /// <summary>The server has answered, so there is a ledger after all. Start reporting again.</summary>
        internal static void NoteAnswered()
        {
            _gaveUp = false;
        }

        /// <summary>Folds one report into the ledger. Server side; the one place a row grows.</summary>
        internal static void Take(WorldKillRow report)
        {
            if (!_loaded || report == null || string.IsNullOrEmpty(report.Key)) return;
            if (!_rows.TryGetValue(report.Key, out var row))
            {
                if (_rows.Count >= MaxSpecies)
                {
                    _full = true;
                    if (!_warnedFull)
                    {
                        _warnedFull = true;
                        DudeWhatAreMyStatsMod.Log.LogWarning(
                            $"[DudeWhatAreMyStats] The world kill ledger is holding {MaxSpecies} species, which is " +
                            "more than any world should have; new ones are being ignored.");
                    }
                    return;
                }
                _rows[report.Key] = row = new WorldKillRow { Key = report.Key };
            }
            row.Kills += report.Kills;
            row.Tame += report.Tame;
            row.Shared += report.Shared;
            row.Starred += report.Starred;
            if (report.BestLevel > row.BestLevel) row.BestLevel = report.BestLevel;
            // Stamped here rather than taken from the report, so every age in the ledger is
            // measured by one clock however many machines fed it. The same rule the stats store
            // keeps for its own arrival times.
            row.LastUtc = Snapshot.NowUtc();
            MarkDirty();
        }

        // ── reading it ──────────────────────────────────────────────────────────────

        /// <summary>Every species, the most killed first.</summary>
        internal static List<WorldKillRow> All()
        {
            var list = new List<WorldKillRow>(_rows.Values);
            Sort(list);
            return list;
        }

        internal static void Sort(List<WorldKillRow> list)
        {
            list.Sort((a, b) =>
            {
                int byCount = b.Total.CompareTo(a.Total);
                return byCount != 0 ? byCount : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
        }

        internal static int TotalKills()
        {
            int n = 0;
            foreach (var row in _rows.Values) n += row.Kills;
            return n;
        }

        // ── the wire, which is also the file ────────────────────────────────────────

        /// <summary>
        /// Writes the rows into a package. Shared by the file and the message on purpose: the two
        /// hold the same thing, and one shape means one place for a later field to be added.
        /// </summary>
        internal static void Write(ZPackage pkg, List<WorldKillRow> rows, long startedUtc)
        {
            pkg.Write(startedUtc);
            pkg.Write(rows.Count);
            foreach (var row in rows)
            {
                pkg.Write(row.Key ?? "");
                pkg.Write(row.Kills);
                pkg.Write(row.Tame);
                pkg.Write(row.Shared);
                pkg.Write(row.Starred);
                pkg.Write(row.BestLevel);
                pkg.Write(row.LastUtc);
            }
        }

        /// <summary>
        /// Reads rows back. A package from a later build is readable as long as it only ever
        /// appended fields past this point, which is the contract every other message here keeps.
        /// </summary>
        internal static List<WorldKillRow> Read(ZPackage pkg, out long startedUtc)
        {
            startedUtc = pkg.ReadLong();
            int count = pkg.ReadInt();
            var rows = new List<WorldKillRow>(Mathf.Clamp(count, 0, MaxSpecies));
            for (int i = 0; i < count; i++)
            {
                var row = new WorldKillRow
                {
                    Key = pkg.ReadString(),
                    Kills = pkg.ReadInt(),
                    Tame = pkg.ReadInt(),
                    Shared = pkg.ReadInt(),
                    Starred = pkg.ReadInt(),
                    BestLevel = pkg.ReadInt(),
                    LastUtc = pkg.ReadLong(),
                };
                if (!string.IsNullOrEmpty(row.Key) && rows.Count < MaxSpecies) rows.Add(row);
            }
            return rows;
        }

        internal static void Save()
        {
            if (!_loaded || _path == null) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                var pkg = new ZPackage();
                pkg.Write(FileSchema);
                Write(pkg, All(), _startedUtc);
                string tmp = _path + ".tmp";
                File.WriteAllBytes(tmp, pkg.GetArray());
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                _dirty = false;
            }
            catch (Exception e)
            {
                // Back off rather than retrying next frame, so a directory nobody can write to
                // costs one line a half-minute instead of a stack trace every frame.
                float now = Time.unscaledTime;
                _saveAt = now + SaveRetry;
                _saveBy = now + SaveRetry;
                DudeWhatAreMyStatsMod.Log.LogError(
                    $"[DudeWhatAreMyStats] Could not save {_path}; trying again in {SaveRetry:0}s: {e.Message}");
            }
        }

        private static void FromBytes(byte[] bytes)
        {
            _rows.Clear();
            if (bytes == null || bytes.Length == 0) return;
            var pkg = new ZPackage(bytes);
            int schema = pkg.ReadInt();
            if (schema != FileSchema)
            {
                Preserve("fileschema");
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    $"[DudeWhatAreMyStats] The world kill ledger is file version {schema} and this build reads " +
                    $"{FileSchema}. The old file has been kept alongside; counting again from now.");
                return;
            }
            var rows = Read(pkg, out long started);
            if (started > 0L) _startedUtc = started;
            foreach (var row in rows) _rows[row.Key] = row;
        }

        private static void Preserve(string why)
        {
            try { File.Copy(_path, _path + "." + why + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); }
            catch { /* best effort */ }
        }

        private static string BuildPath()
        {
            var world = ZNet.World;
            if (world == null) return null;
            return System.IO.Path.Combine(Paths.ConfigPath, "DudeWhatAreMyStats",
                Sanitize(world.m_name) + "_" + world.m_seed + ".world.bin");
        }

        private static string Sanitize(string s)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "world" : sb.ToString();
        }
    }

    /// <summary>
    /// What the server last told this client about the world's kills.
    ///
    /// Kept apart from the ledger so that the host of a listen server, who is both ends of this,
    /// still reads the same copy over the same path as anybody else: one way for the panel to be
    /// wrong is better than two.
    /// </summary>
    internal static class WorldView
    {
        private static List<WorldKillRow> _rows = new List<WorldKillRow>();

        internal static bool Answered { get; private set; }
        internal static long StartedUtc { get; private set; }
        internal static float ReceivedAt { get; private set; }
        internal static List<WorldKillRow> Rows => _rows;

        internal static void Accept(List<WorldKillRow> rows, long startedUtc)
        {
            _rows = rows ?? new List<WorldKillRow>();
            WorldKills.Sort(_rows);
            StartedUtc = startedUtc;
            Answered = true;
            ReceivedAt = Time.unscaledTime;
            WorldKills.NoteAnswered();
        }

        internal static void Clear()
        {
            _rows = new List<WorldKillRow>();
            Answered = false;
            StartedUtc = 0L;
            ReceivedAt = 0f;
        }

        internal static int TotalKills()
        {
            int n = 0;
            foreach (var row in _rows) n += row.Kills;
            return n;
        }

        internal static int TotalTame()
        {
            int n = 0;
            foreach (var row in _rows) n += row.Tame;
            return n;
        }

        internal static int TotalShared()
        {
            int n = 0;
            foreach (var row in _rows) n += row.Shared;
            return n;
        }

        internal static int TotalStarred()
        {
            int n = 0;
            foreach (var row in _rows) n += row.Starred;
            return n;
        }
    }
}
