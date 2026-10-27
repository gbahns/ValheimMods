using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Seeing a place, as opposed to seeing a thing.
    ///
    /// <para>Everything else in the ledger is found by a single ray down the middle of the
    /// screen: whatever that ray strikes is handed to the catalog, and if the catalog knows it,
    /// you have seen it. That is exactly right for a thing -- a berry bush, a copper rock, a
    /// runestone -- because a thing is what the crosshair lands on.</para>
    ///
    /// <para>It is wrong for a place. Stand on a rise looking down at a Fuling village and the
    /// crosshair is on grass, or on the sky above the roofs, or on a goblin walking about; look
    /// across a tar pit and it is on tar, which is terrain. None of those classify as anything,
    /// so the ray reports nothing and the village you are plainly staring at is never found. The
    /// only way it ever registered was by accident -- the crosshair happening to cross a hut wall,
    /// which is a networked piece and does classify. That is why these felt so fiddly: you were
    /// not failing to look at the village, you were failing to hit one specific part of it.</para>
    ///
    /// <para>So places are asked about directly, and the question is the one a person would ask:
    /// is it in front of me, is it near enough, and is there anything in the way? The landscape
    /// is the only thing allowed to be in the way. A hill between you and a tar pit hides it; a
    /// tree at the edge of a village, or one of its own walls, does not -- you can see a village
    /// perfectly well while something stands inside it.</para>
    ///
    /// <para>Nothing here is proximity radar. A place behind you, over a ridge, or past its
    /// sighting distance is not found, and the dwell applies the same as for anything else. It is
    /// the same three honest signals the ledger has always used, asked of something too big to
    /// put a crosshair on.</para>
    /// </summary>
    internal static class Landmarks
    {
        /// <summary>
        /// How far off the middle of the screen a place may sit and still count as looked at.
        /// The game draws about 65 degrees across, so half of that is roughly the edge of the
        /// screen: if it is on screen and you are facing it, you can see it.
        /// </summary>
        private const float HalfAngle = 30f;

        /// <summary>How many of the nearest spawned locations are weighed in one pass.</summary>
        private const int Candidates = 12;

        /// <summary>
        /// Sighting a place is measured to the middle of it, so the target is lifted off the
        /// floor by about the height of a person. The origin of a location sits on the ground,
        /// and a ray that ends exactly on the ground grazes it the whole way in.
        /// </summary>
        private const float OffTheFloor = 1.5f;

        private static int _blockers = -1;

        /// <summary>
        /// What is allowed to stand between you and a place: the landscape, and nothing else.
        /// Hills and cliffs hide a village; its own huts, the trees around it and whatever is
        /// walking through it do not, and a mask that included them would put us back where we
        /// started, refusing to see a place because something was standing in it.
        /// </summary>
        private static int Blockers()
        {
            if (_blockers < 0) _blockers = LayerMask.GetMask("terrain", "static_solid");
            return _blockers;
        }

        /// <summary>
        /// The place the player is looking at, or null. At most one line-of-sight ray is cast per
        /// call: the candidates are ordered by how squarely they sit in view and the first one
        /// that answers every question wins.
        /// </summary>
        internal static Found Sighted(Player player, GameCamera cam)
        {
            if (player == null || cam == null) return null;
            try
            {
                Vector3 eye = cam.transform.position;
                Vector3 here = player.transform.position;
                foreach (var entry in InView(here, eye, cam.transform.forward))
                {
                    if (!Catalog.TryClassifyLocation(entry, out var found)) continue;
                    if (!Eligible(found)) continue;
                    if (Geo.FlatDistance(here, entry.Pos) > Reach(found)) continue;
                    if (!Clear(eye, entry)) continue;
                    return found;
                }
            }
            catch (System.Exception)
            {
                // Never let a question about the view stop the ordinary crosshair recording.
            }
            return null;
        }

        /// <summary>
        /// Structures are the one kind left out, and not because they are hard to see. A
        /// compound -- an abandoned farm, a ruin with four buildings -- is deliberately marked
        /// building by building, by the piece the crosshair found and the building that piece
        /// belongs to. A sighting of the place as a whole has no building in it, so it would
        /// write a second marker in the middle of the yard beside the ones on the houses. The
        /// catch-all that makes any unlisted location a structure would bring in every rock
        /// formation in the world besides.
        /// </summary>
        private static bool Eligible(Found found) => found != null && found.Cat != Category.Structure;

        /// <summary>
        /// How far away a place can be seen: the sighting distance for its kind, measured to its
        /// edge rather than to its origin.
        ///
        /// <para>The per-kind distances were set with things in mind, and a thing is a point. A
        /// tar pit is sixty meters across, so "forty meters" asked of its origin means standing
        /// almost in it, while the same forty meters asked of a copper rock means what it says.
        /// Adding the radius makes the setting mean one thing for both: how far from the edge of
        /// what you are looking at you may stand.</para>
        /// </summary>
        private static float Reach(Found found) => TgmConfig.LookDistanceFor(found.Cat) + found.Radius;

        /// <summary>Is the landscape out of the way between the eye and the middle of the place?</summary>
        private static bool Clear(Vector3 eye, LocationIndex.Entry entry)
        {
            Vector3 target = entry.Pos + Vector3.up * OffTheFloor;
            Vector3 to = target - eye;
            float span = to.magnitude;
            if (span < 0.01f) return true;
            RaycastHit hit;
            if (!Physics.Raycast(eye, to / span, out hit, span, Blockers())) return true;
            // The ground of the place itself is not something standing in front of the place.
            // Looking across a tar pit, the ray lands in the tar; looking at a village from a
            // rise, it lands on the village green. Both are arrivals, not obstructions.
            return Geo.FlatDistance(hit.point, entry.Pos) <= Radius(entry);
        }

        private static float Radius(LocationIndex.Entry entry) => Mathf.Max(entry.Radius, 4f);

        /// <summary>
        /// The spawned locations that fall inside the view, the squarest first. A large place
        /// close by fills the view even when its origin is off to one side, so each is given the
        /// angle it actually subtends rather than being judged by its middle alone.
        /// </summary>
        private static List<LocationIndex.Entry> InView(Vector3 here, Vector3 eye, Vector3 forward)
        {
            var ordered = new List<KeyValuePair<float, LocationIndex.Entry>>();
            foreach (var entry in LocationIndex.Nearest(here, Candidates))
            {
                float off = OffCenter(eye, forward, entry);
                if (off > HalfAngle) continue;
                ordered.Add(new KeyValuePair<float, LocationIndex.Entry>(off, entry));
            }
            ordered.Sort((a, b) => a.Key.CompareTo(b.Key));
            var result = new List<LocationIndex.Entry>(ordered.Count);
            foreach (var kv in ordered) result.Add(kv.Value);
            return result;
        }

        /// <summary>Degrees between where the eye points and the nearest edge of the place; 0 when it is being looked at squarely.</summary>
        private static float OffCenter(Vector3 eye, Vector3 forward, LocationIndex.Entry entry)
        {
            Vector3 to = entry.Pos - eye;
            float span = to.magnitude;
            if (span < 0.01f) return 0f;
            float spread = Mathf.Atan2(Radius(entry), span) * Mathf.Rad2Deg;
            return Mathf.Max(0f, Vector3.Angle(forward, to) - spread);
        }

        /// <summary>
        /// The tgm_look report for places, which answers a question the crosshair half of that
        /// report cannot: why the village in front of you is not being written down. Every
        /// nearby place is listed with the one thing that ruled it out, because "nothing found"
        /// is the least useful thing a diagnostic can say.
        /// </summary>
        internal static List<string> Describe(Player player, GameCamera cam)
        {
            var lines = new List<string>();
            if (player == null || cam == null) return lines;
            Vector3 eye = cam.transform.position;
            Vector3 forward = cam.transform.forward;
            Vector3 here = player.transform.position;
            var near = LocationIndex.Nearest(here, Candidates);
            lines.Add($"places in view (seen without the crosshair on them): {near.Count} location(s) indexed nearby");
            if (near.Count == 0)
            {
                lines.Add("  none spawned around you, so there is nothing for this to see");
                return lines;
            }
            foreach (var entry in near)
            {
                float d = Geo.FlatDistance(here, entry.Pos);
                float off = OffCenter(eye, forward, entry);
                string head = $"  '{entry.Prefab}' {d:0} m away, radius {entry.Radius:0}, {off:0} deg off center";
                if (!Catalog.TryClassifyLocation(entry, out var found))
                {
                    lines.Add(head + " -- the catalog makes nothing of it");
                    continue;
                }
                if (!Eligible(found))
                {
                    lines.Add(head + $" -- {found.Cat}, marked building by building instead");
                    continue;
                }
                float reach = Reach(found);
                head += $" -- {found.Cat} '{found.Name}', reach {reach:0} m";
                if (off > HalfAngle) { lines.Add(head + ": NOT IN VIEW (over " + HalfAngle + " deg off center)"); continue; }
                if (d > reach) { lines.Add(head + ": TOO FAR"); continue; }
                if (!Clear(eye, entry)) { lines.Add(head + ": BLOCKED by the landscape"); continue; }
                lines.Add(head + ": SEEN" +
                          (DiscoveryLedger.IsRecorded(found.Key) ? " (already recorded this session)"
                           : DiscoveryLedger.IsPending(found.Key) ? " (found, waiting to be written)" : ""));
            }
            return lines;
        }
    }
}
