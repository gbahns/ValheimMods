# The Greatest Portal

Choose where each portal goes.

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
| Destination list | Every other portal in the world, favorites first, each with where it leads and its distance. Above it sits **Recents**: the 15 portals anybody has most recently traveled through or built, newest first, with the row's background saying who -- blue for a portal only you have been through, green once anybody else has, each banded diagonally in its own color so the two read without the color as well. Right-click the heading to split it into the three sections it replaces (**Recents**, **Others' Recents**, **Recently Created**, 5 each) and right-click any of those to put it back. A star at the end of every row marks a favorite and flips it: filled for a favorite, hollow for not. The first entry, *Open portal*, means no fixed destination. Up/Down move the selection, Enter confirms; a double-click on a row does both. Right-click a row for Travel here now, Rename, Favorite / Un-favorite, Make private and Open to everyone (your own portals only), whichever categories you have made, New category, and Show on map; renaming works on any portal, wherever it is. |
| Gold edge on a row | The last portal you traveled through. Standing at a portal that is normally the one you came from, so the way back is one click. |
| Hovering a row | Everyone who has been through that portal, newest first, and who built it, with how long ago for each. Works on every row, not only the recent ones. |
| Search | Type part of a name (or a biome) to filter the list. |
| Group by biome | Sections per biome, with a Favorites section first; a favorite is listed only there. Click a header to fold or open that group; Expand all / Collapse all do the lot. All of it is remembered between sessions. |
| Categories | Sections you name yourself. Right-click a portal, choose **New category**, type a name (say *Farms*), and a Farms section appears above the list with that portal in it; right-click any other portal and **Add to Farms** files it there too. A portal is in one category at a time, stays in the list proper as well, and the sections fold like any other. **Take out of Farms** removes it, and a category with nothing left in it is gone -- there is nothing to create or delete separately. Typing a category name in the search box finds its portals. Yours alone, per world, like favorites. |
| Default new portals to point here | New portals you build lead here automatically. |
| Private (only you) | Shown only where the server allows private portals (**Allow Private Portals**, off by default). Makes this portal yours alone: nobody else sees it in a list or on the map, and nobody else can walk through it or arrive at it. Only the character who built it can set this, and it takes effect as you click it. A padlock marks it in your own lists; click the padlock to make it public again. |
| Open to everyone for 15 min | Under the Private toggle, on your own private portals. Lets everybody see and use this one for a while, then it is yours alone again -- no list of names to keep and nobody to take off it. The button becomes **Shut it now** with the time left, and an open padlock marks the row for everyone, so a portal that appears in somebody's list says there that it is temporary. Set the length with **Open For Minutes**; the server caps it with **Maximum Open Minutes**. Somebody still on 1.5.0 sees the portal but is refused the trip. |
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
grouping as the panel. Click a pin or a row to travel there. The pin button at the end of a row puts the map over that portal instead, for a look at where it is before you commit. Right-click a pin or a row for a menu: travel there, center the map on it, favorite it, rename it. The list can be dragged by its heading and resized from the grip in its bottom-right corner, and stays where you put it. Esc, or stepping out of the doorway, closes the
map and you stay. The usual rules apply: no traveling with ore, or during a boss fight if the world
forbids it.

**Going somewhere else just this once.** A portal with a destination normally takes you straight
there. Hold the detour key (default `Alt`) as you step in and the destination map opens instead:
pick anywhere, travel, and the portal is unchanged for the next person who uses it. Handy when a
remote portal is set to lead home but you want a different stop this time. The same thing is on the
panel: press Use, right-click a portal in the list and choose **Travel here now**.

**On the map at other times.** The list belongs to a portal: you get it by stepping into an open
one, or from **Show on map** in the panel. The map you open in the field can still draw the portals
as plain markers -- turn on **Show Pins In Normal Map View**, and `P` shows and hides them. With
TheGreatestMap installed, or any other mod that marks portals, that setting does nothing, because
those pins are already there.

## Keys (configurable)

| Key | Action |
|---|---|
| `E` on a portal | Configure it |
| `Up` / `Down`, `Enter`, `Esc` in the panel | Move the selection, confirm, cancel |
| Mouse wheel over a list | Scroll by a few rows per notch (configurable) |
| `P` on the large map | Show or hide the portal pins |
| `Alt` held while stepping into a portal | Choose a destination for this trip only |

