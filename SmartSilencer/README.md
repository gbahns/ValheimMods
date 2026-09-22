# Smart Silencer

The game goes quiet, by itself, while something else has your ears.

Put on Spotify and Valheim's sound fades out. Pause it and the game fades back in. Same when a YouTube video starts in your browser, and same when you tab out to another window. Nothing is paused and nothing else changes: the world keeps running, your volume settings stay as they were, and the moment you are back to it the sound is too.

## What you get

Three triggers, each with its own switch, all on by default:

- **Music Playing.** A music player is making sound: Spotify, Apple Music, iTunes, MusicBee, foobar2000, Winamp, AIMP, TIDAL, Deezer, Amazon Music and a few more, in an editable list.
- **Video Playing.** A video player is making sound: any browser (Chrome, Edge, Firefox, Brave, Opera, Vivaldi, Arc, ...), VLC, mpv, MPC, PotPlayer, Netflix, Plex, Kodi and the Windows video apps. Browsers are in this list because a browser is how most people watch anything.
- **Game Not Focused.** Another window is in front.

And a fourth, off by default:

- **Other Apps Playing.** Any other program is making sound, apart from an ignored list (Discord, Teams, Zoom, Slack, Steam, Windows itself). Off so a voice call or a notification does not silence the game.

Two more, also off by default:

- **Game Paused.** The game is paused: the ESC menu in single player, or a pause granted by PauseMyServer on a server. The ESC menu on a server does not pause, so it does not count.
- **Main Menu.** You are on the main menu, before a world is loaded and after leaving one.

While any trigger holds, the game's sound fades down to **Quiet Volume** (silent by default) over a short fade, and fades back up when none does. A short top-left message says which program it went quiet for, and when the sound came back.

Music and video are detected the same way the Windows volume mixer shows a moving level bar next to each program: Smart Silencer reads those per-app level meters a few times a second. So it knows the difference between Spotify being open and Spotify actually playing, and it needs no plug-in, account or browser extension. It does not know what a browser is playing, only that it is playing something.

## Switching it on and off

- **In the config**, with a mod manager's config editor or the in-game Configuration Manager (F1). Every setting takes effect immediately; no restart.
- **A hotkey** (`Toggle Key`) flips the master switch while playing. It is empty by default, since every plain key already does something in Valheim; a combination such as `LeftControl + F9` is a safe pick.
- **The console** (F5): `silencer` shows the state, `silencer on` and `silencer off` flip the master switch, `silencer music off` (or `video`, `other`, `focus`, `paused`, `menu`) flips one trigger, and `silencer apps` lists every program that has an audio session right now, with its level and how Smart Silencer classifies it. That last one is how to find the name to put in a list when something is not detected, or is detected and should not be.

## Configuration

`BepInEx/config/DeathMonger.SmartSilencer.cfg`:

| Setting | Default | What it does |
| --- | --- | --- |
| Mod Enabled | true | Master switch, and what the hotkey flips. |
| Toggle Key | (none) | Hotkey for the master switch. |
| Show Messages | false | Top-left messages on going quiet and coming back. Handy for confirming it works. The hotkey's on/off confirmation always shows. |
| Log Transitions | false | Also write those transitions to the BepInEx log. Handy for confirming it works, noise afterwards. |
| Quiet Volume | 0 | The game's volume while silenced, 0 to 1. 0.2 keeps it faintly there under the music. |
| Fade Out Seconds | 0.4 | How long the sound takes to go down. |
| Fade In Seconds | 0.8 | How long it takes to come back. |
| Music Playing | true | Trigger on programs in the Music Apps list. |
| Video Playing | true | Trigger on programs in the Video Apps list. |
| Other Apps Playing | false | Trigger on any other program not in Ignored Apps. |
| Game Not Focused | true | Trigger when another window is in front. |
| Game Paused | false | Trigger while the game is paused. |
| Main Menu | false | Trigger on the main menu. |
| Music Apps, Video Apps, Ignored Apps | see above | Program names without `.exe`, comma separated, case does not matter. Ignored wins over the other two. |
| Poll Interval Seconds | 0.25 | How often the level meters are read. |
| Sound Threshold | 0.005 | The meter reading (0 to 1) that counts as sound. Music at any listenable volume is far above it. |
| Sound Delay Seconds | 0.3 | How long a program must have been making sound before the game goes quiet. |
| Silence Grace Seconds | 2 | How long everything must have been silent before the sound comes back. Covers the gap between songs and a video buffering. |

## Notes

- **Windows only** for the music and video triggers; they read the Windows Audio Session API. On Linux or the Steam Deck those two triggers switch themselves off and the focus trigger still works.
- Sound from a program that runs elevated (as administrator) still silences the game, but its name may show as `pid 1234`, which puts it in the Other category. Add the name you know it by to a list anyway; the pid form is used only when Windows will not give the name.
- The game's own master, music and effects volumes are untouched: Smart Silencer scales Unity's audio listener on top of them, the same thing the game does around its own cutscenes, and hands it back when a cutscene plays.
- Client-side only. It does nothing on a dedicated server and nothing over the network.

## Source

[github.com/gbahns/ValheimMods/tree/main/SmartSilencer](https://github.com/gbahns/ValheimMods/tree/main/SmartSilencer)
