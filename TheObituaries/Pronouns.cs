namespace TheObituaries
{
    /// <summary>
    /// The victim's pronouns, from the character's body type unless the config says otherwise.
    /// Filled into the line by the victim's own game, so the wire format and other clients
    /// never see the placeholders.
    /// </summary>
    internal static class Pronouns
    {
        internal enum Set { He, She, They }

        internal static Set For(Player victim)
        {
            switch (TheObituariesMod.PronounChoice.Value)
            {
                case TheObituariesMod.PronounMode.He:   return Set.He;
                case TheObituariesMod.PronounMode.She:  return Set.She;
                case TheObituariesMod.PronounMode.They: return Set.They;
            }
            // Auto: model 0 is the male body, 1 the female one.
            if (victim == null) return Set.They;
            return victim.GetPlayerModel() == 1 ? Set.She : Set.He;
        }

        internal static string Fill(string template, Set set)
        {
            if (string.IsNullOrEmpty(template)) return template;
            string he, him, his, himself;
            switch (set)
            {
                case Set.He:  he = "he";   him = "him";  his = "his";   himself = "himself";  break;
                case Set.She: he = "she";  him = "her";  his = "her";   himself = "herself";  break;
                default:      he = "they"; him = "them"; his = "their"; himself = "themself"; break;
            }
            return template
                .Replace("{himself}", himself)
                .Replace("{his}", his)
                .Replace("{him}", him)
                .Replace("{he}", he);
        }
    }
}
