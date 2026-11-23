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
            "system_cpu_pct,nearby_pieces,loading,free_memory_mb,terrain_zones,terrain_bytes," +
            "gpu_ms,cpu_main_ms,cpu_render_ms,main_cpu_ms_sec,settings_rev," +
            "biome,pos_x,pos_z," +
            "unowned_ai_transient,unowned_ai_outside,unowned_ai_closest_m";

        internal static readonly string[] Columns = Header.Split(',');

        /// <summary>
        /// A label fit for a '#' metadata line: no commas, no line breaks.
        ///
        /// The free text at the end of one of these lines - a settings line, a hardware line - runs
        /// to the end of the line and contains commas of its own, so a reader splits on the first
        /// comma or two and takes the rest whole. That only works while the fields in front of the
        /// free text cannot themselves contain a comma, and a character name can.
        /// </summary>
        internal static string MetaLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace(',', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        /// <summary>
        /// Free text fit for the end of a '#' metadata line: no line breaks, commas allowed.
        /// </summary>
        /// <summary>
        /// A '# span' line: when a run of samples starts and ends, and how many seconds it holds.
        ///
        /// Every capture used to state what was ASKED for and nothing about what it got. Those
        /// differ constantly - a client that joined twenty minutes ago answers a sixty-minute
        /// request with twenty minutes, and the file looked the same either way. Worse, three
        /// captures taken within an hour of each other overlapped by 44 minutes and nothing in any
        /// of them said so, which is an easy way to read one evening's data as three samples.
        ///
        /// first and last are the real extremes of the rows below, not the request.
        /// </summary>
        internal static string SpanLine(string label, long firstTicks, long lastTicks, int rows)
        {
            if (firstTicks <= 0 || lastTicks <= 0) return null;
            int covered = (int)((lastTicks - firstTicks) / System.TimeSpan.TicksPerSecond) + 1;
            return "# span," + (string.IsNullOrEmpty(label) ? "" : MetaLabel(label) + ",")
                 + Iso(firstTicks) + "," + Iso(lastTicks) + "," + covered + "s," + rows + " rows";
        }

        /// <summary>Heightmap.Biome as its name, or "" for none.</summary>
        internal static string BiomeName(int biome)
        {
            if (biome == 0) return "";
            switch (biome)
            {
                case 1: return "Meadows";
                case 2: return "Swamp";
                case 4: return "Mountain";
                case 8: return "BlackForest";
                case 16: return "Plains";
                case 32: return "AshLands";
                case 64: return "DeepNorth";
                case 256: return "Ocean";
                case 512: return "Mistlands";
                // Heightmap.Biome is [Flags] and a boundary can carry two. The number is kept
                // rather than guessed at, since a reader can still tell what it means.
                default: return "biome" + biome.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        internal static string MetaText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

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
                s.TerrainZones.ToString(c), s.TerrainBytes.ToString(c),
                // Blank, not zero, where the build does not count them: a measured 0 ms on the
                // graphics card is a meaningful reading and an absent one is not, and a column
                // that cannot tell them apart would have made this instrument useless for the
                // one question it was added to answer.
                s.GpuMs >= 0f ? s.GpuMs.ToString("0.00", c) : "",
                s.CpuMainMs >= 0f ? s.CpuMainMs.ToString("0.00", c) : "",
                s.CpuRenderMs >= 0f ? s.CpuRenderMs.ToString("0.00", c) : "",
                s.MainCpuMsPerSec >= 0f ? s.MainCpuMsPerSec.ToString("0.0", c) : "",
                s.SettingsRev.ToString(c),
                // The name, not the flag value: a reader should not have to know that 32 is the
                // Ashlands. Blank where there is no player to ask, which is every server row.
                BiomeName(s.Biome),
                s.Biome != 0 ? s.PosX.ToString(c) : "",
                s.Biome != 0 ? s.PosZ.ToString(c) : "",
                // Why the ownerless creatures are ownerless. Blank, not zero, for the distance
                // when there are none to measure - a nearest of 0 m would read as one underfoot.
                s.UnownedAiTransient.ToString(c), s.UnownedAiOutside.ToString(c),
                s.UnownedAiClosest >= 0f ? s.UnownedAiClosest.ToString("0.0", c) : "",
            });

        internal static string Iso(long ticks) =>
            ticks <= 0 ? "" : new System.DateTime(ticks, System.DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

        internal static string Csv(string s) =>
            string.IsNullOrEmpty(s) ? "" : (s.IndexOf(',') >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s);
    }
}
