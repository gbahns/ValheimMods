using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace StationExtensionGuard
{
    [BepInPlugin(ModGuid, "Station Extension Guard", "1.0.0")]
    [BepInProcess("valheim.exe")]
    public class StationExtensionGuardMod : BaseUnityPlugin
    {
        public const string ModGuid = "DeathMonger.StationExtensionGuard";
        internal static ManualLogSource Log;

        // Side tables keyed by Unity instance ID, populated when each StationExtension's
        // Awake / OnDestroy runs.  We keep them around after destruction so we can still
        // attribute a swept dead-list entry back to the prefab/mod it came from.
        internal static readonly Dictionary<int, string> RegisteredNames = new Dictionary<int, string>();
        internal static readonly HashSet<int> OnDestroyFired = new HashSet<int>();

        // Private Valheim fields are accessible at compile time via the publicized DLL, but
        // Harmony's runtime dynamic methods enforce the real assembly's access modifiers.
        // Use AccessTools.FieldRefAccess to generate visibility-bypassing accessors once.
        internal static readonly AccessTools.FieldRef<List<StationExtension>> AllExtensionsRef =
            AccessTools.StaticFieldRefAccess<List<StationExtension>>(
                AccessTools.Field(typeof(StationExtension), "m_allExtensions"));

        internal static readonly AccessTools.FieldRef<StationExtension, CraftingStation> CraftingStationOf =
            AccessTools.FieldRefAccess<StationExtension, CraftingStation>("m_craftingStation");

        private readonly Harmony harmony = new Harmony(ModGuid);

        private void Awake()
        {
            Log = Logger;
            harmony.PatchAll();
            Log.LogInfo("Station Extension Guard armed.");
        }
    }

    // Postfix on StationExtension.Awake: capture name + position context at registration
    // time and stash it in a side table keyed by instance ID.  Awake is the standard place
    // Valheim's StationExtension registers itself into the static m_allExtensions list,
    // so this fires once per extension entering the world.
    [HarmonyPatch(typeof(StationExtension), "Awake")]
    internal static class StationExtension_Awake_Trace
    {
        [HarmonyPostfix]
        private static void Postfix(StationExtension __instance)
        {
            int id = __instance.GetInstanceID();
            string name = __instance.gameObject != null ? __instance.gameObject.name : "(no gameObject)";
            var station = StationExtensionGuardMod.CraftingStationOf(__instance);
            string stationName = station != null ? station.name : "(none)";

            StationExtensionGuardMod.RegisteredNames[id] = name;
            StationExtensionGuardMod.Log.LogInfo(
                $"StationExtension Awake: name='{name}' instanceID={id} station='{stationName}' " +
                $"totalCount={StationExtensionGuardMod.AllExtensionsRef().Count}");
        }
    }

    // Postfix on StationExtension.OnDestroy: record that OnDestroy actually fired for
    // this instance.  At sweep time we cross-reference: a dead entry whose OnDestroy
    // never fired is a strong signal the mod isn't cleaning up — point the blame there.
    [HarmonyPatch(typeof(StationExtension), "OnDestroy")]
    internal static class StationExtension_OnDestroy_Trace
    {
        [HarmonyPostfix]
        private static void Postfix(StationExtension __instance)
        {
            int id = __instance.GetInstanceID();
            StationExtensionGuardMod.OnDestroyFired.Add(id);
            StationExtensionGuardMod.RegisteredNames.TryGetValue(id, out string name);
            StationExtensionGuardMod.Log.LogInfo(
                $"StationExtension OnDestroy: name='{name ?? "(unrecorded)"}' instanceID={id}");
        }
    }

    // Prefix on CraftingStation.GetExtensions: scrub null/destroyed entries from the
    // static m_allExtensions list before vanilla iterates and NREs.  We only LOG when
    // we actually find something to sweep — vanilla calls this every FixedUpdate near
    // any station, so unconditional logging would flood at 50 Hz.
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetExtensions))]
    internal static class CraftingStation_GetExtensions_NullGuard
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            var list = StationExtensionGuardMod.AllExtensionsRef();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var ext = list[i];
                // Unity's overloaded == returns true for both true-null AND Unity-destroyed
                // objects, which is exactly what vanilla's iteration assumes.  We want to
                // distinguish the two cases for diagnostic attribution.
                if (ext == null)
                {
                    if (!ReferenceEquals(ext, null))
                    {
                        // C# ref still valid → Unity-destroyed but reference leaked into the
                        // list.  GetInstanceID still works on destroyed Unity objects.
                        int id = ext.GetInstanceID();
                        StationExtensionGuardMod.RegisteredNames.TryGetValue(id, out string name);
                        bool sawOnDestroy = StationExtensionGuardMod.OnDestroyFired.Contains(id);
                        StationExtensionGuardMod.Log.LogWarning(
                            $"Sweeping destroyed StationExtension: name='{name ?? "(no record)"}'" +
                            $" instanceID={id} OnDestroyFired={sawOnDestroy}" +
                            $" listCount={list.Count}");
                    }
                    else
                    {
                        // Truly-null entry.  No way to attribute — something set list[i] = null
                        // directly, or the slot was never initialized.
                        StationExtensionGuardMod.Log.LogWarning(
                            $"Sweeping null StationExtension entry (no C# reference)" +
                            $" listCount={list.Count}");
                    }
                    list.RemoveAt(i);
                }
            }
        }
    }
}
