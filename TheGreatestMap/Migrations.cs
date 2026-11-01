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
            int unnamed = UnnameWhatTheIconSays();
            int unguessed = ForgetKindsGuessedFromADot();
            // Whatever the catalog could not settle, the world is asked about -- and answers only
            // count where they agree with the icon the marker already wears. A marker wearing
            // nothing has no icon to agree, so it is left out of this entirely: see Repair.
            // The answer comes back later, over the network, and says what it did in the log.
            Repair.Run();
            if (named == 0 && identified == 0 && kinded == 0 && rekinded == 0 && traders == 0
                && unnamed == 0 && unguessed == 0) return;
            ClientPins.Restyle();
            var parts = new System.Collections.Generic.List<string>();
            if (identified > 0) parts.Add($"worked out what {identified} marker(s) stand for");
            if (kinded > 0) parts.Add($"said of {kinded} that their kind is the whole answer");
            if (named > 0) parts.Add($"marked {named} as placed by hand");
            if (rekinded > 0) parts.Add($"gave {rekinded} placed by hand their kind back");
            if (traders > 0) parts.Add($"retired {traders} trader marker(s) the game marks by itself");
            if (unnamed > 0) parts.Add($"took the name off {unnamed} spawner marker(s), whose icon says it");
            if (unguessed > 0) parts.Add($"stopped calling {unguessed} marker(s) that know nothing about themselves a dungeon");
            TheGreatestMapMod.Log.LogInfo(
                "[TheGreatestMap] Brought the map up to date: " + string.Join(", ", parts.ToArray()) +
                ". Markers written before the mod recorded what a thing is are filled in once, " +
                "here, so nobody has to run anything.");
        }

        /// <summary>
        /// Forget a kind that was read off the plain dot, which is the picture a marker wears
        /// when nothing is known about it.
        ///
        /// <para>Inference turns an icon into a kind, and the plain dot was the dungeons' own
        /// fallback picture as well as the one every unidentified marker falls back to. So a
        /// marker carrying nothing -- no source, no icon, no name -- came out of that as a
        /// dungeon, and the kind was then written onto it, which made the guess permanent and
        /// hid it from the passes that look for markers knowing nothing: those test for an empty
        /// kind, and this one now had "Dungeon" in it.</para>
        ///
        /// <para>Only markers with no source at all. A marker naming a prefab the catalog has no
        /// line for also draws as a dot, and its kind came from the recorder rather than from
        /// the picture -- that one knows what it is and the catalog is what is missing.</para>
        /// </summary>
        private static int ForgetKindsGuessedFromADot()
        {
            int n = 0;
            foreach (var pin in new System.Collections.Generic.List<SharedPin>(ClientPins.All))
            {
                if (pin == null || !pin.Auto) continue;
                if (!string.IsNullOrEmpty(pin.Source) || string.IsNullOrEmpty(pin.Kind)) continue;
                if (!string.IsNullOrEmpty(pin.Icon)
                    && !IconRegistry.SameKey(pin.Icon, IconRegistry.FallbackKey)) continue;
                pin.SetKind("");
                PersonalMap.MarkDirty();
                n++;
            }
            return n;
        }

        /// <summary>
        /// Take the written name off a recorded marker of a kind that never labels.
        ///
        /// The design is that the icon speaks for itself: a greydwarf trophy means a greydwarf
        /// spawner, and every kind whose picture settles it is set never to write a label --
        /// dungeons, camps, structures, boss altars, ore, every berry and mushroom. A kind that
        /// falls through that switch inherits the catch-all, which labels, and that catch-all
        /// exists for traders, where three different ones share one coin icon. Spawners were
        /// added to the kinds after the switch was written and inherited it, so every nest and
        /// bone pile ever recorded came out with its own name drawn beside it.
        ///
        /// <para>Changing the rule only fixes what is written next. This fixes what is already
        /// written, which is the whole point of this file: the player who read the changelog is
        /// not the only one whose map should come right.</para>
        ///
        /// <para>Only a name this mod would have written itself is taken off -- the catalog's own
        /// text for what the marker stands for -- and only on a spawner, which is the kind the
        /// oversight was about. The rule is read from the setting rather than from the default, so
        /// a player who turns the spawner labels on keeps them.</para>
        ///
        /// <para><b>Why spawners only.</b> This was written to run on every kind that never
        /// labels, on the grounds that a name matching the catalog's own words is the mod's text
        /// rather than anybody's. That stopped being true the moment a player could type a name
        /// into a marker by hand: "Copper" typed onto a copper vein is a player asking for that
        /// word on the map, and is character for character what the catalog would have written.
        /// Nothing stored can tell the two apart, so the general form would quietly eat a name
        /// somebody meant -- on the next login, with no way to tell what had happened. A pass
        /// that cannot distinguish its own writing from a player's has no business guessing, so
        /// it is given the one kind it was needed for.</para>
        /// </summary>
        private static int UnnameWhatTheIconSays()
        {
            int n = 0;
            foreach (var pin in new System.Collections.Generic.List<SharedPin>(ClientPins.All))
            {
                if (pin == null || !pin.Auto || string.IsNullOrEmpty(pin.Name)) continue;
                var kind = ClientPins.KindOf(pin);
                if (!kind.HasValue) continue;
                if (kind.Value != Category.Spawner) continue;
                if (!TgmConfig.LabelSpacing.TryGetValue(kind.Value, out var spacing) || spacing.Value >= 0f) continue;
                if (!string.Equals(pin.Name, Catalog.NameForSource(pin.Source), System.StringComparison.Ordinal)) continue;
                ClientPins.RenameMarker(pin.Key, "");
                n++;
            }
            return n;
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
