# Changelog

## 1.3.0 — 2026-09-21

- The warning labels no longer cover other things at the top of the screen, such as a compass mod or the boss health bar. They move down below whatever is there, and back up a few seconds after it goes away.
- New **[Labels]** settings: **Placement** switches between that `Automatic` behavior and `Manual`, which pins the labels at **Manual Y Offset** pixels from the top of the screen.
- The smoke and poison labels no longer draw on top of each other when you are both smoked and poisoned. Visible labels now stack in order: hunger, smoke, poison.
- `hv_labels` console command lists what Automatic placement is avoiding.

## 1.2.1 — 2026-09-15

- The poison overlay now actually appears when you are poisoned. The status effect it watched for was never filled in, so only the test command and the config preview could show it. It reads the hash from the game now instead of a hardcoded number, which also keeps the smoke overlay working if those ever change.

## 1.2.0 — 2026-09-09

- Rebuilt for Valheim 1.0.7 (Unity 6).
- Dismiss key (default **H**, configurable): acknowledge a warning to clear the vignette and label. The HUD status icon stays as a quiet reminder; the full warning returns on its own if hunger worsens, and resets once you eat.
- No hunger nag before the first meal of a session.
- Dependency bumped to BepInExPack 5.4.2350.
