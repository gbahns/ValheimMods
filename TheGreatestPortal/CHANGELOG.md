# Changelog

## 0.2.0 — 2026-09-13

Works with a 0.1.0 server, but update everyone anyway.

- New icon.
- Search box in the panel and in the map list: type part of a name or a biome to filter.
- Group by biome: a switch in the panel and on the map (remembered in the config) puts a
  Favorites section first, then one section per biome.
- Up/Down move the selection in the panel, Enter confirms, Esc cancels.
- A scrollbar appears on the lists when they overflow, and the mouse wheel moves a few rows per
  notch ("List Scroll Rows", default 4).
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
