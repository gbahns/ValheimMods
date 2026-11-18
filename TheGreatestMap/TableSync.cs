using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Reads and writes the cartography table automatically while the player is within reach,
    /// and on demand with the sync key. Silent when there is nothing to exchange: the table's
    /// data is read and applied first (only new areas count), and vanilla's write path, with its
    /// "map saved" message and effect, runs only when we hold exploration the table lacks. The
    /// ClientPins patches keep player-placed markers out of the table data.
    /// </summary>
    internal static class TableSync
    {
        private static readonly Dictionary<int, float> _lastSync = new Dictionary<int, float>();

        /// <summary>
        /// What the last table we read held, so that reading it again costs almost nothing.
        ///
        /// <para>A table is read once a minute while somebody stands near it, and nearly every
        /// one of those reads finds nothing: the log says so itself, two hundred milliseconds of
        /// work ending in "nothing of ours to write". The two expensive halves are vanilla's --
        /// unpacking several megabytes and applying them, then parsing the same bytes a second
        /// time to compare -- and both of them are answering a question about bytes that have
        /// not changed since the last visit.</para>
        ///
        /// <para>So the compressed bytes are fingerprinted before they are unpacked, which is a
        /// walk over a megabyte rather than a decompression of four. Matching the last
        /// fingerprint means the table holds what it held, which means applying it again cannot
        /// uncover anything, and means the explored bits parsed out of it last time are still
        /// the right ones to compare against. What is left is our own half of the comparison,
        /// which has to run: we keep exploring, so what we have that it lacks changes even when
        /// it does not.</para>
        /// </summary>
        private static int _knownRaw;
        private static bool _knowRaw;
        private static List<bool> _knownExplored;
        private static readonly Collider[] _buffer = new Collider[64];
        private static int _pieceMask = -1;
        private static float _next;

        private static int PieceMask
        {
            get
            {
                if (_pieceMask < 0) _pieceMask = LayerMask.GetMask("piece", "piece_nonsolid");
                return _pieceMask;
            }
        }

        internal static void Reset()
        {
            _lastSync.Clear();
            _next = 0f;
            // A new world, or the same one loaded again: our own explored map starts empty, so
            // a table we have "already applied" has not been applied to this one.
            _knowRaw = false;
            _knownExplored = null;
        }

        internal static void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            bool keyPressed = Keys.IsDown(TgmConfig.SyncTableKey.Value) && Keys.CanTakeInput();
            if (keyPressed)
            {
                SyncNow(player, announceMissing: true);
                return;
            }
            if (!TgmConfig.AutoSyncTable.Value || Time.time < _next) return;
            _next = Time.time + 1f;

            var table = FindNearestTable(player);
            if (table == null) return;
            int id = table.GetInstanceID();
            if (_lastSync.TryGetValue(id, out float last) && Time.time - last < TgmConfig.TableSyncCooldown.Value) return;
            _lastSync[id] = Time.time;
            Sync(table, player, manual: false);
        }

        /// <summary>Sync the nearest table within reach immediately. Returns false when none is in reach.</summary>
        internal static bool SyncNow(Player player, bool announceMissing)
        {
            var table = FindNearestTable(player);
            if (table == null)
            {
                if (announceMissing)
                    TheGreatestMapMod.Message(
                        $"No cartography table within {TgmConfig.TableSyncRadius.Value:0.#} m. tgm_resync merges without one.");
                return false;
            }
            _lastSync[table.GetInstanceID()] = Time.time;
            Sync(table, player, manual: true);
            return true;
        }

        private static void Sync(MapTable table, Player player, bool manual)
        {
            var map = Minimap.instance;
            if (map == null) return;
            var nview = Access.TableView(table);
            if (nview == null || !nview.IsValid()) return;
            if (!PrivateArea.CheckAccess(table.transform.position, 0f, flash: manual))
            {
                if (manual) TheGreatestMapMod.Message("No access to this cartography table.");
                return;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            byte[] raw = null;
            try { raw = nview.GetZDO().GetByteArray(ZDOVars.s_data); }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read the cartography table: {e.Message}");
            }

            // The fingerprint is taken of the packed bytes, before anything unpacks them, which
            // is the whole point: it is the one question about this table that can be answered
            // without paying for the answer.
            int mark = Mark(raw);
            bool known = raw != null && _knowRaw && mark == _knownRaw;
            long markMs = clock.ElapsedMilliseconds;

            byte[] data = null;
            if (raw != null && !known)
            {
                try { data = Utils.Decompress(raw); }
                catch (Exception e)
                {
                    TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not unpack the cartography table: {e.Message}");
                }
            }

            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] {System.DateTime.Now:HH:mm:ss} cartography table: starting " +
                $"({(raw == null ? "the table is empty"
                    : known ? (raw.Length / 1024f).ToString("0.#") + " KB on it, unchanged since last time"
                    : data == null ? "unreadable"
                    : (data.Length / 1024f).ToString("0.#") + " KB on it")}, " +
                $"{ClientPins.Count} markers on our map).");

            // Read: apply the table's exploration (and vanilla-shared pins) to our map. Skipped
            // when the bytes are the ones we applied last time, which can only uncover what it
            // already uncovered.
            bool gotNew = data != null && map.AddSharedMapData(data);
            long readMs = clock.ElapsedMilliseconds - markMs;

            // Write only if the table lacks something we know; vanilla's path shows "map saved".
            bool haveNew = raw == null || HasExplorationNotIn(map, data, known);
            long compareMs = clock.ElapsedMilliseconds - readMs - markMs;
            if (haveNew) Access.TableWrite(table, player);
            long writeMs = clock.ElapsedMilliseconds - readMs - compareMs - markMs;
            // Only once the write is done, because a write changes the table and the bytes we
            // fingerprinted are no longer what is on it.
            if (raw != null && !haveNew) { _knownRaw = mark; _knowRaw = true; }
            else _knowRaw = false;
            // The table's own work is all vanilla's: reading its explored area into ours, working
            // out whether ours has anything it lacks, and writing ours back. None of it said
            // anything, so a table visit that stopped the game for fifteen seconds left no trace.
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] {System.DateTime.Now:HH:mm:ss} cartography table: its own work done in " +
                $"{clock.ElapsedMilliseconds} ms (fingerprinting {markMs} ms, reading its map {readMs} ms, " +
                $"comparing {compareMs} ms, writing ours back {writeMs} ms)" +
                $"{(haveNew ? "" : ", nothing of ours to write")}. Asking the server next.");

            if (gotNew) TheGreatestMapMod.Message(haveNew ? "Map exchanged with the cartography table." : "New map areas read from the cartography table.");
            else if (manual && !haveNew) TheGreatestMapMod.Message("Cartography table already up to date.");

            // Markers travel the same way: merge the personal map with the shared map on the server.
            SyncEngine.SyncWithServer(announceNothing: manual, asked: manual);
        }

        /// <summary>
        /// A number standing for the table's packed bytes, which is enough to say whether it
        /// holds what it held. Length first, so two different lengths can never agree.
        /// </summary>
        private static int Mark(byte[] raw)
        {
            if (raw == null) return 0;
            unchecked
            {
                int hash = (int)2166136261u ^ raw.Length;
                for (int i = 0; i < raw.Length; i++) hash = (hash ^ raw[i]) * 16777619;
                return hash;
            }
        }

        /// <summary>
        /// True if any area we have explored (ourselves or via other tables) is missing from this
        /// table's data.
        ///
        /// <para><paramref name="known"/> says the table's bytes are the ones parsed last time,
        /// so the explored bits kept from then still describe it. Parsing is the expensive half
        /// of this -- several million bits read back out of a package -- and the comparison
        /// against our own map still runs either way, because our map is what keeps changing.
        /// </para>
        /// </summary>
        private static bool HasExplorationNotIn(Minimap map, byte[] data, bool known)
        {
            try
            {
                List<bool> table;
                if (known && _knownExplored != null) table = _knownExplored;
                else
                {
                    if (data == null) return true;
                    var pkg = new ZPackage(data);
                    int version = pkg.ReadInt();
                    table = Access.ReadExploredArray(map, pkg, version);
                    _knownExplored = table;
                }
                if (table == null) return true;
                var mine = Access.Explored(map);
                var others = Access.ExploredOthers(map);
                int n = Math.Min(table.Count, mine.Length);
                for (int i = 0; i < n; i++)
                {
                    if (!table[i] && (mine[i] || others[i])) return true;
                }
                return mine.Length > table.Count;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not compare with the cartography table, writing anyway: {e.Message}");
                return true;
            }
        }

        private static MapTable FindNearestTable(Player player)
        {
            Vector3 here = player.transform.position;
            int n = Physics.OverlapSphereNonAlloc(here, TgmConfig.TableSyncRadius.Value, _buffer, PieceMask);
            MapTable best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var table = _buffer[i] != null ? _buffer[i].GetComponentInParent<MapTable>() : null;
                if (table == null) continue;
                float d = Vector3.Distance(here, table.transform.position);
                if (d < bestDistance) { bestDistance = d; best = table; }
            }
            return best;
        }
    }
}