## Configuration

`BepInEx/config/DeathMonger.TheGreatestPortal.cfg`. Rules are server-synced; the rest is yours.

| Section | Setting | Default | Meaning |
|---|---|---|---|
| General | Mod Enabled | true | Turn the whole mod off without removing the DLL; portals go back to pairing by name. Not server-synced, and read once at startup |
| General | Lock Configuration | true | While on, only the server's admins (adminlist.txt) can change the Rules, from their game; everyone else sees them read-only (server-synced) |
| Rules | Max Name Length | 32 | Longest portal name (server-synced) |
| Rules | Open Portals Show The Map | true | An open portal shows the map when you step in. When false it goes nowhere, like an unpaired vanilla portal (server-synced) |
| Rules | Adopt Existing Connections | true | Portals this mod has not configured keep their vanilla tag pair (server-synced) |
| Rules | Anyone Can Redirect All Portals | false | Let any player use "Point all portals to here"; otherwise admins only (server-synced) |
| Rules | Anyone Can Rename Remote Portals | true | Let any player rename portals from the list without standing at them; otherwise admins only (server-synced) |
| Keys | Toggle Portal Pins | P | On the large map: show or hide the portal pins |
| Keys | Detour Key | LeftAlt | Hold while stepping into a portal to pick a destination for that trip only |
| Display | Show Pins In Normal Map View | false | Draw every portal on the large map when you are not at a portal; does nothing when another mod already marks them |
| Display | Show Distances | true | Distances in the lists |
| Display | One Combined Recents List | true | One Recents section instead of three, colored by who was last at each portal; right-click the heading to switch |
| Display | Combined Recents Count | 15 | How many portals that combined section shows; 0 hides it |
| Display | Recent Portals | 5 | With the combined list off: how many recent portals the Recents section shows; 0 hides it |
| Display | Other Players Recent Portals | 5 | With it off: how many portals other players have used lately to show in Others' Recents; 0 hides it |
| Display | Recently Created Portals | 5 | With it off: how many newly built portals to show in Recently Created; 0 hides it |
| Display | Map List Size | 380,660 | Width and height of the portal list on the map, remembered when you drag its corner |
| Display | Map List Position | 20,-12 | Where that list sits, from the top-left of the map, remembered when you drag it by its heading |
| Display | Group By Biome | false | Group the lists by biome; the switches on the panel and the map change it too |
| Display | Panel Size | 680,600 | The portal panel's size, saved when you drag its corner |
| Display | Panel Position | 0,0 | The panel's offset from the screen center, saved when you drag its title |
| Display | List Scroll Rows | 4 | Rows a list moves per mouse-wheel notch |
| Display | Collapsed Groups | empty | Which biome groups are folded away, written for you as you fold them |
| Display | Auto Close Grace Seconds | 0.5 | Leave the doorway for this long and the destination map closes |
| Display | Show Messages | true | Small top-left messages |
| Rules | Allow Private Portals | false | Whether a portal may be made private at all. Synced; turn it on only once everybody is on 1.5.0 or newer |
| Rules | Maximum Open Minutes | 60 | The longest a private portal may be opened to everyone for. Synced; 0 turns opening off |
| General | Open For Minutes | 15 | How long your own "Open to everyone" lasts, up to the server's maximum |
| Display | Show Private Portals | false | Admins only: ask the server for other players private portals too. Ignored for anyone not in adminlist.txt |
| Appearance | Row Color Yours Alone | blue-grey | The background of a portal only you have been through |
| Appearance | Row Color Used By Others | green-grey | The background once another player has been through it |
| Appearance | Row Color Strength | 0.17 | How strongly those two colors are painted; 0 leaves every row plain |
| Appearance | Row Color Patterns | true | Band each color diagonally as well, leaning right for yours alone and left for the rest |
| Appearance | Panel Opacity | 1 | How solid the panel and the map list are; lower shows more of the world |

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

## Known limitations

- Gamepad: the panel and the map list are mouse and keyboard only for now.
- Portal pins on the map use vanilla's portal icon; no icon of their own yet.
