# Status Icon Sub-Text — Investigation Notes

Goal: display a short message like "Getting Hungry" or "Hungry" as sub-text under the
Hungry icon in the same row as Rested, Shelter, etc.  The Resting status shows
"Comfort: 8" in that slot, which confirmed the feature is possible.

---

## Attempts and outcomes

### 1. Override `GetHudText()` on the StatusEffect subclass
Added `public override string GetHudText() => ...` to `HungerStatusEffect`.

**Result:** CS0115 compile error — `GetHudText` is not declared virtual on the base class,
so it cannot be overridden.

---

### 2. Harmony-patch `StatusEffect.GetHudText`
Used `[HarmonyPatch(typeof(StatusEffect), "GetHudText")]` to intercept the call.

**Result:** Runtime `ArgumentException: Undefined target method`.  The method does not
exist on `StatusEffect` at all — the Harmony attribute's type lookup failed.

**Investigation:** Binary search of `assembly_valheim.dll` found `GetHudText` appears
exactly once in the string table, sandwiched between `m_foodText` and `m_freespaceText`,
both of which are Text fields on the `Hud` class.  Conclusion: `GetHudText` is a method
on `Hud`, not on `StatusEffect`.  There is no virtual/overridable hook on StatusEffect
for injecting sub-text.

---

### 3. Manipulate `m_ttl` / `m_time` to show a built-in timer countdown
Set `_activeStatusEffect.m_ttl = 9999f` and `m_time = 9999f - remaining` each frame so
the HUD's own timer display would show the seconds until the worst slot expires.

**Result:** Runtime `FieldAccessException: Field 'StatusEffect:m_time' is inaccessible`.
The publicized reference assembly (used for compilation) exposes `m_time` as public, but
the actual game DLL has it non-public.  Mono enforces the real access level at runtime.
Tested in-game first: no visible sub-text appeared even before the exception was noticed.

**Takeaway:** Even if the field were accessible, it's unclear whether the current Valheim
version's status effect element template has a visible Text component for timers.

---

### 4. Postfix-patch `Hud.UpdateStatusEffects`, inject a Text directly
Added `HudPatch.cs`.  Approach:
- After the method runs, get `m_statusEffectListRoot` (found in the DLL string table via
  binary search) via `AccessTools.FieldRefAccess`.
- Iterate children.  Identify our element by comparing each child's `Image.sprite` against
  `HungerStatusEffect.Icon` (a static reference to the exact Sprite instance we created).
- If found: if a second `Text` child exists (built-in timer slot), use it; otherwise
  create a new `GameObject("HungerSubText")` with a `Text` component and position it
  below the icon.
- Clear any stale `HungerSubText` on non-matching elements.

**Result:** Ran without errors or exceptions, but no sub-text appeared in-game.

**Likely causes (not verified):**
- `IsHungerElement` never returns true.  The status effect element may not contain a
  plain `Image` component with our sprite — Valheim might use a `RawImage`, or nest the
  icon differently, or the `Image` holding the sprite is on a different object than
  expected.
- `m_statusEffectListRoot` might not be the direct parent of status effect elements in
  this Valheim version; the actual layout could be more deeply nested.
- `CreateSubText` is called and creates a child, but it's positioned or sized in a way
  that puts it outside the clipping rect or behind other elements.

**Note:** Because the patch ran silently, it's impossible to tell from the outside which
case applied.

---

## What's needed to go further

A proper decompilation of `Hud.UpdateStatusEffects` is the prerequisite for any further
attempt.  Binary string-table searches are too coarse to reveal the element hierarchy.

Tools that would work:
- **dnSpy** — open `assembly_valheim.dll`, navigate to `Hud.UpdateStatusEffects`, read C#.
- **ILSpy** (GUI or `ilspycmd` CLI if installed with .NET 3.x runtime).
- **Auga / BetterUI source on GitHub** — both are Valheim HUD overhaul mods that patch
  status effect elements; their source would reveal the exact child structure.

Once the actual field names and element hierarchy are known, the inject-a-Text approach
(attempt 4) is almost certainly the right strategy — it just needs the correct path to
the icon Image and the correct sibling/child slot for the sub-text.

---

## Current state

`HudPatch.cs` was deleted.  The `HungerStatusEffect.Icon` static property and
`GetTooltipString()` override were also removed as they existed only to support the patch.
The status icon still shows and works; it just has no sub-text.
