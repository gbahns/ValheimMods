using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CaptainsLog
{
    // Snapshot of the most recent sample, shared with the HUD so it doesn't need to
    // re-derive ship state every frame.
    internal struct ShipSample
    {
        public float SpeedMs;
        public float SpeedKnots;
        public Ship.Speed SpeedSetting;
        public string Propulsion;
        public float Rudder;
        public float WindAngleDeg;
        public float WindAngleFactor;
        public float WindIntensity;
        public float SampledAtTime;
    }

    internal static class ShipTelemetry
    {
        // 1 knot = 1 nautical mile/hour = 1852m / 3600s.
        internal const float MetersPerSecondToKnots = 3600f / 1852f;

        private static StreamWriter _writer;
        private static float _lastSampleTime = -999f;

        // The dedicated server samples every sailed ship, so it throttles per ship. Never
        // pruned: one small entry per ship sailed during a server run.
        private static readonly Dictionary<ZDOID, float> _lastServerSampleTime = new Dictionary<ZDOID, float>();

        internal static ShipSample LastSample;
        internal static Ship LastShip;

        internal static bool IsDedicatedServer => ZNet.instance != null && ZNet.instance.IsDedicated();

        private static float Interval => Mathf.Max(0.05f, CaptainsLogMod.Instance.SampleIntervalSeconds.Value);

        // Client: the local player's ship only, feeding both the HUD and the CSV.
        internal static void SampleLocal(Ship ship)
        {
            var now = Time.time;
            if (now - _lastSampleTime < Interval) return;
            _lastSampleTime = now;

            var sample = Build(ship, ship.GetSpeed(), now);
            LastSample = sample;
            LastShip = ship;
            Write(ship, sample, Player.m_localPlayer.GetPlayerName());
        }

        // Dedicated server: any ship someone is sailing. The server doesn't simulate a ship a
        // player owns - it only interpolates the synced transform - so speed comes from the
        // velocity the owning client writes into the ship's ZDO every sync (ZSyncTransform.
        // OwnerSync), which is that client's exact rigidbody velocity.
        internal static void SampleServer(Ship ship)
        {
            if (!CaptainsLogMod.Instance.LoggingEnabled.Value) return;
            var nview = ship.GetComponent<ZNetView>(); // Ship.m_nview is private
            if (nview == null || !nview.IsValid()) return;
            var zdo = nview.GetZDO();

            // Ship.m_players (and so HaveValidUser/IsPlayerInBoat) comes from trigger events
            // on this machine's copy of the ship, so it isn't trusted here; the helm's synced
            // user id and the synced throttle are.
            var helmUser = ship.m_shipControlls != null ? ship.m_shipControlls.GetUser() : 0L;
            if (helmUser == 0L && ship.GetSpeedSetting() == Ship.Speed.Stop) return;

            var now = Time.time;
            var id = zdo.m_uid;
            if (_lastServerSampleTime.TryGetValue(id, out var last) && now - last < Interval) return;
            _lastServerSampleTime[id] = now;

            var speedMs = nview.IsOwner()
                ? ship.GetSpeed()
                : Vector3.Dot(zdo.GetVec3(ZDOVars.s_velHash, Vector3.zero), ship.transform.forward);

            Write(ship, Build(ship, speedMs, now), PlayerNameFor(helmUser, zdo.GetOwner()));
        }

        // The helmsman if someone is steering, otherwise the player whose client simulates the
        // ship (a sail left up keeps it moving with nobody at the helm).
        private static string PlayerNameFor(long helmUser, long ownerSession)
        {
            if (helmUser != 0L)
            {
                foreach (var player in Player.GetAllPlayers())
                {
                    if (player != null && player.GetPlayerID() == helmUser)
                        return player.GetPlayerName();
                }
            }
            var peer = ZNet.instance.GetPeer(ownerSession);
            return peer != null ? peer.m_playerName : string.Empty;
        }

        private static ShipSample Build(Ship ship, float speedMs, float now)
        {
            var speedSetting = ship.GetSpeedSetting();
            var windIntensity = EnvMan.instance != null ? EnvMan.instance.GetWindIntensity() : 0f;
            return new ShipSample
            {
                SpeedMs = speedMs,
                SpeedKnots = speedMs * MetersPerSecondToKnots,
                SpeedSetting = speedSetting,
                Propulsion = PropulsionLabel(speedSetting),
                // GetRudderValue() is where the rudder sits (-1 full left to 1 full right) and
                // what steers the ship; GetRudder() is only this frame's steering input.
                Rudder = ship.GetRudderValue(),
                // Ship.GetWindAngle() returns the same angle unwrapped into (-360, 0] rather
                // than a signed range; renormalize to (-180, 180] where 0 = tailwind (wind at
                // the stern) and +/-180 = headwind, which is what GetWindAngleFactor() peaks/
                // bottoms out against.
                WindAngleDeg = NormalizeSignedAngle(ship.GetWindAngle()),
                WindAngleFactor = ship.GetWindAngleFactor(),
                WindIntensity = windIntensity,
                SampledAtTime = now,
            };
        }

        private static void Write(Ship ship, ShipSample sample, string playerName)
        {
            if (!CaptainsLogMod.Instance.LoggingEnabled.Value) return;

            var pos = ship.transform.position;
            var heading = NormalizeAngle(ship.transform.eulerAngles.y);
            var windDir = EnvMan.instance != null ? EnvMan.instance.GetWindDir() : Vector3.zero;
            var windDirWorldDeg = NormalizeAngle(Mathf.Atan2(windDir.x, windDir.z) * Mathf.Rad2Deg);
            var biome = Heightmap.FindBiome(pos);
            var shipName = ship.gameObject.name.Replace("(Clone)", string.Empty).Trim();

            EnsureWriter();
            _writer.WriteLine(string.Join(",", new[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                CsvField(playerName),
                shipName,
                pos.x.ToString("F1", CultureInfo.InvariantCulture),
                pos.z.ToString("F1", CultureInfo.InvariantCulture),
                biome.ToString(),
                sample.SpeedMs.ToString("F2", CultureInfo.InvariantCulture),
                sample.SpeedKnots.ToString("F2", CultureInfo.InvariantCulture),
                sample.SpeedSetting.ToString(),
                sample.Propulsion,
                sample.Rudder.ToString("F2", CultureInfo.InvariantCulture),
                heading.ToString("F1", CultureInfo.InvariantCulture),
                sample.WindAngleDeg.ToString("F1", CultureInfo.InvariantCulture),
                sample.WindAngleFactor.ToString("F2", CultureInfo.InvariantCulture),
                sample.WindIntensity.ToString("F2", CultureInfo.InvariantCulture),
                windDirWorldDeg.ToString("F1", CultureInfo.InvariantCulture),
            }));
        }

        // Player names are free text and may contain commas or quotes.
        private static string CsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static float NormalizeAngle(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        private static float NormalizeSignedAngle(float deg)
        {
            deg = NormalizeAngle(deg);
            return deg > 180f ? deg - 360f : deg;
        }

        // Ship.IsSailUp() is *not* an independent "is the sail mesh raised" sensor - its
        // actual body is just `m_speed == Half || m_speed == Full`, so it carries no
        // information beyond SpeedSetting. Derive a human-readable propulsion mode from the
        // throttle enum directly instead: Back/Slow are oar-powered (reverse/forward), Half/
        // Full are sail-powered, per the RPC_Forward/RPC_Backward state transitions.
        private static string PropulsionLabel(Ship.Speed speed)
        {
            switch (speed)
            {
                case Ship.Speed.Back: return "Oars-Reverse";
                case Ship.Speed.Slow: return "Oars-Forward";
                case Ship.Speed.Half: return "Sail-Half";
                case Ship.Speed.Full: return "Sail-Full";
                default: return "Idle";
            }
        }

        private static void EnsureWriter()
        {
            if (_writer != null) return;

            var dir = Path.Combine(Paths.BepInExRootPath, "CaptainsLog");
            Directory.CreateDirectory(dir);
            var prefix = IsDedicatedServer ? "shiplog-server" : "shiplog";
            var path = Path.Combine(dir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _writer = new StreamWriter(path, append: false) { AutoFlush = true };
            _writer.WriteLine("Timestamp,Player,ShipName,PosX,PosZ,Biome,SpeedMs,SpeedKnots,SpeedSetting,Propulsion,Rudder,HeadingDeg,WindAngleDeg,WindAngleFactor,WindIntensity,WindDirWorldDeg");
            CaptainsLogMod.Log.LogInfo($"Logging ship telemetry to {path}");
        }

        internal static void Shutdown()
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    // Ship.CustomFixedUpdate is the per-tick update every loaded Ship instance receives
    // (Valheim's IMonoUpdater pattern), so this postfix runs for every ship in range each
    // physics step. A client bails unless the local player is aboard; a dedicated server
    // (no local player) considers every ship, which is how it logs all players' sailing.
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate_Telemetry
    {
        [HarmonyPostfix]
        private static void Postfix(Ship __instance)
        {
            if (ShipTelemetry.IsDedicatedServer)
            {
                ShipTelemetry.SampleServer(__instance);
                return;
            }
            if (Player.m_localPlayer == null) return;
            if (!__instance.IsPlayerInBoat(Player.m_localPlayer)) return;
            ShipTelemetry.SampleLocal(__instance);
        }
    }
}
