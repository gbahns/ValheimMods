using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Client-side view of the personal map. Every marker is mirrored as a vanilla
    /// Minimap.PinData with m_ownerID = 0 (so vanilla never sweeps it as somebody else's pin)
    /// and m_save = true (so vanilla's right-click and check-off lookups can find it); the
    /// GetMapData and GetSharedMapData patches keep it out of the vanilla profile and table data,
    /// because the personal map is saved separately with the character. Local edits go to the
    /// PersonalMap store; merges from the shared map or another player come in through ApplyMerge.
    /// The pin type of a marker is resolved locally from its icon key.
    /// </summary>
    internal static class ClientPins
    {
        private static readonly Dictionary<string, Minimap.PinData> _pinById = new Dictionary<string, Minimap.PinData>();
        private static readonly Dictionary<Minimap.PinData, string> _idByPin = new Dictionary<Minimap.PinData, string>();
        private static readonly Color AutoTint = new Color(1f, 0.93f, 0.72f, 1f);
        private static bool _applyingRemote;

        internal static bool InTableRead;
        internal static bool ApplyingRemote => _applyingRemote;
        internal static int Count => Store.Pins.Count;
        internal static int SuppressionCount => Store.Suppressions.Count;
        internal static int TombstoneCount => Store.Tombstones.Count;
        internal static IEnumerable<SharedPin> All => Store.Pins.Values;

        private static Minimap Map => Minimap.instance;
        private static MapStore Store => PersonalMap.Store;

        internal static void Reset()
        {
            PersonalMap.Reset();
            _pinById.Clear();
            _idByPin.Clear();
            InTableRead = false;
        }

        private static int LocalType(SharedPin shared)
        {
            if (!string.IsNullOrEmpty(shared.Icon)) return IconRegistry.TypeFor(shared.Icon);
            return PinTypes.IsCustom(shared.Type) ? (int)Minimap.PinType.Icon3 : shared.Type;
        }

        // ── merging in from the shared map or another player ────────────────────────

        internal static MergeResult ApplyMerge(MapStore incoming)
        {
            var result = Store.Merge(incoming);
            if (!result.Any) return result;
            foreach (var t in result.NewTombstones) RemovePinData(t.Id);
            foreach (var pin in result.ChangedPins) ReplacePinData(pin);
            PersonalMap.MarkDirty();
            return result;
        }

        internal static void ClearSuppressions()
        {
            Store.Suppressions.Clear();
            PersonalMap.MarkDirty();
        }

        // ── local edits ─────────────────────────────────────────────────────────────

        /// <summary>Record or place a marker on the personal map.</summary>
        internal static SharedPin CreateShared(string name, Vector3 pos, string icon, string kind, bool auto, bool isChecked = false)
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
                Kind = kind ?? "",
                Checked = isChecked,
                Auto = auto,
                Created = MapStore.Now,
            };
            Store.Upsert(pin);
            EnsurePin(pin);
            PersonalMap.Touch();
            return pin;
        }

        /// <summary>Turns a vanilla local pin (a vanilla icon) into a personal-map marker in place.</summary>
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
                Created = MapStore.Now,
            };
            pinData.m_save = true;
            pinData.m_ownerID = 0L;
            Store.Upsert(pin);
            _pinById[pin.Id] = pinData;
            _idByPin[pinData] = pin.Id;
            PersonalMap.Touch();
            return pin;
        }

        /// <summary>Adopts every vanilla local player-placed pin onto the personal map.</summary>
        internal static int ImportLocalPins()
        {
            if (Map == null) return 0;
            int n = 0;
            foreach (var pin in new List<Minimap.PinData>(Access.Pins(Map)))
            {
                if (!pin.m_save || IsOurs(pin) || !PinTypes.IsPlayerPlaceable((int)pin.m_type) || PinTypes.IsCustom((int)pin.m_type)) continue;
                if (HasOwnPinNear(IconRegistry.KeyForVanilla((int)pin.m_type), pin.m_pos, 1f)) { Map.RemovePin(pin); continue; }
                if (AdoptLocalPin(pin, auto: false) != null) n++;
            }
            return n;
        }

        /// <summary>Deletes vanilla local player-placed pins that are not on the personal map, including stale ones imported from tables.</summary>
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

        /// <summary>Crosses off the nearest marker with this icon within the radius. False if none or already crossed off.</summary>
        internal static bool TryCheckOff(string icon, Vector3 pos, float radius)
        {
            SharedPin best = null;
            float bestDistance = float.MaxValue;
            foreach (var pin in Store.Pins.Values)
            {
                if (pin.Checked || !IconRegistry.SameKey(pin.Icon, icon)) continue;
                float d = Geo.FlatDistance(pin.Pos, pos);
                if (d <= radius && d < bestDistance) { bestDistance = d; best = pin; }
            }
            if (best == null) return false;
            best.Checked = true;
            Store.Upsert(best);
            if (_pinById.TryGetValue(best.Id, out var pinData))
            {
                pinData.m_checked = true;
                if (Map != null) Access.PinUpdateRequired(Map) = true;
            }
            PersonalMap.Touch();
            return true;
        }

        /// <summary>Erase recorded markers of one kind within a radius of a point (maintenance; tombstones carry it to everyone).</summary>
        internal static int EraseKindNear(Category kind, Vector3 pos, float radius)
        {
            var ids = new List<string>();
            foreach (var pin in Store.Pins.Values)
                if (pin.Auto && KindOf(pin) == kind && Geo.FlatDistance(pin.Pos, pos) <= radius) ids.Add(pin.Id);
            foreach (var id in ids)
            {
                RemovePinData(id);
                Store.Delete(id, out _);
            }
            if (ids.Count > 0) PersonalMap.Touch();
            return ids.Count;
        }

        /// <summary>The kind of a recorded marker; markers from before kinds were stored are inferred from the icon where possible.</summary>
        internal static Category? KindOf(SharedPin pin)
        {
            if (pin.KindCategory.HasValue) return pin.KindCategory;
            if (pin.Auto && IconRegistry.SameKey(pin.Icon, "pin:Icon1")) return Category.Structure;
            return null;
        }

        /// <summary>
        /// Apply the current label rules to recorded markers that already exist: within each
        /// kind, the oldest marker of a same-named cluster keeps its label and the others lose
        /// theirs. Labels are only ever removed, never added. Returns the count changed.
        /// </summary>
        internal static int ApplyLabelRules(Category? only)
        {
            int changed = 0;
            foreach (var kind in Categories.All)
            {
                if (only.HasValue && only.Value != kind) continue;
                float spacing = TgmConfig.LabelSpacing.TryGetValue(kind, out var s) ? s.Value : 0f;
                if (spacing == 0f) continue; // always label: nothing to strip
                var markers = new List<SharedPin>();
                foreach (var pin in Store.Pins.Values)
                    if (pin.Auto && !string.IsNullOrEmpty(pin.Name) && KindOf(pin) == kind) markers.Add(pin);
                markers.Sort((a, b) => a.Created.CompareTo(b.Created));
                var kept = new List<SharedPin>();
                foreach (var pin in markers)
                {
                    bool strip = spacing < 0f;
                    if (!strip)
                    {
                        foreach (var k in kept)
                        {
                            if (SameName(k.Name, pin.Name) && Geo.FlatDistance(k.Pos, pin.Pos) <= spacing) { strip = true; break; }
                        }
                    }
                    if (!strip) { kept.Add(pin); continue; }
                    pin.Name = "";
                    Store.Upsert(pin);
                    ReplacePinData(pin);
                    changed++;
                }
            }
            if (changed > 0) PersonalMap.Touch();
            return changed;
        }

        // ── queries ─────────────────────────────────────────────────────────────────

        internal static bool IsOurs(Minimap.PinData pin) => pin != null && _idByPin.ContainsKey(pin);

        private static bool SameName(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Any pin on the map (personal or vanilla local) with this icon within the radius.</summary>
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

        internal static bool HasOwnPinNear(string icon, Vector3 pos, float radius)
        {
            foreach (var pin in Store.Pins.Values)
                if (IconRegistry.SameKey(pin.Icon, icon) && Geo.FlatDistance(pin.Pos, pos) <= radius) return true;
            return false;
        }

        /// <summary>A recorded marker with this icon was erased near here, so do not put it back.</summary>
        internal static bool IsSuppressed(string icon, Vector3 pos, float radius)
        {
            foreach (var s in Store.Suppressions)
                if (IconRegistry.SameKey(s.Icon, icon) && Geo.FlatDistance(s.Pos, pos) <= radius) return true;
            return false;
        }

        // ── PinData bookkeeping ─────────────────────────────────────────────────────

        internal static void RebuildPins()
        {
            if (Map == null) return;
            foreach (var pin in Store.Pins.Values) EnsurePin(pin);
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

        /// <summary>Drop the marker's vanilla pin and add it again from the record (name or icon changes).</summary>
        private static void ReplacePinData(SharedPin shared)
        {
            RemovePinData(shared.Id);
            EnsurePin(shared);
        }

        private static void RemovePinData(string id)
        {
            if (!_pinById.TryGetValue(id, out var old)) return;
            _pinById.Remove(id);
            _idByPin.Remove(old);
            if (Map == null) return;
            _applyingRemote = true;
            try { if (Access.Pins(Map).Contains(old)) Map.RemovePin(old); }
            finally { _applyingRemote = false; }
        }

        /// <summary>Vanilla removed a pin (right-click, and so on). If it is ours, erase it on the personal map.</summary>
        internal static void OnLocalRemove(Minimap.PinData pin)
        {
            if (_applyingRemote || pin == null) return;
            if (!_idByPin.TryGetValue(pin, out var id)) return;
            _idByPin.Remove(pin);
            _pinById.Remove(id);
            if (Store.Delete(id, out _)) PersonalMap.Touch();
        }

        /// <summary>Vanilla cleared every pin (profile load). Our PinData objects are gone; records stay.</summary>
        internal static void OnPinsCleared()
        {
            _pinById.Clear();
            _idByPin.Clear();
        }

        /// <summary>A new pin got its name on the map screen: put it on the personal map if configured.</summary>
        internal static void OnPinPlaced(Minimap.PinData pin)
        {
            if (pin == null || Map == null || !TgmConfig.SharePlacedPins.Value) return;
            if (!Access.Pins(Map).Contains(pin) || IsOurs(pin)) return;
            if (!PinTypes.IsPlayerPlaceable((int)pin.m_type)) return;
            AdoptLocalPin(pin, auto: false);
        }

        /// <summary>After a left click: record any check-off changes.</summary>
        internal static void PushCheckedChanges()
        {
            bool changed = false;
            foreach (var kv in _idByPin)
            {
                if (!Store.Pins.TryGetValue(kv.Value, out var shared)) continue;
                if (kv.Key.m_checked == shared.Checked) continue;
                shared.Checked = kv.Key.m_checked;
                Store.Upsert(shared);
                changed = true;
            }
            if (changed) PersonalMap.Touch();
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

        /// <summary>
        /// After vanilla has laid out the markers: tint recorded ones, scale each kind to its
        /// configured size, and hide kinds that are switched off on the small map.
        /// </summary>
        internal static void StylePins(Minimap map)
        {
            bool smallMap = map != null && map.m_mode != Minimap.MapMode.Large;
            foreach (var kv in _idByPin)
            {
                var pin = kv.Key;
                if (pin.m_uiElement == null) continue;
                if (!Store.Pins.TryGetValue(kv.Value, out var shared)) continue;
                var kind = shared.KindCategory;
                bool hidden = TgmConfig.IsIconHidden(shared.Icon);
                if (!hidden && kind.HasValue)
                {
                    if (TgmConfig.ShowKind.TryGetValue(kind.Value, out var showKind) && !showKind.Value) hidden = true;
                    else if (smallMap && TgmConfig.ShowOnMinimap.TryGetValue(kind.Value, out var show) && !show.Value) hidden = true;
                }
                SetMarkerActive(pin, !hidden);
                if (hidden) continue;
                float scale = 1f;
                if (kind.HasValue && TgmConfig.MarkerSize.TryGetValue(kind.Value, out var size))
                    scale = Mathf.Clamp(size.Value, 20, 100) / 100f;
                pin.m_uiElement.localScale = new Vector3(scale, scale, 1f);
                if (pin.m_iconElement != null && shared.Auto)
                    pin.m_iconElement.color = AutoTint;
            }
        }

        private static void SetMarkerActive(Minimap.PinData pin, bool active)
        {
            var icon = pin.m_uiElement.gameObject;
            if (icon.activeSelf != active) icon.SetActive(active);
            var label = pin.m_NamePinData != null ? pin.m_NamePinData.PinNameGameObject : null;
            if (label != null && label.activeSelf != active) label.SetActive(active);
        }
    }

    // ── Minimap patches ────────────────────────────────────────────────────────────

    // Erasing one of our markers is deliberate or not at all: off unless the server allows it,
    // and then only with Shift held while right-clicking. Our own bookkeeping (merges, label
    // changes) removes vanilla pins with the remote flag set and is never gated.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), new[] { typeof(Minimap.PinData) })]
    internal static class Minimap_RemovePin_Patch
    {
        private static bool Prefix(Minimap.PinData pin)
        {
            if (!ClientPins.IsOurs(pin) || ClientPins.ApplyingRemote) return true;
            if (!TgmConfig.AllowErasingMarkers.Value)
            {
                TheGreatestMapMod.Message("Erasing map markers is switched off on this server.");
                return false;
            }
            if (!ZInput.GetKey(KeyCode.LeftShift) && !ZInput.GetKey(KeyCode.RightShift))
            {
                TheGreatestMapMod.Message("Hold Shift and right-click to erase a marker.");
                return false;
            }
            return true;
        }

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
    // pin name input as in vanilla); otherwise record any cross-off on the personal map.
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

    // Saving the vanilla player profile: keep personal-map markers out of it (they are saved
    // separately with the character), by clearing m_save on ours for the duration of the call.
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
    internal static class Minimap_UpdatePins_Style_Patch
    {
        private static void Postfix(Minimap __instance) => ClientPins.StylePins(__instance);
    }
}
