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
        private static readonly Dictionary<PinKey, Minimap.PinData> _pinById = new Dictionary<PinKey, Minimap.PinData>();
        private static readonly Dictionary<Minimap.PinData, PinKey> _idByPin = new Dictionary<Minimap.PinData, PinKey>();
        private static readonly Color AutoTint = new Color(1f, 0.93f, 0.72f, 1f);
        private static readonly Color FireTint = new Color(1f, 0.5f, 0.12f, 1f);
        private static readonly Color CrossTint = new Color(1f, 0.33f, 0.28f, 1f);   // the cross over a crossed-off marker
        // The portal markers currently drawn, rebuilt by each styling pass. Portals are painted
        // every frame instead of here, because their color can fade over time.
        private static readonly List<Minimap.PinData> _portalPins = new List<Minimap.PinData>();
        // Crossed-off markers, so the portal painter can fade its own the same way. Portals are
        // painted every frame rather than here, because their color moves.
        private static readonly HashSet<Minimap.PinData> _fadedPins = new HashSet<Minimap.PinData>();
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
            Transports.Reset();
            PlayerSpotlight.Reset();
            MinimapClip.Reset();
            MapFrame.Reset();
            Focus.Clear();
            Locate.Reset();
            Watchdog.Reset();
            Tilled.Reset();
            Migrations.Reset();
        }

        /// <summary>
        /// The icon a marker wears now. A recorded marker that remembers what it is wears
        /// whatever the catalog says that thing wears today; one placed by hand follows its
        /// kind, since that is all it ever claimed to be. Everything else keeps the icon written
        /// on it -- markers from before this was recorded, vanilla pins adopted as they were, and
        /// anything whose catalog line has since been removed.
        /// </summary>
        internal static string IconOf(SharedPin shared)
        {
            if (shared == null) return IconRegistry.FallbackKey;
            // "Manual" is not a prefab. Asking the catalog about it would find nothing, which is
            // the right answer by luck rather than by meaning; saying so here keeps it right if
            // some mod ever ships a prefab by that name.
            string fromCatalog = shared.Source == SharedPin.Manual ? null : Catalog.IconFor(shared.Source);
            if (!string.IsNullOrEmpty(fromCatalog)) return fromCatalog;
            var kind = KindOf(shared);
            // The kind answers for two sorts of marker: one placed by hand, which never claimed
            // to be anything more than its kind, and one of a kind the catalog does not divide at
            // all. A portal is a portal and a runestone is a runestone -- there is no list of
            // types for either, so the kind's own icon is the whole answer and these follow it
            // without ever needing to be told what they are.
            if (kind.HasValue && (!shared.Auto || !Categories.UsesPrefabList(kind.Value))
                && TgmConfig.CategoryIcon.TryGetValue(kind.Value, out var cfg)
                && !string.IsNullOrEmpty(cfg.Value))
                return IconRegistry.Normalize(cfg.Value) ?? shared.Icon;
            return shared.Icon;
        }

        private static int LocalType(SharedPin shared)
        {
            string icon = IconOf(shared);
            if (!string.IsNullOrEmpty(icon)) return IconRegistry.TypeFor(icon);
            return PinTypes.IsCustom(shared.Type) ? (int)Minimap.PinType.Icon3 : shared.Type;
        }

        // ── merging in from the shared map or another player ────────────────────────

        internal static MergeResult ApplyMerge(MapStore incoming)
        {
            // Timed because this is the other half of a merge and the tidying that follows was
            // the only part with a clock on it. Redrawing a marker takes a pin out of the game's
            // list and puts a new one in, so a merge that changes thousands of them -- which one
            // does, the first time a map catches up with a big edit -- is thousands of those.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = Store.Merge(incoming);
            long mergeMs = clock.ElapsedMilliseconds;
            if (!result.Any) return result;
            foreach (var t in result.NewTombstones) RemovePinData(t.Key);
            foreach (var pin in result.ChangedPins) ReplacePinData(pin);
            PersonalMap.MarkDirty();
            long total = clock.ElapsedMilliseconds;
            if (total >= 50L)
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Taking in {result.Added + result.Updated} changed marker(s) took {total} ms " +
                    $"(reading them {mergeMs} ms, redrawing them {total - mergeMs} ms).");
            return result;
        }

        internal static void ClearSuppressions()
        {
            Store.Suppressions.Clear();
            PersonalMap.MarkDirty();
        }

        // ── local edits ─────────────────────────────────────────────────────────────

        /// <summary>Record or place a marker on the personal map.</summary>
        /// <summary>
        /// Who is writing this marker, or zero if nobody can be. Player.GetPlayerID() answers
        /// zero whenever the player's networked object is not valid -- through spawning and
        /// again on the way out -- so a present player is not proof of a usable id.
        /// </summary>
        private static long WriterId()
        {
            var player = Player.m_localPlayer;
            if (player == null) return 0L;
            try { return player.GetPlayerID(); }
            catch (System.Exception) { return 0L; }
        }

        internal static SharedPin CreateShared(string name, Vector3 pos, string icon, string kind, bool auto, bool isChecked = false, string source = "")
        {
            var player = Player.m_localPlayer;
            // No owner, no marker. Writing one owned by nobody puts it in a namespace shared with
            // every other player's ownerless markers, which is the one way two people's markers
            // can collide outright rather than merely sit on top of one another.
            long owner = WriterId();
            if (owner == 0L)
            {
                TheGreatestMapMod.Log.LogError(
                    $"[TheGreatestMap] Not recording \"{name}\": no player id yet (spawning or logging out). " +
                    "The find stays pending and is written when there is someone to own it.");
                return null;
            }
            string key = IconRegistry.Normalize(icon) ?? IconRegistry.FallbackKey;
            // Whether this was recorded or placed is said by the source now, so a caller asking
            // for a hand-placed marker has to be given the word for it rather than a flag.
            if (!auto && string.IsNullOrEmpty(source)) source = SharedPin.Manual;
            var pin = new SharedPin
            {
                OwnerId = owner,
                Author = player != null ? player.GetPlayerName() : "",
                Name = name ?? "",
                Pos = pos,
                Icon = key,
                Kind = kind ?? "",
                Source = source ?? "",
                Checked = isChecked,
                Created = MapStore.Now,
            };
            Store.Upsert(pin);
            EnsurePin(pin);
            Reveals.Add(pin.Key);
            PersonalMap.Touch();
            return pin;
        }

        /// <summary>Turns a vanilla local pin (a vanilla icon) into a personal-map marker in place.</summary>
        internal static SharedPin AdoptLocalPin(Minimap.PinData pinData, bool auto)
        {
            if (pinData == null || IsOurs(pinData)) return null;
            // A custom type is normally one of our own markers drawn from the store, which must
            // never be adopted a second time -- hence the refusal. The exception is a marker the
            // player asked for by kind: that one is theirs, and it is spent as it is taken, so the
            // refusal still stands for everything else.
            bool custom = PinTypes.IsCustom((int)pinData.m_type);
            var placing = custom ? ManualPlace.Take((int)pinData.m_type) : null;
            if (placing == null && custom) return null;
            var player = Player.m_localPlayer;
            long owner = WriterId();
            if (owner == 0L)
            {
                TheGreatestMapMod.Log.LogError(
                    "[TheGreatestMap] Not adopting a pin: no player id yet (spawning or logging out).");
                return null;
            }
            var pin = new SharedPin
            {
                OwnerId = owner,
                Author = player != null ? player.GetPlayerName() : "",
                Name = pinData.m_name ?? "",
                Pos = pinData.m_pos,
                Icon = placing != null ? placing.Icon : IconRegistry.KeyForVanilla((int)pinData.m_type),
                Source = SharedPin.Manual,
                Checked = pinData.m_checked,
                Created = MapStore.Now,
            };
            // Placed by hand, but still of a kind, so it hides and groups with its fellows. A kind
            // is otherwise only ever inferred, and only for markers this mod recorded itself.
            if (placing != null) pin.SetKind(placing.Kind.ToString());
            pinData.m_save = true;
            pinData.m_ownerID = 0L;
            Store.Upsert(pin);
            _pinById[pin.Key] = pinData;
            _idByPin[pinData] = pin.Key;
            // Ask for a redraw, or the marker keeps the size and colour the game gave it until
            // something else happens to dirty the layout -- a click, a pan -- which looked like
            // the marker not being accepted until you clicked away.
            Restyle();
            PersonalMap.Touch();
            return pin;
        }

        private static Minimap.PinData _naming;

        /// <summary>
        /// Take on a pin whose name box was closed without being confirmed.
        ///
        /// Adoption hangs off Minimap.OnPinTextEntered -- the moment the name box is accepted --
        /// which catches a pin whose name was entered, even an empty one, and misses one placed
        /// and then waved away with Escape or a click elsewhere. The game keeps the pin either
        /// way. We did not, so it sat there as an orphan: no tooltip, no marker menu, vanilla's
        /// erase on a right click, and the wrong picture after a reload, because an unadopted pin
        /// keeps the raw pin type it was placed with and those are handed out afresh each session.
        ///
        /// Nothing said any of this, and whether a marker worked came down to how its name box
        /// had been dismissed -- which is why it looked random for months.
        ///
        /// Watching the box close is the exact moment, and it has to be that moment rather than
        /// a sweep of the map: a sweep either runs while the box is still open and takes the pin
        /// before the player has finished typing, losing the name, or runs so late that the pin
        /// has been saved and reloaded as something no longer recognisable.
        /// </summary>
        internal static void AdoptStrays()
        {
            var map = Map;
            if (map == null) { _naming = null; return; }
            var naming = Access.NamePin(map);
            var closed = _naming;
            _naming = naming;
            if (closed == null || ReferenceEquals(naming, closed)) return;  // none open, or still the same one
            if (!TgmConfig.SharePlacedPins.Value) return;
            if (IsOurs(closed)) return;                       // confirmed, and already taken on
            if (!Access.Pins(map).Contains(closed)) return;   // the game dropped it after all
            if (!PinTypes.IsPlayerPlaceable((int)closed.m_type)) return;
            if (AdoptLocalPin(closed, auto: false) != null)
                TheGreatestMapMod.Log.LogInfo(
                    "[TheGreatestMap] Took on a marker placed by hand whose name box was closed without being " +
                    "accepted. The game keeps such a pin but only mentions it when a name is confirmed.");
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
            if (_pinById.TryGetValue(best.Key, out var pinData))
            {
                pinData.m_checked = true;
                if (Map != null) Access.PinUpdateRequired(Map) = true;
            }
            PersonalMap.Touch();
            return true;
        }

        /// <summary>Cross a marker off, or uncross it, from the marker menu.</summary>
        internal static void SetChecked(PinKey id, bool isChecked)
        {
            if (id.IsNone || !Store.Pins.TryGetValue(id, out var shared) || shared.Checked == isChecked) return;
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
            Store.Upsert(best);
            ReplacePinData(best);
            PersonalMap.Touch();
            return true;
        }

        /// <summary>Write a changed record back and redraw it; the change merges like any other.</summary>
        internal static void UpdateMarker(SharedPin shared)
        {
            if (shared == null || !Store.Pins.ContainsKey(shared.Key)) return;
            Store.Upsert(shared);
            ReplacePinData(shared);
            PersonalMap.Touch();
        }

        /// <summary>
        /// Change what a marker says it is, which under the natural key changes which marker it
        /// is -- so it is done as an erasure and a fresh write rather than as an edit.
        ///
        /// This is what tgm_identify does: it fills in what a marker stands for, for the markers
        /// written before a marker stood for anything, and takes the answer back on the rare
        /// occasion it was wrong. Assigning the field and saving would not work, because by the
        /// time the save ran the record would be looked up under a key nothing is filed under,
        /// and the change would be dropped without a word.
        ///
        /// The erasure does not suppress the spot. A suppression says the player does not want a
        /// marker here; this is the mod correcting its own record of one it does want.
        /// </summary>
        internal static SharedPin Reidentify(SharedPin shared, string source)
        {
            if (shared == null) return null;
            string next = source ?? "";
            if (string.Equals(shared.Source, next, System.StringComparison.Ordinal)) return shared;
            var was = shared.Key;
            if (!Store.Pins.ContainsKey(was)) { shared.Source = next; return shared; }
            RemovePinData(was);
            Store.Delete(was, out _, suppress: false);
            shared.Source = next;
            Store.Upsert(shared);
            EnsurePin(shared);
            PersonalMap.Touch();
            return shared;
        }

        /// <summary>Rename a marker on the personal map (the portal reconciler; the change merges like any other).</summary>
        internal static void RenameMarker(PinKey id, string name)
        {
            if (id.IsNone || !Store.Pins.TryGetValue(id, out var shared) || shared.Name == name) return;
            shared.Name = name ?? "";
            Store.Upsert(shared);
            ReplacePinData(shared);
            PersonalMap.Touch();
        }

        /// <summary>
        /// Erase a marker that no longer matches the world, without suppressing the spot: the
        /// thing may come back (a portal rebuilt where one stood) and should be recorded again.
        /// </summary>
        internal static void RemoveStale(PinKey id)
        {
            if (id.IsNone || !Store.Pins.ContainsKey(id)) return;
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
        /// <summary>
        /// Cross off every marker of one kind around a spot, and say how many. A deposit mined out
        /// is one marker, but a leviathan carries several barnacles and takes them all down at
        /// once, so the nearest one is not the whole story. Markers already crossed off are left
        /// alone, so nothing is dated twice.
        /// </summary>
        internal static int MarkClearedNear(Vector3 pos, float radius, Category kind)
        {
            var hits = new List<PinKey>();
            foreach (var pin in Store.Pins.Values)
            {
                if (!pin.Auto || pin.Checked || KindOf(pin) != kind) continue;
                if (Geo.FlatDistance(pin.Pos, pos) <= radius) hits.Add(pin.Key);
            }
            foreach (var id in hits) SetChecked(id, true);
            return hits.Count;
        }

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
            SetChecked(best.Key, true);
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
        internal static bool EraseById(PinKey id)
        {
            if (id.IsNone || !Store.Pins.ContainsKey(id)) return false;
            RemovePinData(id);
            Store.Delete(id, out _);
            PersonalMap.Touch();
            return true;
        }

        /// <summary>Erase recorded markers of one kind within a radius of a point (maintenance; tombstones carry it to everyone).</summary>
        internal static int EraseKindNear(Category kind, Vector3 pos, float radius)
        {
            var ids = new List<PinKey>();
            foreach (var pin in Store.Pins.Values)
                if (pin.Auto && KindOf(pin) == kind && Geo.FlatDistance(pin.Pos, pos) <= radius) ids.Add(pin.Key);
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
                // Kept labels filed by where they are, so a marker is compared with the few near
                // it rather than with every label of its kind. Five hundred thistles is a quarter
                // of a million name comparisons otherwise, on every merge.
                var kept = new Dictionary<long, List<SharedPin>>();
                float cell = Mathf.Max(spacing, 1f);
                System.Func<Vector3, int, int, long> key = (p, dx, dz) =>
                    ((long)(Mathf.FloorToInt(p.x / cell) + dx) << 32) ^ (uint)(Mathf.FloorToInt(p.z / cell) + dz);
                foreach (var pin in markers)
                {
                    bool strip = spacing < 0f;
                    if (!strip)
                    {
                        for (int dx = -1; dx <= 1 && !strip; dx++)
                            for (int dz = -1; dz <= 1 && !strip; dz++)
                            {
                                if (!kept.TryGetValue(key(pin.Pos, dx, dz), out var near)) continue;
                                foreach (var k in near)
                                    if (SameName(k.Name, pin.Name) && Geo.FlatDistance(k.Pos, pin.Pos) <= spacing) { strip = true; break; }
                            }
                    }
                    if (!strip)
                    {
                        long at = key(pin.Pos, 0, 0);
                        if (!kept.TryGetValue(at, out var here)) kept[at] = here = new List<SharedPin>();
                        here.Add(pin);
                        continue;
                    }
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

        /// <summary>Every marker drawn on the map, with the record behind it.</summary>
        internal static IEnumerable<KeyValuePair<Minimap.PinData, PinKey>> Drawn => _idByPin;

        internal static bool TryGet(PinKey id, out SharedPin shared)
        {
            shared = null;
            return !id.IsNone && Store.Pins.TryGetValue(id, out shared);
        }

        /// <summary>
        /// How large the other players' markers are drawn, as a fraction of normal. Vanilla's
        /// markers rather than ours, so this is the one size not covered by a kind's own
        /// setting -- and the one worth raising, since a player is a moving dot among hundreds
        /// of still ones.
        /// </summary>
        internal static float PlayerScale =>
            TgmConfig.PlayerMarkerSize != null ? Mathf.Clamp(TgmConfig.PlayerMarkerSize.Value, 50, 400) / 100f : 1f;

        /// <summary>The size this kind of marker is configured to be drawn at.</summary>
        internal static float ScaleOf(SharedPin shared)
        {
            var kind = KindOf(shared);
            if (kind.HasValue && TgmConfig.MarkerSize.TryGetValue(kind.Value, out var size))
                return Mathf.Clamp(size.Value, 20, 100) / 100f;
            return 1f;
        }

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

        /// <summary>
        /// Throw away every marker held and take the ones in this map instead (tgm_restore).
        /// The drawn pins go with them, because a pin left over from the old map would have
        /// nothing behind it.
        /// </summary>
        internal static void ReplaceAll(MapStore store)
        {
            if (store == null) return;
            // One at a time rather than clearing the indexes, because the drawn pins have to come
            // off the map as well; emptying the bookkeeping alone would leave every old marker
            // visible with no record behind it.
            foreach (var key in new List<PinKey>(_pinById.Keys)) RemovePinData(key);
            PersonalMap.Replace(store);
            RebuildPins();
            Restyle();
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
            if (_pinById.TryGetValue(shared.Key, out var existing))
            {
                if (Access.Pins(Map).Contains(existing))
                {
                    existing.m_checked = shared.Checked;
                    return;
                }
                _pinById.Remove(shared.Key);
                _idByPin.Remove(existing);
            }
            // m_save must be true: vanilla's pin-under-pointer lookup (right-click erase, click to
            // check off) ignores unsaved pins. The GetMapData patch below keeps them out of the profile.
            var pin = Map.AddPin(shared.Pos, (Minimap.PinType)LocalType(shared), shared.Name ?? "", true, shared.Checked, 0L);
            if (pin == null) return;
            // A marker that carries no label still has a name; it is simply not written on the map.
            // Vanilla builds a label only for a pin added with a name, so naming it afterwards puts
            // no word on the map and still gives anything that reads a pin's name something to know
            // it by -- which is how a compass mod is told to leave the mushrooms off its band.
            if (string.IsNullOrEmpty(pin.m_name)) pin.m_name = IconRegistry.DisplayName(shared.Icon) ?? "";
            _pinById[shared.Key] = pin;
            _idByPin[pin] = shared.Key;
        }

        /// <summary>Drop the marker's vanilla pin and add it again from the record (name or icon changes).</summary>
        private static void ReplacePinData(SharedPin shared)
        {
            RemovePinData(shared.Key);
            EnsurePin(shared);
        }

        private static void RemovePinData(PinKey id)
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
            Transports.OnPinsCleared();
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
            // The player spotlight puts the whole map out, including anything a reveal would have
            // forced back on: while it is up, the only thing worth seeing is where people are.
            bool hideEverything = (TgmConfig.ShowAllMarkers != null && !TgmConfig.ShowAllMarkers.Value) || PlayerSpotlight.On;
            _portalPins.Clear();
            _fadedPins.Clear();
            foreach (var kv in _idByPin)
            {
                var pin = kv.Key;
                if (pin.m_uiElement == null) continue;
                if (!Store.Pins.TryGetValue(kv.Value, out var shared)) continue;
                var kind = KindOf(shared);
                bool hiddenByHand = ViewPrefs.IsHidden(kv.Value);
                bool hidden = hideEverything || hiddenByHand || TgmConfig.IsIconHidden(IconOf(shared));
                if (!hidden && kind.HasValue)
                {
                    if (TgmConfig.ShowKind.TryGetValue(kind.Value, out var showKind) && !showKind.Value) hidden = true;
                    else if (smallMap && TgmConfig.ShowOnMinimap.TryGetValue(kind.Value, out var show) && !show.Value) hidden = true;
                    // TheGreatestPortal's picker draws a pin for every portal itself, so ours would
                    // sit on top of its, two labels over one place. Only portals stand down; the
                    // rest of the map stays, which is the point of showing it there at all.
                    else if (kind.Value == Category.Portal && Portals.PortalPickerOpen()) hidden = true;
                }
                // Just recorded: show it for a while anyway, so writing something down always
                // shows you what you wrote. Not against the master switch or a marker hidden by
                // hand, which both mean "not this one".
                if (hidden && !hideEverything && !hiddenByHand && Reveals.IsRevealed(kv.Value)) hidden = false;
                // Crossed off, and the player would rather not see that kind of crossed-off marker
                // any more: used-up deposits, or searched places by type.
                if (!hidden && CrossedOff.IsHidden(shared)) hidden = true;
                // Looking for one thing: it wins over every switch above, since a player who asks
                // for frost caves means the frost caves whether or not dungeons are switched off.
                // Not over the master switch or a marker put out by hand, which both mean "not
                // this one" rather than "not right now".
                if (Focus.On && !hideEverything && !hiddenByHand) hidden = !Focus.Shows(shared);
                // Being asked where something is outranks having switched it off: the markers
                // come back for the few seconds the pulse lasts and hide themselves again after.
                // Still not past the master switch or a marker put away by hand, which mean "not
                // this one" rather than "not just now".
                if (hidden && !hideEverything && !hiddenByHand && Locate.On && Locate.Matches(shared)) hidden = false;
                SetMarkerActive(pin, !hidden);
                if (hidden) continue;
                float scale = ScaleOf(shared);
                pin.m_uiElement.localScale = new Vector3(scale, scale, 1f);
                // Crossed off: the marker itself goes pale, not only the little mark over it.
                // That mark is drawn on top of the picture, so a busy one swallows it and a label
                // from a neighboring marker can cover it outright; a faded marker still reads as
                // done at a glance, and from any distance.
                // Either says crossed off. The cross the game draws comes from the pin's own flag
                // while the record keeps its own, and the two are brought together by separate
                // paths -- so a marker can wear the cross before the record has caught up, or the
                // other way about. Keying the fade to one of them alone is how two frost caves,
                // both crossed off, ended up looking different from each other.
                bool faded = shared.Checked || pin.m_checked;
                if (faded) _fadedPins.Add(pin);
                MarkChecked(pin, faded);
                if (kind == Category.Portal) { _portalPins.Add(pin); continue; }
                if (pin.m_iconElement == null || !shared.Auto) { if (faded && pin.m_iconElement != null) pin.m_iconElement.color = Faded(pin.m_iconElement.color); continue; }
                // A campfire wears its own build-menu picture, which is already the color of fire,
                // so it keeps its colors. Should it fall back to vanilla's white fire pin, that
                // is painted orange instead.
                var tint = kind == Category.Campfire
                    ? (IconRegistry.IsVanillaPin(shared.Icon) ? FireTint : Color.white)
                    : AutoTint;
                pin.m_iconElement.color = faded ? Faded(tint) : tint;
            }
            ResizePlayers(map);
            // Vanilla lays the pins out again whenever the map moves, which on the minimap is every
            // step the player takes, and a freshly built icon starts at the prefab's plain white.
            // Painting here, right after that rebuild, is what stops portals flashing white while
            // walking; the per-frame pass carries the fade on while standing still.
            Portals.Paint();
        }

        /// <summary>Paint the portal markers on this map; called every frame so their color can fade.</summary>
        /// <summary>
        /// What is actually true of one marker as it is drawn right now, for tgm_list: the record's
        /// crossed-off flag, the game's own, whether we are drawing it at all, and the color on its
        /// icon this moment. A marker that says it is crossed off and is not pale has to be one of
        /// those four disagreeing, and this says which.
        /// </summary>
        internal static string DrawnState(SharedPin shared)
        {
            if (shared == null) return "";
            if (!_pinById.TryGetValue(shared.Key, out var pin) || pin == null) return "not drawn";
            string color = pin.m_iconElement != null
                ? $"{pin.m_iconElement.color.r:0.00}/{pin.m_iconElement.color.g:0.00}/{pin.m_iconElement.color.b:0.00} a{pin.m_iconElement.color.a:0.00}"
                : "no icon";
            bool shown = pin.m_uiElement != null && pin.m_uiElement.gameObject.activeSelf;
            // The pin's own type against the type this marker should be drawn with. They differ
            // when the pin on screen was not made from this record -- the game made it and we
            // took it over, or it was never ours at all -- which is invisible from everything
            // else here and is exactly what makes one marker of six come out wrong.
            int want = LocalType(shared);
            int got = (int)pin.m_type;
            string drawn = got == want
                ? $"type {got}"
                : $"type {got} but the record wants {want}  <-- MISMATCH";
            return $"icon {IconOf(shared)}, stored '{shared.StoredIcon}', {drawn}, " +
                   $"crossed {(shared.Checked ? "yes" : "no")}/{(pin.m_checked ? "yes" : "no")}, " +
                   $"{(_fadedPins.Contains(pin) ? "faded" : "not faded")}, {color}, " +
                   (shown ? "shown" : "hidden -- " + WhyHidden(shared));
        }

        /// <summary>
        /// Which switch put this marker away. Six things can hide a marker and from the outside
        /// they are indistinguishable, which cost an evening of guessing: a marker vanished, the
        /// command that shows everything brought it back, and that narrowed it to all six at once.
        /// </summary>
        internal static string WhyHidden(SharedPin shared)
        {
            if (shared == null) return "no marker";
            if (TgmConfig.ShowAllMarkers != null && !TgmConfig.ShowAllMarkers.Value) return "every marker is switched off";
            if (ViewPrefs.IsHidden(shared.Key)) return "hidden on its own, through the marker menu";
            if (TgmConfig.IsIconHidden(IconOf(shared))) return $"its icon ({IconOf(shared)}) is in Hidden Icons";
            var kind = KindOf(shared);
            if (kind.HasValue)
            {
                if (TgmConfig.ShowKind.TryGetValue(kind.Value, out var showKind) && !showKind.Value)
                    return $"\"Show {Categories.Label(kind.Value)}\" is off";
                if (TgmConfig.ShowOnMinimap.TryGetValue(kind.Value, out var onMini) && !onMini.Value)
                    return $"\"{Categories.Label(kind.Value)} On Minimap\" is off and this is the small map";
                if (kind.Value == Category.Portal && Portals.PortalPickerOpen()) return "the portal picker is open";
            }
            if (CrossedOff.IsHidden(shared)) return "it is crossed off and that kind of crossed-off marker is hidden";
            if (Focus.On && !Focus.Shows(shared)) return $"the map is showing only {Focus.What()}";
            return "no reason found -- which is itself worth reporting";
        }

        /// <summary>
        /// Draw the other players at the size the player asked for. Left alone while the
        /// spotlight or a ping is up, since both of those are saying something about size
        /// themselves and will put it back when they finish.
        /// </summary>
        private static void ResizePlayers(Minimap map)
        {
            if (map == null || PlayerSpotlight.On || Locate.On) return;
            float scale = PlayerScale;
            foreach (var pin in Access.Pins(map))
            {
                if (pin == null || pin.m_uiElement == null || pin.m_type != Minimap.PinType.Player) continue;
                var at = pin.m_uiElement.localScale;
                if (!Mathf.Approximately(at.x, scale)) pin.m_uiElement.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>
        /// What the drawing order looks like where the pointer is, for when a marker is behind
        /// something it should be in front of. Reports every pin near that spot in the order the
        /// game draws them, last on top, so "ours is not being lifted" can be told apart from
        /// "ours is on top and the thing over it is simply bigger".
        /// </summary>
        internal static List<string> DescribeOrder(Vector3 world, float radius)
        {
            var lines = new List<string>();
            var map = Map;
            if (map == null) { lines.Add("No map."); return lines; }
            lines.Add($"{_portalPins.Count} portal pin(s) known; large map: {map.m_mode == Minimap.MapMode.Large}");
            var near = new List<Minimap.PinData>();
            foreach (var pin in Access.Pins(map))
                if (pin != null && pin.m_uiElement != null && Geo.FlatDistance(pin.m_pos, world) <= radius)
                    near.Add(pin);
            near.Sort((a, b) => a.m_uiElement.GetSiblingIndex().CompareTo(b.m_uiElement.GetSiblingIndex()));
            Transform parent = near.Count > 0 ? near[0].m_uiElement.parent : null;
            lines.Add($"{near.Count} pin(s) within {radius:0} m" + (parent != null ? $", root holds {parent.childCount}" : ""));
            foreach (var pin in near)
            {
                var shared = SharedFor(pin);
                string what = shared != null ? (KindOf(shared)?.ToString() ?? "ours, no kind") : "not ours";
                lines.Add($"  sibling {pin.m_uiElement.GetSiblingIndex()}: type {(int)pin.m_type}, " +
                          $"{(string.IsNullOrEmpty(pin.m_name) ? "(no label)" : pin.m_name)}, {what}" +
                          $"{(pin.m_doubleSize ? ", double size" : "")}");
            }
            lines.Add("Drawn in the order listed; the last one is on top.");
            return lines;
        }

        internal static void TintPortals(Color color)
        {
            foreach (var pin in _portalPins)
                if (pin != null && pin.m_iconElement != null)
                    pin.m_iconElement.color = _fadedPins.Contains(pin) ? Faded(color) : color;
        }

        /// <summary>
        /// A crossed-off marker, washed out: toward gray so a colorful icon stops competing with
        /// the ones still to do, and part-transparent so the map beneath shows through. How far is
        /// the player's to set, because how crowded a map gets is theirs too.
        /// </summary>
        private static Color Faded(Color color)
        {
            float f = TgmConfig.CrossedOffFade != null ? Mathf.Clamp01(TgmConfig.CrossedOffFade.Value / 100f) : 0.6f;
            if (f <= 0f) return color;
            // Toward a dark gray rather than a light one: these markers sit on a dark map, so
            // washing a gold icon toward pale barely changes it. Most of the effect is in the
            // darkening; the alpha only softens the edge.
            var washed = Color.Lerp(color, new Color(0.34f, 0.33f, 0.31f, color.a), f);
            washed.a = color.a * (1f - 0.45f * f);
            return washed;
        }

        /// <summary>
        /// Vanilla's little cross, made to stand out: it is drawn at the marker's own size in its
        /// own pale color, which disappears into a dark or busy picture. Painted here instead, and
        /// drawn last so nothing of ours sits over it.
        /// </summary>
        private static void MarkChecked(Minimap.PinData pin, bool crossed)
        {
            var mark = pin.m_checkedElement;
            if (mark == null || !crossed) return;
            var image = mark.GetComponent<UnityEngine.UI.Image>();
            if (image != null) image.color = CrossTint;
            mark.transform.SetAsLastSibling();
        }


        /// <summary>
        /// Show or hide one marker. Hiding takes its label with it; showing does not bring the
        /// label back, because whether a label belongs is the game's decision and not ours -- it
        /// draws them on the large map only, and works that out afresh every time it lays the
        /// markers out, which is just before this runs. Switching labels on here put words on the
        /// minimap that the game had deliberately turned off a moment earlier. Only portals showed
        /// it, because portals are nearly the only markers that carry a label at all.
        /// </summary>
        private static void SetMarkerActive(Minimap.PinData pin, bool active)
        {
            var icon = pin.m_uiElement.gameObject;
            if (icon.activeSelf != active) icon.SetActive(active);
            if (active) return;
            var label = pin.m_NamePinData != null ? pin.m_NamePinData.PinNameGameObject : null;
            if (label != null && label.activeSelf) label.SetActive(false);
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
    //
    // A marker of one of this mod's own kinds never goes to a table, even when the table is set to
    // carry player markers. Its type is a number this mod made up: a client without the mod looks
    // that number up in the game's icon list, finds nothing, and indexes its visibility array past
    // the end. Nothing is lost by holding them back -- such a client could not draw them anyway --
    // and the table is the one place a marker of ours can reach somebody who is not running this.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.GetSharedMapData))]
    internal static class Minimap_GetSharedMapData_Patch
    {
        private static void Prefix(Minimap __instance, ref List<Minimap.PinData> __state)
        {
            bool carry = TgmConfig.TableCarriesPins.Value;
            __state = new List<Minimap.PinData>();
            foreach (var pin in Access.Pins(__instance))
            {
                if (!pin.m_save) continue;
                int type = (int)pin.m_type;
                bool hold = PinTypes.IsCustom(type) || (!carry && PinTypes.IsPlayerPlaceable(type));
                if (!hold) continue;
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
        private static void Postfix(Minimap __instance)
        {
            ClientPins.StylePins(__instance);
            // Vanilla has just built markers afresh, and a new one starts visible. Putting the
            // spotlight's out here rather than waiting for our own Update keeps them from showing
            // for the frame between: the two Updates have no order between them, so zooming or
            // panning -- which is what makes vanilla rebuild -- made every label blink.
            PlayerSpotlight.AfterLayout(__instance);
        }
    }
}
