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
                if (PinTypes.IsCustom((int)Access.SelectedType(map)))
                    Access.SelectedType(map) = Minimap.PinType.Icon0;
            }
            catch (System.Exception) { /* the map is going away; nothing to put back */ }
        }

        /// <summary>
        /// Arm a kind: the next marker placed by hand is one of these. The icon is the kind's own,
        /// the one the legend shows for it, since a marker placed by hand stands for the kind
        /// rather than for a particular thing the catalog knows about.
        /// </summary>
        internal static bool Arm(Minimap map, Category kind)
        {
            if (map == null) return false;
            string icon = TgmConfig.CategoryIcon.TryGetValue(kind, out var cfg) && !string.IsNullOrEmpty(cfg.Value)
                ? cfg.Value
                : Categories.DefaultIcon(kind);
            int type = IconRegistry.TypeFor(icon);
            if (type <= 0) return false;
            _armed = new Placing { Kind = kind, Icon = IconRegistry.Normalize(icon), Type = type };
            Access.SelectedType(map) = (Minimap.PinType)type;
            // Double-click, as vanilla has always placed a pin. This said "right-click" for
            // months, which is not merely wrong but harmful: right-click is the marker menu,
            // whose first item is "Hide this marker". Following the instruction over an existing
            // marker hid it, and a marker hidden one at a time stays hidden through everything
            // short of tgm_show -- so the advice made markers disappear and gave no hint why.
            TheGreatestMapMod.Message($"Double-click the map to place a {Categories.Label(kind)} marker.");
            return true;
        }

        /// <summary>
        /// What is being placed, if this pin is it. Spent on the way out, so a second pin placed
        /// afterwards is an ordinary one again and the player is never left arming something they
        /// have forgotten about.
        /// </summary>
        internal static Placing Take(int type)
        {
            if (_armed == null || _armed.Type != type) return null;
            var placing = _armed;
            Disarm(Minimap.instance);
            return placing;
        }
    }
}
