using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Client-side view of the shared markers. Every shared marker is mirrored as a vanilla
    /// Minimap.PinData with m_ownerID = 0 (so vanilla never sweeps it as somebody else's pin)
    /// and m_save = true (so vanilla's right-click and check-off lookups can find it); the
    /// GetMapData and GetSharedMapData patches keep it out of the profile and the table.
    /// The PinData ↔ id maps let vanilla's own remove and check-off paths propagate to the
    /// server. The pin type of a shared marker is resolved locally from its icon key.
    /// </summary>
    internal static class ClientPins
    {
        private static readonly Dictionary<string, SharedPin> _shared = new Dictionary<string, SharedPin>();
        private static readonly Dictionary<string, Minimap.PinData> _pinById = new Dictionary<string, Minimap.PinData>();
        private static readonly Dictionary<Minimap.PinData, string> _idByPin = new Dictionary<Minimap.PinData, string>();
        private static readonly List<Suppression> _suppressions = new List<Suppression>();
        private static readonly Color AutoTint = new Color(1f, 0.93f, 0.72f, 1f);
        private static bool _applyingRemote;

        internal static bool InTableRead;
        internal static bool Synced { get; private set; }
        internal static int Count => _shared.Count;
        internal static int SuppressionCount => _suppressions.Count;
        internal static IEnumerable<SharedPin> All => _shared.Values;

        private static Minimap Map => Minimap.instance;

        internal static void Reset()
        {
            _shared.Clear();
            _pinById.Clear();
            _idByPin.Clear();
            _suppressions.Clear();
            Synced = false;
            InTableRead = false;
        }

        private static int LocalType(SharedPin shared)
        {
            if (!string.IsNullOrEmpty(shared.Icon)) return IconRegistry.TypeFor(shared.Icon);
            return PinTypes.IsCustom(shared.Type) ? (int)Minimap.PinType.Icon3 : shared.Type;
        }

        // ── applying server state ───────────────────────────────────────────────────

        internal static void ApplyFullSync(ZPackage pkg)
        {
            var pins = new Dictionary<string, SharedPin>();
            var supp = new List<Suppression>();
            PinStore.ReadAll(pkg, pins, supp);

            RemoveAllOurPinData();
            _shared.Clear();
            foreach (var kv in pins) _shared[kv.Key] = kv.Value;
            _suppressions.Clear();
            _suppressions.AddRange(supp);
            RebuildPins();
            Synced = true;
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Synced {_shared.Count} shared markers and {_suppressions.Count} erased spots.");
        }

        internal static void ApplyAdd(SharedPin pin)
        {
            if (pin == null || string.IsNullOrEmpty(pin.Id)) return;
            _shared[pin.Id] = pin;
            EnsurePin(pin);
        }

        internal static void ApplyRemove(string id, Suppression suppression)
        {
            if (suppression != null) _suppressions.Add(suppression);
            if (string.IsNullOrEmpty(id)) return;
            _shared.Remove(id);
            if (_pinById.TryGetValue(id, out var pin))
            {
                _pinById.Remove(id);
                _idByPin.Remove(pin);
                if (Map != null)
                {
                    _applyingRemote = true;
                    try { Map.RemovePin(pin); }
                    finally { _applyingRemote = false; }
                }
            }
        }

        internal static void ApplyUpdate(string id, string name, bool isChecked)
        {
            if (!_shared.TryGetValue(id, out var shared)) return;
            shared.Name = name ?? "";
            shared.Checked = isChecked;
            if (_pinById.TryGetValue(id, out var pin))
            {
                pin.m_checked = isChecked;
                if (Map != null) Access.PinUpdateRequired(Map) = true;
            }
        }

        internal static void ApplyWipe(bool autoOnly)
        {
            var ids = new List<string>();
            foreach (var kv in _shared) if (!autoOnly || kv.Value.Auto) ids.Add(kv.Key);
            foreach (var id in ids) ApplyRemove(id, null);
            _suppressions.Clear();
        }

        internal static void ClearSuppressions() => _suppressions.Clear();

        // ── creating and adopting markers locally ───────────────────────────────────

        /// <summary>Creates a new shared marker (optimistically shown at once) and sends it to the server.</summary>
        internal static SharedPin CreateShared(string name, Vector3 pos, string icon, bool auto)
        {
            var player = Player.m_localPlayer;
            string key = IconRegistry.Normalize(icon) ?? IconRegistry.FallbackKey;
            var pin = new SharedPin
            {
                Id = SharedPin.NewId(),
                OwnerId = player != null ? player.GetPlayerID() : 0L,
                Author = player != null ? player.GetPlayerName() : "",
                Name = name ?? "",
                Pos = pos,
                Type = IconRegistry.TypeFor(key),
                Icon = key,
                Auto = auto,
                Created = System.DateTime.UtcNow.Ticks,
            };
            ApplyAdd(pin);
            PinNetwork.SendAdd(pin);
            return pin;
        }

        /// <summary>Turns a vanilla local pin (a vanilla icon) into a shared one in place and uploads it.</summary>
        internal static SharedPin AdoptLocalPin(Minimap.PinData pinData, bool auto)
        {
            if (pinData == null || IsOurs(pinData) || PinTypes.IsCustom((int)pinData.m_type)) return null;
            var player = Player.m_localPlayer;
            var pin = new SharedPin
            {
                Id = SharedPin.NewId(),
                OwnerId = player != null ? player.GetPlayerID() : 0L,
                Author = player != null ? player.GetPlayerName() : "",
                Name = pinData.m_name ?? "",
                Pos = pinData.m_pos,
                Type = (int)pinData.m_type,
                Icon = IconRegistry.KeyForVanilla((int)pinData.m_type),
                Checked = pinData.m_checked,
                Auto = auto,
                Created = System.DateTime.UtcNow.Ticks,
            };
            pinData.m_save = true;
            pinData.m_ownerID = 0L;
            _shared[pin.Id] = pin;
            _pinById[pin.Id] = pinData;
            _idByPin[pinData] = pin.Id;
            PinNetwork.SendAdd(pin);
            return pin;
        }

        /// <summary>Uploads every local player-placed pin (vanilla icons) as shared markers.</summary>
        internal static int ImportLocalPins()
        {
            if (Map == null) return 0;
            int n = 0;
            foreach (var pin in new List<Minimap.PinData>(Access.Pins(Map)))
            {
                if (!pin.m_save || IsOurs(pin) || !PinTypes.IsPlayerPlaceable((int)pin.m_type) || PinTypes.IsCustom((int)pin.m_type)) continue;
                if (HasSharedPinNear(IconRegistry.KeyForVanilla((int)pin.m_type), pin.m_pos, 1f)) { Map.RemovePin(pin); continue; }
                if (AdoptLocalPin(pin, auto: false) != null) n++;
            }
            return n;
        }

        /// <summary>Deletes local (non-shared) player-placed pins, including stale ones imported from tables.</summary>
        internal static int ClearLocalPins()
        {
            if (Map == null) return 0;
            int n = 0;
            foreach (var pin in new List<Minimap.PinData>(Access.Pins(Map)))
            {
                if (IsOurs(pin) || !PinTypes.IsPlayerPlaceable((int)pin.m_type)) continue;
                Map.RemovePin(pin);
                n++;
            }
            return n;
        }

        // ── queries ─────────────────────────────────────────────────────────────────

        internal static bool IsOurs(Minimap.PinData pin) => pin != null && _idByPin.ContainsKey(pin);

        internal static bool TryGetShared(Minimap.PinData pin, out SharedPin shared)
        {
            shared = null;
            return pin != null && _idByPin.TryGetValue(pin, out var id) && _shared.TryGetValue(id, out shared);
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Any pin on the map (shared or local) with this icon within the radius.</summary>
        internal static bool HasPinNear(string icon, Vector3 pos, float radius)
        {
            if (Map == null) return false;
            int type = IconRegistry.TypeFor(icon);
            foreach (var pin in Access.Pins(Map))
                if ((int)pin.m_type == type && Geo.FlatDistance(pin.m_pos, pos) <= radius) return true;
            return false;
        }

        /// <summary>A pin with this icon and name that already carries a text label within the radius.</summary>
        internal static bool HasLabeledPinNear(string icon, string name, Vector3 pos, float radius)
        {
            if (Map == null || radius <= 0f) return false;
            int type = IconRegistry.TypeFor(icon);
            foreach (var pin in Access.Pins(Map))
            {
                if ((int)pin.m_type != type || string.IsNullOrEmpty(pin.m_name)) continue;
                if (SameName(pin.m_name, name) && Geo.FlatDistance(pin.m_pos, pos) <= radius) return true;
            }
            return false;
        }

        internal static bool HasSharedPinNear(string icon, Vector3 pos, float radius)
        {
            foreach (var pin in _shared.Values)
                if (IconRegistry.SameKey(pin.Icon, icon) && Geo.FlatDistance(pin.Pos, pos) <= radius) return true;
            return false;
        }

        /// <summary>A recorded marker with this icon was erased near here, so do not put it back.</summary>
        internal static bool IsSuppressed(string icon, Vector3 pos, float radius)
        {
            foreach (var s in _suppressions)
                if (IconRegistry.SameKey(s.Icon, icon) && Geo.FlatDistance(s.Pos, pos) <= radius) return true;
            return false;
        }

        // ── PinData bookkeeping ─────────────────────────────────────────────────────

        internal static void RebuildPins()
        {
            if (Map == null) return;
            foreach (var pin in _shared.Values) EnsurePin(pin);
        }

        private static void EnsurePin(SharedPin shared)
        {
            if (Map == null) return;
            if (_pinById.TryGetValue(shared.Id, out var existing))
            {
                if (Access.Pins(Map).Contains(existing))
                {
                    existing.m_checked = shared.Checked;
                    return;
                }
                _pinById.Remove(shared.Id);
                _idByPin.Remove(existing);
            }
            // m_save must be true: vanilla's pin-under-pointer lookup (right-click erase, click to
            // check off) ignores unsaved pins. The GetMapData patch below keeps them out of the profile.
            var pin = Map.AddPin(shared.Pos, (Minimap.PinType)LocalType(shared), shared.Name ?? "", true, shared.Checked, 0L);
            if (pin == null) return;
            _pinById[shared.Id] = pin;
            _idByPin[pin] = shared.Id;
        }

        private static void RemoveAllOurPinData()
        {
            if (Map != null)
            {
                _applyingRemote = true;
                try
                {
                    foreach (var pin in new List<Minimap.PinData>(_idByPin.Keys))
                        if (Access.Pins(Map).Contains(pin)) Map.RemovePin(pin);
                }
                finally { _applyingRemote = false; }
            }
            _pinById.Clear();
            _idByPin.Clear();
        }

        /// <summary>Vanilla removed a pin (right-click, and so on). If it was shared, tell the server.</summary>
        internal static void OnLocalRemove(Minimap.PinData pin)
        {
            if (_applyingRemote || pin == null) return;
            if (!_idByPin.TryGetValue(pin, out var id)) return;
            _idByPin.Remove(pin);
            _pinById.Remove(id);
            _shared.Remove(id);
            PinNetwork.SendRemove(id);
        }

        /// <summary>Vanilla cleared every pin (profile load). Our PinData objects are gone; records stay.</summary>
        internal static void OnPinsCleared()
        {
            _pinById.Clear();
            _idByPin.Clear();
        }

        /// <summary>A new pin got its name on the map screen: share it if configured.</summary>
        internal static void OnPinPlaced(Minimap.PinData pin)
        {
            if (pin == null || Map == null || !TgmConfig.SharePlacedPins.Value) return;
            if (!Access.Pins(Map).Contains(pin) || IsOurs(pin)) return;
            if (!PinTypes.IsPlayerPlaceable((int)pin.m_type)) return;
            AdoptLocalPin(pin, auto: false);
        }

        /// <summary>After a left click: push any check-off changes on shared markers.</summary>
        internal static void PushCheckedChanges()
        {
            foreach (var kv in _idByPin)
            {
                if (!_shared.TryGetValue(kv.Value, out var shared)) continue;
                if (kv.Key.m_checked == shared.Checked) continue;
                shared.Checked = kv.Key.m_checked;
                PinNetwork.SendUpdate(shared.Id, shared.Name, shared.Checked);
            }
        }

        /// <summary>Sweeps stale player-placed pins that vanilla imported from a table before this mod.</summary>
        internal static void SweepImportedPlayerPins()
        {
            if (Map == null || TgmConfig.TableCarriesPins.Value) return;
            foreach (var pin in new List<Minimap.PinData>(Access.Pins(Map)))
            {
                if (pin.m_ownerID == 0L || IsOurs(pin)) continue;
                if (!PinTypes.IsPlayerPlaceable((int)pin.m_type)) continue;
                Map.RemovePin(pin);
            }
        }

        internal static void TintPins()
        {
            foreach (var kv in _idByPin)
            {
                var pin = kv.Key;
                if (pin.m_iconElement == null) continue;
                if (_shared.TryGetValue(kv.Value, out var shared) && shared.Auto)
                    pin.m_iconElement.color = AutoTint;
            }
        }
    }

    // ── Minimap patches ────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), new[] { typeof(Minimap.PinData) })]
    internal static class Minimap_RemovePin_Patch
    {
        private static void Postfix(Minimap.PinData pin) => ClientPins.OnLocalRemove(pin);
    }

    [HarmonyPatch(typeof(Minimap), "ClearPins")]
    internal static class Minimap_ClearPins_Patch
    {
        private static void Postfix() => ClientPins.OnPinsCleared();
    }

    [HarmonyPatch(typeof(Minimap), "SetMapData")]
    internal static class Minimap_SetMapData_Patch
    {
        private static void Postfix()
        {
            ClientPins.SweepImportedPlayerPins();
            ClientPins.RebuildPins();
        }
    }

    // Left click crosses a marker off. When editing requires the map out, snapshot every pin's
    // checked state before the click and put it back afterwards (the click still closes the
    // pin name input as in vanilla); otherwise push any cross-off to the server.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    internal static class Minimap_OnMapLeftClick_Patch
    {
        private static bool Gated => TgmConfig.RequireMapOutToEdit.Value && !PocketMap.IsOut;

        private static void Prefix(Minimap __instance, ref Dictionary<Minimap.PinData, bool> __state)
        {
            __state = null;
            if (!Gated) return;
            __state = new Dictionary<Minimap.PinData, bool>();
            foreach (var pin in Access.Pins(__instance)) __state[pin] = pin.m_checked;
        }

        private static void Postfix(Minimap __instance, Dictionary<Minimap.PinData, bool> __state)
        {
            if (__state == null)
            {
                ClientPins.PushCheckedChanges();
                return;
            }
            bool reverted = false;
            foreach (var kv in __state)
            {
                if (kv.Key.m_checked == kv.Value) continue;
                kv.Key.m_checked = kv.Value;
                reverted = true;
            }
            if (reverted)
            {
                Access.PinUpdateRequired(__instance) = true;
                TheGreatestMapMod.Message("Take out your map to cross off a marker.");
            }
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick))]
    internal static class Minimap_OnMapDblClick_Patch
    {
        private static bool Prefix(Minimap __instance)
        {
            if (!TgmConfig.RequireMapOutToEdit.Value || PocketMap.IsOut) return true;
            var selected = Access.SelectedType(__instance);
            if (selected == Minimap.PinType.Ping || selected == Minimap.PinType.Death) return true;
            TheGreatestMapMod.Message("Take out your map to write on it.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    internal static class Minimap_RemovePinUnderPointer_Patch
    {
        private static bool Prefix()
        {
            if (!TgmConfig.RequireMapOutToEdit.Value || PocketMap.IsOut) return true;
            TheGreatestMapMod.Message("Take out your map to erase a marker.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnPinTextEntered))]
    internal static class Minimap_OnPinTextEntered_Patch
    {
        private static void Prefix(Minimap __instance, ref Minimap.PinData __state)
        {
            __state = Access.NamePin(__instance);
        }

        private static void Postfix(Minimap.PinData __state)
        {
            ClientPins.OnPinPlaced(__state);
        }
    }

    // Writing to a cartography table: hide player-placed pins from the serialiser by clearing
    // m_save for the duration of the call, then restore.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.GetSharedMapData))]
    internal static class Minimap_GetSharedMapData_Patch
    {
        private static void Prefix(Minimap __instance, ref List<Minimap.PinData> __state)
        {
            __state = null;
            if (TgmConfig.TableCarriesPins.Value) return;
            __state = new List<Minimap.PinData>();
            foreach (var pin in Access.Pins(__instance))
            {
                if (!pin.m_save || !PinTypes.IsPlayerPlaceable((int)pin.m_type)) continue;
                pin.m_save = false;
                __state.Add(pin);
            }
        }

        private static void Postfix(List<Minimap.PinData> __state)
        {
            if (__state == null) return;
            foreach (var pin in __state) pin.m_save = true;
        }
    }

    // Saving the player profile: keep shared markers out of it (the server is their only home),
    // by clearing m_save on ours for the duration of the call.
    [HarmonyPatch(typeof(Minimap), "GetMapData")]
    internal static class Minimap_GetMapData_Patch
    {
        private static void Prefix(Minimap __instance, ref List<Minimap.PinData> __state)
        {
            __state = new List<Minimap.PinData>();
            foreach (var pin in Access.Pins(__instance))
            {
                if (!pin.m_save || !ClientPins.IsOurs(pin)) continue;
                pin.m_save = false;
                __state.Add(pin);
            }
        }

        private static void Postfix(List<Minimap.PinData> __state)
        {
            if (__state == null) return;
            foreach (var pin in __state) pin.m_save = true;
        }
    }

    // Reading from a cartography table: the AddPin prefix below drops player-placed pins while
    // this flag is set, and the postfix sweeps stale imported ones.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.AddSharedMapData))]
    internal static class Minimap_AddSharedMapData_Patch
    {
        private static void Prefix() => ClientPins.InTableRead = true;

        private static void Postfix()
        {
            ClientPins.InTableRead = false;
            ClientPins.SweepImportedPlayerPins();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.AddPin))]
    internal static class Minimap_AddPin_Patch
    {
        private static bool Prefix(Minimap.PinType type, ref Minimap.PinData __result)
        {
            if (!ClientPins.InTableRead || TgmConfig.TableCarriesPins.Value) return true;
            if (!PinTypes.IsPlayerPlaceable((int)type)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Minimap_UpdatePins_Tint_Patch
    {
        private static void Postfix() => ClientPins.TintPins();
    }
}
