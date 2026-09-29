# Changelog

## 0.3.1 — 2026-09-29

- A death by the poison or fire a hit left behind is credited to whoever dealt it. The poison ticks name no attacker, so "was poisoned to death" was all the mod could say when a Writhan's blast did the damage; now it is "was slimed by a Writhan", or one of the Writhan's own lines.
- A creature that blows up as it dies has no body left to ask when its blast lands, so its identity is read off its network data instead, and a blast nobody owns is credited to the last creature that hit you in the seconds before.
- Explosions read as explosions ("was blown up by", "stood too close to", "should have backed away from") rather than as a punch.
- Writhan lines.

## 0.3.0 — 2026-09-28

- A config file written by 0.1.0 or 0.2.0 kept the small top-left message and the old victim color, since a changed default never touches a value already in the file. Once per file, a value still at an old default now moves to the new one; anything you changed yourself is left alone.
- **Louder.** The whole line is orange in the chat window (Line Color), a little bigger (Text Size), and the window stays up 30 seconds for it (Chat Seconds). The on-screen message now defaults to the center of the screen and stays 15 seconds (Center Seconds), which covers the 10 you lie there before the respawn starts, instead of fading after 4.
- **The weapon matters.** A Draugr with a bow reads "accepted the Draugr's shaft" or "ate the Draugr's arrow"; one with an axe "was sliced in half"; a Fuling with a spear "was skewered"; a Skeleton with a mace "got his head bashed in"; a Troll with a log "was swatted by the Troll's log". Every weapon kind has a pool of lines, and the creature's own lines are mixed in.
- **Pronouns.** His or her from the character's body type, or set Pronouns to He, She or They.
- **More entertaining everywhere.** An unidentified weapon reads "got jacked up by" rather than "was killed by".
- The BepInEx log gets one line per death with what the game said about the killing hit (Log Killing Hit), for tuning: send it along when a creature reads wrong.
- The console preview takes a weapon: `obituary draugr bow`, `obituary pvp club`, `obituary troll log`.

## 0.2.0 — 2026-09-27

- Another mod that kills a player itself can now say what the obituary should read as, instead of
  the blank line kept for deaths nothing is known about. Valheim records only the blow, so such a
  death carries no cause of its own for this mod to work from. Nothing changes for a death the
  game deals.

## 0.1.0 — 2026-09-25

First release.
