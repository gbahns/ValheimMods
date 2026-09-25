using System;
using System.Collections.Generic;

namespace DiagnoseServerLag
{
    /// <summary>
    /// One second of measurements taken on this machine, whichever end of the connection it is.
    ///
    /// Everything here is sampled once per second and never reset, because the numbers belong to
    /// the game, not to this mod. ISocket.GetAndResetStats would give cleaner byte totals and is
    /// deliberately NOT used anywhere in this mod: it zeroes the counters Valheim keeps for its
    /// own bandwidth display, so reading it here would quietly corrupt the game's accounting.
    /// GetConnectionQuality reads the same figures without resetting anything.
    /// </summary>
    internal struct Sample
    {
        /// <summary>Time.unscaledTime when this second ended. Per-process; meaningless off this machine.</summary>
        internal float At;

        /// <summary>
        /// Wall clock when this second ended, in UTC ticks.
        ///
        /// The only field here that means the same thing on two different machines, which is what
        /// makes a group capture more than a pile of unrelated summaries: stalls that line up on
        /// the same wall-clock second across several clients are one shared event, and stalls that
        /// do not are several local ones. Time.unscaledTime cannot answer that - it counts from
        /// process start, so two machines agree on nothing.
        ///
        /// Accurate to whatever the machines' clocks agree on, which with normal time sync is well
        /// inside the one-second resolution this is compared at.
        /// </summary>
        internal long UtcTicks;

        // ── how long the frames took ────────────────────────────────────────────────
        // On a client these are render frames. On a dedicated server they are simulation ticks:
        // the same Update loop, with nothing to draw. Either way a number far above the machine's
        // normal cadence means that end could not keep up during this second.
        internal float FrameMsAvg;
        internal float FrameMsMax;
        /// <summary>Frames in this second that ran longer than the configured stall threshold.</summary>
        internal int Stalls;
        internal int Frames;

        // ── the link ────────────────────────────────────────────────────────────────
        // Ping and quality come from ZSteamSocket, which reads Steam's own connection status.
        // Quality is a 0..1 fraction that drops when packets are lost. Note that the plain TCP
        // path (ZNetStats) hardcodes ping = 0 and quality = 0, so a zero here is "not measurable",
        // not "perfect" - HasPing says which.
        internal int Ping;
        internal float LocalQuality;
        internal float RemoteQuality;
        internal bool HasPing;
        /// <summary>The ping was measured by the mod's own round trip, not reported by the socket.</summary>
        internal bool PingFromRoundTrip;
        internal float OutByteSec;
        internal float InByteSec;

        /// <summary>
        /// Bytes still queued for sending and not yet on the wire. The single most telling number
        /// in this mod: a queue that keeps growing means this end is producing more than the link
        /// can drain, which is saturation rather than any kind of computation problem.
        /// </summary>
        internal int SendQueue;
        internal int SendRate;

        // ── the world ───────────────────────────────────────────────────────────────
        /// <summary>Networked objects this machine knows about. The server's figure is the whole world.</summary>
        internal int Zdos;
        /// <summary>Networked objects instantiated into the scene. Clients only; a headless server keeps none.</summary>
        internal int Instances;
        /// <summary>ZDOs sent and received in the last second, straight from ZDOMan's own counters.</summary>
        internal int ZdosSent;
        internal int ZdosRecv;
        /// <summary>Objects a client has changed and not yet had acknowledged.</summary>
        internal int ChangeQueue;

        internal int Peers;

        // ── who is actually simulating the creatures ────────────────────────────────
        // Valheim runs a creature's AI only on the machine that owns its ZDO; every other
        // client just renders what the owner reports. Ownership goes to whoever was in range
        // when the object had none and sticks until they walk away, with no balancing of any
        // kind - so a group that piles into one zone can leave one person simulating all of it
        // on a machine nobody chose. These two numbers are what make that visible.
        /// <summary>Creatures whose AI this machine is running.</summary>
        internal int OwnedAI;
        /// <summary>Creatures loaded here at all, owned or not.</summary>
        internal int NearbyAI;

