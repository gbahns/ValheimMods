using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Asking the world what a building actually is.
    ///
    /// Markers written before a marker remembered its own identity can usually be read back from
    /// the icon they wear, but the structures cannot: a dozen catalog lines share the house pin,
    /// the label rules erased the names that would have told them apart, and guessing from the
    /// biome narrows the field without ever settling it -- a meadows house pin is an abandoned
    /// farm, an abandoned house, a ruin or a stone house, and picking one would quietly file every
    /// building in the game under whichever came first.
    ///
    /// There is no need to guess. The world generator placed each of those buildings and the
    /// server still holds the list: every location, where it stands and what it is called. A
    /// marker is matched to the building it was written for and reads its name off the world,
    /// which is a fact rather than an inference.
    ///
    /// Only the server has that list -- it is made when the world is generated, and a client's
    /// copy is empty -- so the client sends the places it cannot account for and the server sends
    /// back the names.
    /// </summary>
    internal static class LocationNames
    {
        private const string RpcAsk = "TGM_WhatIsHere";     // client -> server: what stands at these places
        private const string RpcTell = "TGM_WhatIsHereIs";  // server -> that client: the names, in order

        // The same question asked better, added in 4.2.0 and given names of its own rather than
        // changing the shape of the old pair. A server that predates it simply never answers, the
        // asker gives up, and nothing is harmed -- where altering the reply would have had an old
        // server send a shape the new client reads as nonsense.
        private const string RpcAskAll = "TGM_WhatStandsHere";
        private const string RpcTellAll = "TGM_WhatStandsHereIs";

        /// <summary>
        /// How far from a marker to look when the location itself does not say. A structure's
        /// marker sits on the piece the player saw rather than on the middle of the yard, so the
        /// location's own radius is used where there is one and this is the backstop.
        /// </summary>
        private const float SearchRadius = 48f;

        internal static Action<List<string>> OnAnswer;

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null)
            {
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] Nowhere to register the world questions yet; nobody will be able to ask.");
                return;
            }
            Rpc.Register<ZPackage>(rpc, RpcAsk, RPC_Ask);
            Rpc.Register<ZPackage>(rpc, RpcTell, RPC_Tell);
            Rpc.Register<ZPackage>(rpc, RpcAskAll, RPC_AskAll);
            Rpc.Register<ZPackage>(rpc, RpcTellAll, RPC_TellAll);
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Ready to answer questions about the world (this is {(IsServer ? "the server" : "a client")}).");
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ── the server's copy of what the generator placed ──────────────────────────

        // ZoneSystem.m_locationInstances is private; AccessTools so runtime enforcement does not
        // throw, and a Valheim that renames it costs a log line rather than a crash.
        private static readonly System.Reflection.FieldInfo _instancesField =
            AccessTools.Field(typeof(ZoneSystem), "m_locationInstances");

        /// <summary>
        /// The game's index of what stands in each sector.
        ///
        /// Reached by name rather than written as `man.m_objectsBySector`, which compiles --
        /// the Libs copy of the game has every member public -- and then throws
        /// FieldAccessException against the real assembly, where the field is private.
        ///
        /// It throws in a way that hides itself, too. The runtime raises it when the method is
        /// compiled, not where the field is read, so the try/catch wrapped around the read never
        /// sees it: the whole method fails before its first statement. Here that meant the
        /// server took the repair's question, answered nothing, logged nothing, and threw
        /// nothing that any guard inside could catch -- several evenings of looking at silence.
        /// Only a guard in the calling method ever caught it.
        ///
        /// audit-private-access.ps1 finds these by compiling against the real assembly and
        /// reporting what will not resolve. Worth running before a release.
        /// </summary>
        private static readonly AccessTools.FieldRef<ZDOMan, List<ZDO>[]> ObjectsBySector =
            AccessTools.FieldRefAccess<ZDOMan, List<ZDO>[]>("m_objectsBySector");

        private static IDictionary Instances()
        {
            var zone = ZoneSystem.instance;
            if (zone == null || _instancesField == null) return null;
            try { return _instancesField.GetValue(zone) as IDictionary; }
            catch (Exception) { return null; }
        }

        /// <summary>One placed location, already read out of the game's own table.</summary>
        internal struct Placed
        {
            public Vector3 Pos;
            public float Radius;
            public string Prefab;
        }

        /// <summary>
        /// Every location the generator placed, read once.
        ///
        /// Reading one costs four reflective field reads, because the table and the fields on it
        /// are all private. That is affordable for a single question and ruinous for a thousand:
        /// the repair asks about every marker that does not know what it is, and doing this per
        /// marker meant the whole table walked once per marker -- hundreds of thousands of
        /// reflective reads for one request, which is what made the server go quiet rather than
        /// answer. Read the table once and ask the snapshot instead.
        /// </summary>
        internal static List<Placed> Snapshot()
        {
            var list = new List<Placed>();
            var instances = Instances();
            if (instances == null) return list;
            foreach (DictionaryEntry entry in instances)
            {
                var instance = entry.Value;
                if (instance == null) continue;
                var location = Traverse.Create(instance).Field("m_location").GetValue();
                if (location == null) continue;
                string name = Traverse.Create(location).Field("m_prefabName").GetValue<string>();
                if (string.IsNullOrEmpty(name)) continue;
                list.Add(new Placed
                {
                    Pos = Traverse.Create(instance).Field("m_position").GetValue<Vector3>(),
                    Radius = Traverse.Create(location).Field("m_exteriorRadius").GetValue<float>(),
                    Prefab = name,
                });
            }
            return list;
        }

        /// <summary>The nearest location in the snapshot whose reach covers the point, or empty.</summary>
        internal static string At(Vector3 pos, List<Placed> places)
        {
            string best = "";
            float bestDistance = float.MaxValue;
            if (places == null) return best;
            for (int i = 0; i < places.Count; i++)
            {
                float d = Geo.FlatDistance(places[i].Pos, pos);
                if (d >= bestDistance) continue;
                if (d > Mathf.Max(places[i].Radius, SearchRadius)) continue;
                best = places[i].Prefab;
                bestDistance = d;
            }
            return best;
        }

        /// <summary>
        /// What stands at a place, by the world's own reckoning, or empty. The nearest location
        /// whose reach covers the point wins; a point inside two of them takes the closer, which
        /// is the same rule a player standing there would use.
        /// </summary>
        internal static string At(Vector3 pos)
        {
            var instances = Instances();
            if (instances == null) return "";
            string best = "";
            float bestDistance = float.MaxValue;
            foreach (DictionaryEntry entry in instances)
            {
                var instance = entry.Value;
                if (instance == null) continue;
                var location = Traverse.Create(instance).Field("m_location").GetValue();
                if (location == null) continue;
                var at = Traverse.Create(instance).Field("m_position").GetValue<Vector3>();
                float d = Geo.FlatDistance(at, pos);
                if (d >= bestDistance) continue;
                float radius = Traverse.Create(location).Field("m_exteriorRadius").GetValue<float>();
                if (d > Mathf.Max(radius, SearchRadius)) continue;
                string name = Traverse.Create(location).Field("m_prefabName").GetValue<string>();
                if (string.IsNullOrEmpty(name)) continue;
                best = name;
                bestDistance = d;
            }
            return best;
        }

        // ── everything standing at a place, not only the location ──────────────────

        /// <summary>How far from a marker an object may stand and still be what that marker is for.</summary>
        private const float ObjectRadius = 6f;

        /// <summary>
        /// How many answers one place is worth sending. A thousand-marker question has to stay a
        /// message somebody can carry, and nothing a marker could agree with is more than a few
        /// objects deep -- but see the trim below for the one answer that is always last.
        /// </summary>
        internal const int Most = 8;

        /// <summary>
        /// Every prefab the world has at a place, nearest first: the objects standing there and
        /// the location whose ground it is.
        ///
        /// Asking only for the location is what made this dangerous. A location is true about the
        /// *place* -- the ruin, the camp, the village -- and a marker is usually about a *thing*
        /// standing on that place. Told only the location, a carrot patch inside a ruin became the
        /// ruin and drew as a house, and a greydwarf nest would have become the camp around it.
        /// Both are facts; neither is the answer to the question being asked.
        ///
        /// The objects come from the server's own ZDOs, which it holds for the whole world whether
        /// anyone is standing there or not -- that is what lets a map be repaired without anybody
        /// going to look. Nearest first, so the nest at arm's length is offered before the camp
        /// fifty meters off, and the caller takes the first that agrees with what it already knows.
        /// </summary>
        internal static List<string> StandingAt(Vector3 pos) => StandingAt(pos, null);

        internal static List<string> StandingAt(Vector3 pos, List<Placed> places)
        {
            var found = new List<KeyValuePair<float, string>>();
            try
            {
                var man = ZDOMan.instance;
                var scene = ZNetScene.instance;
                var bySector = man != null ? ObjectsBySector(man) : null;
                if (scene != null && bySector != null)
                {
                    // A place near a sector's edge has its object in the next one along, so the
                    // corners of the search box are sampled and the sectors they fall in are the
                    // ones read. Four lookups at most, usually one.
                    var sectors = new HashSet<uint>();
                    for (int dx = -1; dx <= 1; dx += 2)
                        for (int dz = -1; dz <= 1; dz += 2)
                            sectors.Add(ZoneSystem.GetSectorIndex(
                                pos + new Vector3(dx * ObjectRadius, 0f, dz * ObjectRadius)).Sector);
                    foreach (uint sector in sectors)
                    {
                        if (sector >= bySector.Length) continue;
                        var list = bySector[sector];
                        if (list == null) continue;
                        foreach (var zdo in list)
                        {
                            if (zdo == null) continue;
                            float d = Geo.FlatDistance(zdo.GetPosition(), pos);
                            if (d > ObjectRadius) continue;
                            var prefab = scene.GetPrefab(zdo.GetPrefab());
                            if (prefab == null || string.IsNullOrEmpty(prefab.name)) continue;
                            found.Add(new KeyValuePair<float, string>(d, prefab.name));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not read what stands at a place: " + e.Message);
            }

            // Inside the guard, not after it. This sat outside and was the one call on the path
            // that could throw its way out of the whole request: the handler's only log line
            // comes after the loop, so one bad place meant the server answered nothing at all
            // and said nothing about why.
            string here = "";
            try { here = places != null ? At(pos, places) : At(pos); }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not name the place at a marker: " + e.Message);
            }
            if (!string.IsNullOrEmpty(here)) found.Add(new KeyValuePair<float, string>(float.MaxValue, here));
            found.Sort((a, b) => a.Key.CompareTo(b.Key));

            var names = new List<string>();
            foreach (var kv in found) if (!names.Contains(kv.Value)) names.Add(kv.Value);

            // Trimmed here, where the place is still known, and never at its expense.
            //
            // The place this stands on goes in at float.MaxValue so that every object standing
            // on it is offered first -- a marker is usually about a thing, not about the ground.
            // Which means it is always last, and a trim that keeps the head of the list throws
            // away the one answer a marker about the *place* could ever have agreed with. A
            // ruined house has dozens of planks, walls and roof pieces within six meters, so the
            // location was dropped from every answer that had one: the server read 12,100 placed
            // locations, found one at 202 of 212 markers, and sent none of them.
            if (names.Count > Most)
            {
                bool hasPlace = !string.IsNullOrEmpty(here) && names[names.Count - 1] == here;
                int keep = hasPlace ? Most - 1 : Most;
                names.RemoveRange(keep, names.Count - keep);
                if (hasPlace) names.Add(here);
            }
            return names;
        }

        // ── the same question, asked from inside the world ──────────────────────────

        /// <summary>
        /// Are we where the world is? Only there do the generator's table of locations and the
        /// records of every object standing in it exist to be read at all.
        /// </summary>
        internal static bool HaveWorld => IsServer;

        /// <summary>The game's own index of every object in the world, by sector, or null.</summary>
        internal static List<ZDO>[] AllSectors()
        {
            try
            {
                var man = ZDOMan.instance;
                return man != null ? ObjectsBySector(man) : null;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not read the world's objects: " + e.Message);
                return null;
            }
        }

        /// <summary>One thing the world has at a place: what it is, where it is, how far it reaches.</summary>
        internal struct Standing
        {
            public string Prefab;
            public Vector3 Pos;
            /// <summary>The exterior radius where the generator placed this; zero for a thing standing about.</summary>
            public float Radius;
            /// <summary>
            /// Somebody built this: its record carries a creator. A campfire is one of this mod's
            /// markers only because a player lit it, and this is the same question the piece road
            /// asks the piece itself -- a prefab cannot say who put it there.
            /// </summary>
            public bool Built;
            /// <summary>
            /// How much of a deposit is left, as a fraction of its pieces, or -1 for anything that
            /// is not a deposit or has never been touched. Read here because it comes off the
            /// record, which the caller no longer has.
            /// </summary>
            public float Left;
        }

        /// <summary>
        /// Everything the world has at a place, nearest first, with the positions kept.
        ///
        /// <para>The same question StandingAt answers, and a richer answer. That one returns bare
        /// names, because names are all that fits in a reply to a client who cannot see the world.
        /// A caller standing in the world itself wants more: the position, so that a marker can go
        /// on the thing rather than on the spot somebody guessed at, and a reach of its own,
        /// because a pin dropped by hand lands near a thing rather than on it. The reply's shape
        /// was fixed in 4.2.0 and carries neither, and widening it would partition anyone still
        /// reading the old one -- so this is a second road to the same place rather than a change
        /// to the first.</para>
        /// </summary>
        internal static List<Standing> WhatStandsAt(Vector3 pos, float reach, List<Placed> places)
        {
            var found = new List<KeyValuePair<float, Standing>>();
            reach = Mathf.Max(reach, 1f);
            try
            {
                var man = ZDOMan.instance;
                var scene = ZNetScene.instance;
                var bySector = man != null ? ObjectsBySector(man) : null;
                if (scene != null && bySector != null)
                {
                    // A place near a sector's edge has its object in the next one along, so the
                    // search box is sampled at its corners, sides and middle and whichever sectors
                    // those fall in are the ones read. Nine lookups at most, usually one.
                    var sectors = new HashSet<uint>();
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            sectors.Add(ZoneSystem.GetSectorIndex(
                                pos + new Vector3(dx * reach, 0f, dz * reach)).Sector);
                    foreach (uint sector in sectors)
                    {
                        if (sector >= bySector.Length) continue;
                        var list = bySector[sector];
                        if (list == null) continue;
                        foreach (var zdo in list)
                        {
                            if (zdo == null || !zdo.IsValid()) continue;
                            var at = zdo.GetPosition();
                            float d = Geo.FlatDistance(at, pos);
                            if (d > reach) continue;
                            // Valheim builds the inside of a dungeon in a space of its own above
                            // y = 3000 (Character.InInterior is that line), at no particular
                            // relation to the ground above it -- and this measures flat distance,
                            // which cannot tell the two apart. So the yellow mushrooms in a
                            // burial chamber stood eight meters from a pin dropped on the hill
                            // above them and answered for it. A thing is an answer about a place
                            // only if it is on the same side of that line as the place. It is the
                            // rule recording already follows by refusing to record at all while
                            // the player is inside one.
                            if (Character.InInterior(at) != Character.InInterior(pos)) continue;
                            var prefab = scene.GetPrefab(zdo.GetPrefab());
                            if (prefab == null || string.IsNullOrEmpty(prefab.name)) continue;
                            found.Add(new KeyValuePair<float, Standing>(d, new Standing
                            {
                                Prefab = prefab.name,
                                Pos = at,
                                Radius = 0f,
                                Built = zdo.GetLong(ZDOVars.s_creator, 0L) != 0L,
                                Left = Catalog.Remaining(zdo),
                            }));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not read what stands at a place: " + e.Message);
            }

            try
            {
                AddPlacedAt(pos, reach, places, found);
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not name the place at a marker: " + e.Message);
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));

            var all = new List<Standing>();
            foreach (var kv in found)
            {
                bool had = false;
                for (int i = 0; i < all.Count && !had; i++)
                    had = string.Equals(all[i].Prefab, kv.Value.Prefab, StringComparison.OrdinalIgnoreCase);
                if (!had) all.Add(kv.Value);
            }
            return all;
        }

        /// <summary>
        /// Every location whose footprint covers the point, or whose middle is within reach, each
        /// at its own distance.
        ///
        /// <para>It used to be the nearest one only, and offered after everything standing on the
        /// ground rather than among it: the reasoning was that a thing at arm's length is what a
        /// marker points at more often than the ground it stands on. That is true of a marker this
        /// mod wrote and false of one a player dropped by eye. A hammer on a troll cave is a
        /// hammer about the cave, and offering the cave last let a mushroom answer for it; offering
        /// only the nearest location let a ruin ten meters away answer for a cave fifteen meters
        /// away. Both are now candidates, with their distances, and the caller ranks them.</para>
        /// </summary>
        private static void AddPlacedAt(Vector3 pos, float reach, List<Placed> places,
                                        List<KeyValuePair<float, Standing>> found)
        {
            var all = places ?? Snapshot();
            for (int i = 0; i < all.Count; i++)
            {
                if (string.IsNullOrEmpty(all[i].Prefab)) continue;
                float d = Geo.FlatDistance(all[i].Pos, pos);
                if (d > Mathf.Max(all[i].Radius, reach)) continue;
                found.Add(new KeyValuePair<float, Standing>(d, new Standing
                {
                    Prefab = all[i].Prefab,
                    Pos = all[i].Pos,
                    // A location always reaches at least as far as the catalog's own floor, so a
                    // caller can tell a placed location from a thing standing about by this alone.
                    Radius = Mathf.Max(all[i].Radius, 4f),
                }));
            }
        }

        private static void RPC_AskAll(long sender, ZPackage pkg)
        {
            if (!IsServer || pkg == null || ZRoutedRpc.instance == null)
            {
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] A question about the world arrived from {sender} and was not answered " +
                    $"(server: {IsServer}, package: {pkg != null}, routing: {ZRoutedRpc.instance != null}).");
                return;
            }
            int n = pkg.ReadInt();
            var reply = new ZPackage();
            reply.Write(n);
            int answered = 0;
            var places = Snapshot();
            for (int i = 0; i < n; i++)
            {
                var names = StandingAt(pkg.ReadVector3(), places);
                if (names.Count > 0) answered++;
                // The trim is StandingAt's, because only it knows which entry is the place. It
                // was done here, by cutting the tail, which is exactly where the place sits.
                reply.Write(names.Count);
                foreach (var name in names) reply.Write(name);
            }
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Asked what stands at {n} place(s) against {places.Count} placed location(s); " +
                $"found something at {answered}.");
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcTellAll, reply);
        }

        private static void RPC_TellAll(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            var all = new List<List<string>>();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
            {
                int m = pkg.ReadInt();
                var names = new List<string>(m);
                for (int j = 0; j < m; j++) names.Add(pkg.ReadString());
                all.Add(names);
            }
            var handler = OnStanding;
            OnStanding = null;
            if (handler == null)
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] The world answered, but nothing was waiting for the answer any more.");
            if (handler != null) handler(all);
        }

        internal static Action<List<List<string>>> OnStanding;

        /// <summary>
        /// Ask what stands at each of these places. The answer comes back in the same order,
        /// nearest first within each place, empty where the world has nothing to say.
        /// </summary>
        internal static bool AskStanding(List<Vector3> places, Action<List<List<string>>> answer)
        {
            if (places == null || places.Count == 0 || answer == null) return false;
            if (IsServer)
            {
                var all = new List<List<string>>(places.Count);
                foreach (var p in places) all.Add(StandingAt(p));
                answer(all);
                return true;
            }
            if (ZRoutedRpc.instance == null) return false;
            var pkg = new ZPackage();
            pkg.Write(places.Count);
            foreach (var p in places) pkg.Write(p);
            OnStanding = answer;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcAskAll, pkg);
            return true;
        }

        private static void RPC_Ask(long sender, ZPackage pkg)
        {
            if (!IsServer || pkg == null || ZRoutedRpc.instance == null) return;
            int n = pkg.ReadInt();
            var reply = new ZPackage();
            reply.Write(n);
            int found = 0;
            for (int i = 0; i < n; i++)
            {
                string name = At(pkg.ReadVector3());
                if (!string.IsNullOrEmpty(name)) found++;
                reply.Write(name ?? "");
            }
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Asked what stands at {n} place(s); named {found}.");
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcTell, reply);
        }

        private static void RPC_Tell(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            var names = new List<string>();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++) names.Add(pkg.ReadString());
            var handler = OnAnswer;
            OnAnswer = null;
            if (handler != null) handler(names);
        }

        /// <summary>
        /// Ask the server what stands at each of these places. The answer comes back in the same
        /// order, empty where the world has nothing to say. False when there is nobody to ask --
        /// on a server this reads its own table instead and answers at once.
        /// </summary>
        internal static bool Ask(List<Vector3> places, Action<List<string>> answer)
        {
            if (places == null || places.Count == 0 || answer == null) return false;
            if (IsServer)
            {
                var names = new List<string>(places.Count);
                foreach (var p in places) names.Add(At(p));
                answer(names);
                return true;
            }
            if (ZRoutedRpc.instance == null) return false;
            var pkg = new ZPackage();
            pkg.Write(places.Count);
            foreach (var p in places) pkg.Write(p);
            OnAnswer = answer;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcAsk, pkg);
            return true;
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_LocationNames_Patch
    {
        private static void Postfix()
        {
            LocationNames.OnAnswer = null;
            LocationNames.OnStanding = null;
            LocationNames.Register();
        }
    }
}
