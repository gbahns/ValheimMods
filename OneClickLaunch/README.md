# One Click Launch

One click from the main menu back into the game you were playing.

Vanilla's main menu makes you click Start, confirm a character, then pick a world and click Start again, and it remembers the character and the world separately. Change the world and it loads whichever character you used last time into it, which is how a character ends up in a world they were never meant to be in. This mod remembers each character **together with** the world (or server) they were actually played in, and puts a button for each recent pair at the top of the main menu.

## What you get

**Continue buttons.** The three most recent character + world pairs (the number is configurable) appear at the top of the main menu, most recent first, as buttons like *Sigrun in Midgard* or *Sigrun on Our Server*. Click one and the game starts. With a controller, the first one is already selected.

- A world starts with the hosting options it was last started with: open to others or not, public or not, crossplay or not, and the password if it had one.
- A server, whether a dedicated one or a friend's game over Steam or crossplay, is rejoined directly. If you typed a password the last time, it is entered for you.

**The full list.** Right-click any Continue button to open every remembered game as a scrollable list, newest first, with how long ago each was played. Click one to start it, or *forget* to drop it. A More... button under the Continue buttons does the same if you turn it on. The buttons are gold by default, with the menu's own orange ornament line under them, so they read as separate from the vanilla menu; both are configurable.

**A wrong-world check.** When the vanilla Start button is about to load a character into a world that character has never been in, the game asks first, and lists the worlds that character has been in. A brand-new character is never asked, and neither are the Continue buttons, since those pairs were played before.

Nothing else in the menu changes. The vanilla Start button, character screen and world list all work as before.

## How it learns

Every launch is remembered when the game starts loading, whatever started it: the vanilla buttons, a Continue button, a Steam invite or a command-line join. So the first time you run the mod there are no buttons yet; play once and there will be.

The history lives in `BepInEx/config/DeathMonger.OneClickLaunch.history.json`, next to the mod's config, so your mod manager keeps it with the profile. It keeps the last 100 pairs (History Size), one entry per distinct pair however often it is played, so raising the button count later brings older ones back.

A character or world that has since been deleted gets a short message instead of an error, and the pair drops out of the list the next time you play something else.

## Passwords

With **Remember Passwords** on (the default), a password you type for a server, or set when hosting a world as open, is remembered so the button can enter it for you. If a remembered password is rejected, it is forgotten, and the next click asks you again and remembers the new one.

It is stored in the history file **encrypted with Windows DPAPI**, tied to your Windows account on this computer: a copy of the file, a backup or a shared profile carries nothing that another account or machine can read. It is not a defense against someone running programs as you. On Linux and Steam Deck the game's Mono uses its own per-user protection instead; if neither is available the password is stored as plain text and the log says so.

Turn the setting off to be asked every time; passwords already stored are dropped the next time the game starts.

## Configuration

`BepInEx/config/DeathMonger.OneClickLaunch.cfg`, editable in a mod manager's config editor:

| Setting | Default | What it does |
| --- | --- | --- |
| Mod Enabled | true | Master toggle. Restart the game after changing it. |
| Buttons | 3 | How many recent pairs get a button (1 to 8). |
| History Size | 100 | How many pairs to remember, most recent first. |
| World Button Label | `{character} in {world}` | Text of a world button. |
| Server Button Label | `{character} on {server}` | Text of a server button; `{server}` is the server's name, or its address when the name is not known. 127.0.0.1 reads as localhost, and the port is shown only when the same host is remembered on more than one port. |
| Remember Passwords | true | See above. |
| Warn On Unknown World | true | The wrong-world check on the vanilla Start button. |
| Button Color | `#F5D76E` | Text color of the Continue buttons; a hex color or a name, blank for vanilla's. |
| Divider | true | The menu's ornament line, repeated between the Continue buttons and the vanilla menu. |
| More Button | false | A More... button that opens the full list; right-click does the same, so off by default. |
| Menu Bottom Margin | 40 | The menu's buttons stack downward, and every button a mod adds pushes Quit further down. When the lowest button would come closer than this to the bottom of the screen, the whole list is lifted to keep it there. Negative disables the lift. |

## Install

Client-side only; the server needs nothing. Drop `OneClickLaunch.dll` into `BepInEx/plugins`, or install from your mod manager. Requires BepInEx.

## Compatibility

Works alongside ServerConnect: its button and these sit in the same list, and the menu is lifted as far as all of them together need, so Quit stays on screen. The mod copies the main menu's own buttons, so it follows whatever menu theme other mods apply.

Source: https://github.com/gbahns/ValheimMods/tree/main/OneClickLaunch
