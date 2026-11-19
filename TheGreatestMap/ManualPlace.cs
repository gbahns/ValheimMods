namespace TheGreatestMap
{
    /// <summary>
    /// Placing one of this mod's own kinds by hand.
    ///
    /// Everything the recorder writes is something the player found, which is the whole rule. But
    /// a player knows things the catalog never will -- where a serpent surfaced, which bay fishes
    /// well, where a troll patrols -- and the only markers they could place were vanilla's five
    /// plain icons. This lets them pick a kind instead, so the thing they write down is of a piece
    /// with everything else on the map: it has a kind, so it hides and groups with its fellows, and
    /// it travels to the rest of the crew like any other marker.
    ///
    /// The game does the placing. Arming a kind only writes its pin type into Minimap's own
    /// m_selectedType, which is what a right click on the map reads, so vanilla's naming box and
    /// placement are untouched. The arming is spent by the first marker placed and no other: an
    /// armed kind that quietly applied to every later pin would be a trap.
    /// </summary>
    internal static class ManualPlace
    {
        internal sealed class Placing
        {
            internal Category Kind;
            internal string Icon;
            internal int Type;
        }

        private static Placing _armed;

        internal static Category? Armed => _armed != null ? _armed.Kind : (Category?)null;

        /// <summary>
        /// Whether this exact row is the armed one: a kind, and the picture it will place.
        ///
        /// <para>The kind alone stopped being the answer when type rows got a "+" of their own.
        /// Arming silver and arming ore are both arming Ore, and a row lit by kind would light
        /// every type row under it -- four buttons claiming to be the one that is armed, which
        /// is three lies. A null icon asks about the kind's own picture, which is what a kind
        /// row places.</para>
        /// </summary>
        internal static bool IsArmed(Category kind, string icon)
        {
            if (_armed == null || _armed.Kind != kind) return false;
            return IconRegistry.SameKey(_armed.Icon, IconFor(kind, icon));
        }

        /// <summary>
        /// The picture a placement will wear: the type's own where a type row asked, and
        /// otherwise the kind's, which is the picture chosen to stand for the whole kind.
        /// </summary>
        private static string IconFor(Category kind, string icon)
        {
            if (!string.IsNullOrEmpty(icon)) return icon;
            return TgmConfig.CategoryIcon.TryGetValue(kind, out var cfg) && !string.IsNullOrEmpty(cfg.Value)
                ? cfg.Value
                : Categories.DefaultIcon(kind);
        }

        internal static void Cancel() => Disarm(Minimap.instance);

        /// <summary>
        /// Forget what was armed and give the game back a pin type of its own.
        ///
        /// Arming works by writing our type into Minimap's m_selectedType, which is what a
        /// placement reads. Clearing only our side left that type behind, so the next pin the
        /// player placed was still of our kind while nothing was armed to claim it -- it came out
        /// refused, unadopted, and drawn by vanilla as a type it has no business owning.
        /// </summary>
        private static void Disarm(Minimap map)
        {
            _armed = null;
            if (map == null) return;
            try
            {
                var selected = Access.SelectedType(map);
                // The ping goes with them. It is one of vanilla's types rather than one of ours,
                // so IsCustom never covered it, and it was the one arming in the mod that
                // outlived the map being shut: a player who armed it and forgot found their next
                // double-click sending a real ping to everyone in range instead of placing a
                // marker, and leaving a circle on every map in earshot. Nothing on screen said
                // why, because the row that says so is only drawn while the map is open.
                if (!PinTypes.IsCustom((int)selected) && selected != Minimap.PinType.Ping) return;
                // Through the game's own method rather than the field, because this one has a
                // button: SelectIcon also puts out the highlight on it, and writing the field
                // alone would leave the map lit as though the ping were still ready.
                Access.SelectIcon(map, Minimap.PinType.Icon0);
            }
            catch (System.Exception) { /* the map is going away; nothing to put back */ }
        }

        /// <summary>
        /// Arm a kind: the next marker placed by hand is one of these. The icon is the kind's own,
        /// the one the legend shows for it, since a marker placed by hand stands for the kind
        /// rather than for a particular thing the catalog knows about.
        /// </summary>
        /// <summary>
        /// Put an arming away once the map is shut. A kind armed in one sitting should not still
        /// be armed when the map is next opened, which is the trap the old one-shot rule was
        /// written to avoid -- this avoids it by the clock the player is actually keeping.
        /// </summary>
        internal static void Update()
        {
            // No early way out on _armed being null: the ping is armed in vanilla's field
            // without anything of ours to show for it, and returning here is what let it stand.
            var map = Minimap.instance;
            if (map == null) { _armed = null; return; }
            if (map.m_mode != Minimap.MapMode.Large) Disarm(map);
        }

        internal static bool Arm(Minimap map, Category kind) => Arm(map, kind, null);

        /// <summary>
        /// Arm a kind, and within it a type: the next marker placed by hand wears this picture.
        ///
        /// <para>The picture is the whole of it, and that is not a shortcut. A marker placed by
        /// hand is identified by what it looks like -- the marker list files it by picture, and
        /// Focus.TypeLabel reads its name back out of the catalog from the kind and the picture
        /// together. So handing a placement the silver ore icon is what makes it a silver
        /// marker: it lands in the Silver row, under Ore, named Silver, beside the veins that
        /// were recorded by being looked at. Nothing else has to be carried for that to happen,
        /// and nothing else should be, since a second source of truth about what a marker is
        /// would be a second thing to disagree.</para>
        ///
        /// <para>A null icon means the kind's own, which is what a kind row arms and says
        /// nothing narrower than the kind.</para>
        /// </summary>
        internal static bool Arm(Minimap map, Category kind, string icon)
        {
            if (map == null) return false;
            icon = IconFor(kind, icon);
            // Nothing to swap here any more: every icon key resolves to a pin type of this
            // mod's own, vanilla pictures included. A key that named one of the game's own types
            // used to produce a pin the game would not let a player place -- drawn, named, and
            // shared with nobody, because adoption refused it. See IconRegistry.TypeFor.
            int type = IconRegistry.TypeFor(icon);
            if (type <= 0) return false;
            _armed = new Placing { Kind = kind, Icon = IconRegistry.Normalize(icon), Type = type };
            Access.SelectedType(map) = (Minimap.PinType)type;
            // Nothing is said on screen. There was a line here telling the player how to place
            // one, and it was wrong for months -- it said right-click, which is the marker menu,
            // whose first item hides the marker. But the reason it is gone rather than corrected
            // is that the "+" on the row lights up while its kind is armed, which says the same
            // thing in the place the player is already looking and goes on saying it while the
            // game is paused, which an on-screen message does not.
            return true;
        }

        /// <summary>
        /// What is being placed, if this pin is it.
        ///
        /// It used to be spent by the first marker, on the reasoning that an arming left standing
        /// would quietly turn later pins into something the player had forgotten asking for. The
        /// reasoning was sound and the result was worse than the thing it guarded against:
        /// marking eight ore veins meant arming eight times, and anybody who armed once and
        /// placed eight got one marker and seven pins this mod refused -- and a refused pin is
        /// not an ordinary one, it is an orphan with no tooltip, no menu, and vanilla's erase on
        /// a right click. Nothing said so.
        ///
        /// So it lasts while the map is open, which is the span a player is thinking about it in,
        /// and <see cref="Update"/> puts it away when the map closes.
        /// </summary>
        internal static Placing Take(int type)
        {
            if (_armed == null || _armed.Type != type) return null;
            return _armed;   // still armed: the next one is of the same kind
        }
    }
}
