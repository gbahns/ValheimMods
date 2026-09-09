using System;
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
        private const float MetersPerSecondToKnots = 3600f / 1852f;

        private static StreamWriter _writer;
        private static float _lastSampleTime = -999f;

        internal static ShipSample LastSample;

        // Called from a Harmony postfix on Ship.CustomFixedUpdate, which fires every
        // physics tick for every loaded ship - cheap to no-op here, so the per-ship
        // "is this the local player's ship" filtering happens in the patch itself.
        internal static void Sample(Ship ship)
        {
            var now = Time.time;
            var interval = Mathf.Max(0.05f, CaptainsLogMod.Instance.SampleIntervalSeconds.Value);
            if (now - _lastSampleTime < interval) return;
            _lastSampleTime = now;

            var pos = ship.transform.position;
            var heading = NormalizeAngle(ship.transform.eulerAngles.y);
            var windDir = EnvMan.instance != null ? EnvMan.instance.GetWindDir() : Vector3.zero;
            var windIntensity = EnvMan.instance != null ? EnvMan.instance.GetWindIntensity() : 0f;
            var windDirWorldDeg = NormalizeAngle(Mathf.Atan2(windDir.x, windDir.z) * Mathf.Rad2Deg);
            var biome = Heightmap.FindBiome(pos);
            var shipName = ship.gameObject.name.Replace("(Clone)", string.Empty).Trim();

            var speedMs = ship.GetSpeed();
            var speedSetting = ship.GetSpeedSetting();
            var sample = new ShipSample
            {
                SpeedMs = speedMs,
                SpeedKnots = speedMs * MetersPerSecondToKnots,
                SpeedSetting = speedSetting,
                Propulsion = PropulsionLabel(speedSetting),
                Rudder = ship.GetRudder(),
                // Ship.GetWindAngle() returns the same angle unwrapped into (-360, 0] rather
                // than a signed range; renormalize to (-180, 180] where 0 = tailwind (wind at
                // the stern) and +/-180 = headwind, which is what GetWindAngleFactor() peaks/
                // bottoms out against.
                WindAngleDeg = NormalizeSignedAngle(ship.GetWindAngle()),
                WindAngleFactor = ship.GetWindAngleFactor(),
                WindIntensity = windIntensity,
                SampledAtTime = now,
            };
            LastSample = sample;

            if (!CaptainsLogMod.Instance.LoggingEnabled.Value) return;

            EnsureWriter();
            _writer.WriteLine(string.Join(",", new[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
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
            var path = Path.Combine(dir, $"shiplog-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _writer = new StreamWriter(path, append: false) { AutoFlush = true };
            _writer.WriteLine("Timestamp,ShipName,PosX,PosZ,Biome,SpeedMs,SpeedKnots,SpeedSetting,Propulsion,Rudder,HeadingDeg,WindAngleDeg,WindAngleFactor,WindIntensity,WindDirWorldDeg");
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
    // physics step - we bail immediately unless the local player is actually aboard.
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate_Telemetry
    {
        [HarmonyPostfix]
        private static void Postfix(Ship __instance)
        {
            if (Player.m_localPlayer == null) return;
            if (!__instance.IsPlayerInBoat(Player.m_localPlayer)) return;
            ShipTelemetry.Sample(__instance);
        }
    }
}
