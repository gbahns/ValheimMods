# Sap Extractor Progression Fix

Sap extractors are meant to keep filling while you are away, the way beehives do. In the unmodified game they mostly don't: you come back after a long trip and find 0 or 1 sap. This mod makes them credit the whole time you were gone.

## What was wrong

When you return, the extractor spends the time you were away before checking whether it can use it. Three things then go wrong:

- The extractor wakes up before the ancient root under it has loaded, decides it is not connected, and throws the time away.
- The root has not yet refilled for the time you were away, so the extractor sees an empty root and throws the time away, or takes only the little that was left.
- When a catch-up does work, the extractor drains the root by everything it earned, not by what fit in it, which starves the other extractors on that root.

The mod waits for the root, refills the root first, and drains only what was actually collected.

## Installing

Client-side. The fix runs on the computer of whoever walks up to the extractors, so every player who wants it needs it. Nothing goes on the server.

## Configuration

`BepInEx/config/DeathMonger.SapExtractorProgressionFix.cfg`. Every setting takes effect immediately.

| Setting | Default | What it does |
| --- | --- | --- |
| Mod Enabled | true | Master switch. Off leaves sap extractors exactly as the game runs them. |
| Log Catch Up | true | Write a line to the BepInEx log each time you return to an extractor: how long you were away, what the unmodified game would have done, and what was credited. |

## Source

https://github.com/gbahns/ValheimMods/tree/main/SapExtractorProgressionFix
