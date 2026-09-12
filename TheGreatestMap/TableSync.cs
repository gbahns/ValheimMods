using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Reads and writes the cartography table automatically while the player is within reach,
    /// and on demand with the sync key. Uses the vanilla write path (which reads first), so ward
    /// access and the "map saved" feedback behave as in vanilla; the ClientPins patches keep
    /// player-placed markers out of the table data.
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
            Access.TableWrite(table, player);
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
            Access.TableWrite(table, player);
            return true;
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
