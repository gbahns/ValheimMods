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
            rpc.Register<ZPackage>(RpcAsk, RPC_Ask);
            rpc.Register<ZPackage>(RpcTell, RPC_Tell);
            rpc.Register<ZPackage>(RpcAskAll, RPC_AskAll);
            rpc.Register<ZPackage>(RpcTellAll, RPC_TellAll);
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Ready to answer questions about the world (this is {(IsServer ? "the server" : "a client")}).");
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ── the server's copy of what the generator placed ──────────────────────────

        // ZoneSystem.m_locationInstances is private; AccessTools so runtime enforcement does not
        // throw, and a Valheim that renames it costs a log line rather than a crash.
        private static readonly System.Reflection.FieldInfo _instancesField =
            AccessTools.Field(typeof(ZoneSystem), "m_locationInstances");

        private static IDictionary Instances()
        {
            var zone = ZoneSystem.instance;
            if (zone == null || _instancesField == null) return null;
            try { return _instancesField.GetValue(zone) as IDictionary; }
            catch (Exception) { return null; }
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
        internal static List<string> StandingAt(Vector3 pos)
        {
            var found = new List<KeyValuePair<float, string>>();
            try
            {
                var man = ZDOMan.instance;
                var scene = ZNetScene.instance;
                if (man != null && scene != null && man.m_objectsBySector != null)
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
                        if (sector >= man.m_objectsBySector.Length) continue;
                        var list = man.m_objectsBySector[sector];
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

            string here = At(pos);
            if (!string.IsNullOrEmpty(here)) found.Add(new KeyValuePair<float, string>(float.MaxValue, here));
            found.Sort((a, b) => a.Key.CompareTo(b.Key));

            var names = new List<string>();
            foreach (var kv in found) if (!names.Contains(kv.Value)) names.Add(kv.Value);
            return names;
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
            for (int i = 0; i < n; i++)
            {
                var names = StandingAt(pkg.ReadVector3());
                if (names.Count > 0) answered++;
                // Eight is more than any real place has that the catalog could know, and it keeps
                // a thousand-marker question from becoming a message nobody can carry.
                if (names.Count > 8) names.RemoveRange(8, names.Count - 8);
                reply.Write(names.Count);
                foreach (var name in names) reply.Write(name);
            }
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Asked what stands at {n} place(s); found something at {answered}.");
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