        /// <summary>
        /// Median gap between updates arriving from the server, in milliseconds. Client side only;
        /// zero on the server, where the same callback measures something else. See Feed.
        /// </summary>
        internal float FeedMs;

        /// <summary>
        /// Every loaded object this machine owns, and how many are loaded here at all - not just
        /// creatures. Ownership routing is not special to AI: TreeBase.RPC_Damage opens with the
        /// same owner check BaseAI.UpdateAI does, so a tree, a rock or a workbench somebody else
        /// owns costs a round trip through them exactly as a greydwarf does. Counting only
        /// creatures described the wrong population for the commonest complaint of all, which is
        /// that chopping wood feels slow.
        /// </summary>
        internal int OwnedObjects;
        internal int NearbyObjects;

        /// <summary>
        /// Loaded objects nobody owns yet. Not a fault - it is the gap between an object existing
        /// and the two-second pass that hands it to somebody - but it is worth seeing, because an
        /// unowned creature runs no AI at all. A large number means a stretch of world that is
        /// present and inert.
        /// </summary>
        internal int UnownedObjects;

        /// <summary>Of those, how many are creatures - the ones running no AI at all.</summary>
        internal int UnownedAI;

        // ── what the process costs the machine ──────────────────────────────────────
        // The measurements that survive a frame cap. See Machine for why tick time does not.
        /// <summary>Milliseconds of CPU burned per second of wall clock. 1000 is one core fully busy.</summary>
        internal float CpuMsPerSec;
        internal bool HasCpu;
        /// <summary>Garbage collections that happened during this second, by generation.</summary>
        internal int Gc0, Gc1, Gc2;
        internal long HeapBytes;
        internal long WorkingSetBytes;
    }

    /// <summary>
    /// Puts a whole Sample on the wire and takes it off again.
    ///
    /// Every field, not a chosen subset. The group capture used to send five columns on the
    /// reasoning that nobody would line the rest up by hand - which was wrong the first time it
    /// mattered: the very first real group capture answered "were the stalls shared" and then could
    /// not answer "what was the machine doing", so it took a second command on a second machine to
    /// finish the job the first was supposed to do.
    ///
    /// The payload argument that justified trimming does not hold either. The window is already
    /// recorded before any of it is sent, so the transfer cannot contaminate the measurement it is
    /// carrying, and ZPackage.WriteCompressed squeezes a series of slowly-changing numbers hard.
    /// </summary>
    internal static class SampleWire
    {
        internal static void Write(ZPackage pkg, Sample s)
        {
            // Layout 7 appends UnownedAI, 6 appended UnownedObjects, 5 appended OwnedObjects/NearbyObjects, 4 appended FeedMs, 3 OwnedAI/NearbyAI. Writers always write the
            // newest shape; readers
            // are told which one they are looking at, so an older client stays readable instead
            // of being misparsed into nonsense.

            pkg.Write(s.At);
            pkg.Write(s.UtcTicks);
            pkg.Write(s.FrameMsAvg);
            pkg.Write(s.FrameMsMax);
            pkg.Write(s.Stalls);
            pkg.Write(s.Frames);
            pkg.Write(s.Ping);
            pkg.Write(s.HasPing);
            pkg.Write(s.PingFromRoundTrip);
            pkg.Write(s.LocalQuality);
            pkg.Write(s.RemoteQuality);
            pkg.Write(s.OutByteSec);
            pkg.Write(s.InByteSec);
            pkg.Write(s.SendQueue);
            pkg.Write(s.SendRate);
            pkg.Write(s.Zdos);
            pkg.Write(s.Instances);
            pkg.Write(s.ZdosSent);
            pkg.Write(s.ZdosRecv);
            pkg.Write(s.ChangeQueue);
            pkg.Write(s.Peers);
            pkg.Write(s.CpuMsPerSec);
            pkg.Write(s.HasCpu);
            pkg.Write(s.Gc0);
            pkg.Write(s.Gc1);
            pkg.Write(s.Gc2);
            pkg.Write(s.HeapBytes);
            pkg.Write(s.WorkingSetBytes);
            pkg.Write(s.OwnedAI);
            pkg.Write(s.NearbyAI);
            pkg.Write(s.FeedMs);
            pkg.Write(s.OwnedObjects);
            pkg.Write(s.NearbyObjects);
            pkg.Write(s.UnownedObjects);
            pkg.Write(s.UnownedAI);
        }

