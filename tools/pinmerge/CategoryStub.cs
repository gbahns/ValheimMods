// Copied verbatim from TheGreatestMap/Catalog.cs so the member set matches exactly.
// Only SharedPin.KindCategory parses it, which merging never calls - but Kind travels as a
// string, so even that cannot affect what is read or written.
namespace TheGreatestMap
{
    internal enum Category
        {
            Berries,
            Mushrooms,
            Herbs,
            Ore,
            Dungeon,
            Runestone,
            Trader,
            Camp,
            BossAltar,
            Portal,
            Structure,
            Seeds,
            Plants,
            Campfire,
        }
}
