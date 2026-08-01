# Station Extension Guard

A small defensive Valheim mod that suppresses a recurring `NullReferenceException` from `CraftingStation.GetExtensions()` and reports where the dangling extension came from.

## The bug it works around

Some Valheim mods register a `StationExtension` component (anything that "extends" a workbench, forge, etc.) into Valheim's static `StationExtension.m_allExtensions` list via the component's `Awake`, but fail to deregister on destruction. Once a dead reference is sitting in that list, every `Player.FixedUpdate` within range of any crafting station trips a `NullReferenceException` in `CraftingStation.GetExtensions()` — typically at 50 Hz, filling the log.

The NRE is harmless to gameplay but extremely noisy and confusing when trying to diagnose other issues.

## What this mod does

Three Harmony patches:

- **`StationExtension.Awake` postfix** — records each extension's name, position, and parent station the moment it registers itself, keyed by Unity instance ID.
- **`StationExtension.OnDestroy` postfix** — records that a clean destruction happened. (If the mod that's leaking doesn't call OnDestroy, this is silently absent — which is itself a diagnostic.)
- **`CraftingStation.GetExtensions` prefix** — sweeps any null or Unity-destroyed entries out of the static list before vanilla iterates over it. Logs a warning per sweep with:
  - the recorded prefab name (from the side table populated on Awake),
  - the instance ID,
  - whether `OnDestroy` was ever observed for that instance,
  - the current list size.

That last line is the key diagnostic: if you see `OnDestroyFired=false` repeatedly for the same prefab name, you've identified exactly which mod is leaking — its piece is being destroyed in a way that bypasses Unity's normal lifecycle.

## Performance

The sweep prefix runs once per `CraftingStation.GetExtensions()` call (so up to 50 Hz while the player is near a station). A `RemoveAll`-style scan over a list of typically <50 entries is essentially free. The Awake/OnDestroy postfixes fire once per extension lifecycle event and just write to a small dictionary.

Logging is silent when nothing needs sweeping. Awake/OnDestroy events log one line each — useful for correlating but not noisy.

## Configuration

None. The patches are always on. If you want to turn off the sweep, uninstall the mod.

## Compatibility

Should work with any mod that adds or destroys `StationExtension` components. Specifically intended to defang the NRE seen with mods like OdinShipPlus, but the mechanism is generic.
