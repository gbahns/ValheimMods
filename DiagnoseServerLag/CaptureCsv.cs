using System.Globalization;

namespace DiagnoseServerLag
{
    /// <summary>
    /// The one column layout every capture is written in.
    ///
    /// There used to be two, and nobody noticed until it cost three captures in one evening. The
    /// group capture wrote forty columns; the local one stopped at twenty-nine, because every field
    /// added while chasing a problem - the feed interval, whether the second was spent loading,
    /// machine-wide CPU, free memory, objects by owner, built pieces - was added to the group writer
    /// and not to its twin. So `dsl_bench` on a client produced a file that was missing precisely
    /// the measurements it had been extended to take, and the gap only showed up when a session was
    /// captured three times and none of the three could say whether a 570-stall stretch was loading.
    ///
    /// One Header and one Row, used by both writers. A field added here appears in every file at
    /// once, which is the only arrangement that does not quietly rot.
    ///
    /// The layout is the union of what the two used to write, so nothing that could read either can
    /// no longer read it: `utc` and `machine` as the group file had, `second` as the local file had.
    /// Read these files by column name rather than by position - new columns go on the end, but the
    /// name is the contract.
    /// </summary>
    internal static class CaptureCsv
    {
        internal const string Header =
            "utc,machine,second,frame_avg_ms,frame_max_ms,stalls,frames," +
            "ping_ms,ping_measured,ping_round_trip,quality_local,quality_remote," +
            "in_bytes_sec,out_bytes_sec,send_queue_bytes,send_rate_bytes_sec," +
            "zdos,instances,zdos_sent_sec,zdos_recv_sec,change_queue,peers," +
            "cpu_ms_per_sec,cpu_measured,gc0,gc1,gc2,collections,heap_bytes,working_set_bytes," +
            "owned_ai,nearby_ai,feed_ms,owned_objects,nearby_objects,unowned_objects,unowned_ai," +
            "system_cpu_pct,nearby_pieces,loading,free_memory_mb";

        internal static readonly string[] Columns = Header.Split(',');

        /// <summary>
        /// A row where only some columns are known, addressed by name rather than by position.
        ///
        /// For an old client that sent only the correlation columns. The previous version of this
        /// hand-counted commas - twenty-odd empty strings in a row - which was correct for exactly
        /// the column order it was written against and would have silently shifted every later
        /// field the moment one was inserted. Naming the few known columns cannot drift.
        ///
        /// Unset columns are left empty, which a reader can tell apart from a measured zero.
        /// </summary>
        internal static string Sparse(params string[] pairs)
        {
            var cells = new string[Columns.Length];
            for (int i = 0; i < cells.Length; i++) cells[i] = "";
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                int at = System.Array.IndexOf(Columns, pairs[i]);
                if (at >= 0) cells[at] = pairs[i + 1];
                else DiagnoseServerLagMod.Log.LogWarning(
                    "[DiagnoseServerLag] capture column '" + pairs[i] + "' does not exist; dropped.");
            }
            return string.Join(",", cells);
        }

        /// <summary>
        /// One second. <paramref name="machine"/> names whose second it was - a player's name in a
        /// group capture, or the role ("client", "dedicated", "host") in a local one.
        /// </summary>
        internal static string Row(string machine, Sample s, CultureInfo c) =>
            string.Join(",", new[]
            {
                Iso(s.UtcTicks), Csv(machine), s.At.ToString("0.0", c),
                s.FrameMsAvg.ToString("0.00", c), s.FrameMsMax.ToString("0.00", c),
                s.Stalls.ToString(c), s.Frames.ToString(c),
                s.Ping.ToString(c), s.HasPing ? "1" : "0", s.PingFromRoundTrip ? "1" : "0",
                s.LocalQuality.ToString("0.0000", c), s.RemoteQuality.ToString("0.0000", c),
                s.InByteSec.ToString("0", c), s.OutByteSec.ToString("0", c),
                s.SendQueue.ToString(c), s.SendRate.ToString(c),
                s.Zdos.ToString(c), s.Instances.ToString(c),
                s.ZdosSent.ToString(c), s.ZdosRecv.ToString(c), s.ChangeQueue.ToString(c),
                s.Peers.ToString(c),
                s.CpuMsPerSec.ToString("0.0", c), s.HasCpu ? "1" : "0",
                s.Gc0.ToString(c), s.Gc1.ToString(c), s.Gc2.ToString(c),
                Machine.Collections(s).ToString(c),
                s.HeapBytes.ToString(c), s.WorkingSetBytes.ToString(c),
                s.OwnedAI.ToString(c), s.NearbyAI.ToString(c),
                s.FeedMs.ToString("0", c),
                s.OwnedObjects.ToString(c), s.NearbyObjects.ToString(c),
                s.UnownedObjects.ToString(c), s.UnownedAI.ToString(c),
                // Blank rather than zero where the machine could not answer: a missing measurement
                // and a measured nothing read the same otherwise, which is how one capture spent an
                // evening looking like an idle machine.
                s.SystemCpu >= 0f ? (s.SystemCpu * 100f).ToString("0.0", c) : "",
                s.NearbyPieces.ToString(c), s.Loading ? "1" : "0",
                s.FreeMemoryMB >= 0 ? s.FreeMemoryMB.ToString(c) : "",
            });

        internal static string Iso(long ticks) =>
            ticks <= 0 ? "" : new System.DateTime(ticks, System.DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

        internal static string Csv(string s) =>
            string.IsNullOrEmpty(s) ? "" : (s.IndexOf(',') >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s);
    }
}
