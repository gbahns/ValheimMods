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
        /// <summary>
        /// A pin type this mod made up: a number it hands out itself, from 100 up.
        ///
        /// <para>Bounded at the top, which it was not. IconRegistry allocates from 100 and stops
        /// at ReservedFloor because the numbers above that belong to other mods -- the comment
        /// there names The Greatest Portal, which draws its portal pins at exactly 1000. This
        /// test claimed everything from 100 upward, so it answered yes for those pins too.</para>
        ///
        /// <para>What that answer is used for makes it serious. A pin of "ours" with no record
        /// behind it is rubbish by definition: the status line counts it as a leftover, the watch
        /// warns about it, tgm_clearlocal deletes it and the login sweep deletes it. So this mod
        /// was taking another mod's pins off the map, warning about them, and reporting them as
        /// its own wreckage -- and they came back the next time that mod drew them, which is how
        /// something gets deleted every session and is never gone.</para>
        /// </summary>
        internal static bool IsCustom(int type) => type >= IconRegistry.Base && type < IconRegistry.ReservedFloor;

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

        /// <summary>
        /// A kind of pin the game puts up once, for one player, and never puts up again.
        ///
        /// <para>A boss altar somebody read a vegvisir for, one of the places Hildir sent them,
        /// where they died, where they will wake up. Every one of these is a fact about that
        /// player rather than about the world, and the character file is the only copy: the world
        /// holds where the altars are, not which of them you have been told about, and vanilla
        /// draws nothing of its own for one you have not. So there is no walking it back. A
        /// marker of this mod's that goes wrongly comes back at the next merge from anyone else
        /// holding it; one of these that goes wrongly is gone.</para>
        ///
        /// <para>Which is what happened: a discovered Bonemass altar left a character file
        /// between two days while the twelve altars beside it stayed, and nothing in the map
        /// could say which pass had taken it or put it back. Hence this, and the two guards that
        /// read it -- the rule is not "do not delete these by mistake", it is "no sweep of this
        /// mod's deletes one at all".</para>
        /// </summary>
        internal static bool GamePlacedOnce(Minimap.PinType type)
        {
            switch (type)
            {
                case Minimap.PinType.Boss:
                case Minimap.PinType.Hildir1:
                case Minimap.PinType.Hildir2:
                case Minimap.PinType.Hildir3:
                case Minimap.PinType.Death:
                case Minimap.PinType.Bed:
                    return true;
                default:
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(Minimap), "Start")]
    internal static class Minimap_Start_PinTypes_Patch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance)
        {
            IconRegistry.OnMinimapStart(__instance);
            // After the arrays are the right size, since this writes into vanilla's.
            VanillaPins.Restore(__instance);
        }
    }

    // A picture is one thing, so the game's own filter button moves this mod's half of it as
    // well. The row in the marker list does the same from the other side.
    [HarmonyPatch(typeof(Minimap), "ToggleIconFilter")]
    internal static class Minimap_ToggleIconFilter_Patch
    {
        private static void Postfix(Minimap __instance, Minimap.PinType type) =>
            VanillaPins.MirrorFromVanilla(__instance, type);
    }

    // Belt and braces: if another mod replaces m_visibleIconTypes with a shorter array, grow it
    // back before vanilla indexes it with one of our types.
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Minimap_UpdatePins_Guard_Patch
    {
        private static void Prefix(Minimap __instance) => IconRegistry.EnsureArrays(__instance);
    }
}
