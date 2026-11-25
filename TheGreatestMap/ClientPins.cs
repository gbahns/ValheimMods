using System;
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
        // What the styling pass has decided to draw, before thinning has had a word. Kept between
        // passes rather than made each time: this runs on every layout of the map, which on the
        // minimap is every step the player takes.
        private static readonly List<Drawn> _shown = new List<Drawn>();

        /// <summary>The markers thinning held back at the last layout, so one can be asked why.</summary>
        private static readonly HashSet<PinKey> _thinned = new HashSet<PinKey>();
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
            LabelEditor.Forget();
            MarkerTooltip.Reset();
            IconRegistry.ForgetPicks();
            Reveals.Reset();
            JustCrossedOff.Reset();
            Portals.Reset();
            Transports.Reset();
            Builds.Reset();
            MapPaint.Reset();
            PlayerSpotlight.Reset();
            MinimapClip.Reset();
            MapFrame.Reset();
            Focus.Clear();
            Locate.Reset();
            Watchdog.Reset();
            StrayWatch.Reset();
            Minimap_GetMapData_Patch.Forget();
            Tilled.Reset();
            Erasures.Reset();
            SurveyPreview.Reset();
            OwnMarks.Reset();
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
            string fromCatalog = shared.HandPlaced ? null : Catalog.IconFor(shared.Source);
            if (!string.IsNullOrEmpty(fromCatalog)) return fromCatalog;
            var kind = KindOf(shared);
            // A marker placed by hand wears the picture it was placed with, and that picture is
            // the whole of what it is: the marker list files it by picture, the switch that hides
            // it is the picture's switch, and Focus.TypeLabel reads its name back out of the
            // catalog from the kind and the picture together. Arming silver under Ore and getting
            // a marker drawn as the Ore pickaxe is not a cosmetic slip -- it files the marker
            // under the pickaxe as well, where the Silver row cannot touch it, which is the
            // place-by-hand button on a type row not working at all.
            //
            // The kind used to answer here, and it was right while a kind row was the only thing
            // that could be armed: such a marker had never claimed to be anything narrower. Type
            // rows got a "+" of their own in 4.12.0 and that stopped being true.
            //
            // The picture a kind row arms is the kind's, resolved when it is armed, so those
            // markers look the same either way -- they simply stop following a later change to
            // the kind's icon setting. Which is the better of the two: a marker travels to the
            // rest of the crew, their setting is not yours, and a picture re-read from each
            // reader's config is one marker looking like a different thing to every person
            // holding the map.
            if (shared.HandPlaced && !string.IsNullOrEmpty(shared.Icon))
                return IconRegistry.Normalize(shared.Icon) ?? shared.Icon;
            // The kind still answers for a marker of a kind the catalog does not divide at all. A
            // portal is a portal and a runestone is a runestone -- there is no list of types for
            // either, so the kind's own icon is the whole answer and these follow it without ever
            // needing to be told what they are.
            if (kind.HasValue && !Categories.UsesPrefabList(kind.Value)
                && TgmConfig.CategoryIcon.TryGetValue(kind.Value, out var cfg)
                && !string.IsNullOrEmpty(cfg.Value))
                return IconRegistry.Normalize(cfg.Value) ?? shared.Icon;
            // And for anything still without a picture, the kind's own rather than the fallback
            // dot. A kind that divides into types prefers the type's picture, which is what the
            // test above is for -- but preferring it is not the same as insisting on it, and
            // where the catalog gives a line a name and no picture there was nothing left to
            // prefer. Those markers came out as plain dots carrying a name that said what they
            // were: "Abandoned Farm" drawn as a dot, by the hundred, while the Structures icon
            // sat configured as a house the whole time. Every prefab the catalog lists without a
            // picture was in this state, and so was every prefab it does not list at all while
            // its kind is set to record unlisted ones.
            if (string.IsNullOrEmpty(shared.Icon) && kind.HasValue
                && TgmConfig.CategoryIcon.TryGetValue(kind.Value, out var own)
                && !string.IsNullOrEmpty(own.Value))
                return IconRegistry.Normalize(own.Value) ?? shared.Icon;
            return shared.Icon;
        }

        /// <summary>
        /// Would this marker be drawn on the large map as the switches stand?
        ///
        /// <para>Asked so that the list can say how much of the map it is showing. It used to ask
        /// the find box instead, which knows about one filter out of six: hiding two thousand
        /// resources and being told "showing all 2662 markers" is the panel contradicting the row
        /// at its own foot, which was busy offering to put a kind back.</para>
        ///
        /// <para>The switches only. The two rules that un-hide a marker for a moment -- the few
        /// seconds after it is recorded, and the pulse while something is being pointed at -- are
        /// left out on purpose: a number that counted those would tick up and down on its own
        /// while nobody touched anything. The minimap's own switches are left out because this
        /// answers for the large map, where the list lives. <see cref="Restyle"/> is what actually
        /// draws, and it applies those four on top of this answer; the two have to be read
        /// together if either is changed.</para>
        /// </summary>
        /// <summary>
        /// Is this marker's picture switched off?
        ///
        /// <para>Which list to ask depends on whether anyone recorded it. A recorded marker
        /// answers to Hidden Icons, which is the list the kind and type rows write and the one a
        /// player edits by hand. A marker with no kind was placed by a person, and answers to the
        /// Yours rows' own list. They were one list until 4.9.1, and the two meet on vanilla's
        /// house pin: every structure wears it, for want of a picture of an abandoned farm, so
        /// switching off a House row labelled "3" hid 218 recorded structures with it.</para>
        /// </summary>
        internal static bool PictureHidden(SharedPin shared)
        {
            if (shared == null) return false;
            string icon = IconOf(shared);
            return KindOf(shared).HasValue ? TgmConfig.IsIconHidden(icon) : TgmConfig.IsOwnIconHidden(icon);
        }

        /// <summary>
        /// Is this marker's kind switched off where it stands? Only for the kinds whose rows are
        /// biomes rather than pictures, and only worth the question once something is hidden --
        /// this is asked of every marker on every relayout, and the biome of a place that cannot
        /// move is worked out once and remembered.
        /// </summary>
        internal static bool BiomeHidden(SharedPin shared, Category? kind)
        {
            if (shared == null || !kind.HasValue) return false;
            if (TgmConfig.HiddenBiomeCount() == 0) return false;
            if (!Categories.GroupsByBiome(kind.Value) || !Biomes.Known) return false;
            return TgmConfig.IsBiomeHidden(kind.Value, Biomes.Of(shared.Pos));
        }

        /// <summary>Somebody else's marker, put on the map by their own hand rather than recorded.</summary>
        internal static bool TheirsByHand(SharedPin shared)
        {
            if (shared == null || shared.Auto) return false;
            long mine = WriterId();
            // No local player means nothing is being drawn for anybody, and guessing "theirs"
            // here would hide markers on the strength of not knowing whose they are.
            return mine != 0L && shared.OwnerId != mine;
        }

        internal static bool IsShown(SharedPin shared)
        {
            if (shared == null) return false;
            // The master switch and a marker put away by hand mean "not this one", and nothing
            // below overrides them -- not even being searched for.
            if (TgmConfig.ShowAllMarkers != null && !TgmConfig.ShowAllMarkers.Value) return false;
            if (PlayerSpotlight.On) return false;
            if (ViewPrefs.IsHidden(shared.Key)) return false;
            // Looking for one thing wins over every switch below it, since a player who asks for
            // frost caves means the frost caves whether or not dungeons are switched off.
            if (Focus.On) return Focus.Shows(shared);
            if (PictureHidden(shared)) return false;
            var kind = KindOf(shared);
            if (kind.HasValue && TgmConfig.ShowKind.TryGetValue(kind.Value, out var showKind) && !showKind.Value) return false;
            // Another player's hand-placed marker, which this player would rather not look at.
            // Hidden and not refused: it stays on the map, keeps syncing and still passes on, and
            // the switch can be taken back without meeting anybody again. Refusing is the other
            // switch's job -- see TgmConfig.RecordManualFromOthers, which decides what arrives.
            if (TgmConfig.ShowManualFromOthers != null && !TgmConfig.ShowManualFromOthers.Value
                && TheirsByHand(shared)) return false;
            if (BiomeHidden(shared, kind)) return false;
            // Never a recorded altar marker. The game marks every altar on every map from the
            // first minute, so one of ours beside it is the same stones twice -- and a swap that
            // drew the records would be a swap into nothing wherever this mod had not recorded
            // one, which is what it was: a player with records for two altars turned it on, had
            // the game's seven pins hidden as promised, and got two markers. What the switch
            // draws instead is one marker per altar the game has marked, from the game's own
            // pins, in OwnMarks. The records stay as they are -- they still sync, and they still
            // remember what was seen and when -- they are simply not what is drawn.
            if (kind.HasValue && kind.Value == Category.BossAltar) return false;
            return !CrossedOff.IsHidden(shared);
        }


        /// <summary>
        /// The pin number a marker is drawn with: always one of this mod's own making.
        ///
        /// <para>Always, which is the half 4.8.5 missed. It stopped a "pin:" icon from borrowing
        /// the game's pin type as well as its picture, and left the case where a marker has no
        /// icon at all borrowing both -- a record carrying one of the game's numbers was drawn
        /// with that number, and one carrying a stale number of ours was drawn as a literal
        /// Icon3. So the markers that do not know what they are, which are exactly the ones a
        /// player cannot identify by looking, were the game's own dots as far as anything could
        /// tell: vanilla's dot button hid them while the marker list could not account for them,
        /// because the list counts a pin of ours as ours whatever number it wears. Right-clicking
        /// the dot under the map hid most of a map and the hammer and house buttons beside it hid
        /// nothing, and no row anywhere said why.</para>
        ///
        /// <para>The picture is kept exactly as it was: the stored number is turned back into the
        /// "pin:" key for that picture and minted from there, so a marker drawn as a house stays
        /// a house and is simply no longer a house <em>pin</em>. KeyForVanilla answers the
        /// fallback for a number the game does not define, which is what a stale number of ours
        /// is.</para>
        /// </summary>
        private static int LocalType(SharedPin shared)
        {
            string icon = IconOf(shared);
            if (!string.IsNullOrEmpty(icon)) return IconRegistry.TypeFor(icon);
            return IconRegistry.TypeFor(IconRegistry.KeyForVanilla(shared.Type));
        }

        /// <summary>
        /// The name of the kind of a marker this player does not record, or null to take it in.
        /// Handed to the merge, which turns away whatever this names.
        ///
        /// <para><b>Your own markers are never turned away</b>, whatever the switches say. One
        /// you recorded is already recorded, and the question a Record switch answers is what to
        /// write down from somewhere else; turning your own copy back at the door would mean a
        /// reinstall, or a fresh table visit, could not hand you back your own map.</para>
        ///
        /// <para>A kind with no Record switch at all is taken in, since nothing is being
        /// declined: the ones only ever placed by hand have no switch because nothing records
        /// them, and the ones the game marks itself are answered further down by
        /// <see cref="DropWhatTheGameMarks"/>, which is a different statement -- those are not
        /// wanted on anybody's map rather than not wanted on this one.</para>
        /// </summary>
        /// <summary>
        /// Whose map this is, read once for the merge about to run rather than per marker. A
        /// merge reads thousands of them, and asking the game for the local player each time is
        /// the kind of per-marker cost the thinning notes are about.
        /// </summary>
        private static long _merging;

        private static string RefusedKind(SharedPin pin)
        {
            if (pin == null) return null;
            if (_merging != 0L && pin.OwnerId == _merging) return null;
            // Placed by hand, which is a decision of its own and not a kind: a marker somebody
            // dropped on a silver vein they guessed at is Ore, so no Record switch below can say
            // "recorded ore, yes; somebody's guess at ore, no". Asked before the kind for the
            // same reason it exists -- it is also the only switch covering the kinds that are
            // never anything but hand-placed, Fish and Big Game, which have no Record switch.
            if (!pin.Auto && TgmConfig.RecordManualFromOthers != null
                && !TgmConfig.RecordManualFromOthers.Value) return "placed by hand";
            var kind = KindOf(pin);
            if (!kind.HasValue) return null;
            return !TgmConfig.CategoryEnabled.TryGetValue(kind.Value, out var records) || records.Value
                ? null
                : Categories.Label(kind.Value);
        }

        // ── merging in from the shared map or another player ────────────────────────

        /// <summary>
        /// Take another map's markers into this one. <paramref name="fromServer"/> says where
        /// they came from, which decides whether they are ours to pass on: markers the server
        /// handed us are not news to the server, and writing them down as ours to send would
        /// hand every one of them straight back at the next sync. That is what the whole-map
        /// upload did, and why this used to need undoing by hand afterwards.
        /// </summary>
        internal static MergeResult ApplyMerge(MapStore incoming, bool fromServer)
        {
            // Timed because this is the other half of a merge and the tidying that follows was
            // the only part with a clock on it. Redrawing a marker takes a pin out of the game's
            // list and puts a new one in, so a merge that changes thousands of them -- which one
            // does, the first time a map catches up with a big edit -- is thousands of those.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MergeResult result;
            _merging = WriterId();
            if (fromServer) PersonalMap.BeginQuiet();
            try { result = Store.Merge(incoming, RefusedKind); }
            finally { if (fromServer) PersonalMap.EndQuiet(); }
            long mergeMs = clock.ElapsedMilliseconds;
            if (result.Refused)
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] An erasure turned away {result.RefusedByErasure + result.RefusedByOldErasure} " +
                    "marker(s) offered by that map, which is erasures doing the job they exist for.");
            if (result.TurnedAway)
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] {result.RefusedByKind} marker(s) that map offered were not copied onto " +
                    $"this one, being kinds it does not record: {result.KindsTurnedAway()}.");
            if (!result.Any) return result;
            foreach (var t in result.NewTombstones) RemovePinData(t.Key);
            foreach (var pin in result.ChangedPins) ReplacePinData(pin);
            // No ghost sweep here, and this is load-bearing. Folding two markers that arrived on
            // one spot erases the loser, and that erasure looks exactly like a ghost: an erasure
            // naming a place where a marker still stands under another name. Sweeping after a
            // merge ate the fold's erasure before it could be sent up, so the server kept its
            // copy and handed it back at the next merge, to be folded and eaten again -- 129,
            // then 118, then 118 forever. The sweep belongs to the one-time conversion that
            // needed it, not to every merge.
            // A merge writes the map directly rather than through the calls that report a
            // change, so what it brought is named here. Only from another player or a table:
            // see the note on fromServer.
            if (!fromServer)
            {
                foreach (var pin in result.ChangedPins) PersonalMap.Took(pin.Key);
                foreach (var t in result.NewTombstones) PersonalMap.Took(t.Key);
                // Erasures from before the rekey know their marker by an id this map no longer
                // uses, so there is no key to name and no way to send them but all of it.
                if (incoming.LegacyTombstones.Count > 0) PersonalMap.SendEverything();
            }
            // What the game marks itself does not need a marker, and a merge is where one comes
            // back from: the shared map still carries them, and the server never runs the tidy
            // pass that takes them down. Dropped as they arrive, every time, so taking one down
            // on one map is not undone by the next player to sync with it.
            int marked = DropWhatTheGameMarks();
            if (marked > 0)
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] That map offered {marked} marker(s) for places the game marks by itself; " +
                    "they are not kept.");
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
            PersonalMap.SendEverything();
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
            Store.Upsert(pin);
            // The game's pin comes off and ours goes up in its place, rather than this record
            // taking that pin over.
            //
            // Taking it over left the marker half-adopted for the rest of the session: it was a
            // record of ours bound to a pin still wearing one of vanilla's numbers, so vanilla's
            // button under the map hid it today and could not touch it tomorrow, when the next
            // login redrew it with a number of this mod's own. Every sweep, guard and switch that
            // asks a pin's number got a different answer on either side of a relog. Drawing our
            // own now means a marker is the same object from the moment it exists.
            //
            // Quietly, because taking a pin off the map is how a player erases a marker: the
            // removal patch would see this one go and erase the record we have just written.
            _applyingRemote = true;
            try { if (Access.Pins(Map).Contains(pinData)) Map.RemovePin(pinData); }
            finally { _applyingRemote = false; }
            EnsurePin(pin);
            // Shown at once, whatever is switched off, the same courtesy a recorded marker gets:
            // putting something on the map and not being shown it reads as the placing having
            // failed, and the player's next move is to place it again.
            Reveals.Add(pin.Key);
            // Ask for a redraw, or the marker keeps the size and color the game gave it until
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

        /// <summary>
        /// Pins on the game's map with no record of ours behind them, told apart by what they are.
        /// <paramref name="orphans"/> carry a type number this mod made up, so nothing else could
        /// have made them: they are markers of ours that lost their record, and deleting them
        /// costs nothing. <paramref name="placed"/> are the game's own five, which a player may
        /// well have put there by hand -- those are worth adopting before anything deletes them.
        /// </summary>
        internal static void StrayPins(out int orphans, out int placed)
        {
            orphans = 0;
            placed = 0;
            if (Map == null) return;
            var ours = DrawnByUs();
            foreach (var pin in Access.Pins(Map))
            {
                if (pin == null || ours.Contains(pin)) continue;
                int type = (int)pin.m_type;
                // Custom first: IsPlayerPlaceable counts ours among the placeable ones.
                if (PinTypes.IsCustom(type)) orphans++;
                else if (PinTypes.IsPlayerPlaceable(type)) placed++;
            }
        }

        private static readonly HashSet<Minimap.PinData> _drawnByUs = new HashSet<Minimap.PinData>();

        /// <summary>
        /// Every pin this mod put on the map, gathered once for a sweep over the whole list.
        ///
        /// <para>Being ours is not the same as having a record. The markers have records and live
        /// in the index; the live ships, carts and portals, the ghosts of erased markers and a
        /// survey's preview do not -- they are drawn from what is happening now and tracked in
        /// their own lists. Every one of them wears a number of this mod's own making.</para>
        ///
        /// <para>Asking only the index called all of those leftovers. The counting panel had the
        /// right test and the sweeps did not, so the status line reported a player's twelve carts
        /// and six karves as markers that had lost their records, the watch warned about them
        /// once a session, and tgm_clearlocal deleted them -- live pins for boats on the water,
        /// redrawn a moment later, so it looked like nothing had happened and the number was
        /// wrong every time.</para>
        ///
        /// <para>Gathered into a set rather than asked pin by pin: the lists are short but this
        /// runs over every pin on the map, and the watch runs it every few seconds.</para>
        /// </summary>
        private static HashSet<Minimap.PinData> DrawnByUs()
        {
            _drawnByUs.Clear();
            foreach (var kv in _idByPin) _drawnByUs.Add(kv.Key);
            foreach (var portal in Portals.DrawnPins) if (portal != null) _drawnByUs.Add(portal);
            foreach (var shown in Transports.DrawnPins) if (shown != null && shown.Pin != null) _drawnByUs.Add(shown.Pin);
            foreach (var ghost in Erasures.DrawnPins) if (ghost != null) _drawnByUs.Add(ghost);
            foreach (var ghost in SurveyPreview.DrawnPins) if (ghost != null) _drawnByUs.Add(ghost);
            foreach (var mark in OwnMarks.DrawnPins) if (mark != null) _drawnByUs.Add(mark);
            return _drawnByUs;
        }

        /// <summary>
        /// Pins of a kind the game never writes down that are carrying save anyway, counted by
        /// kind. See <see cref="PinTypes.IsNeverSaved"/>: each one came out of a file or a table
        /// and the running game cannot have made it.
        /// </summary>
        internal static string SavedGhosts(out int total)
        {
            total = 0;
            if (Map == null) return "";
            var ours = DrawnByUs();
            var byKind = new Dictionary<Minimap.PinType, int>();
            foreach (var pin in Access.Pins(Map))
            {
                if (!IsSavedGhost(pin, ours)) continue;
                byKind.TryGetValue(pin.m_type, out int n);
                byKind[pin.m_type] = n + 1;
                total++;
            }
            return Listed(byKind);
        }

        /// <summary>
        /// A pin the game would never have saved, saved. Asked of this mod's own drawings too --
        /// through the same test the counting panel uses -- because the portals, the ships, the
        /// carts and the erasure ghosts are ours and wear whatever kind suits them.
        /// </summary>
        private static bool IsSavedGhost(Minimap.PinData pin, HashSet<Minimap.PinData> ours)
        {
            return pin != null && pin.m_save && PinTypes.IsNeverSaved(pin.m_type) && !ours.Contains(pin);
        }

        /// <summary>
        /// The leftovers by the picture each is actually wearing, named as the game names it.
        ///
        /// <para>A leftover keeps the number it was saved with, and that number means whatever
        /// this session happened to hand it -- the numbers are given out in the order icons are
        /// first met, so the same marker comes back as a different thing every login. Reading
        /// the sprite's own name is the only way to say what a player is looking at, and it is
        /// the question their screenshots could not answer: a map of rings and a map of houses
        /// can be the same pins on two evenings.</para>
        /// </summary>
        internal static string LeftoverSprites()
        {
            if (Map == null) return "";
            var ours = DrawnByUs();
            var byName = new Dictionary<string, int>();
            foreach (var pin in Access.Pins(Map))
            {
                if (pin == null || ours.Contains(pin) || !PinTypes.IsCustom((int)pin.m_type)) continue;
                string name = pin.m_icon != null ? pin.m_icon.name : "(no picture at all)";
                byName.TryGetValue(name, out int n);
                byName[name] = n + 1;
            }
            if (byName.Count == 0) return "";
            var names = new List<KeyValuePair<string, int>>(byName);
            names.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count && i < 6; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(names[i].Key).Append(" ").Append(names[i].Value);
            }
            if (names.Count > 6) sb.Append(" and ").Append(names.Count - 6).Append(" more");
            return sb.ToString();
        }

        /// <summary>"Ping 4984, None 12", biggest first.</summary>
        private static string Listed(Dictionary<Minimap.PinType, int> byKind)
        {
            if (byKind.Count == 0) return "";
            var kinds = new List<KeyValuePair<Minimap.PinType, int>>(byKind);
            kinds.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder();
            foreach (var kv in kinds)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key).Append(" ").Append(kv.Value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Everything else on the map: pins that are neither ours nor one of the five a player
        /// can place, counted by what kind of pin they are. Returns a readable list and sets
        /// <paramref name="total"/>.
        ///
        /// <para>Written because the two counts above are a report on two of the game's eighteen
        /// kinds of pin. Ours are the numbers above a hundred and the placed ones are Icon0 to
        /// Icon4; the other sixteen -- a death marker, a bed, a shout, a world landmark, a boss,
        /// another player, a random event, a ping, an event area, Hildir's three, a memorial --
        /// were counted by neither bucket and deleted by nothing, tgm_clearlocal all included.
        /// So "0 left over from this mod" was a true sentence about two kinds that read like a
        /// statement about the whole map, and a player whose map was covered in memorials and
        /// Hildir marks and ping rings could run the command, be told there was nothing to
        /// delete, and have no way left to ask.</para>
        ///
        /// <para>Nothing here is deleted by this mod. Several of these are the game's own and
        /// belong to it -- the landmarks, the boss altars, the live players and pings -- and some
        /// are a player's own death marker or bed. This says what is there and leaves it alone;
        /// knowing the number is the part that was missing.</para>
        /// </summary>
        internal static string OtherPins(out int total)
        {
            total = 0;
            if (Map == null) return "";
            var ours = DrawnByUs();
            var byKind = new Dictionary<Minimap.PinType, int>();
            foreach (var pin in Access.Pins(Map))
            {
                if (pin == null || ours.Contains(pin)) continue;
                // Counted on their own line, so the two numbers never say the same pin twice.
                if (IsSavedGhost(pin, ours)) continue;
                int type = (int)pin.m_type;
                if (PinTypes.IsCustom(type) || PinTypes.IsPlayerPlaceable(type)) continue;
                byKind.TryGetValue(pin.m_type, out int n);
                byKind[pin.m_type] = n + 1;
                total++;
            }
            return Listed(byKind);
        }

        /// <summary>
        /// Deletes pins with no record of ours behind them. Leftovers of this mod's own -- which
        /// carry a type number nothing else could have made -- always go.
        /// <paramref name="includePlaced"/> also takes the ones a player put down the game's own
        /// way, and <paramref name="kept"/> says how many of those were spared.
        ///
        /// <para>Spared by default, because the two are not alike. A leftover of ours is a marker
        /// that lost its record and can only ever be rubbish; a pin on one of the game's own five
        /// types may be something a person walked out and placed by hand, and this deleted both
        /// on one word with one number to show for it. Not hypothetical: it took 124 hand-placed
        /// pins off the author's own map while the leftovers it was aimed at numbered 18.
        /// tgm_import adopts those first, and "all" is there for anyone who means it.</para>
        /// </summary>
        internal static int ClearLocalPins(bool includePlaced, out int kept)
        {
            kept = 0;
            if (Map == null) return 0;
            int n = 0;
            var ours = DrawnByUs();
            foreach (var pin in new List<Minimap.PinData>(Access.Pins(Map)))
            {
                if (pin == null || ours.Contains(pin)) continue;
                int type = (int)pin.m_type;
                // Custom asked first: IsPlayerPlaceable counts ours among the placeable ones.
                bool leftover = PinTypes.IsCustom(type) || IsSavedGhost(pin, ours);
                if (!leftover && !PinTypes.IsPlayerPlaceable(type)) continue;
                if (!leftover && !includePlaced) { kept++; continue; }
                Map.RemovePin(pin);
                n++;
            }
            return n;
        }

        /// <summary>Crosses off the nearest marker with this icon within the radius. False if none or already crossed off.</summary>
        internal static bool TryCheckOff(string icon, Vector3 pos, float radius) =>
            TryCheckOff(icon, pos, radius, out _);

        /// <summary>
        /// The same, saying which marker it was. A caller that has to be able to put this back
        /// needs the key: crossing a marker off is a change to somebody else's marker as much as
        /// to your own, and an admin tool that did it has to be able to undo exactly that one.
        /// </summary>
        internal static bool TryCheckOff(string icon, Vector3 pos, float radius, out PinKey crossed)
        {
            crossed = default(PinKey);
            SharedPin best = null;
            float bestDistance = float.MaxValue;
            foreach (var pin in Store.Pins.Values)
            {
                if (pin.Checked || !IconRegistry.SameKey(pin.Icon, icon)) continue;
                float d = Geo.FlatDistance(pin.Pos, pos);
                if (d <= radius && d < bestDistance) { bestDistance = d; best = pin; }
            }
            if (best == null) return false;
            crossed = best.Key;
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
            // Asked before the source is changed, because changing it is what makes this marker
            // stop saying nothing.
            bool saidNothing = shared.SaysNothing;
            RemovePinData(was);
            // Lifted rather than erased: the marker is not gone, it is being written down under
            // the name we have just worked out for it, and an erasure here leaves a ghost that
            // outlives everything it was ever meant to guard.
            //
            // Lifting is safe only because every other copy of the map works the same answer out
            // of the same catalog and arrives here by itself. A marker that said nothing is the
            // one case where that is false: there is nothing on it for anyone to work anything
            // out from, so a copy that has not been repaired keeps handing the blank back, and
            // the folding pass cannot settle the pair either, because the two wear different
            // icons -- the dot and whatever the world named. That one is erased, so the answer
            // travels instead of being argued with forever. There are a handful of these, not
            // the four thousand fills that made lifting necessary in the first place.
            if (saidNothing) Store.Delete(was, out _, suppress: false);
            else Store.Lift(was, out _);
            shared.Source = next;
            Store.Upsert(shared);
            EnsurePin(shared);
            PersonalMap.Touch();
            return shared;
        }

        /// <summary>
        /// Take a marker out as though it had never been written: no erasure, no suppression,
        /// nothing left behind that says anything about the spot.
        ///
        /// This is for a tool undoing its own writing. An erasure is a claim about the world --
        /// "the thing is not there" -- and it travels as one, which is right when a player says it
        /// and wrong when a tool is being told to put a map back the way it was. The cost is that
        /// it travels nowhere: a copy that already took these markers in, another player or a
        /// cartography table, hands them straight back at the next merge, because nothing has told
        /// it otherwise. Whoever asks for this has to want that trade.
        /// </summary>
        internal static void Lift(PinKey id)
        {
            if (id.IsNone || !Store.Pins.ContainsKey(id)) return;
            RemovePinData(id);
            Store.Lift(id, out _);
            PersonalMap.Touch();
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
        /// <summary>
        /// Put an erased marker back, and make the putting back travel.
        ///
        /// <para>An erasure is a tombstone with a date on it, and a tombstone only beats a marker
        /// older than itself -- so writing the marker again with today's date is all it takes.
        /// Upsert stamps it, drops our own tombstone for it, and at the next merge every other
        /// copy finds a record newer than the erasure it is holding and lets go of it too. That
        /// is the difference between this and restoring a backup, which replays the dates the
        /// markers had when the backup was taken: those are older than the erasure, so the
        /// erasure wins and the restore appears to do nothing.</para>
        ///
        /// <para>It can only undo an erasure that left no suppression behind. A suppression is a
        /// standing instruction not to record there again; the list of them is append-only and
        /// merges by union, so one that has reached another player cannot be taken back from
        /// here. Every bulk tool erases with suppress false for exactly this reason.</para>
        /// </summary>
        internal static bool Restore(SharedPin pin)
        {
            if (pin == null || pin.Key.IsNone) return false;
            if (Store.Pins.ContainsKey(pin.Key)) return false;   // already back
            var copy = pin.Clone();
            Store.Upsert(copy);
            EnsurePin(copy);
            PersonalMap.Touch();
            return true;
        }

        /// <summary>
        /// Every recorded marker standing for something in one group, and how many of them carry
        /// a label somebody wrote. A marker with a label is not scenery any more, whatever it
        /// stands for, so it is counted apart and never erased in bulk.
        /// </summary>
        internal static List<SharedPin> InGroup(string group, out int labeled)
        {
            var found = new List<SharedPin>();
            labeled = 0;
            if (string.IsNullOrEmpty(group)) return found;
            foreach (var pin in Store.Pins.Values)
            {
                if (pin == null || !pin.Auto) continue;
                if (!string.Equals(Catalog.GroupOf(pin.Source), group, System.StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(pin.Name)) { labeled++; continue; }
                found.Add(pin);
            }
            return found;
        }

        /// <summary>
        /// Erase every recorded marker in one group, for everyone, and write down what went so it
        /// can be taken back.
        ///
        /// <para>Through <see cref="RemoveStale"/>, which erases without suppressing. That is not
        /// a detail: a suppression is a standing instruction not to record there again, the list
        /// of them is append-only and merges by union, and one that has reached another player
        /// cannot be called back from here -- so an erasure that suppressed could never be undone
        /// whatever record was kept of it. What stops these coming straight back is the group's
        /// own switch, which is turned off in the same breath.</para>
        /// </summary>
        internal static int EraseGroup(string group, AdminUndo.Entry record, out int labeled)
        {
            var doomed = InGroup(group, out labeled);
            if (doomed.Count == 0) return 0;
            if (record != null) foreach (var pin in doomed) record.Erased.Add(pin.Clone());
            foreach (var pin in doomed) RemoveStale(pin.Key);
            Restyle();
            return doomed.Count;
        }

        /// <summary>
        /// Put a marker back under the identity it had before something rewrote its source, and
        /// make the correction travel.
        ///
        /// <para>A marker's key is made of its owner, its place and its source, so changing the
        /// source is changing which marker it is. Two things have to happen at once: the identity
        /// it was given has to go, and the one it should have had has to arrive.</para>
        ///
        /// <para><b>The old one is erased rather than lifted, and that is the whole difference
        /// between a repair that holds and one that comes undone.</b> Lifting leaves no tombstone,
        /// so every other copy still holding the rewritten marker hands it straight back at the
        /// next merge and the map finishes with both -- the same marker twice, under two
        /// identities, which is what a wrong source costs in the first place. Erasing says so
        /// outright, and the record arriving with today's date beats any erasure older than it,
        /// so the pair settles the same way everywhere.</para>
        ///
        /// <para>suppress false, so this stays undoable: a suppression is append-only and merges
        /// by union, so one that has reached another player could never be called back.</para>
        /// </summary>
        /// <summary>Whether the map already holds a marker under this identity.</summary>
        internal static bool Holds(PinKey key) => !key.IsNone && Store.Pins.ContainsKey(key);

        internal static bool Rewrite(PinKey was, SharedPin back)
        {
            if (back == null || back.Key.IsNone) return false;
            if (was.IsNone || !Store.Pins.ContainsKey(was)) return false;
            if (Store.Pins.ContainsKey(back.Key)) return false;   // already there under that name
            RemovePinData(was);
            Store.Delete(was, out _, suppress: false);
            Store.Upsert(back);
            EnsurePin(back);
            PersonalMap.Touch();
            return true;
        }

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
                // Remembered locally, and Modified is untouched on purpose, so this is not a
                // change to anybody else -- a copy that received it would compare the two and
                // keep its own. Nothing is marked for sending for that reason. The map is
                // written to the character whole, so it is kept all the same.
                pin.SetKind(inferred.Value.ToString());
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

        /// <summary>The vanilla pin drawn for a marker, or null if it is not on the map now.</summary>
        internal static Minimap.PinData PinDataFor(PinKey id)
        {
            return !id.IsNone && _pinById.TryGetValue(id, out var pin) ? pin : null;
        }

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
            float scale = kind.HasValue && TgmConfig.MarkerSize.TryGetValue(kind.Value, out var size)
                ? Mathf.Clamp(size.Value, 20, 100) / 100f
                : 1f;
            // And the one correction that is about the picture rather than the kind: an icon whose
            // artwork fills more of its frame than the others looks a size bigger at the same
            // size. Here rather than at the styling pass, so everything that reads a marker's
            // size -- the ping's swell included -- reads the same number.
            //
            // The swell of a marker in its cross-off grace is in here for that reason and not
            // only for tidiness. Written anywhere else it would be fighting this: the styling
            // pass sets a marker's scale from this number whenever the map lays out, so a pulse
            // applied on top would be wiped on every layout -- which on a map that is being
            // panned, or a minimap, is every frame. Here, every writer agrees.
            return scale * IconRegistry.SizeOf(IconOf(shared)) * JustCrossedOff.Grow(shared.Key);
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

        /// <summary>
        /// A marker that already stands for this very thing, near enough to be the same one.
        ///
        /// Near enough is the thing's own size where the game knows it. A tar pit is tens of
        /// meters across and its marker sits wherever the player who found it stood, so two
        /// markers for one pit are commonly five to nine meters apart -- further than any fixed
        /// number small enough to be safe elsewhere. For a thing the world places many of, a rock
        /// or a bush, there is no radius and the measure stays tight, because two copper rocks
        /// four meters apart are two rocks.
        /// </summary>
        internal static bool AlreadyStandsFor(string source, Vector3 pos)
        {
            if (string.IsNullOrEmpty(source)) return false;
            float radius = Mathf.Max(4f, Catalog.LocationRadius(source));
            foreach (var pin in All)
            {
                if (pin == null) continue;
                if (!string.Equals(pin.Source, source, System.StringComparison.Ordinal)) continue;
                if (Geo.FlatDistance(pin.Pos, pos) <= radius) return true;
            }
            return false;
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

        /// <summary>Called once the game has laid the markers out; see ApplyLabelVisibility.</summary>
        internal static void AfterLayoutLabels(Minimap map)
        {
            if (map != null) ApplyLabelVisibility(map);
        }

        /// <summary>Whether the player's own vanilla pins are currently held off, so the pass that
        /// puts them back still runs the once after the master switch comes on again.</summary>
        private static bool _ownPinsHidden;

        /// <summary>
        /// "Hide all" means all of it: the vanilla pins a player made as well as this mod's.
        ///
        /// <para>It used to mean only ours, which made the words wrong on a map that still had a
        /// hundred hand-placed pins and every death marker on it. What a player wants from that
        /// switch is the land back -- the terrain, and the few things that are not theirs.</para>
        ///
        /// <para>So the line falls between what a player put on the map and what the game put
        /// there. Everything a player made goes: this mod's markers, the five pins vanilla lets
        /// them place, their death markers, their bed. The game's own landmarks stay, because
        /// they are the map rather than notes on it -- the boss altars, the traders, Hildir's
        /// camps and the world's center are all <c>PinType.None</c> pins the game keeps in
        /// <c>m_locationPins</c> and rebuilds every five seconds from the locations it knows, so
        /// hiding them would be hiding the world. Players, pings and shouts stay too: they are
        /// live and they are gone in a moment anyway.</para>
        ///
        /// <para>Done per pin rather than through vanilla's own <c>m_visibleIconTypes</c>, which
        /// is what its icon buttons set: writing to that would gray out those buttons behind the
        /// player's back and fight them for the same switch. A marker vanilla rebuilds starts
        /// visible, so this re-asserts on every layout -- but only touches a pin whose state is
        /// wrong, because this runs on every move of the map and SetActive on thousands of
        /// objects a frame is exactly the sort of thing that has stalled this client before.</para>
        /// </summary>
        internal static void AfterLayoutMaster(Minimap map)
        {
            if (map == null) return;
            bool hide = TgmConfig.ShowAllMarkers != null && !TgmConfig.ShowAllMarkers.Value;
            if (!hide && !_ownPinsHidden) return;
            _ownPinsHidden = hide;
            foreach (var pin in Access.Pins(map))
            {
                if (pin == null || pin.m_uiElement == null) continue;
                if (IsOurs(pin)) continue;              // the styling pass above has these
                if (!PlacedByAPlayer(pin.m_type)) continue;
                SetMarkerActive(pin, !hide);
            }
        }

        /// <summary>
        /// A vanilla pin type that exists because a player made it, as opposed to one the game
        /// draws for the world or for the moment.
        /// </summary>
        private static bool PlacedByAPlayer(Minimap.PinType type)
        {
            switch (type)
            {
                // The five the game lets a player drop by hand.
                case Minimap.PinType.Icon0:
                case Minimap.PinType.Icon1:
                case Minimap.PinType.Icon2:
                case Minimap.PinType.Icon3:
                case Minimap.PinType.Icon4:
                // Where they died, and where they will wake up. Nobody chose to place these, but
                // they are about the player rather than about the world.
                case Minimap.PinType.Death:
                case Minimap.PinType.Bed:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Show the markers again after the label switch moved. A relayout and nothing else: the
        /// labels exist either way now, and ApplyLabelVisibility turns them on and off.
        ///
        /// This rebuilt every pin until 4.5.1, because a marker born without a name had no label
        /// to put back -- a quarter of a second of destroying and instantiating thousands of
        /// objects, for something a player expects to be instant.
        /// </summary>
        internal static void RedrawLabels()
        {
            Restyle();
        }

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
            // The name is always given, even with labels switched off. Withholding it is what
            // vanilla wants -- a label is built for a pin that had a name at AddPin and never
            // afterwards -- but it makes the switch cost a rebuild of every pin on the map,
            // because a marker born without a name has no label to switch back on. That was a
            // quarter of a second of destroying and instantiating thousands of objects for
            // something a player expects to be instant. The label is built and then hidden
            // instead, which is what the game's own pins needed anyway. Few markers pay for it:
            // an auto-recorded berry bush carries no name and gets no label either way.
            var pin = Map.AddPin(shared.Pos, (Minimap.PinType)LocalType(shared), shared.Name ?? "",
                                 true, shared.Checked, 0L);
            if (pin == null) return;
            // A marker that carries no label still has a name; it is simply not written on the map.
            // Vanilla builds a label only for a pin added with a name, so naming it afterwards puts
            // no word on the map and still gives anything that reads a pin's name something to know
            // it by -- which is how a compass mod is told to leave the mushrooms off its band.
            // What it is rather than what it is wearing: a dvergr statue borrowing the black
            // marble block's picture should not tell a compass mod it is a block of black marble.
            if (string.IsNullOrEmpty(pin.m_name)) pin.m_name = Focus.TypeLabel(shared) ?? "";
            _pinById[shared.Key] = pin;
            _idByPin[pin] = shared.Key;
        }

        /// <summary>
        /// Switch off the labels the game drew for pins this mod did not make.
        ///
        /// Our own markers never get one: the name is withheld at AddPin, and vanilla writes a
        /// label only for a pin that had a name then. That does nothing for the pins the game
        /// puts up itself -- a death marker, a bed, a boss, one of Hildir's -- and those are the
        /// labelled ones in practice, because a recorded marker is usually a berry bush that
        /// explains itself and carries no name at all.
        ///
        /// So those are hidden rather than prevented. The label is its own object hanging off the
        /// pin, and hiding it leaves every name exactly where it was: nothing is blanked, nothing
        /// is saved differently, and switching back on needs no record of what used to be there.
        /// Preventing them is not an option anyway -- the game reads the label's text component
        /// straight after creating it, without checking, so a pin whose label was refused takes
        /// the map down on the next pass.
        ///
        /// Costs a field read for each pin and nothing more for the great majority, which have no
        /// label object to ask about. That matters because this runs off the layout pass, and the
        /// layout pass runs every time the map moves.
        /// </summary>
        /// <summary>Whether a label is currently switched off, so the pass that puts them back
        /// still runs the once after every switch is on again.</summary>
        private static bool _labelsHidden;

        private static void ApplyLabelVisibility(Minimap map)
        {
            bool markers = TgmConfig.ShowMarkerLabels == null || TgmConfig.ShowMarkerLabels.Value;
            bool portals = TgmConfig.ShowPortalLabels == null || TgmConfig.ShowPortalLabels.Value;
            bool bosses = TgmConfig.ShowBossLabels == null || TgmConfig.ShowBossLabels.Value;
            // The master switch is in here because a name is part of the marker: a pin held off
            // the map must not leave its label floating over the ground where it stood.
            bool all = TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value;
            // Nothing to hide and nothing hidden: the common case, and the only one worth
            // skipping. Returning merely because every switch is on would leave whatever was
            // hidden earlier hidden for good, since this is the only thing that puts a label
            // back.
            if (markers && portals && bosses && all && !_labelsHidden) return;
            _labelsHidden = !(markers && portals && bosses && all);
            foreach (var pin in Access.Pins(map))
            {
                var data = pin != null ? pin.m_NamePinData : null;
                var go = data != null ? data.PinNameGameObject : null;
                if (go == null) continue;
                // Live players and chat pings are not markers and keep their names: they are
                // transient, there are a handful, and hiding who is where is not what either
                // switch was asked for.
                if (pin.m_type == Minimap.PinType.Player || pin.m_type == Minimap.PinType.Ping) continue;
                // A boss's name is one of five on a whole world and each is worth reading, so it
                // follows its own switch rather than the one about berry bushes. The pin type is
                // what says so, which also covers a boss altar this mod recorded itself: those
                // wear the same picture, and "the boss labels" means the labels on those.
                bool want = Portals.IsPortalPin(pin) ? portals
                    : IsBossMarker(pin) ? bosses
                    : markers;
                // And never a name over a marker that is not being drawn, whatever hid it.
                if (want && pin.m_uiElement != null && !pin.m_uiElement.gameObject.activeSelf) want = false;
                if (go.activeSelf != want) go.SetActive(want);
            }
        }

        /// <summary>
        /// A boss's marker: one of the game's own vegvisir pins, or an altar this mod recorded.
        ///
        /// <para>The number answered for both until 4.8.5, because an altar of this mod's was
        /// drawn as a vanilla boss pin. It wears a number of this mod's own now, so the record
        /// behind it is what says this is a boss.</para>
        /// </summary>
        private static bool IsBossMarker(Minimap.PinData pin)
        {
            if (pin.m_type == Minimap.PinType.Boss) return true;
            if (!_idByPin.TryGetValue(pin, out var id)) return false;
            return Store.Pins.TryGetValue(id, out var shared)
                   && IconRegistry.BorrowedVanillaType(IconOf(shared), out int type)
                   && type == (int)Minimap.PinType.Boss;
        }

        /// <summary>Drop the marker's vanilla pin and add it again from the record (name or icon changes).</summary>
        private static void ReplacePinData(SharedPin shared)
        {
            RemovePinData(shared.Key);
            EnsurePin(shared);
        }

        /// <summary>
        /// A pin the game put there itself, by the two things that say so together: a kind the
        /// game places once and never again (<see cref="PinTypes.GamePlacedOnce"/>), under a name
        /// out of the game's own text table.
        ///
        /// <para>Both halves are needed, because this mod draws markers with the game's pin
        /// numbers on purpose -- a boss altar of its own is drawn as PinType.Boss, so the number
        /// alone cannot tell the game's pin from one of ours standing beside it. The name can:
        /// the game writes keys ($enemy_bonemass, $hud_pin_hildir1) and this mod writes the words
        /// a player reads. A marker of ours is therefore still cleaned up, and the altar the game
        /// marked for you is left exactly where it is.</para>
        ///
        /// <para>The case this is for is a record of ours that was bound to the game's own pin
        /// rather than to one we drew, which an older version could do and this one cannot.
        /// Retiring such a record used to take the game's pin off the map with it.</para>
        /// </summary>
        internal static bool TheGamesOwn(Minimap.PinData pin)
        {
            return pin != null && PinTypes.GamePlacedOnce(pin.m_type)
                && (pin.m_name ?? "").StartsWith("$", System.StringComparison.Ordinal);
        }

        private static void RemovePinData(PinKey id)
        {
            if (!_pinById.TryGetValue(id, out var old)) return;
            _pinById.Remove(id);
            _idByPin.Remove(old);
            if (Map == null) return;
            // Let go of it and leave it standing, where the pin is the game's own. The record is
            // gone either way; the altar it was bound to is not ours to unmark.
            if (TheGamesOwn(old)) return;
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

        /// <summary>
        /// The character file has just been read back. Take out every pin wearing one of this
        /// mod's own numbers, because not one of them can mean anything.
        ///
        /// <para>The numbers are handed out in the order icons are first met and so do not even
        /// mean the same thing twice: a pin saved as 114 comes back as whatever 114 happens to be
        /// today, which is how a map filled with tar pits and runestones and the pins somebody
        /// placed by hand came back wearing the wrong pictures and then the right ones. Nothing
        /// is behind them -- the markers themselves are saved with the character, separately, and
        /// are about to be drawn afresh -- so there is nothing to weigh and nothing to lose.</para>
        ///
        /// <para>This runs inside the profile load, before any marker of ours is drawn, so every
        /// pin of our kind in the list at this moment is a leftover by definition. 4.8.1 stopped
        /// them being written; this stops the ones already written from ever being seen again,
        /// which matters because the player could not see them for what they were: they carry no
        /// record, so not one of this mod's switches could hide them and the panel could not
        /// count them. One player was carpeted with 4,984 of them over a map of 5,148 real
        /// markers and had to be told a console command to get his map back.</para>
        /// </summary>
        internal static void DropProfileLeftovers(Minimap map)
        {
            if (map == null) return;
            int n = 0;
            int ghosts = 0;
            var ours = DrawnByUs();
            foreach (var pin in new List<Minimap.PinData>(Access.Pins(map)))
            {
                if (pin == null || ours.Contains(pin)) continue;
                bool mine = PinTypes.IsCustom((int)pin.m_type);
                if (!mine && !IsSavedGhost(pin, ours)) continue;
                map.RemovePin(pin);
                if (mine) n++; else ghosts++;
            }
            if (n > 0)
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Took {n} marker(s) of this mod's off the map that an older version had " +
                    "left in the character file. They had no record behind them and no settled picture; the " +
                    "markers themselves are saved with the character and are unaffected.");
            if (ghosts > 0)
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Took {ghosts} pin(s) off the map that the game never saves -- pings, " +
                    "shouts, other players, the world's own landmarks -- and which were saved anyway. The game " +
                    "makes those afresh from what is happening now, so a saved one is a picture of a moment " +
                    "long past and nothing can tell it from the moment itself.");
        }

        /// <summary>
        /// Take out the copies of this mod's markers that are sitting in the character file
        /// wearing one of the game's own pin numbers.
        ///
        /// <para>These are the leak the mint change closed, already written. Until 4.8.5 a
        /// marker whose icon named one of the game's pictures was drawn as that kind of vanilla
        /// pin -- a runestone as a memorial, a structure as a house, a camp as a fire -- and a
        /// copy that reached the character file was afterwards indistinguishable from a pin the
        /// player had dropped by hand. Every sweep and every switch asks the number, so nothing
        /// found it, nothing hid it, and the save guard wrote it out again at every save. It came
        /// back at every login, under this mod's own words, standing on top of the marker it was
        /// a copy of. Deleting one by hand got it back the next day.</para>
        ///
        /// <para>What makes a copy recognizable is the marker it was copied from. A pin is taken
        /// off only where a record of this mod's stands within a meter of it, wearing the vanilla
        /// number that record's icon borrows, under exactly the name this mod would have written
        /// for it. The game names its own pins with localization keys -- $enemy_eikthyr,
        /// $hud_pin_hildir1 -- and this mod writes plain words, so a vanilla pin cannot answer to
        /// all three. A player's own pin could, by placing it on a marker with the same picture
        /// and the same name, and then nothing is lost either: the marker is standing in the same
        /// place wearing the same picture.</para>
        ///
        /// <para>Ownership is not asked. A copy that travelled to another player through a
        /// cartography table came back carrying their id, and it is the same wreckage.</para>
        /// </summary>
        internal static void DropVanillaTypedCopies()
        {
            if (Map == null || Store.Pins.Count == 0) return;
            // What this mod would have drawn, by the number the game would have given it and the
            // name it would have carried. Keyed on both, so only an exact impostor is looked at.
            var want = new Dictionary<string, List<Vector3>>(StringComparer.Ordinal);
            foreach (var shared in Store.Pins.Values)
            {
                string name = !string.IsNullOrEmpty(shared.Name)
                    ? shared.Name
                    : (Focus.TypeLabel(shared) ?? "");

                // Every marker, whatever it wears, may have left a copy wearing the plain dot.
                //
                // Not because the dot was ever chosen: Minimap.AddPin rewrites a type past the
                // end of m_visibleIconTypes to Icon3 and keeps the name, so for as long as this
                // mod's range was out of that array's reach -- see IconRegistry.EnsureArrays --
                // every copy the character file handed back came in wearing the dot. The number
                // that said whose it was is the one thing the game threw away, which is why the
                // sweep that reads the number walks straight past them, and why deleting one by
                // hand brings it back at the next login.
                //
                // So they are recognized the only way left: a saved pin wearing the dot,
                // standing on a record of this mod's, under exactly the name this mod would have
                // written for it. On one map that was 16,198 of them, two to a marker, against
                // not one dot that was anybody's own.
                Expect(want, (int)Minimap.PinType.Icon3, name, shared.Pos);

                // And the copies that wear a vanilla picture because the marker does. A "like:"
                // marker always had a number of this mod's own, so no copy of one can be wearing
                // a vanilla number -- and looking for one would mean hunting the game's own pins
                // by position and name.
                string icon = IconRegistry.Normalize(IconOf(shared));
                if (icon == null || !icon.StartsWith("pin:")) continue;
                if (!IconRegistry.BorrowedVanillaType(icon, out int type)) continue;
                if (type != (int)Minimap.PinType.Icon3) Expect(want, type, name, shared.Pos);
            }
            if (want.Count == 0) return;

            int n = 0, dots = 0;
            var ours = DrawnByUs();
            foreach (var pin in new List<Minimap.PinData>(Access.Pins(Map)))
            {
                if (pin == null || !pin.m_save || ours.Contains(pin)) continue;
                // The name test below is meant to do this on its own -- a copy of ours carries
                // this mod's words and the game's own pin carries a key -- but it is doing it on
                // a record's name, and a record adopted from one of these pins carries the key
                // too. Asked here of the pin, where the answer cannot have been copied.
                if (TheGamesOwn(pin)) continue;
                if (!want.TryGetValue((int)pin.m_type + "|" + (pin.m_name ?? ""), out var spots)) continue;
                bool onAMarker = false;
                foreach (var spot in spots)
                    if (Utils.DistanceXZ(spot, pin.m_pos) < 1f) { onAMarker = true; break; }
                if (!onAMarker) continue;
                if (pin.m_type == Minimap.PinType.Icon3) dots++;
                Map.RemovePin(pin);
                n++;
            }
            if (n > 0)
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Took {n} copy(ies) of this mod's markers off the map that an older version " +
                    "left in the character file wearing one of the game's own pin pictures" +
                    (dots > 0 ? $", {dots} of them the plain dot the game puts on a marker it cannot place" : "") +
                    ". Each was standing on the marker it was a copy of, under the same name, and nothing here " +
                    "could tell it from a pin you had placed by hand. The markers themselves are untouched.");
        }

        /// <summary>One more place a copy of this marker could be standing, under this name.</summary>
        private static void Expect(Dictionary<string, List<Vector3>> want, int type, string name, Vector3 pos)
        {
            string key = type + "|" + name;
            if (!want.TryGetValue(key, out var spots)) want[key] = spots = new List<Vector3>();
            spots.Add(pos);
        }

        /// <summary>
        /// Take down every record for something the game marks by itself, and say how many.
        ///
        /// <para>The boss altars, the traders, the three errands Hildir sends you on. The game
        /// pins all of those without being asked -- the altars from the first minute of a world,
        /// the rest the moment you find one -- so a marker of this mod's for one was always a
        /// second pin saying the same thing in a different picture, and a player looking at two
        /// pictures on one altar has no way to know which of them is his to switch off.</para>
        ///
        /// <para><b>A standing rule rather than a pass that runs once.</b> Retiring them at a
        /// migration was not enough and could not be: the markers live on the shared map as well
        /// as this one, so the next merge with a player who had not yet run that version -- or
        /// with the server, which never runs it -- handed every one of them straight back. So
        /// this runs when the map is loaded and after every merge, and what it takes down stays
        /// down however often it comes round again.</para>
        ///
        /// <para>Retired, not erased. An erasure is a claim about the world -- the thing is not
        /// there -- and it travels as one; these are still there, and still marked. What is
        /// dropped is this mod's second opinion about them.</para>
        /// </summary>
        /// <summary>
        /// How many recorded markers no longer know what they are, and the handful of things they
        /// stood for, most first.
        ///
        /// <para>A recorded marker does not carry its kind: it works it out from the catalog
        /// every time it is read. So taking a prefab out of a catalog line, or sweeping it into a
        /// kind's exclusions, quietly unmakes every marker already written for it -- the record
        /// survives and everything that made it legible does not. No kind means no name, no
        /// picture and no row in the marker list, which leaves a gold dot titled "marker" that no
        /// switch can hide and nothing explains.</para>
        ///
        /// <para>Counted at every login because that is the thing nobody had. The catalog is
        /// edited over months, a line goes, and the markers it unmade sit on the map for as long
        /// as it takes somebody to notice a dot and ask. One line in the log turns that into
        /// something the map says about itself.</para>
        /// </summary>
        internal static int CountForgotten(out string what)
        {
            what = "";
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var pin in All)
            {
                if (pin == null || !pin.Auto || KindOf(pin).HasValue) continue;
                n++;
                string src = string.IsNullOrEmpty(pin.Source) ? "(nothing recorded)" : pin.Source;
                counts.TryGetValue(src, out int had);
                counts[src] = had + 1;
            }
            if (n == 0) return 0;
            var sources = new List<string>(counts.Keys);
            sources.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a])
                                                          : string.Compare(a, b, StringComparison.Ordinal));
            var said = new List<string>();
            for (int i = 0; i < sources.Count && i < 5; i++) said.Add($"{sources[i]} x{counts[sources[i]]}");
            what = string.Join(", ", said.ToArray());
            if (sources.Count > 5) what += $", and {sources.Count - 5} more";
            return n;
        }

        internal static int DropWhatTheGameMarks()
        {
            if (Store.Pins.Count == 0) return 0;
            var stale = new List<PinKey>();
            foreach (var pin in All)
            {
                var kind = KindOf(pin);
                if (!kind.HasValue || !Categories.MarkedByGame(kind.Value)) continue;
                // One placed by hand is kept unless the game is already marking that spot. "The
                // trader is over there" is a perfectly good thing to write down for somebody who
                // has not met him, and the panel offers the button that writes it -- the game
                // has pinned nothing there, so there is nothing superfluous about it. Dropped
                // where the game's own pin is standing on it, which is the whole argument.
                if (!pin.Auto && !WorldPins.MarkedWithin(pin.Pos, ByHandNear)) continue;
                stale.Add(pin.Key);
            }
            foreach (var key in stale) RemoveStale(key);
            return stale.Count;
        }

        /// <summary>How near the game's own mark a hand-placed marker has to be to be the same place.</summary>
        private const float ByHandNear = 48f;

        /// <summary>Vanilla cleared every pin (profile load). Our PinData objects are gone; records stay.</summary>
        internal static void OnPinsCleared()
        {
            _pinById.Clear();
            _idByPin.Clear();
            Portals.OnPinsCleared();
            Transports.OnPinsCleared();
            OwnMarks.OnPinsCleared();
        }

        /// <summary>A new pin got its name on the map screen: put it on the personal map if configured.</summary>
        internal static void OnPinPlaced(Minimap.PinData pin)
        {
            if (pin == null || Map == null) return;
            if (!Access.Pins(Map).Contains(pin) || IsOurs(pin)) return;
            if (!PinTypes.IsPlayerPlaceable((int)pin.m_type)) return;
            AdoptLocalPin(pin, auto: false);
        }

        /// <summary>After a left click: record any check-off changes.</summary>
        internal static void PushCheckedChanges()
        {
            bool changed = false;
            // The ones that just went off the map, collected rather than acted on in the loop:
            // revealing asks for a layout, and nothing should ask for one while the list the
            // layout reads is being walked.
            List<SharedPin> crossed = null;
            foreach (var kv in _idByPin)
            {
                if (!Store.Pins.TryGetValue(kv.Value, out var shared)) continue;
                if (kv.Key.m_checked == shared.Checked) continue;
                bool crossedOff = kv.Key.m_checked;
                shared.Checked = kv.Key.m_checked;
                Store.Upsert(shared);
                changed = true;
                // Crossing a marker off by clicking it is the one edit that can hide its own
                // undo: the gesture that takes it back is another click on the same marker, and
                // the marker can be gone before the eye has found it. It is also the easiest edit
                // to make by accident, being a left click anywhere near a marker.
                if (crossedOff) (crossed ?? (crossed = new List<SharedPin>())).Add(shared);
            }
            if (changed) PersonalMap.Touch();
            if (crossed == null) return;
            // Every one of them, whatever the switches say. There are two ways the marker goes --
            // hidden for being crossed off, or thinned away for losing its patch to a neighbor
            // that is not -- and the second needs nothing switched on to happen.
            foreach (var shared in crossed) JustCrossedOff.Add(shared.Key);
            // Named only where it would have left the map outright. One held back from thinning is
            // still there to be looked at, and a player working down a row of ruins does not want
            // telling forty times what is in front of them.
            int gone = 0;
            SharedPin last = null;
            foreach (var shared in crossed) if (CrossedOff.IsHidden(shared)) { gone++; last = shared; }
            if (gone == 0) return;
            TheGreatestMapMod.Message(gone == 1
                ? $"Crossed off {Focus.TypeLabel(last)}. Click it again to put it back."
                : $"Crossed off {gone} markers. Click one again to put it back.");
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
            // Where the player is, read once rather than per marker. The distance is flat on
            // purpose: inside a dungeon the character stands a few thousand meters above the
            // world, and a marker at that spot on the map is still the marker you are next to.
            float nearRadius = TgmConfig.ShowHiddenWithin != null ? TgmConfig.ShowHiddenWithin.Value : 0f;
            float nearRadiusSq = nearRadius > 0f ? nearRadius * nearRadius : 0f;
            var localPlayer = Player.m_localPlayer;
            bool havePlayer = localPlayer != null;
            Vector3 playerPos = havePlayer ? localPlayer.transform.position : Vector3.zero;
            _portalPins.Clear();
            _fadedPins.Clear();
            // Two passes now, because thinning cannot be decided one marker at a time: whether
            // this berry bush is drawn depends on the other bushes that are going to be drawn.
            // So the first pass settles every switch and collects what would be shown, Thinning
            // takes out the repetition, and the second pass styles what is left. The styling is
            // what costs -- scale, fade, label, sprite, about 1.7 us a marker -- so doing it only
            // for survivors is worth something on its own, quite apart from the canvas.
            _shown.Clear();
            _thinned.Clear();
            foreach (var kv in _idByPin)
            {
                var pin = kv.Key;
                if (pin.m_uiElement == null) continue;
                if (!Store.Pins.TryGetValue(kv.Value, out var shared)) continue;
                var kind = KindOf(shared);
                // Something asked for this marker by name, so thinning leaves it alone: it is
                // close enough to the player to be part of the honest near map, it was just
                // recorded, or it is answering tgm_locate. See Drawn.Always.
                bool asked = false;
                bool hiddenByHand = ViewPrefs.IsHidden(kv.Value);
                bool hidden = hideEverything || hiddenByHand || PictureHidden(shared);
                if (!hidden && kind.HasValue)
                {
                    if (TgmConfig.ShowKind.TryGetValue(kind.Value, out var showKind) && !showKind.Value) hidden = true;
                    else if (smallMap && TgmConfig.ShowOnMinimap.TryGetValue(kind.Value, out var show) && !show.Value) hidden = true;
                    // TheGreatestPortal's picker draws a pin for every portal itself, so ours would
                    // sit on top of its, two labels over one place. Only portals stand down; the
                    // rest of the map stays, which is the point of showing it there at all.
                    else if (kind.Value == Category.Portal && Portals.PortalPickerOpen()) hidden = true;
                    else if (BiomeHidden(shared, kind)) hidden = true;
                }
                // Just recorded: show it for a while anyway, so writing something down always
                // shows you what you wrote. Not against the master switch or a marker hidden by
                // hand, which both mean "not this one".
                if (hidden && !hideEverything && !hiddenByHand && Reveals.IsRevealed(kv.Value))
                { hidden = false; asked = true; }
                else if (!hidden && Reveals.IsRevealed(kv.Value)) asked = true;
                // Standing next to it: show it anyway, whatever is switched off. Hiding a kind
                // otherwise costs you the answer to a question the map cannot be asked -- walking
                // over ground with no raspberry marker on it, you cannot tell "no raspberries
                // here" from "raspberries hidden", and the doubt is what drives a player to leave
                // everything switched on and read a map covered in berries. Within this radius
                // there is no doubt, so the rest of the map can be as quiet as you like.
                // Measured whether or not the marker is hidden, because this is also what
                // exempts a marker from thinning: the near map has to be complete to be honest,
                // and "nothing there means nothing recorded there" is exactly as true for a
                // marker thinned away as for one switched off.
                bool near = nearRadiusSq > 0f && havePlayer
                            && Geo.FlatDistanceSquared(shared.Pos, playerPos) <= nearRadiusSq;
                if (near && !hideEverything && !hiddenByHand)
                {
                    // Except for the things you built. The doubt this rule is here to settle is
                    // "nothing recorded there" against "recorded and hidden", and that doubt is
                    // about the world: you cannot know whether the ground you are standing on has
                    // raspberries on it. You do know whether your own base has a workbench in it,
                    // so switching workbenches off and having six of them stay lit over your base
                    // is the rule answering a question nobody asked.
                    //
                    // Still exempt from thinning, which is the other half of what this does and
                    // is about a crowded map rather than a hidden kind.
                    if (!(kind.HasValue && Categories.IsBuilt(kind.Value))) hidden = false;
                    asked = true;
                }
                // Crossed off, and the player would rather not see that kind of crossed-off marker
                // any more: used-up deposits, or searched places by type.
                //
                // Unless it was crossed off a moment ago by a click, which is the edit that hides
                // its own undo: what takes it back is another click on the same marker, and there
                // would be nothing left to click. Held and blinking for a few seconds instead --
                // see JustCrossedOff. Asked here rather than beside the reveal above, which is a
                // different window for a different reason, because this is decided after it.
                //
                bool grace = JustCrossedOff.Holds(kv.Value);
                if (!hidden && CrossedOff.IsHidden(shared) && !grace) hidden = true;
                // And exempt from thinning for the same few seconds, which is a second way the
                // same marker disappears and the one that actually bit first. Thinning's very
                // first rule is that a crossed-off marker loses its patch to any neighbor that is
                // not crossed off -- so the click does not hide the marker, it makes it lose a
                // contest it had been winning, and zoomed out the patches are wide enough that
                // there is nearly always a neighbor to lose to. That happens whether or not
                // crossed-off markers are hidden at all, so it is asked of every marker in its
                // grace rather than only the ones the switch above would have taken away.
                if (!hidden && grace) asked = true;
                // Looking for one thing: it wins over every switch above, since a player who asks
                // for frost caves means the frost caves whether or not dungeons are switched off.
                // Not over the master switch or a marker put out by hand, which both mean "not
                // this one" rather than "not right now".
                if (Focus.On && !hideEverything && !hiddenByHand) hidden = !Focus.Shows(shared);
                // Being asked where something is outranks having switched it off: the markers
                // come back for the few seconds the pulse lasts and hide themselves again after.
                // Still not past the master switch or a marker put away by hand, which mean "not
                // this one" rather than "not just now".
                if (hidden && !hideEverything && !hiddenByHand && Locate.On && Locate.Matches(shared))
                { hidden = false; asked = true; }
                // Never thinned: a marker a person chose to put down is the opposite of the thing
                // thinning is for. Thinning exists to quiet repetition -- the fortieth raspberry
                // bush in one meadow, recorded because somebody walked past it -- and it decides
                // which of two markers in a patch survives by a hash of the key, nothing else. A
                // marker placed by hand put into that contest lost it on a coin toss against the
                // recorded veins around it, and with the limit at eight hundred against eight
                // thousand markers the patches are wide and it lost nearly every time.
                //
                // Which read as the placing not working, in the most misleading way available:
                // the marker was there while the name box was open, because the pin the game puts
                // down is not ours to thin, and went the moment it became ours. Nothing said so,
                // tgm_list reported no reason found, and placing it again did the same thing.
                if (!hidden && shared.HandPlaced) asked = true;
                if (hidden) { SetMarkerActive(pin, false); continue; }
                _shown.Add(new Drawn { Pin = pin, Shared = shared, Kind = kind, Always = asked });
            }
            Thinning.Hold(map, _shown);
            for (int i = 0; i < _shown.Count; i++)
            {
                var pin = _shown[i].Pin;
                var shared = _shown[i].Shared;
                var kind = _shown[i].Kind;
                if (_shown[i].Held) { _thinned.Add(_shown[i].Shared.Key); SetMarkerActive(pin, false); continue; }
                SetMarkerActive(pin, true);
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
                if (kind == Category.Portal)
                {
                    // A portal wearing the game's own render of itself keeps its colors, for the
                    // same reason the campfire below does: the portal color is a multiply, and
                    // #FF6600 over a blue-lit stone render leaves an orange smudge where the
                    // picture was. Only the plain pin sprites go in the painted list.
                    if (IconRegistry.CarriesItsOwnColor(shared.Icon))
                    {
                        if (pin.m_iconElement != null)
                            pin.m_iconElement.color = faded ? Faded(Color.white) : Color.white;
                    }
                    else _portalPins.Add(pin);
                    continue;
                }
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
            // The game draws only the part of the map on screen: UpdatePins destroys the icon of
            // anything outside it (!IsPointVisible -> DestroyPinMarker) and builds it again when
            // it comes back. A marker with no icon is therefore the game's own culling and not a
            // switch of ours, and the styling pass skips it for the same reason.
            //
            // This used to be read as hidden, and then nothing could say what had hidden it: three
            // hundred markers on one listing came out as "no reason found -- which is itself worth
            // reporting", which is this diagnostic accusing the mod of a bug that was its own
            // reading of a map scrolled somewhere else. Nothing about a marker off screen is known
            // here -- not its color, not whether it would be faded -- so none of it is claimed.
            bool offScreen = pin.m_uiElement == null;
            // The pin's own type against the type this marker should be drawn with. They differ
            // when the pin on screen was not made from this record -- the game made it and we
            // took it over, or it was never ours at all -- which is invisible from everything
            // else here and is exactly what makes one marker of six come out wrong.
            int want = LocalType(shared);
            int got = (int)pin.m_type;
            string drawn = got == want
                ? $"type {got}"
                : $"type {got} but the record wants {want}  <-- MISMATCH";
            string state = $"icon {IconOf(shared)}, stored '{shared.StoredIcon}', {drawn}, " +
                           $"crossed {(shared.Checked ? "yes" : "no")}/{(pin.m_checked ? "yes" : "no")}";
            if (offScreen)
                return state + ", not on screen -- outside the part of the map being drawn, so the " +
                       "game keeps no icon for it and nothing here hid it";
            return state + $", {(_fadedPins.Contains(pin) ? "faded" : "not faded")}, {color}, " +
                   (pin.m_uiElement.gameObject.activeSelf ? "shown" : "hidden -- " + WhyHidden(shared));
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
            if (PictureHidden(shared))
                return KindOf(shared).HasValue
                    ? $"its icon ({IconOf(shared)}) is in Hidden Icons"
                    : $"its icon ({IconOf(shared)}) is switched off under Yours in the marker list";
            var kind = KindOf(shared);
            if (kind.HasValue)
            {
                if (TgmConfig.ShowKind.TryGetValue(kind.Value, out var showKind) && !showKind.Value)
                    return $"\"Show {Categories.Label(kind.Value)}\" is off";
                if (BiomeHidden(shared, kind))
                    return $"{Categories.Label(kind.Value)} are switched off in the {Biomes.Label(Biomes.Of(shared.Pos))}";
                if (TgmConfig.ShowOnMinimap.TryGetValue(kind.Value, out var onMini) && !onMini.Value)
                    return $"\"{Categories.Label(kind.Value)} On Minimap\" is off and this is the small map";
                if (kind.Value == Category.Portal && Portals.PortalPickerOpen()) return "the portal picker is open";
            }
            if (CrossedOff.IsHidden(shared)) return "it is crossed off and that kind of crossed-off marker is hidden";
            if (Focus.On && !Focus.Shows(shared)) return $"the map is showing only {Focus.What()}";
            // Thinning, which is not a switch on this marker at all but a contest with its
            // neighbors, and so was the one way of hiding a marker this list could not name. It
            // answered "no reason found -- which is itself worth reporting" instead, accusing the
            // mod of a bug for doing exactly what it was configured to do.
            if (_thinned.Contains(shared.Key))
                return "another marker with the same picture is already drawn in the same patch of ground and " +
                       "\"Thin Overlapping Markers\" is on -- raise \"Most Markers Drawn\", lower " +
                       "\"Marker Spacing On Screen\", or stand within \"Show Hidden Markers Within\" of it";
            // Asked only of a marker whose icon exists and is switched off, so every way of
            // hiding one has been tried by the time this is reached. If it answers, the pass that
            // hid the marker and the list above disagree about why, which is a bug in this mod
            // rather than a setting anybody can change.
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
        /// <summary>
        /// Show or hide one pin on the map, label and all, for the passes that hide pins this mod
        /// did not draw: the master switch above, and the world's own landmarks.
        /// </summary>
        internal static void ShowMarker(Minimap.PinData pin, bool active)
        {
            if (pin != null && pin.m_uiElement != null) SetMarkerActive(pin, active);
        }

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

    // Loading the vanilla player profile. Vanilla empties the pin list and fills it from the
    // file, so at this moment every pin wearing one of our numbers was put there by the file and
    // not by us -- nothing of ours is drawn until RebuildPins two lines down.
    //
    // Clearing up after the load rather than refusing the pins as they arrive: if the load
    // throws, which it does on a map of the wrong size, nothing has been promised and the next
    // load tries again. A flag set on the way in could be left standing by that same throw, and
    // a stuck flag here would refuse every marker this mod ever drew.
    [HarmonyPatch(typeof(Minimap), "SetMapData")]
    internal static class Minimap_SetMapData_Patch
    {
        private static void Postfix(Minimap __instance)
        {
            ClientPins.DropProfileLeftovers(__instance);
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
            // Naming one of ours rather than taking on a new one: the marker is written and
            // there is nothing to adopt.
            if (LabelEditor.Took(__state)) return;
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
            int strays = 0;
            foreach (var pin in Access.Pins(__instance))
            {
                if (!pin.m_save) continue;
                // Ours by its record, or ours by its number. The second half is what was missing,
                // and it is the same rule the cartography table has always used a few lines down.
                //
                // A type at or above IconRegistry.Base is a number this mod made up, so nothing
                // else can have created that pin and it has no meaning to anyone else -- the
                // numbers are handed out in the order icons are first met and so do not even mean
                // the same thing in the next session. Such a pin has no business in the character
                // file under any circumstance.
                //
                // Being ours was decided only by a dictionary held in memory, and a marker that
                // fell out of it -- added to the game's list before being written down, so
                // anything throwing in between stranded it -- kept m_save and was written to the
                // character. It came back at the next login as an ordinary pin with no record
                // behind it, was saved again, and the pile grew every session: nothing swept it,
                // because it carries m_ownerID 0 like all of ours and both sweeps spare those.
                // Nothing of ours could hide it either, since every switch asks the record it no
                // longer had, which is how a map could be carpeted while the panel honestly
                // reported the few markers it knew about. One player reached 24,205 of these
                // against 5,087 real markers, each wearing whatever picture its stale number
                // happened to mean this time.
                //
                // Holding them back here also clears up the ones already written: they are loaded
                // once more and then left out of the next save, so the pile drains by itself.
                bool ours = ClientPins.IsOurs(pin);
                if (!ours && !PinTypes.IsCustom((int)pin.m_type)) continue;
                if (!ours) strays++;
                pin.m_save = false;
                __state.Add(pin);
            }
            Report(__state.Count, strays);
        }

        private static bool _said;

        internal static void Forget() => _said = false;

        /// <summary>
        /// Say once a session how many pins this guard kept out of the character file.
        ///
        /// <para>For the sake of the line's absence. This guard is the whole of what stands
        /// between a map of markers and a character file full of them, and when a player's file
        /// filled up anyway there was no way to tell a guard that had failed to recognize its own
        /// pins from a guard that was never reached at all -- another mod writing the profile by
        /// its own route, a patch that silently did not take. Those want opposite fixes. One line
        /// at the first save of a session settles it in the log the player already sends.</para>
        /// </summary>
        private static void Report(int held, int strays)
        {
            if (_said) return;
            _said = true;
            if (held == 0)
            {
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] {DateTime.Now:HH:mm:ss} Nothing of this mod's was on the map at this save, " +
                    "so nothing needed keeping out of the character file.");
                return;
            }
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] {DateTime.Now:HH:mm:ss} Kept {held} of this mod's pins out of the character " +
                "file at this save" + (strays > 0
                    ? $", {strays} of them with no record behind them."
                    : ". Every one of them has a record behind it."));
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
        /// <summary>
        /// What this mod does to the map after vanilla has laid it out, in order, each stage
        /// named so a failure can say which one it was.
        ///
        /// <para>The order is the meaning and must not be shuffled. The styling settles what is
        /// shown and thins the crowd; the master switch runs before the labels so they see a pin
        /// already put away and leave its name alone rather than hanging it over empty ground;
        /// the spotlight runs last because vanilla has just built markers afresh and a new one
        /// starts visible.</para>
        ///
        /// <para>The delegates hold nothing and are built once: a lambda that captures no
        /// variable is kept in a static by the compiler, so running this list costs no
        /// allocation. Capturing the map instead would make eight objects on every step the
        /// player takes, which is how often this runs.</para>
        /// </summary>
        private static readonly string[] _stageNames =
        {
            "styling", "portals to the front", "live markers", "erasures", "survey preview",
            "master switch", "world landmarks", "labels", "player spotlight",
        };

        private static readonly System.Action<Minimap>[] _stages =
        {
            m => ClientPins.StylePins(m),
            // Vanilla may have added or reordered elements, which would bury the portals again.
            m => Portals.BringToFront(),
            // The size and the minimap switch for the kinds drawn live from the server, which the
            // styling pass above never sees: they are pins rather than markers of ours.
            m => LiveMarkers.AfterLayout(m),
            m => Erasures.Paint(),
            // What a survey would write, if one has been asked for and not yet applied.
            m => SurveyPreview.Paint(),
            m => ClientPins.AfterLayoutMaster(m),
            // The world's own landmarks, hidden one pin at a time because the game draws them all
            // as one kind of pin and the list tells them apart.
            m => WorldPins.AfterLayout(m),
            m => ClientPins.AfterLayoutLabels(m),
            m => PlayerSpotlight.AfterLayout(m),
        };

        /// <summary>Stages that have already been complained about, so each is said once.</summary>
        private static readonly HashSet<string> _reported = new HashSet<string>();

        private static void Postfix(Minimap __instance)
        {
            // Each stage on its own, because they used to share one fate. A throw in the first --
            // the styling, which is the pass that hides and thins -- skipped the seven after it,
            // labels included, and left every marker in view drawn at vanilla's size with no
            // name on it. The panel went on reporting the intended count the whole time, because
            // that is counted from the switches rather than from the screen, so the map and the
            // number it was showing could disagree completely with nothing saying why.
            for (int i = 0; i < _stages.Length; i++)
            {
                try
                {
                    _stages[i](__instance);
                }
                catch (System.Exception e)
                {
                    // Said once per stage. This runs every time the map is laid out again, which
                    // on the minimap is every step the player takes, so a stage that fails every
                    // frame would otherwise write the log faster than anyone could read it and
                    // cost more than whatever it was failing to do.
                    if (!_reported.Add(_stageNames[i])) continue;
                    TheGreatestMapMod.Log.LogError(
                        $"[TheGreatestMap] The '{_stageNames[i]}' step of drawing the map failed; the steps after it " +
                        "still ran, and this is said once rather than every frame. Markers may be drawn that should " +
                        $"be hidden, at the wrong size, or without their names. {e}");
                }
            }
            // A stage that failed is tried again on the next layout rather than switched off for
            // the session: one can fail while the HUD is being rebuilt under it and be perfectly
            // well afterwards, and giving up would turn a single blink into a map that stays
            // wrong until the game is restarted.
        }
    }
}
