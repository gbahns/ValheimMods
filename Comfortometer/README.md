# Comfortometer

What is giving you comfort right now, how far away is it, and what did you just walk out of range of?

Press **F4** and a small panel appears on the HUD. It stays up while you move around, so you can walk through your base and watch the comfort level change, and see exactly which piece changed it.

> **Comfort 14** &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; sheltered · rested 18 min
>
> Hearth &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; +3 &nbsp;&nbsp; 2.4 m
> Dragon bed &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; +2 &nbsp;&nbsp; 4.1 m
> Raven throne &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; +3 &nbsp;&nbsp; 3.0 m
> Black marble table &nbsp;&nbsp; +2 &nbsp;&nbsp; 3.3 m
> Deer rug ×2 &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; +2 &nbsp;&nbsp; 1.8 m
> <span style="color:#a09a90">Chair (chair)</span> &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; <span style="color:#a09a90">+1 &nbsp;&nbsp; 2.2 m</span>
> <span style="color:#ff5c4d">Banner &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; +1 &nbsp;&nbsp; 12 m</span>

Each line is one kind of piece: its comfort points and the distance to the nearest one. When more than one of the same kind is in range the line says so, as in `Deer rug ×2`, which is your cue that the second one is doing nothing.

**Red** is the line you are here for. Walk out of range of something and it stays on the list in red, with its current distance, so you know what you lost and how far back it is. It drops off once you are well clear of it. An unlit fire is red too, marked *unlit*, and when you are not under a roof the header says **NO SHELTER** in red, because then nothing counts.

**Dimmed** lines are pieces that give nothing because a better piece of the same group is also in range: a chair next to a throne, a stool next to a chair. The group is named in brackets so you can see why. They are off by default; turn on *Show Superseded* to see them.

The list is built exactly the way the game builds the comfort level: the same 10 m radius, the same one-per-group and one-per-name rule, the same shelter test. The level in the title is the game's own number.

## Moving and resizing

With the panel up, press **F4** again to free the mouse. The title strip drags the panel, the grip in the lower-right corner resizes it, and the **x** in the corner closes it. You can still walk around meanwhile. Press F4 once more to give the mouse back to the camera. The position and size are remembered.

The x and the drag also work whenever the cursor is already free, such as with the inventory open.

The panel grows and shrinks to fit its list, so the grip only changes the width. Turn off *Auto Height* to set a fixed height instead; then a list that does not fit ends in `+N more`.

## Installing

Client-side. Install it on the computers that should see the panel; nothing goes on the server.

## Configuration

`BepInEx/config/DeathMonger.Comfortometer.cfg`. Every setting takes effect immediately.

| Setting | Default | What it does |
| --- | --- | --- |
| Mod Enabled | true | Master switch. |
| Toggle Panel | F4 | Shows the panel; while it is up, frees or locks the mouse so you can drag, resize or close it. |
| Open At Start | false | Show the panel as soon as you spawn in. |
| Forget Beyond | 30 | A piece you walked out of range of stays listed in red until you are this many meters from it. |
| Show Superseded | false | Also list, dimmed, the pieces in range that give nothing because of a better one in their group. |
| Show Rested Time | true | Show how long Rested lasts at the current comfort level, next to the level. |
| Show Range | false | Show each distance against the range it counts within, as in `8.2 / 10 m`. The range is 10 m for every piece, fixed by the game. |
| Bold Names | true | Draw the piece names in bold. |
| Auto Height | true | The panel grows and shrinks to fit its list; the grip then only changes the width. |
| Font Size | 15 | Text size in the panel. Row height follows it. |
| Panel Position, Panel Size | | Written when you drag or resize the panel. Clear them to go back to the defaults. |

## Notes

- Distances are measured from where you stand to the piece's center, the same way the game measures them, so `9.8 m` counts and `10.2 m` does not.
- The panel hides with the rest of the HUD.
- The panel does not take the keyboard or the mouse while you play. Only arrange mode touches the mouse, and even then movement and hotkeys keep working.