        internal static Sample Read(ZPackage pkg, int layout)
        {
            var s = new Sample();
            s.At = pkg.ReadSingle();
            s.UtcTicks = pkg.ReadLong();
            s.FrameMsAvg = pkg.ReadSingle();
            s.FrameMsMax = pkg.ReadSingle();
            s.Stalls = pkg.ReadInt();
            s.Frames = pkg.ReadInt();
            s.Ping = pkg.ReadInt();
            s.HasPing = pkg.ReadBool();
            s.PingFromRoundTrip = pkg.ReadBool();
            s.LocalQuality = pkg.ReadSingle();
            s.RemoteQuality = pkg.ReadSingle();
            s.OutByteSec = pkg.ReadSingle();
            s.InByteSec = pkg.ReadSingle();
            s.SendQueue = pkg.ReadInt();
            s.SendRate = pkg.ReadInt();
            s.Zdos = pkg.ReadInt();
            s.Instances = pkg.ReadInt();
            s.ZdosSent = pkg.ReadInt();
            s.ZdosRecv = pkg.ReadInt();
            s.ChangeQueue = pkg.ReadInt();
            s.Peers = pkg.ReadInt();
            s.CpuMsPerSec = pkg.ReadSingle();
            s.HasCpu = pkg.ReadBool();
            s.Gc0 = pkg.ReadInt();
            s.Gc1 = pkg.ReadInt();
            s.Gc2 = pkg.ReadInt();
            s.HeapBytes = pkg.ReadLong();
            s.WorkingSetBytes = pkg.ReadLong();
            if (layout >= 3)
            {
                s.OwnedAI = pkg.ReadInt();
                s.NearbyAI = pkg.ReadInt();
            }
            if (layout >= 4)
            {
                s.FeedMs = pkg.ReadSingle();
            }
            if (layout >= 5)
            {
                s.OwnedObjects = pkg.ReadInt();
                s.NearbyObjects = pkg.ReadInt();
            }
            if (layout >= 6)
            {
                s.UnownedObjects = pkg.ReadInt();
            }
            if (layout >= 7)
            {
                s.UnownedAI = pkg.ReadInt();
            }
            return s;
        }
    }

    /// <summary>
    /// A fixed-length history of the most recent samples, oldest first when enumerated.
    ///
    /// The mod keeps a rolling window rather than a growing log because the question is always
    /// "what was happening around the stall", and a stall that has scrolled out of the window is
    /// one nobody is still asking about. Nothing is allocated after construction.
    /// </summary>
    internal sealed class Ring
    {
        private readonly Sample[] _items;
        private int _next;
        private int _count;

        internal Ring(int capacity) { _items = new Sample[Math.Max(1, capacity)]; }

        internal int Count => _count;
        internal int Capacity => _items.Length;

        internal void Add(Sample s)
        {
            _items[_next] = s;
            _next = (_next + 1) % _items.Length;
            if (_count < _items.Length) _count++;
        }

        internal void Clear() { _next = 0; _count = 0; }

        /// <summary>Index 0 is the oldest sample held, Count-1 the newest.</summary>
        internal Sample this[int i]
        {
            get
            {
                if (i < 0 || i >= _count) throw new IndexOutOfRangeException();
                int start = (_count == _items.Length) ? _next : 0;
                return _items[(start + i) % _items.Length];
            }
        }

