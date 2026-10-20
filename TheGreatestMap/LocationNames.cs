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
            if (rpc == null) return;
            rpc.Register<ZPackage>(RpcAsk, RPC_Ask);
            rpc.Register<ZPackage>(RpcTell, RPC_Tell);
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
            LocationNames.Register();
        }
    }
}
