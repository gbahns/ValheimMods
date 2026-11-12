using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TheGreatestPortal
{
    /// <summary>
    /// What this mod stores on a portal's ZDO, next to vanilla's "tag" (the name).
    ///
    /// Valheim hands every ZDO a fresh ZDOID each time the world is loaded, so a target cannot
    /// be remembered as a ZDOID. Instead every portal gets a random, permanent id of its own
    /// the first time the server (or the player who builds it) sees it, and a destination is
    /// stored as the target's permanent id. The server turns those ids into vanilla portal
    /// connections every couple of seconds, so the actual teleport is the game's own code.
    /// </summary>
    internal static class PortalData
    {
        /// <summary>long: this portal's permanent id (0 = not assigned yet).</summary>
        internal static readonly int IdHash = "TGP_id".GetStableHashCode();

        /// <summary>long: the permanent id of the destination portal (0 = none: choose on the map).</summary>
        internal static readonly int ToHash = "TGP_to".GetStableHashCode();

        /// <summary>int: 1 once this mod has configured the portal. Until then an existing vanilla
        /// or XPortal connection is adopted as the destination, so upgrading a world keeps its pairs.</summary>
        internal static readonly int SetHash = "TGP_set".GetStableHashCode();

        /// <summary>long: the world second this portal was built (0 = from before this was recorded).</summary>
        internal static readonly int BornHash = "TGP_born".GetStableHashCode();

        /// <summary>long: the world second someone last travelled through it (0 = not since the mod saw it).</summary>
        internal static readonly int UsedHash = "TGP_use".GetStableHashCode();

        /// <summary>string: who that last traveller was.</summary>
        internal static readonly int UserHash = "TGP_by".GetStableHashCode();

        /// <summary>string: everyone who has been through it, newest first, one entry per player.
        /// Packed; read it with <see cref="ParseVisits"/> rather than by hand.</summary>
        internal static readonly int LogHash = "TGP_log".GetStableHashCode();

        /// <summary>string: who built it, stamped once when the server first meets the portal
        /// (empty for one built before this was recorded, or when the builder could not be told).</summary>
        internal static readonly int BornByHash = "TGP_bby".GetStableHashCode();

        private static readonly Regex RichText = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static readonly System.Random Rng = new System.Random();

        internal static long GetId(ZDO zdo) => zdo == null ? 0L : zdo.GetLong(IdHash, 0L);
        internal static long GetTarget(ZDO zdo) => zdo == null ? 0L : zdo.GetLong(ToHash, 0L);
        internal static bool IsConfigured(ZDO zdo) => zdo != null && zdo.GetInt(SetHash, 0) != 0;
        internal static string GetName(ZDO zdo) => zdo == null ? "" : (zdo.GetString(ZDOVars.s_tag, "") ?? "");

        internal static long GetBorn(ZDO zdo) => zdo == null ? 0L : zdo.GetLong(BornHash, 0L);
        internal static long GetUsed(ZDO zdo) => zdo == null ? 0L : zdo.GetLong(UsedHash, 0L);
        internal static string GetUser(ZDO zdo) => zdo == null ? "" : (zdo.GetString(UserHash, "") ?? "");
        internal static string GetBornBy(ZDO zdo) => zdo == null ? "" : (zdo.GetString(BornByHash, "") ?? "");
        internal static string GetLog(ZDO zdo) => zdo == null ? "" : (zdo.GetString(LogHash, "") ?? "");

        /// <summary>One player and the last time they went through a portal.</summary>
        internal struct Visit
        {
            public string Who;
            public long At;

            public Visit(string who, long at) { Who = who; At = at; }
        }

        /// <summary>
        /// How many players a portal remembers going through it.
        ///
        /// One entry per player rather than one per trip: the question a list of names answers is
        /// who uses this portal, and somebody who goes through it twenty times a day would
        /// otherwise be the whole answer. There is a cap at all because this rides along in every
        /// portal list the server sends to everyone.
        /// </summary>
        internal const int MaxVisits = 8;

        // Characters no name has in it, so a name can never be read as the end of its own entry.
        private const char VisitField = '\u001f';
        private const char VisitRecord = '\u001e';

        internal static List<Visit> ParseVisits(string packed)
        {
            var visits = new List<Visit>();
            if (string.IsNullOrEmpty(packed)) return visits;
            foreach (var record in packed.Split(VisitRecord))
            {
                int cut = record.IndexOf(VisitField);
                if (cut <= 0) continue;
                if (!long.TryParse(record.Substring(cut + 1), out long when)) continue;
                visits.Add(new Visit(record.Substring(0, cut), when));
            }
            return visits;
        }

        internal static string PackVisits(List<Visit> visits)
        {
            if (visits == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < visits.Count && i < MaxVisits; i++)
            {
                if (sb.Length > 0) sb.Append(VisitRecord);
                sb.Append(Plain(visits[i].Who)).Append(VisitField).Append(visits[i].At);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The log with one more trip written into it: that player's own entry brought up to date
        /// and moved to the front, and the player nobody has seen for longest dropped once there
        /// are more than the cap.
        /// </summary>
        internal static string NoteVisit(string packed, string who, long at)
        {
            if (string.IsNullOrEmpty(who)) return packed ?? "";
            var visits = ParseVisits(packed);
            visits.RemoveAll(v => string.Equals(v.Who, who, StringComparison.OrdinalIgnoreCase));
            visits.Add(new Visit(who, at));
            visits.Sort((a, b) => b.At.CompareTo(a.At));
            if (visits.Count > MaxVisits) visits.RemoveRange(MaxVisits, visits.Count - MaxVisits);
            return PackVisits(visits);
        }

        /// <summary>A name with the log's own separators taken out of it.</summary>
        private static string Plain(string who)
        {
            if (string.IsNullOrEmpty(who)) return "";
            return who.Replace(VisitField, ' ').Replace(VisitRecord, ' ');
        }

        /// <summary>
        /// The world's own clock in whole seconds. It is saved with the world and carries on where
        /// it left off, so unlike Time.time it can still order two events either side of a restart.
        /// </summary>
        internal static long Now()
        {
            var znet = ZNet.instance;
            return znet != null ? (long)znet.GetTimeSeconds() : 0L;
        }

        /// <summary>A random permanent id. Never 0, which means "unassigned".</summary>
        internal static long NewId()
        {
            long id;
            do
            {
                var bytes = new byte[8];
                Rng.NextBytes(bytes);
                id = BitConverter.ToInt64(bytes, 0) & 0x7FFFFFFFFFFFFFFF;
            } while (id == 0L);
            return id;
        }

        /// <summary>Strips rich text and line breaks and trims to the allowed length.</summary>
        internal static string CleanName(string name, int maxLength)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string s = RichText.Replace(name, "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            if (maxLength > 0 && s.Length > maxLength) s = s.Substring(0, maxLength);
            return s;
        }

        /// <summary>Where vanilla says this portal goes right now (None while unconnected).</summary>
        internal static ZDOID Connection(ZDO zdo)
        {
            return zdo == null ? ZDOID.None : zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
        }

        /// <summary>
        /// True when the portal has no destination of its own, so stepping in opens the map
        /// (if the server allows it). A destination that has been set but is still being connected,
        /// or whose portal has been destroyed, is not "open".
        /// </summary>
        internal static bool IsOpen(ZDO zdo)
        {
            return zdo != null && GetTarget(zdo) == 0L && Connection(zdo) == ZDOID.None;
        }
    }
}
