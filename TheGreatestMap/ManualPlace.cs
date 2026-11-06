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

        internal static bool Arm(Minimap map, Category kind)
        {
            if (map == null) return false;
            string icon = TgmConfig.CategoryIcon.TryGetValue(kind, out var cfg) && !string.IsNullOrEmpty(cfg.Value)
                ? cfg.Value
                : Categories.DefaultIcon(kind);
            int type = IconRegistry.TypeFor(icon);
            // A vanilla pin type the game will not let a player place is a marker that is drawn,
            // named, and shared with nobody -- adoption refuses it and it stays a plain local
            // pin. Runestones, boss altars and players all default to one, and a player who has
            // set their own icon to a vanilla pin would hit it for any kind at all. So the
            // picture is kept and the pin underneath is swapped for one of ours.
            if (!PinTypes.IsPlayerPlaceable(type))
            {
                icon = IconRegistry.AsOwnPin(icon);
                type = IconRegistry.TypeFor(icon);
            }
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
