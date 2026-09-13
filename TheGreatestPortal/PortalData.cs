using System;
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

        private static readonly Regex RichText = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static readonly System.Random Rng = new System.Random();

        internal static long GetId(ZDO zdo) => zdo == null ? 0L : zdo.GetLong(IdHash, 0L);
        internal static long GetTarget(ZDO zdo) => zdo == null ? 0L : zdo.GetLong(ToHash, 0L);
        internal static bool IsConfigured(ZDO zdo) => zdo != null && zdo.GetInt(SetHash, 0) != 0;
        internal static string GetName(ZDO zdo) => zdo == null ? "" : (zdo.GetString(ZDOVars.s_tag, "") ?? "");

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
