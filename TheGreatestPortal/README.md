# The Greatest Portal

**Early alpha.** Choose where each portal goes.

- Press **Use** on a portal to name it and pick its destination from a list of every portal in
  the world, or click **Pick on map** and choose it on the map. No tag pairing, no portal hubs.
- Mark one portal as your **default** and every portal you build afterwards leads there.
- Leave a portal **open** (no destination) and stepping into it shows the map: click any portal,
  on the map or in the list on the left, and you are there.
- Mark **favourites**: they sit at the top of every list and carry a star on the map.

Install on the server and on every client. All players must run the same version; a player
without the mod, or with an older version than the server, is refused at connect.

## How it works in play

**Setting up a portal.** Press Use (E) on it. The panel shows:

| Field | What it does |
|---|---|
| Name | Up to 32 characters (configurable). Names do not have to be unique. |
| Destination list | Every other portal in the world, favourites first, with distances. The first entry, *Open portal*, means no fixed destination. |
| Favourite | This portal is listed first everywhere and starred on the map. |
| Default | New portals you build lead here automatically. |
| Pick on map | Saves the name and toggles, then opens the map: click a portal there or in the list. |
| Show on map | Opens the map centred on the chosen destination, with every portal drawn. |

Destinations are one-way: `Home -> Swamp` does not make the swamp portal lead home. Set the swamp
portal's destination too, or leave it open. A portal with a destination behaves exactly like a
paired vanilla portal, including the usual item restrictions.

**Travelling through an open portal.** Step in and the map opens on your position with every portal
drawn as a portal pin. The list on the left shows favourites first, then the rest alphabetically,
each with its distance. Click a pin or a row to travel there. Right-click marks a favourite. Esc, or
stepping out of the doorway, closes the map and you stay. The usual rules apply: no travelling with
ore, or during a boss fight if the world forbids it.

**On the map at other times.** Press `P` while the large map is open to show every portal with the
list, click a portal to centre the map on it, press `P` again to hide them. Right-click a portal
to mark a favourite.

## Keys (configurable)

| Key | Action |
|---|---|
| `E` on a portal | Configure it |
| `P` on the large map | Show or hide every portal and the portal list |

## Configuration

`BepInEx/config/DeathMonger.TheGreatestPortal.cfg`. Rules are server-synced; the rest is yours.

| Section | Setting | Default | Meaning |
|---|---|---|---|
| Rules | Max Name Length | 32 | Longest portal name (server-synced) |
| Rules | Open Portals Show The Map | true | An open portal shows the map when you step in. When false it goes nowhere, like an unpaired vanilla portal (server-synced) |
| Rules | Adopt Existing Connections | true | Portals this mod has not configured keep their vanilla tag pair (server-synced) |
| Keys | Toggle Portal Pins | P | On the large map: show or hide every portal |
| Display | Always Show Portal Pins | false | Draw every portal on the large map all the time |
| Display | Hide Other Pins While Choosing | true | Only portals, death markers, players and pings while choosing a destination |
| Display | Show Distances | true | Distances in the lists |
| Display | Show Portal List On Map | true | The list on the left of the map |
| Display | Auto Close Grace Seconds | 0.5 | Leave the doorway for this long and the destination map closes |
| Display | Show Messages | true | Small top-left messages |

Favourites and the default portal are yours alone and are kept per world in
`BepInEx/config/TheGreatestPortal/<world>-<seed>.txt`.

## Adding the mod to an existing world

Nothing is lost. Each portal keeps its name (the vanilla tag). A portal that was paired by tag
keeps that portal as its destination, in both directions; an unpaired portal becomes an open
portal. From then on names no longer pair anything: change destinations in the panel. (XPortal's
stored destinations cannot be carried over: it remembers them by an id the game renumbers on every
world load.)

Removing the mod later leaves the names in place and vanilla pairs by tag again.

## Console commands

| Command | What it does |
|---|---|
| `tgp_status` | Portal count, picker state, favourites |
| `tgp_list` | Every portal with its destination |
| `tgp_default <name>` / `tgp_default clear` | Set or clear the default portal |
| `tgp_refresh` | Ask the server for the portal list again |

## Compatibility

Replaces XPortal, TargetPortal, AnyPortal and PortalRules; do not run it alongside them.
Works with mods that add new portal pieces as long as they register them as portals with the game
(most do). Custom portals' item rules are respected.

## Known limitations of this alpha

- Gamepad: the panel and the map list are mouse-only for now.
- Portal pins on the map use vanilla's portal icon in a colour of their own, but no icon of their own yet.
