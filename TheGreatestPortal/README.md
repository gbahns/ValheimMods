# The Greatest Portal

**Early alpha.** Choose where each portal goes.

- Press **Use** on a portal to name it and pick its destination from a list of every portal in
  the world, or click **Pick on map** and choose it on the map. No tag pairing, no portal hubs.
- Mark one portal as your **default** and every portal you build afterwards leads there.
- Leave a portal **open** (no destination) and stepping into it shows the map: click any portal,
  on the map or in the list on the left, and you are there.
- Mark **favorites**: they sit at the top of every list and carry a star on the map.
- Take a **detour**: hold a key as you step in to go somewhere else just this once, leaving the
  portal's own destination alone.
- Lists can be searched and grouped by biome.

Install on the server and on every client: a player without it is refused at connect, and so is a
server without it. Versions do not have to match. Each side declares the oldest version it can
still talk to, which has been 0.1.0 for every release so far; a release that changes what the two
sides say to each other raises that floor and says so in the changelog.

## How it works in play

**Setting up a portal.** Press Use (E) on it. The panel (drag its title to move it, its bottom-right corner to resize it) shows:

| Field | What it does |
|---|---|
| Name | Up to 32 characters (configurable). Names do not have to be unique. |
| Star beside the name | Marks the portal you are standing at as a favorite; click again to take it off. Takes effect at once. |
| Destination list | Every other portal in the world, favorites first, each with where it leads and its distance. A **Recents** section at the top repeats the portals you most recently traveled to or from (5 by default). The first entry, *Open portal*, means no fixed destination. Up/Down move the selection, Enter confirms; a double-click on a row does both. Right-click a row for Travel here now, Rename, Favorite / Un-favorite and Show on map; renaming works on any portal, wherever it is. |
| Search | Type part of a name (or a biome) to filter the list. |
| Group by biome | Sections per biome, with a Favorites section first; a favorite is listed only there. Click a header to fold or open that group; Expand all / Collapse all do the lot. All of it is remembered between sessions. |
| Default new portals to point here | New portals you build lead here automatically. |
| Point all portals to here | Points every portal in the world at this one. Shown only when this portal is your default and some portal does not lead here yet. Click twice to confirm. Admins only unless the server allows everyone. |
| Pick on map | Saves the name and the default setting, then opens the map: click a portal there or in the list. |
| Travel here now | In a row's right-click menu: travel to that portal right now. The portal you are standing at keeps its own destination. |
| Show on map | Opens the map centered on the chosen destination, with every portal drawn. |

Destinations are one-way: `Home -> Swamp` does not make the swamp portal lead home. Set the swamp
portal's destination too, or leave it open. A portal with a destination behaves exactly like a
paired vanilla portal, including the usual item restrictions.

**Traveling through an open portal.** Step in and the map opens on your position. It is the map you
already know, with everything you normally see on it, and every portal drawn on top as a pin. The
portal under the pointer grows and lights up with a halo in its own color and shows its name, so you
can see what a click will do; hovering a row in the list lights up that portal's pin too. With
TheGreatestMap installed the pins wear the color it gives portals, pulse and all; without it they
are white, as vanilla pins are. The list on the left starts with your recent portals, then favorites, then the rest
alphabetically, each with where it leads and its distance; it has the same search box and biome
grouping as the panel. Click a pin or a row to travel there. Right-click marks a favorite. Esc, or stepping out of the doorway, closes the
map and you stay. The usual rules apply: no traveling with ore, or during a boss fight if the world
forbids it.

**Going somewhere else just this once.** A portal with a destination normally takes you straight
there. Hold the detour key (default `Alt`) as you step in and the destination map opens instead:
pick anywhere, travel, and the portal is unchanged for the next person who uses it. Handy when a
remote portal is set to lead home but you want a different stop this time. The same thing is on the
panel: press Use, right-click a portal in the list and choose **Travel here now**.

**On the map at other times.** Press `P` while the large map is open to show every portal with the
list, click a portal to center the map on it, press `P` again to hide them. Right-click a portal
to mark a favorite.

## Keys (configurable)

| Key | Action |
|---|---|
| `E` on a portal | Configure it |
| `Up` / `Down`, `Enter`, `Esc` in the panel | Move the selection, confirm, cancel |
| Mouse wheel over a list | Scroll by a few rows per notch (configurable) |
| `P` on the large map | Show or hide every portal and the portal list |
| `Alt` held while stepping into a portal | Choose a destination for this trip only |

## Configuration

`BepInEx/config/DeathMonger.TheGreatestPortal.cfg`. Rules are server-synced; the rest is yours.

| Section | Setting | Default | Meaning |
|---|---|---|---|
| General | Mod Enabled | true | Turn the whole mod off without removing the DLL; portals go back to pairing by name. Not server-synced, and read once at startup |
| Rules | Max Name Length | 32 | Longest portal name (server-synced) |
| Rules | Open Portals Show The Map | true | An open portal shows the map when you step in. When false it goes nowhere, like an unpaired vanilla portal (server-synced) |
| Rules | Adopt Existing Connections | true | Portals this mod has not configured keep their vanilla tag pair (server-synced) |
| Rules | Anyone Can Redirect All Portals | false | Let any player use "Point all portals to here"; otherwise admins only (server-synced) |
| Rules | Anyone Can Rename Remote Portals | true | Let any player rename portals from the list without standing at them; otherwise admins only (server-synced) |
| Keys | Toggle Portal Pins | P | On the large map: show or hide every portal |
| Keys | Detour Key | LeftAlt | Hold while stepping into a portal to pick a destination for that trip only |
| Display | Always Show Portal Pins | false | Draw every portal on the large map all the time |
| Display | Show Distances | true | Distances in the lists |
| Display | Recent Portals | 5 | How many recent portals the Recents section shows; 0 hides it |
| Display | Group By Biome | false | Group the lists by biome; the switches on the panel and the map change it too |
| Display | Panel Size | 680,600 | The portal panel's size, saved when you drag its corner |
| Display | Panel Position | 0,0 | The panel's offset from the screen center, saved when you drag its title |
| Display | List Scroll Rows | 4 | Rows a list moves per mouse-wheel notch |
| Display | Collapsed Groups | empty | Which biome groups are folded away, written for you as you fold them |
| Display | Show Portal List On Map | true | The list on the left of the map |
| Display | Auto Close Grace Seconds | 0.5 | Leave the doorway for this long and the destination map closes |
| Display | Show Messages | true | Small top-left messages |

Favorites, recent portals and the default portal are yours alone and are kept per world in
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
| `tgp_status` | Portal count, picker state, favorites |
| `tgp_list` | Every portal with its destination |
| `tgp_default <name>` / `tgp_default clear` | Set or clear the default portal |
| `tgp_refresh` | Ask the server for the portal list again |

## Compatibility

Replaces XPortal, TargetPortal, AnyPortal and PortalRules; do not run it alongside them.
Works with mods that add new portal pieces as long as they register them as portals with the game
(most do). Custom portals' item rules are respected.

## Known limitations of this alpha

- Gamepad: the panel and the map list are mouse and keyboard only for now.
- Portal pins on the map use vanilla's portal icon; no icon of their own yet.
