using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Hold the heavy half of a map exchange until the ground under the player stops moving.
    ///
    /// <para>Comparing explored ground is the same work as reading a cartography table: a few
    /// hundred milliseconds on the main thread, in one frame. At a table nobody minds, because
    /// standing still through a stutter costs nothing. Under sail it costs the player. The hull
    /// keeps moving through the frames the game did not draw, physics then catches up in steps
    /// large enough to carry the deck past the character capsule, and the player comes out the
    /// other side in the water.</para>
    ///
    /// <para>That is not a guess. On 2026-10-26 an exchange with Pitsfatrik spent 346 ms in one
    /// burst -- markers 25 ms, exploration 170 ms, packing ours 150 ms -- on top of the 148 ms
    /// spent packing the offer. Two seconds later the game logged "Player over board, players
    /// left 4", and Greg was swimming.</para>
    ///
    /// <para>Markers are cheap, so they go through at once and the player sees what they came
    /// for. Explored ground waits for a deck that is not going anywhere, which is seconds away
    /// at anchor and never longer than a landing.</para>
    /// </summary>
    internal static class Underway
    {
        /// <summary>
        /// Below this the deck cannot travel far enough in one stalled frame to put the player
        /// through it. Half a meter a second is slower than walking; a ship drifting this gently
        /// moves about 15 cm across the worst burst measured.
        /// </summary>
        private const float StillEnough = 0.5f;

        /// <summary>Their explored ground, waiting for a moment when applying it is harmless.</summary>
        private static readonly Dictionary<long, byte[]> _theirs = new Dictionary<long, byte[]>();

        /// <summary>Who we answered with markers alone and still owe our explored ground.</summary>
        private static readonly Dictionary<long, string> _owed = new Dictionary<long, string>();

        internal static bool Waiting => _theirs.Count > 0 || _owed.Count > 0;

        internal static void Reset()
        {
            _theirs.Clear();
            _owed.Clear();
            _ship = null;
            _body = null;
        }

        // The ship and its rigidbody, remembered so this is not a GetComponent every frame.
        private static Ship _ship;
        private static Rigidbody _body;

        /// <summary>
        /// Is the player standing on something that is going somewhere?
        ///
        /// The ship being steered counts even though the helmsman is attached to the rudder
        /// rather than standing on the deck, which is why the game's own standing-on test is not
        /// enough on its own.
        /// </summary>
        internal static bool Moving()
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null) return false;
                // Not ??: a destroyed UnityEngine.Object is only null to the game's own ==
                // operator, and ?? does not use it, so a wrecked ship would be returned here as
                // a live one.
                var ship = player.GetControlledShip();
                if (ship == null) ship = player.GetStandingOnShip();
                if (ship == null) { _ship = null; _body = null; return false; }
                if (!ReferenceEquals(ship, _ship))
                {
                    _ship = ship;
                    _body = ship.GetComponent<Rigidbody>();
                }
                if (_body == null) return true;   // a deck we cannot measure is a deck to respect
                return _body.linearVelocity.magnitude >= StillEnough;
            }
            catch (System.Exception)
            {
                // Never let a question about the player's footing stop a map being shared.
                return false;
            }
        }

        /// <summary>
        /// What the gate can see, for tgm_underway.
        ///
        /// The queue is simple; the hard part is reading the player's footing, and there is no
        /// way to test that alone with another player on hand. A sentence a sailor can read
        /// while sailing is worth more here than any amount of staring at the code: it says
        /// which ship the gate picked, whether the tiller or the deck answered, and how fast
        /// the game thinks that hull is going.
        /// </summary>
        internal static string Describe()
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null) return "no player yet.";
                var ship = player.GetControlledShip();
                string how = "steering it";
                if (ship == null) { ship = player.GetStandingOnShip(); how = "standing on it"; }
                if (ship == null)
                    return "not on a ship, so nothing would be held back" + Queue() + ".";
                var body = ship.GetComponent<Rigidbody>();
                if (body == null)
                    return $"on {ship.name} ({how}) but it has no rigidbody to measure, so explored " +
                           "ground would be held back to be safe" + Queue() + ".";
                float speed = body.linearVelocity.magnitude;
                return $"on {ship.name} ({how}) at {speed:0.00} m/s; the gate holds at {StillEnough:0.00}, " +
                       $"so explored ground would {(speed >= StillEnough ? "be held back" : "go through now")}" +
                       Queue() + ".";
            }
            catch (System.Exception e)
            {
                return "could not tell: " + e.Message;
            }
        }

        private static string Queue()
        {
            if (!Waiting) return ", and nothing is waiting";
            return $", and {_theirs.Count} map(s) of explored ground and {_owed.Count} owed are waiting";
        }

        /// <summary>Keep their explored ground, and remember if they are still waiting for ours.</summary>
        internal static void Hold(long peer, string name, byte[] exploration, bool owesOurs)
        {
            // Their newest offer replaces an older one rather than queueing behind it: the two
            // describe the same map, and only the later one is true.
            if (exploration != null && exploration.Length > 0) _theirs[peer] = exploration;
            if (owesOurs) _owed[peer] = name ?? "them";
        }

        /// <summary>
        /// Called every frame. Does nothing at all until there is something held and the deck
        /// has settled, and then does one peer's worth of work per frame -- two players handed
        /// over at once would be the very stall this exists to avoid.
        /// </summary>
        internal static void Pump()
        {
            if (!Waiting || Moving() || !PersonalMap.Loaded) return;

            if (_theirs.Count > 0)
            {
                long peer = 0L; byte[] data = null;
                foreach (var kv in _theirs) { peer = kv.Key; data = kv.Value; break; }
                _theirs.Remove(peer);
                bool newAreas = SyncEngine.TakeExploration(data);
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Took in the explored ground held back while we were under way" +
                    (newAreas ? ", and it was new ground." : "; nothing in it we did not have."));
                if (newAreas) TheGreatestMapMod.Message("New map areas from the map you compared under way.");
                return;
            }

            {
                long peer = 0L; string name = null;
                foreach (var kv in _owed) { peer = kv.Key; name = kv.Value; break; }
                _owed.Remove(peer);
                SyncEngine.SendExplorationTo(peer, name);
            }
        }
    }
}
