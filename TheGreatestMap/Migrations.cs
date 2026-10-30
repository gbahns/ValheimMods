using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Bringing an old map up to date without anybody being told to.
    ///
    /// Every time this mod has learned to record something better, the maps already written stayed
    /// as they were, and the repair was a console command somebody had to hear about and run --
    /// tgm_identify for what a marker stands for, tgm_erase for the markers that should never have
    /// been written. That is not a repair. It works for the one person who read the changelog, and
    /// everyone else keeps a map that is quietly worse than the mod can make it, forever.
    ///
    /// Clearing crop markers was the first thing done the other way: it happens as the player walks
    /// about, nobody is told anything, and a field tidies itself. This is the same idea applied to
    /// the rest. Each fix runs once, early in a session, and says in the log what it did.
    ///
    /// <para>Rules for anything added here. It must be <b>safe to run twice</b>, because it will
    /// be -- there is no stamp saying it has been done, on purpose, since a stamp is one more
    /// thing to get out of step with the map it describes. It must be <b>cheap when there is
    /// nothing to do</b>, because that is the usual case. And it must only make changes it can
    /// justify from what is on the map already: no reaching out to the server, no guessing. The
    /// guessing belongs in tgm_identify, where a person is asking for it and can read the answer
    /// before it is written.</para>
    /// </summary>
    internal static class Migrations
    {
        /// <summary>Long enough after the map loads that nothing races the first draw.</summary>
        private const float DelaySeconds = 5f;

        private static bool _done;
        private static float _at;

        internal static void Reset()
        {
            _done = false;
            _at = 0f;
            Repair.Reset();
        }

        internal static void Update()
        {
            if (_done) return;
            if (Player.m_localPlayer == null || !PersonalMap.Loaded) return;
            if (_at == 0f) { _at = Time.unscaledTime + DelaySeconds; return; }
            if (Time.unscaledTime < _at) return;
            _done = true;
            try { Run(); }
            catch (System.Exception e)
            {
                TheGreatestMapMod.Log.LogError("[TheGreatestMap] Bringing the map up to date failed: " + e);
            }
        }

        private static void Run()
        {
            int named = NameTheHandPlaced();
            int identified = Identify.Run(apply: true).Matched;
            int kinded = NameTheKindOnly();
            int rekinded = KindTheHandPlaced();
            int traders = RetireTraders();
            int blanks = ForgetBlanks();
            // Whatever the catalog could not settle, the world is asked about -- and answers only
            // count where they agree with the icon the marker already wears, except for the
            // markers wearing nothing, which have no icon to agree and are named by what stands
            // there. After the erasing above, so a blank already answered for by the marker
            // beside it is gone rather than made into a second marker of the same thing. The
            // answer comes back later, over the network, and says what it did in the log itself.
            Repair.Run();
            if (named == 0 && identified == 0 && kinded == 0 && rekinded == 0 && traders == 0 && blanks == 0) return;
            ClientPins.Restyle();
            var parts = new System.Collections.Generic.List<string>();
            if (identified > 0) parts.Add($"worked out what {identified} marker(s) stand for");
            if (kinded > 0) parts.Add($"said of {kinded} that their kind is the whole answer");
            if (named > 0) parts.Add($"marked {named} as placed by hand");
            if (rekinded > 0) parts.Add($"gave {rekinded} placed by hand their kind back");
            if (traders > 0) parts.Add($"retired {traders} trader marker(s) the game marks by itself");
            if (blanks > 0) parts.Add($"erased {blanks} marker(s) that stood for nothing beside one that stands for something");
            TheGreatestMapMod.Log.LogInfo(
                "[TheGreatestMap] Brought the map up to date: " + string.Join(", ", parts.ToArray()) +
                ". Markers written before the mod recorded what a thing is are filled in once, " +
                "here, so nobody has to run anything.");
        }

        /// <summary>
        /// Erase the markers that say nothing at all, where something else says it instead.
        ///
        /// <para>A marker with no source, no icon, no kind and no name is not a marker: it is a
        /// plain dot on the map standing for nothing, and nothing on it can ever be identified,
        /// because identifying works from the very fields it is missing. Twenty-five of these
        /// were made on the live map by the bug noted on Identify.Mistaken, each
        /// one a tar pit marker with its identity taken off, and each sitting on the spot of the
        /// tar pit marker that came back from the server afterwards -- so the map showed every tar
        /// pit twice, once properly and once as a dot.</para>
        ///
        /// <para>Only where a marker that does know what it is stands on the same spot, which is
        /// what makes the blank one redundant rather than merely unhelpful. A blank standing on
        /// its own is left alone, and is not left for good: <see cref="Repair"/> asks the world
        /// what stands where it stands, which is how a blank gets its identity back rather than
        /// merely being tidied away. This runs first because erasing a blank that is already
        /// answered for costs nothing, while identifying it would make a second marker of a thing
        /// already marked.</para>
        ///
        /// <para>Erased without suppressing the spot, like the crop markers: nothing should stop
        /// the thing being recorded again properly. The tombstone travels, so one player's
        /// cleanup is everybody's, which it has to be -- otherwise the blank comes back from the
        /// next map we merge with.</para>
        /// </summary>
        private const float Twin = 4f;

        private static bool SaysNothing(SharedPin pin) => pin != null && pin.SaysNothing;

        private static int ForgetBlanks()
        {
            var blanks = new System.Collections.Generic.List<SharedPin>();
            foreach (var pin in ClientPins.All) if (SaysNothing(pin)) blanks.Add(pin);
            if (blanks.Count == 0) return 0;   // the usual case, and it costs one pass of the map
            int gone = 0;
            foreach (var blank in blanks)
            {
                bool twinned = false;
                foreach (var other in ClientPins.All)
                {
                    if (other == blank || string.IsNullOrEmpty(other.Source)) continue;
                    if (Geo.FlatDistance(other.Pos, blank.Pos) > Twin) continue;
                    twinned = true;
                    break;
                }
                if (!twinned) continue;
                ClientPins.RemoveStale(blank.Key);
                gone++;
            }
            return gone;
        }

        /// <summary>
        /// Take down the trader markers.
        ///
        /// Valheim marks Haldor, Hildir and the Bog Witch itself the moment you find one, so ours
        /// was always a second pin saying the same thing in a different picture. Nothing records
        /// them any more -- there is not even a setting for it, because the kinds the game marks
        /// are left out of that list -- but maps written before that still carry the old ones,
        /// and erasing them by hand did not stick: the erasure was only on the map that made it,
        /// and the next merge with somebody who still had them brought them straight back.
        ///
        /// Erased, not suppressed: a suppression says do not record here again, and nothing
        /// records these anyway. The spot is not being refused, the record is retired.
        /// </summary>
        private static int RetireTraders()
        {
            var stored = new System.Collections.Generic.List<PinKey>();
            foreach (var pin in ClientPins.All)
                if (pin.Auto && ClientPins.KindOf(pin) == Category.Trader) stored.Add(pin.Key);
            foreach (var key in stored) ClientPins.RemoveStale(key);
            return stored.Count;
        }

        /// <summary>
        /// A marker somebody placed themselves says so, rather than leaving the field blank.
        ///
        /// Blank used to mean two different things -- "placed by hand" and "recorded before this
        /// field existed" -- and a marker cannot be told which it is from a blank. It matters more
        /// now that what a marker stands for is part of what identifies it: a hand-placed marker
        /// and a not-yet-identified one, dropped by the same player on the same spot, would
        /// otherwise be one marker.
        /// </summary>
        /// <summary>
        /// A runestone is a runestone. Kinds with no prefab list have nothing to look up, so
        /// their markers said nothing at all about themselves and were indistinguishable from
        /// the ones nobody had got round to working out. Now they say so.
        /// </summary>
        private static int NameTheKindOnly()
        {
            int n = 0;
            foreach (var pin in new System.Collections.Generic.List<SharedPin>(ClientPins.All))
            {
                if (pin == null || !pin.Auto || !string.IsNullOrEmpty(pin.Source)) continue;
                var kind = ClientPins.KindOf(pin);
                if (!kind.HasValue || Categories.UsesPrefabList(kind.Value)) continue;
                ClientPins.Reidentify(pin, SharedPin.KindOnly(kind.Value));
                n++;
            }
            return n;
        }

        /// <summary>
        /// Give a hand-placed marker its kind back, where a version of this mod wrote it out
        /// without one.
        ///
        /// For a few hours the writer elided a hand-placed marker's kind, on the grounds that it
        /// could be read back from the icon. It cannot: the reader will not infer a kind for
        /// anything placed by hand, because an adopted vanilla pin's icon belongs to a kind it
        /// has nothing to do with. Markers saved in that window came back kindless, which means
        /// drawn at full size and answering to none of their kind's switches.
        ///
        /// The icon still says which kind was chosen, and here it can be trusted, because the
        /// icons a kind hands out are never vanilla's own -- a marker wearing "pin:Icon0" was
        /// adopted rather than chosen, and is left alone.
        /// </summary>
        private static int KindTheHandPlaced()
        {
            int n = 0;
            foreach (var pin in new System.Collections.Generic.List<SharedPin>(ClientPins.All))
            {
                if (pin == null || pin.Auto) continue;
                if (!string.IsNullOrEmpty(pin.StoredKind)) continue;
                string icon = pin.StoredIcon;
                if (string.IsNullOrEmpty(icon) || icon.StartsWith("pin:", System.StringComparison.Ordinal)) continue;
                var kind = KindInference.FromIcon(icon);
                if (!kind.HasValue) continue;
                pin.Kind = kind.Value.ToString();
                ClientPins.UpdateMarker(pin);
                n++;
            }
            return n;
        }

        private static int NameTheHandPlaced()
        {
            int n = 0;
            foreach (var pin in new System.Collections.Generic.List<SharedPin>(ClientPins.All))
            {
                if (pin == null || pin.Auto || !string.IsNullOrEmpty(pin.Source)) continue;
                ClientPins.Reidentify(pin, SharedPin.Manual);
                n++;
            }
            return n;
        }
    }
}
