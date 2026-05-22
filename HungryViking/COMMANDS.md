# Hungry Viking — Console Commands

Open the console with **F5**. Commands marked **(cheat)** require `devcommands` to be active first. Visual test toggles do not.

---

## Visual Testing

These commands toggle each overlay on and off for testing or adjusting config values. No devcommands required.

### `hv_testhunger`
Toggles the hunger vignette and label on/off. Useful for adjusting Intensity and Extent settings — changing either config value also triggers a 2-second auto-preview.

```
hv_testhunger
```

### `hv_testsmoked`
Toggles the smoke vignette and label on/off.

```
hv_testsmoked
```

### `hv_testpoisoned`
Toggles the poison vignette and label on/off.

```
hv_testpoisoned
```

---

## Food Status (cheat)

### `hv_foodstatus`
Prints the internal item name and exact seconds remaining for each active food slot.

```
hv_foodstatus
```

**Output example:**
```
  Slot 1: $item_raspberry  287s remaining
  Slot 2: $item_cookedmeat  614s remaining
  Slot 3: $item_bread  1180s remaining
```

---

## Draining Time (cheat)

### `hv_drainfood <slot> [seconds]`
Subtracts the specified number of seconds from one food slot.

| Argument | Description |
|---|---|
| `slot` | Slot number: `1`, `2`, or `3` |
| `seconds` | Seconds to subtract (default: `60`) |

```
hv_drainfood 1        # drain 60s from slot 1
hv_drainfood 2 300    # drain 300s from slot 2
```

### `hv_drainfoods [seconds]`
Subtracts the specified number of seconds from all active food slots.

| Argument | Description |
|---|---|
| `seconds` | Seconds to subtract (default: `60`) |

```
hv_drainfoods         # drain 60s from all slots
hv_drainfoods 999     # drain 999s from all slots (forces expiry)
```

---

## Setting Time Directly (cheat)

### `hv_setfoodtime <slot> <seconds>`
Sets a food slot to exactly the specified number of seconds remaining.
Useful for landing precisely at a threshold to verify notification timing.

| Argument | Description |
|---|---|
| `slot` | Slot number: `1`, `2`, or `3` |
| `seconds` | Target time in seconds |

```
hv_setfoodtime 1 91   # just above threshold (90s default)
hv_setfoodtime 1 29   # well inside the warning window
hv_setfoodtime 2 0    # expire slot 2 immediately
```

---

## Clearing Food (cheat)

### `hv_clearfood`
Instantly removes all active food buffs. The mod will immediately show the
empty-slot vignette and "You are hungry" label.

```
hv_clearfood
```

---

## Diagnostics (cheat)

### `hv_selist`
Lists all active status effects on the local player, including their internal name and hash. Useful for identifying status effect names for mod development.

```
hv_selist
```

**Output example:**
```
  "$se_rested_name"  hash=-2079273775
  "$se_smoked_name"  hash=-1612278721
  HaveStatusEffect(SmokedHash=-1612278721) = True
```

---

## Suggested Test Flow

1. Eat 3 different foods.
2. `hv_foodstatus` — confirm all 3 slots are visible to the mod.
3. `hv_setfoodtime 1 91` — slot 1 is 1 second above the threshold.
4. Wait ~2 seconds — amber vignette and label should fade in.
5. `hv_setfoodtime 1 29` — slot 1 is now deeper in the warning window.
6. `hv_drainfoods 999` — all slots hit 0 and expire.
7. Full-urgency vignette and "You are hungry." label appear immediately.
8. Eat food to confirm label and vignette clear when all 3 slots are filled.
