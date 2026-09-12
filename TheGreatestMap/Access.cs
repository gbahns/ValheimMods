using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Accessors for non-public game members. The publicized reference assembly lets the code
    /// compile against them, but the live assembly_valheim.dll still has them private or
    /// protected, and direct access then throws FieldAccessException / MethodAccessException at
    /// runtime (see Armory/LoadoutManager.cs). FieldRefAccess builds delegates that bypass the
    /// JIT visibility check; the methods go through reflection.
    /// </summary>
    internal static class Access
    {
        // ── Minimap ─────────────────────────────────────────────────────────────────
        internal static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins =
            AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        internal static readonly AccessTools.FieldRef<Minimap, Minimap.PinData> NamePin =
            AccessTools.FieldRefAccess<Minimap, Minimap.PinData>("m_namePin");
        internal static readonly AccessTools.FieldRef<Minimap, Minimap.PinType> SelectedType =
            AccessTools.FieldRefAccess<Minimap, Minimap.PinType>("m_selectedType");
        internal static readonly AccessTools.FieldRef<Minimap, bool> PinUpdateRequired =
            AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");
        internal static readonly AccessTools.FieldRef<Minimap, bool[]> VisibleIconTypes =
            AccessTools.FieldRefAccess<Minimap, bool[]>("m_visibleIconTypes");

        private static readonly MethodInfo _worldToMapPoint = AccessTools.Method(typeof(Minimap), "WorldToMapPoint");

        internal static void WorldToMapPoint(Minimap map, Vector3 p, out float mx, out float my)
        {
            var args = new object[] { p, 0f, 0f };
            _worldToMapPoint.Invoke(map, args);
            mx = (float)args[1];
            my = (float)args[2];
        }

        // ── Character / Humanoid / Player ───────────────────────────────────────────
        internal static readonly AccessTools.FieldRef<Character, ZNetView> NView =
            AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
        internal static readonly AccessTools.FieldRef<Character, ZSyncAnimation> ZAnim =
            AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        internal static readonly AccessTools.FieldRef<Humanoid, VisEquipment> VisEquip =
            AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");
        internal static readonly AccessTools.FieldRef<Player, int> InteractMask =
            AccessTools.FieldRefAccess<Player, int>("m_interactMask");

        private static readonly MethodInfo _showHandItems = AccessTools.Method(typeof(Humanoid), "ShowHandItems");

        internal static void ShowHandItems(Humanoid humanoid)
        {
            _showHandItems.Invoke(humanoid, new object[] { false, true });
        }

        // ── locations ───────────────────────────────────────────────────────────────
        internal static readonly AccessTools.FieldRef<LocationProxy, GameObject> ProxyInstance =
            AccessTools.FieldRefAccess<LocationProxy, GameObject>("m_instance");

        // ── networking ──────────────────────────────────────────────────────────────
        internal static readonly AccessTools.FieldRef<ZRoutedRpc, long> RoutedId =
            AccessTools.FieldRefAccess<ZRoutedRpc, long>("m_id");

        // ── cartography table ───────────────────────────────────────────────────────
        private static readonly MethodInfo _tableWrite = AccessTools.Method(typeof(MapTable), "OnWrite");

        /// <summary>Vanilla write (which reads first): ward check, serialise, RPC to the owner, "map saved" message.</summary>
        internal static bool TableWrite(MapTable table, Humanoid user)
        {
            return (bool)_tableWrite.Invoke(table, new object[] { null, user, null });
        }
    }
}
