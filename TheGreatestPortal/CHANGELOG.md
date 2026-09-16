# Changelog

## 0.5.0 — unreleased

- A **Recents** section tops the lists in the panel and on the map: the portals you most recently
  traveled to or from, newest first, so the way back is always one click away. They keep their
  usual place below as well. "Recent Portals" sets how many it shows (default 5; 0 hides it). The
  list is yours alone and is kept per world with your favorites.
- Favorite the portal you are standing at: a star beside the name in the panel. Click it to add
  the portal to your favorites and again to take it off. A portal never appears in its own list,
  so until now it could only be favorited from another portal's panel or on the map.

## 0.4.0 — 2026-09-16

## 0.3.3 — 2026-09-15

- Portal pins wear the color TheGreatestMap paints portals, pulse and all, instead of plain white,
  so the picker's pins match the portal markers already on the map. Without that mod they stay
  white, as every vanilla pin is.
- The portal under the pointer keeps its own color and lights up instead: a halo in that same
  color, breathing gently, behind a pin grown half again as large. It used to turn pale gold,
  which threw away the color you had chosen for portals at the moment you were looking hardest.
- The destination map is the map you already know. It used to hide your own markers while you
  picked a portal, which made it look like a different map; now everything the M key shows is
  still there, portals included. The "Hide Other Pins While Choosing" setting is gone with it.

## 0.3.2 — 2026-09-15

- The plugin reports its own version again. 0.3.1's DLL still announced itself to BepInEx as
  0.3.0, so a log line named the wrong release. Nothing else changed.

## 0.3.1 — 2026-09-15

Client-side; works with a 0.2.x server.

- Portals on the map answer the pointer: the one you are about to click grows, turns gold and
  shows its name whatever the zoom, so it is clear what a click will do. Hovering a row in the
  list lights up that portal's pin the same way, which is how you find a name on the map.

## 0.3.0 — 2026-09-14

Client-side; works with a 0.2.x server.

- **Go somewhere else just this once.** Hold the detour key (default Alt) as you step into a
  portal and its destination map opens instead of the usual trip. Pick anywhere and go; the
  portal keeps the destination it is set to. A remote portal can stay pointed home while you
  still take the occasional trip elsewhere from it.
- The same from the panel: right-click a portal in the list and choose "Travel here now".
- A portal whose destination has gone missing, or is still connecting, can be left this way too.

## 0.2.2 — 2026-09-13

Client-side; works with a 0.2.1 server.

- The panel can be moved by dragging its title and resized by dragging its bottom-right corner;
  both are remembered.
- Group headers carry a triangle, pointing right when folded and down when open.
- Every row in the lists shows where that portal leads ("open", "to Name") next to its name.
- With "Group by biome" on, a favorite is listed once, under Favorites, not again under its biome.
- The redirect-all button now reads "Point all portals to here" and appears only when the portal
  is ticked as your default and some other portal does not lead here yet.
- The default checkbox reads "Default new portals to point here". The Favorite checkbox is gone:
  favorites are set from a row's right-click menu (Favorite / Un-favorite) or on the map.

## 0.2.1 — 2026-09-13

Works with a 0.2.0 server except for renaming from the list, which needs the server updated.

- Right-click a portal in the panel's list for a menu: Rename, Add to favorites / Remove favorite,
  Show on map. Rename turns the row's name into a text box; Enter saves, Escape or clicking away
  cancels. Works on any portal, wherever it is. Server rule "Anyone Can Rename Remote Portals"
  (default on) can limit renaming to admins.
- Hovering an open portal no longer shows the "Open: choose the destination when you step in" text;
  a fixed destination still shows "To ...".
- Enter to confirm the panel no longer opens the chat as well.
- Double-clicking a portal in the panel's list chooses it and confirms, like Enter or OK.
- No more "LiberationSans SDF Font Asset was not found" warnings when the lists are built.

## 0.2.0 — 2026-09-13

Works with a 0.1.0 server, but update everyone anyway.

- New icon: an in-game portal.
- Search box in the panel and in the map list: type part of a name or a biome to filter.
- Group by biome: a switch in the panel and on the map (remembered in the config) puts a
  Favorites section first, then one section per biome. Click a section header to fold it away
  or open it again; Expand all and Collapse all do the lot. Folded groups are remembered.
- Up/Down move the selection in the panel, Enter confirms, Esc cancels.
- A scrollbar appears on the lists when they overflow, and the mouse wheel moves a few rows per
  notch ("List Scroll Rows", default 4). The wheel no longer zooms the camera while the panel is open.
- "All portals lead here": points every portal in the world at this one. Click twice to confirm.
  Admins only, unless "Anyone Can Redirect All Portals" is on.
- The default toggle reads "Default: new portals lead here".
- American spelling throughout.

## 0.1.0 — 2026-09-12

Early alpha, first test build. Install on the server and on every client.

- Use on a portal opens a panel: name it and pick its destination from a list of every portal
  in the world (favorites first, with distances), or pick it on the map.
- A default portal: every portal you build afterwards leads there.
- Open portals: leave the destination unset and stepping in shows the map with every portal drawn;
  click a pin, or a row in the list on the left, to travel.
- Favorites (right-click a portal on the map or in a list) sit first in every list.
- `P` on the large map shows every portal with the list; click one to center the map on it.
- Adding the mod to a world keeps existing vanilla tag pairs.
- Console commands `tgp_status`, `tgp_list`, `tgp_default`, `tgp_refresh`.
