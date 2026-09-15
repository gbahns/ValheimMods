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
        // The portal markers currently drawn, rebuilt by each styling pass. Portals are painted
        // every frame instead of here, because their color can fade over time.
        private static readonly List<Minimap.PinData> _portalPins = new List<Minimap.PinData>();
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
            MarkerToggle.Reset();
            MarkerMenu.Close();
            MarkerTooltip.Reset();
            IconRegistry.ForgetPicks();
            Reveals.Reset();
            Portals.Reset();
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
            Reveals.Add(pin.Id);
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

        /// <summary>Cross a marker off, or uncross it, from the marker menu.</summary>
        internal static void SetChecked(string id, bool isChecked)
        {
            if (id == null || !Store.Pins.TryGetValue(id, out var shared) || shared.Checked == isChecked) return;
            shared.Checked = isChecked;
            Store.Upsert(shared);
            if (_pinById.TryGetValue(id, out var pinData))
            {
                pinData.m_checked = isChecked;
                if (Map != null) Access.PinUpdateRequired(Map) = true;
            }
            PersonalMap.Touch();
        }

        /// <summary>
        /// TEMPORARY, added 2026-09-15: delete this and its two automatic call sites once every
        /// player's map has been through it (Greg expects a couple of days). New markers are
        /// written with the right icon, so this only exists for ones already out there.
        ///
        /// Repair dungeon markers that took the kind's fallback icon, the swamp crypt key, because
        /// their location was not in the catalog. Only markers currently wearing that icon are
        /// touched, and only when the name suggests something else, so a real sunken crypt keeps
        /// it. The change merges like any other, so one player fixing them fixes them for all.
        /// </summary>
        internal static int RepairDungeonIcons()
        {
            int changed = 0;
            foreach (var pin in new List<SharedPin>(Store.Pins.Values))
            {
                if (!pin.Auto || KindOf(pin) != Category.Dungeon) continue;
                if (!IconRegistry.SameKey(pin.Icon, "CryptKey")) continue;
                string better = Catalog.IconFromDungeonName(pin.Name);
                if (better == null || IconRegistry.SameKey(better, pin.Icon)) continue;
                pin.Icon = IconRegistry.Normalize(better);
                pin.Type = IconRegistry.TypeFor(pin.Icon);
                Store.Upsert(pin);
                ReplacePinData(pin);
                changed++;
            }
            if (changed > 0) PersonalMap.Touch();
            return changed;
        }

        /// <summary>
        /// Correct a dungeon marker's icon from the place itself, while the player is looking at
        /// it. The repair that works from the marker's name cannot help here, because dungeons
        /// carry no label by default and the label rules blank the name, so a marker written
        /// before its cave was in the catalog has nothing left to identify it. Standing in front
        /// of the thing does: the classifier knows the prefab, so the marker is simply corrected.
        /// Scoped to dungeons on purpose. Within a kind like herbs the icons legitimately differ
        /// from one marker to the next, and "correcting" a dandelion into a thistle would be a
        /// bug, whereas one dungeon entrance has exactly one right picture.
        /// Returns true when a marker was corrected, meaning no new one should be written.
        /// </summary>
        internal static bool CorrectDungeonIconNear(Found found, float radius)
        {
            if (found == null || found.Cat != Category.Dungeon) return false;
            SharedPin best = null;
            float bestDistance = radius;
            foreach (var pin in Store.Pins.Values)
            {
                if (!pin.Auto || KindOf(pin) != Category.Dungeon) continue;
                float d = Geo.FlatDistance(pin.Pos, found.DedupeCenter);
                if (d <= bestDistance) { bestDistance = d; best = pin; }
            }
            if (best == null || IconRegistry.SameKey(best.Icon, found.Icon)) return false;
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Correcting a dungeon marker's icon from {best.Icon} to {found.Icon} ({found.Name}).");
            best.Icon = IconRegistry.Normalize(found.Icon);
            best.Type = IconRegistry.TypeFor(best.Icon);
            Store.Upsert(best);
            ReplacePinData(best);
            PersonalMap.Touch();
            return true;
        }

        /// <summary>Rename a marker on the personal map (the portal reconciler; the change merges like any other).</summary>
        internal static void RenameMarker(string id, string name)
        {
            if (id == null || !Store.Pins.TryGetValue(id, out var shared) || shared.Name == name) return;
            shared.Name = name ?? "";
            Store.Upsert(shared);
            ReplacePinData(shared);
            PersonalMap.Touch();
        }

        /// <summary>
        /// Erase a marker that no longer matches the world, without suppressing the spot: the
        /// thing may come back (a portal rebuilt where one stood) and should be recorded again.
        /// </summary>
        internal static void RemoveStale(string id)
        {
            if (id == null || !Store.Pins.ContainsKey(id)) return;
            RemovePinData(id);
            Store.Delete(id, out _, suppress: false);
            PersonalMap.Touch();
        }

        /// <summary>
        /// Cross off the marker for something that has just been used up: a deposit mined out, or
        /// a plant picked that never grows back. The marker stays, because where a place has been
        /// cleared is worth knowing, and "Show Cleared Deposits" decides whether it is drawn.
        /// Returns whether anything was crossed off.
        /// </summary>
        internal static bool MarkCleared(string icon, Vector3 pos, float radius)
        {
            SharedPin best = null;
            float bestDistance = radius;
            foreach (var pin in Store.Pins.Values)
            {
                if (!pin.Auto || pin.Checked || !IconRegistry.SameKey(pin.Icon, icon)) continue;
                float d = Geo.FlatDistance(pin.Pos, pos);
                if (d <= bestDistance) { bestDistance = d; best = pin; }
            }
            if (best == null) return false;
            SetChecked(best.Id, true);
            return true;
        }

        /// <summary>
        /// Who may remove a marker, which depends on what kind of thing it is.
        ///
        /// A recorded marker stands for something really in the world, and the world does not
        /// forget: erase the marker and the next player to walk past records it again. Removing one
        /// is therefore a repair, for a marker that is wrong, rather than an everyday action, and it
        /// stays behind the server's "Allow Erasing Markers" switch.
        ///
        /// A marker somebody placed by hand stands for nothing but their own note, so the person who
        /// wrote it can always take it back. Anyone else needs the server to allow erasing, since to
        /// them it is somebody else's writing.
        /// </summary>
        internal static bool CanDelete(SharedPin pin, out string why)
        {
            why = null;
            if (pin == null) return false;
            bool allowed = TgmConfig.AllowErasingMarkers.Value;
            if (!pin.Auto)
            {
                var player = Player.m_localPlayer;
                if (player != null && pin.OwnerId == player.GetPlayerID()) return true;
                if (allowed) return true;
                why = "only whoever placed it can remove it";
                return false;
            }
            if (allowed) return true;
            why = "erasing recorded markers is off on this server";
            return false;
        }

        /// <summary>Erase one marker for everyone (a tombstone carries it at the next merge). Not gated: the caller checks the server setting.</summary>
        internal static bool EraseById(string id)
        {
            if (id == null || !Store.Pins.ContainsKey(id)) return false;
            RemovePinData(id);
            Store.Delete(id, out _);
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
            if (!pin.Auto) return null;
            var inferred = KindInference.FromIcon(pin.Icon);
            if (inferred.HasValue)
            {
                pin.SetKind(inferred.Value.ToString()); // remembered locally; Modified untouched so it is not a "change" to others
                PersonalMap.MarkDirty();
            }
            return inferred;
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

        /// <summary>The marker behind one of our vanilla pins, or null for a pin that is not ours.</summary>
        internal static SharedPin SharedFor(Minimap.PinData pin)
        {
            if (pin == null || !_idByPin.TryGetValue(pin, out var id)) return null;
            return Store.Pins.TryGetValue(id, out var shared) ? shared : null;
        }

        /// <summary>
        /// Vanilla's own lookup for a click on the large map (Minimap.GetClosestPin): the closest
        /// saved, visible pin within the radius, ours or not, so a vanilla pin that is nearer than
        /// one of ours still gets vanilla's treatment.
        /// </summary>
        internal static Minimap.PinData ClosestPin(Minimap map, Vector3 pos, float radius)
        {
            if (map == null) return null;
            Minimap.PinData best = null;
            float bestDistance = float.MaxValue;
            foreach (var pin in Access.Pins(map))
            {
                if (!pin.m_save || pin.m_uiElement == null || !pin.m_uiElement.gameObject.activeInHierarchy) continue;
                float d = Utils.DistanceXZ(pos, pin.m_pos);
                if (d < radius && d < bestDistance) { best = pin; bestDistance = d; }
            }
            return best;
        }

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

        /// <summary>A marker of ours near here; a null icon matches any icon.</summary>
        internal static bool HasOwnPinNear(string icon, Vector3 pos, float radius)
        {
            foreach (var pin in Store.Pins.Values)
                if ((icon == null || IconRegistry.SameKey(pin.Icon, icon)) && Geo.FlatDistance(pin.Pos, pos) <= radius) return true;
            return false;
        }

        /// <summary>
        /// What markers of this kind are near a point, for the look report: the icon each one
        /// carries, how far off it is, and whether it is crossed off. A marker that is present but
        /// wearing a different icon, or crossed off as cleared, looks exactly like a missing marker
        /// on screen, and neither shows up in a yes-or-no duplicate check.
        /// </summary>
        internal static string DescribeNear(Category kind, Vector3 pos, float radius)
        {
            var parts = new List<string>();
            foreach (var pin in Store.Pins.Values)
            {
                if (KindOf(pin) != kind) continue;
                float d = Geo.FlatDistance(pin.Pos, pos);
                if (d > radius) continue;
                parts.Add($"{pin.Icon} at {d:0.#} m{(pin.Checked ? ", crossed off" : "")}{(pin.Auto ? "" : ", placed by hand")}");
            }
            return parts.Count == 0 ? "none" : string.Join("; ", parts.ToArray());
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
            Portals.OnPinsCleared();
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
        /// Vanilla lays pins out again only when the map moved, zoomed or was clicked; ask for a
        /// layout now so a display change (hide, size) shows at once.
        /// </summary>
        internal static void Restyle()
        {
            if (Map != null) Access.PinUpdateRequired(Map) = true;
        }

        /// <summary>
        /// After vanilla has laid out the markers: tint recorded ones, scale each kind to its
        /// configured size, and hide kinds that are switched off on the small map.
        /// </summary>
        internal static void StylePins(Minimap map)
        {
            bool smallMap = map != null && map.m_mode != Minimap.MapMode.Large;
            // TheGreatestPortal's picker overlay wants a clean map and hides the saved pins for
            // itself; ours are re-shown by this very pass, so they have to stand down here.
            bool hideEverything = (TgmConfig.ShowAllMarkers != null && !TgmConfig.ShowAllMarkers.Value)
                || Portals.PortalPickerOpen();
            _portalPins.Clear();
            foreach (var kv in _idByPin)
            {
                var pin = kv.Key;
                if (pin.m_uiElement == null) continue;
                if (!Store.Pins.TryGetValue(kv.Value, out var shared)) continue;
                var kind = KindOf(shared);
                bool hiddenByHand = ViewPrefs.IsHidden(kv.Value);
                bool hidden = hideEverything || hiddenByHand || TgmConfig.IsIconHidden(shared.Icon);
                if (!hidden && kind.HasValue)
                {
                    if (TgmConfig.ShowKind.TryGetValue(kind.Value, out var showKind) && !showKind.Value) hidden = true;
                    else if (smallMap && TgmConfig.ShowOnMinimap.TryGetValue(kind.Value, out var show) && !show.Value) hidden = true;
                }
                // Just recorded: show it for a while anyway, so writing something down always
                // shows you what you wrote. Not against the master switch or a marker hidden by
                // hand, which both mean "not this one".
                if (hidden && !hideEverything && !hiddenByHand && Reveals.IsRevealed(kv.Value)) hidden = false;
                // Used up and the player would rather not see it any more. Structures are left
                // alone: their cross-off means searched, not gone.
                if (!hidden && shared.Checked && kind.HasValue && Categories.IsResource(kind.Value)
                    && TgmConfig.ShowClearedDeposits != null && !TgmConfig.ShowClearedDeposits.Value) hidden = true;
                SetMarkerActive(pin, !hidden);
                if (hidden) continue;
                float scale = 1f;
                if (kind.HasValue && TgmConfig.MarkerSize.TryGetValue(kind.Value, out var size))
                    scale = Mathf.Clamp(size.Value, 20, 100) / 100f;
                pin.m_uiElement.localScale = new Vector3(scale, scale, 1f);
                if (kind == Category.Portal) { _portalPins.Add(pin); continue; }
                if (pin.m_iconElement != null && shared.Auto)
                    pin.m_iconElement.color = AutoTint;
            }
            // Vanilla lays the pins out again whenever the map moves, which on the minimap is every
            // step the player takes, and a freshly built icon starts at the prefab's plain white.
            // Painting here, right after that rebuild, is what stops portals flashing white while
            // walking; the per-frame pass carries the fade on while standing still.
            Portals.Paint();
        }

        /// <summary>Paint the portal markers on this map; called every frame so their color can fade.</summary>
        internal static void TintPortals(Color color)
        {
            foreach (var pin in _portalPins)
                if (pin != null && pin.m_iconElement != null) pin.m_iconElement.color = color;
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
    // and then only from the marker's menu (which erases through the personal map itself) or
    // with Shift held while right-clicking. Our own bookkeeping (merges, label changes, the menu)
    // removes vanilla pins with the remote flag set and is never gated.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), new[] { typeof(Minimap.PinData) })]
    internal static class Minimap_RemovePin_Patch
    {
        private static bool Prefix(Minimap.PinData pin)
        {
            if (!ClientPins.IsOurs(pin) || ClientPins.ApplyingRemote) return true;
            if (!ClientPins.CanDelete(ClientPins.SharedFor(pin), out string whyNot))
            {
                TheGreatestMapMod.Message("Cannot remove this marker: " + whyNot + ".");
                return false;
            }
            if (!ZInput.GetKey(KeyCode.LeftShift) && !ZInput.GetKey(KeyCode.RightShift))
            {
                TheGreatestMapMod.Message("Right-click the marker for its menu, or hold Shift and right-click to erase it.");
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

    // Right-click on the large map. On one of our markers it opens the marker menu (hide it,
    // hide its icon or kind, cross off, erase) instead of vanilla's erase; Shift + right-click
    // is still the quick erase where the server allows erasing. Vanilla pins behave as always,
    // subject to the map-out rule.
    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    internal static class Minimap_RemovePinUnderPointer_Patch
    {
        private static bool Prefix(Minimap __instance)
        {
            bool canEdit = !TgmConfig.RequireMapOutToEdit.Value || PocketMap.IsOut;
            var pin = ClientPins.ClosestPin(__instance, Access.ScreenToWorldPoint(__instance, ZInput.pointerPosition), Access.PinInteractRadius(__instance));
            var shared = ClientPins.SharedFor(pin);
            if (shared != null)
            {
                bool shift = ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
                if (shift && canEdit && TgmConfig.AllowErasingMarkers.Value) return true; // quick erase, checked again in the RemovePin prefix
                Access.HidePinTextInput(__instance);
                Access.NamePin(__instance) = null;
                MarkerMenu.Open(shared, ZInput.pointerPosition);
                return false;
            }
            if (canEdit) return true;
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

    // Writing to a cartography table: hide player-placed pins from the serializer by clearing
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
