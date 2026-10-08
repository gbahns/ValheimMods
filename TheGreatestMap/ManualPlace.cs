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

        internal static void Cancel() => _armed = null;

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
            TheGreatestMapMod.Message($"Right-click the map to place a {Categories.Label(kind)} marker.");
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
            _armed = null;
            return placing;
        }
    }
}
