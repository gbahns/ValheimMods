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
                if (announceMissing) TheGreatestMapMod.Message("No cartography table within reach.");
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

            byte[] data = null;
            try
            {
                byte[] raw = nview.GetZDO().GetByteArray(ZDOVars.s_data);
                if (raw != null) data = Utils.Decompress(raw);
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read the cartography table: {e.Message}");
            }

            // Read: apply the table's exploration (and vanilla-shared pins) to our map.
            bool gotNew = data != null && map.AddSharedMapData(data);

            // Write only if the table lacks something we know; vanilla's path shows "map saved".
            bool haveNew = data == null || HasExplorationNotIn(map, data);
            if (haveNew) Access.TableWrite(table, player);

            if (gotNew) TheGreatestMapMod.Message(haveNew ? "Map exchanged with the cartography table." : "New map areas read from the cartography table.");
            else if (manual && !haveNew) TheGreatestMapMod.Message("Cartography table already up to date.");

            // Markers travel the same way: merge the personal map with the shared map on the server.
            SyncEngine.SyncWithServer(announceNothing: manual);
        }

        /// <summary>True if any area we have explored (ourselves or via other tables) is missing from this table's data.</summary>
        private static bool HasExplorationNotIn(Minimap map, byte[] data)
        {
            try
            {
                var pkg = new ZPackage(data);
                int version = pkg.ReadInt();
                var table = Access.ReadExploredArray(map, pkg, version);
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