        internal bool TryNewest(out Sample s)
        {
            if (_count == 0) { s = default; return false; }
            s = this[_count - 1];
            return true;
        }

        /// <summary>The last <paramref name="seconds"/> samples, newest last. Fewer if that is all there is.</summary>
        internal List<Sample> Recent(int seconds)
        {
            var list = new List<Sample>(Math.Min(seconds, _count));
            for (int i = Math.Max(0, _count - seconds); i < _count; i++) list.Add(this[i]);
            return list;
        }
    }

    /// <summary>
    /// Summary arithmetic over a set of samples.
    ///
    /// Medians, not averages, wherever a baseline is being established. A baseline exists to
    /// answer "what does this connection normally look like", and one four-second hitch inside a
    /// sixty-second window moves an average enough to hide the next hitch from the comparison.
    /// The median ignores it, which is the entire reason the mod can accuse a specific subsystem
    /// instead of reporting that everything was a bit worse than usual.
    /// </summary>
    internal static class Stats
    {
        internal static float Median(List<Sample> samples, Func<Sample, float> field)
        {
            if (samples == null || samples.Count == 0) return 0f;
            var values = new List<float>(samples.Count);
            foreach (var s in samples) values.Add(field(s));
            values.Sort();
            int mid = values.Count / 2;
            return (values.Count % 2 == 1) ? values[mid] : (values[mid - 1] + values[mid]) * 0.5f;
        }

        internal static float Max(List<Sample> samples, Func<Sample, float> field)
        {
            float max = 0f;
            if (samples == null) return max;
            foreach (var s in samples) { float v = field(s); if (v > max) max = v; }
            return max;
        }

        internal static float Mean(List<Sample> samples, Func<Sample, float> field)
        {
            if (samples == null || samples.Count == 0) return 0f;
            float total = 0f;
            foreach (var s in samples) total += field(s);
            return total / samples.Count;
        }

        /// <summary>
        /// How much a value jitters, as the mean gap between one second and the next.
        ///
        /// Steady 120 ms ping and ping swinging 20-220 ms average the same and feel completely
        /// different: the steady one is just distance, the swinging one is a link in trouble.
        /// Only consecutive differences tell them apart.
        /// </summary>
        internal static float Jitter(List<Sample> samples, Func<Sample, float> field)
        {
            if (samples == null || samples.Count < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < samples.Count; i++) total += Math.Abs(field(samples[i]) - field(samples[i - 1]));
            return total / (samples.Count - 1);
        }

        /// <summary>
        /// Whether a series is trending upward across the window, as units gained per second.
        ///
        /// Used for the send queue, where the level matters far less than the direction: a queue
        /// holding steady at 40 KB is a link running full but keeping up, and the same 40 KB while
        /// climbing is a link that has already lost and is now building the backlog that will be
        /// felt as rubber-banding a few seconds from now.
        /// </summary>
        internal static float Slope(List<Sample> samples, Func<Sample, float> field)
        {
            if (samples == null || samples.Count < 2) return 0f;
            // Least squares against the sample index; samples are one second apart by construction.
            int n = samples.Count;
            float meanX = (n - 1) * 0.5f;
            float meanY = Mean(samples, field);
            float num = 0f, den = 0f;
            for (int i = 0; i < n; i++)
            {
                float dx = i - meanX;
                num += dx * (field(samples[i]) - meanY);
                den += dx * dx;
            }
            return den <= 0f ? 0f : num / den;
        }

        internal static string Bytes(float b)
        {
            if (b >= 1024f * 1024f) return $"{b / (1024f * 1024f):0.0} MB";
            if (b >= 1024f) return $"{b / 1024f:0.0} KB";
            return $"{b:0} B";
        }
    }
}
