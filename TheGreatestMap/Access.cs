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

        private static readonly MethodInfo _screenToWorldPoint = AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint", new[] { typeof(Vector3) });

        /// <summary>The world position under a screen point on the large map.</summary>
        internal static Vector3 ScreenToWorldPoint(Minimap map, Vector3 screen)
        {
            if (map == null || _screenToWorldPoint == null) return Vector3.zero;
            return (Vector3)_screenToWorldPoint.Invoke(map, new object[] { screen });
        }

        /// <summary>Vanilla's click radius for pins on the large map, in world meters (Minimap.PinInteractRadius).</summary>
        internal static float PinInteractRadius(Minimap map)
        {
            float r = map.m_removeRadius * (map.LargeZoom * 2f);
            if (ZInput.IsTouchActive()) r *= 1.3f;
            return r;
        }

        private static readonly MethodInfo _hidePinTextInput = AccessTools.Method(typeof(Minimap), "HidePinTextInput");

        /// <summary>Close the pin-name box, as vanilla does on a right-click.</summary>
        internal static void HidePinTextInput(Minimap map)
        {
            if (map == null || _hidePinTextInput == null) return;
            var ps = _hidePinTextInput.GetParameters();
            _hidePinTextInput.Invoke(map, ps.Length == 0 ? new object[0] : new object[] { false });
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
        internal static readonly AccessTools.FieldRef<MapTable, ZNetView> TableView =
            AccessTools.FieldRefAccess<MapTable, ZNetView>("m_nview");
        internal static readonly AccessTools.FieldRef<Minimap, System.Collections.BitArray> Explored =
            AccessTools.FieldRefAccess<Minimap, System.Collections.BitArray>("m_explored");
        internal static readonly AccessTools.FieldRef<Minimap, System.Collections.BitArray> ExploredOthers =
            AccessTools.FieldRefAccess<Minimap, System.Collections.BitArray>("m_exploredOthers");

        private static readonly MethodInfo _readExploredArray = AccessTools.Method(typeof(Minimap), "ReadExploredArray");

        /// <summary>The explored-area bits stored in a cartography table's data (version already read from the package).</summary>
        internal static List<bool> ReadExploredArray(Minimap map, ZPackage pkg, int version)
        {
            var enumType = _readExploredArray.GetParameters()[1].ParameterType;
            return (List<bool>)_readExploredArray.Invoke(map, new object[] { pkg, System.Enum.ToObject(enumType, version) });
        }

        private static readonly MethodInfo _tableWrite = AccessTools.Method(typeof(MapTable), "OnWrite");

        /// <summary>Vanilla write (which reads first): ward check, serialize, RPC to the owner, "map saved" message.</summary>
        internal static bool TableWrite(MapTable table, Humanoid user)
        {
            return (bool)_tableWrite.Invoke(table, new object[] { null, user, null });
        }
    }
}
