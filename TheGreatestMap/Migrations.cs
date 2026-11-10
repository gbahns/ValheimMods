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

        /// <summary>
        /// The release each pass below was last needed for. A map already tidied by this version
        /// or a later one skips every pass, which is what makes it safe to delete them later: a
        /// pass that has gone a year without finding anything can be cut, with the log to say so,
        /// rather than carried forever on the chance that somebody's map still needs it.
        ///
        /// <para>Every pass here is dated to the same release because that is the honest answer:
        /// they were written over months and nothing recorded when, so the first version that can
        /// prove a map was tidied is this one. After this each new pass carries its own date and
        /// an old map still gets the lot.</para>
        /// </summary>
        private const string NeededUpTo = "4.8.6";

        /// <summary>
        /// Has this map been brought up to date by this version of the mod or a newer one? The
        /// answer is kept per map, because a player's second world has never been touched.
        /// </summary>
        private static bool AlreadyTidied()
        {
            var done = Stamp(PersonalMap.Label);
            if (done == null) return false;
            try { return done >= new System.Version(NeededUpTo); }
            catch (System.Exception) { return false; }
        }

        private static System.Version Stamp(string map)
        {
            string all = Progress.MapsTidied;
            if (string.IsNullOrEmpty(all) || string.IsNullOrEmpty(map)) return null;
            foreach (var entry in all.Split(';'))
            {
                int eq = entry.LastIndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(entry.Substring(0, eq).Trim(), map, System.StringComparison.Ordinal)) continue;
                try { return new System.Version(entry.Substring(eq + 1).Trim()); }
                catch (System.Exception) { return null; }
            }
            return null;
        }

        /// <summary>Write down that this map has been tidied, replacing any older entry for it.</summary>
        private static void MarkTidied()
        {
            string map = PersonalMap.Label;
            if (string.IsNullOrEmpty(map) || map.IndexOf('=') >= 0 || map.IndexOf(';') >= 0) return;
            var kept = new System.Collections.Generic.List<string>();
            foreach (var entry in Progress.MapsTidied.Split(';'))
            {
                string one = entry.Trim();
                if (one.Length == 0) continue;
                int eq = one.LastIndexOf('=');
                if (eq > 0 && string.Equals(one.Substring(0, eq).Trim(), map, System.StringComparison.Ordinal)) continue;
                kept.Add(one);
            }
            kept.Add(map + "=" + TheGreatestMapMod.ModVersion);
            Progress.MapsTidied = string.Join(";", kept.ToArray());
        }

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
            // Already done, by this version or a later one. The world is still asked about the
            // markers that do not know what they are -- that one is not a fix for an old map but
            // a standing offer, and it costs a message to a server that answers at its leisure.
            if (AlreadyTidied())
            {
                Repair.Run();
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] This map was already brought up to date by {Stamp(PersonalMap.Label)} " +
                    "or later, so the passes that fill in markers written by older versions are skipped.");
                return;
            }
            // There was a pass here that wrote "Manual" into every marker SharedPin.HandPlaced
            // guessed was placed by hand, on the grounds that a blank source carrying a picture
            // could be nothing else. It could: a marker recorded before format 5 had nowhere to
            // put a source. On the map that showed this up the guess was wrong 376 times out of
            // 376, and this pass is what made it permanent -- it rewrote the source, which
            // rekeyed the marker, and shipped the new identity to every other player.
            //
            // Nothing replaces it, because there was nothing to do: SharedPin.Read already turns
            // the recorded-or-placed flag into the word "Manual" for every format that wrote one
            // down, so a genuinely hand-placed old marker arrives here correct. The repair for
            // the maps this ran on is tgm_unmanual.
            //
            // Writes answers, never takes one away: see Identify.Run on why stripping a source
            // is something a person has to ask for.
            var identify = Identify.Run(apply: true, strip: false);
            int identified = identify.Matched;
            if (identify.Corrected > 0)
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] {identify.Corrected} marker(s) name something the catalog no longer calls " +
                    "what they are. Nothing is done about that by itself, because a catalog is a setting and " +
                    "trimming a line from one would otherwise cost every marker written from it: " +
                    "tgm_identify apply clears them when you want it.");
            int kinded = NameTheKindOnly();
            int rekinded = KindTheHandPlaced();
            int traders = RetireWhatTheGameMarks();
            int unnamed = UnnameWhatTheIconSays();
            int unguessed = ForgetKindsGuessedFromADot();
            // Whatever the catalog could not settle, the world is asked about -- and answers only
            // count where they agree with the icon the marker already wears. A marker wearing
            // nothing has no icon to agree, so it is left out of this entirely: see Repair.
            // The answer comes back later, over the network, and says what it did in the log.
            Repair.Run();
            MarkTidied();
            if (identified == 0 && kinded == 0 && rekinded == 0 && traders == 0
                && unnamed == 0 && unguessed == 0)
            {
                TheGreatestMapMod.Log.LogInfo(
                    "[TheGreatestMap] Nothing on this map needed bringing up to date; it is marked as tidied by " +
                    TheGreatestMapMod.ModVersion + " and those passes will not run on it again.");
                return;
            }
            ClientPins.Restyle();
            var parts = new System.Collections.Generic.List<string>();
            if (identified > 0) parts.Add($"worked out what {identified} marker(s) stand for");
            if (kinded > 0) parts.Add($"said of {kinded} that their kind is the whole answer");
            if (rekinded > 0) parts.Add($"gave {rekinded} placed by hand their kind back");
            if (traders > 0) parts.Add($"retired {traders} marker(s) for things the game marks by itself");
            if (unnamed > 0) parts.Add($"took the name off {unnamed} spawner marker(s), whose icon says it");
            if (unguessed > 0) parts.Add($"stopped calling {unguessed} marker(s) that know nothing about themselves a dungeon");
            TheGreatestMapMod.Log.LogInfo(
                "[TheGreatestMap] Brought the map up to date: " + string.Join(", ", parts.ToArray()) +
                ". Markers written before the mod recorded what a thing is are filled in once, " +
                "here, so nobody has to run anything -- and once, is once: this map is marked as " +
                "tidied by " + TheGreatestMapMod.ModVersion + " and these passes will not run on it again.");
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
                // Nothing is marked for sending. SetKind leaves Modified alone, so this never
                // could travel -- a copy receiving it compares the two and keeps its own -- and
                // the map is written to the character whole, so it is kept here regardless.
                pin.SetKind("");
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
        /// Take down the markers for the things the game marks itself.
        ///
        /// Valheim marks Haldor, Hildir and the Bog Witch the moment you find one, and all three
        /// of the places she sends you once she has asked, so ours was always a second pin saying
        /// the same thing in a different picture. Nothing records either kind any more -- there
        /// is not even a setting for it, because the kinds the game marks are left out of that
        /// list -- but maps written before that still carry the old ones, and erasing them by
        /// hand did not stick: the erasure was only on the map that made it, and the next merge
        /// with somebody who still had them brought them straight back.
        ///
        /// Her errands joined the traders once the rows were put right and the five left on one
        /// map turned out to be sitting under a row of their own, all five named after whichever
        /// of her three sites the icon happened to look up first.
        ///
        /// The altars joined them in turn. They were kept at first and simply not drawn -- an
        /// altar record says a player stood there and when, and it costs little to carry -- but
        /// a record nothing will ever draw is a thing a player cannot see, cannot switch and
        /// cannot be told about, and two of them came back as markers on a map anyway through a
        /// route nobody had thought of. A record only this mod can see is not worth that.
        ///
        /// The work is <see cref="ClientPins.DropWhatTheGameMarks"/>, which is also run at every
        /// load and after every merge: these travel on the shared map, so a pass that ran once
        /// per map per version was handed them straight back by anybody who had not run it.
        ///
        /// Erased, not suppressed: a suppression says do not record here again, and nothing
        /// records these anyway. The spot is not being refused, the record is retired.
        /// </summary>
        private static int RetireWhatTheGameMarks() => ClientPins.DropWhatTheGameMarks();

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

    }
}
