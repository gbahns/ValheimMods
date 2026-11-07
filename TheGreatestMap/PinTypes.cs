using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// Pin type helpers. Custom icons are allocated at runtime by IconRegistry from 100 upward;
    /// Valheim's PinType enum is just an int, so a new value only needs an icon entry and a large
    /// enough visibility array.
    /// </summary>
    internal static class PinTypes
    {
        internal static bool IsCustom(int type) => type >= IconRegistry.Base;

        /// <summary>
        /// A kind of pin the game makes for the moment and never writes down.
        ///
        /// <para>Every one of these is created with save false at every place the game creates
        /// it, and each is rebuilt from something live: the ping and the shout from chat, the
        /// player pins from the player list, the bed from the profile's spawn point, the event
        /// ones from the running event, and None from the locations the world knows. The game
        /// destroys and remakes them whenever the thing behind them changes.</para>
        ///
        /// <para>So a pin of one of these kinds carrying save can only have come out of a file
        /// or a cartography table. The running game cannot have made it, nothing rebuilds it,
        /// and no player can place one -- there is no button for a ping. It is a photograph of a
        /// moment years ago that the map cannot tell from the moment itself.</para>
        ///
        /// <para>Death is deliberately not here: the game saves those on purpose
        /// (Player.OnDeath), and a player's death markers are theirs. Boss, Memorial and
        /// Hildir's three are not here either, because where the game makes those could not be
        /// read out of the game's own code with certainty, and a rule this blunt is only safe
        /// where it is certain.</para>
        /// </summary>
        internal static bool IsNeverSaved(Minimap.PinType type)
        {
            switch (type)
            {
                case Minimap.PinType.Ping:
                case Minimap.PinType.Shout:
                case Minimap.PinType.Player:
                case Minimap.PinType.None:
                case Minimap.PinType.Bed:
                case Minimap.PinType.EventArea:
                case Minimap.PinType.RandomEvent:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The five vanilla placeable icons plus this mod's custom types.</summary>
        internal static bool IsPlayerPlaceable(int type)
        {
            return type == (int)Minimap.PinType.Icon0
                || type == (int)Minimap.PinType.Icon1
                || type == (int)Minimap.PinType.Icon2
                || type == (int)Minimap.PinType.Icon3
                || type == (int)Minimap.PinType.Icon4
                || IsCustom(type);
        }
    }

    [HarmonyPatch(typeof(Minimap), "Start")]
    internal static class Minimap_Start_PinTypes_Patch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance) => IconRegistry.OnMinimapStart(__instance);
    }

    // Belt and braces: if another mod replaces m_visibleIconTypes with a shorter array, grow it
    // back before vanilla indexes it with one of our types.
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Minimap_UpdatePins_Guard_Patch
    {
        private static void Prefix(Minimap __instance) => IconRegistry.EnsureArrays(__instance);
    }
}
