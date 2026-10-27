# Plate Durability UI -- working notes for Claude

Body armor and armored rigs get a small gauge on their item icon: one bar per plate slot (front,
back, left, right), slot letters, and a thin bar for the weakest soft insert. Client only, one
BepInEx plugin. Repo https://github.com/JoelHauser/PlateDurabilityUI.git. Idea from UI Fixes
issue tyfon7/UIFixes#112, whose author called a per-plate overlay out of scope.

Joel's brief (2026-10-27): it must look like it shipped with EFT, polished, and work with plates
added by any other mod. Helmets are out for v1. Design mockup (bars style chosen, continuous
colour ramp, icon only): https://claude.ai/artifact/XZUGkBPaihU8Y9xcCn8RfJ

## NEXT, on the home machine: build 0.0.1 and attach it to the pre-release

Joel asked (2026-10-27) for a GitHub **pre-release 0.0.1**. It was created from the work machine
with **no zip**: that machine has no patched install, and a DLL compiled against the unpatched
assembly carries obfuscated names (`_E371`, `_EE74`, ...) in its member references, so it would
not load in SPT. In PowerShell, from the repo root:

1. `git pull`. Check `Get-Process EscapeFromTarkov -ErrorAction SilentlyContinue` first (`-Install`
   cannot replace a DLL the game has loaded).
2. `scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install` -> `dist\PlateDurabilityUI-0.0.1.zip`.
   **If the build fails, stop and fix it before uploading anything.**
3. `gh release upload v0.0.1 dist\PlateDurabilityUI-0.0.1.zip`, then edit the notes to drop the
   "download coming" line: `gh release edit v0.0.1 --notes-file <notes>` (UTF-8 without BOM).
4. Run the raid test list below.

## State as it stands

**0.0.1 written 2026-10-27 on the work machine, never run.** It compiles (0 warnings, warnings
are errors) against `C:\HUH`'s **unpatched** 4.1.5 Assembly-CSharp with
`-p:CompileCheckOnly=true`; that DLL must not ship. A real build needs a patched install (home
machine, `H:\SPT4.1.X`): `scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install`.

The source names **no obfuscated type**, which is why the same source compiles against both the
unpatched (`_E371`, `_EEA7`, ...) and the SPT-remapped assembly. Keep it that way: reach
obfuscated types only through named members (`Slot`, `ArmorComponent`, `ArmorHolderComponent`,
`RepairableComponent`, `CompoundItem.Slots/Grids`) and `IEnumerable<Slot>` covariance.

## How it works

- **One postfix**, `GridItemView.UpdateInfo` (Patches.cs). Every icon-with-numbers view is a
  GridItemView (SlotItemView for equipment, TradingItemView, Ragfair, Insurance, Transfer, ...);
  every override calls base first. UpdateInfo runs on create, on child add/remove (plate
  swapped: `_E00D` -> `location.IsChildOf(Item)`), on examine. Fields injected: `_infoPanel`
  (private, serialized), `BottomPanel` (protected, serialized). `Examined` (protected virtual
  property) through `AccessTools.MethodDelegate`.
- **PlateReader**: a slot counts as armor when it has plate colliders
  (`Slot.ArmorPlateColliderMask != 0`), body colliders (`Slot.ArmorColliders`), is in
  `ArmorHolderComponent.ArmorSlots`, or holds an item with an ArmorComponent. Hard plate = plate
  colliders on slot or plate template, or an unlocked slot without body colliders; everything
  else is soft. Zone from `EArmorPlateCollider` flag names (chest/back/side_left/side_right),
  falling back to the slot id. Any head collider anywhere -> helmet -> no gauge. Rig = has
  `Grids`. Soft-only armor (no plate slots) gets no gauge.
- **Durability scale**: default against `RepairableComponent.TemplateDurability` (original max),
  the same as vanilla `DurabilitySlider` (weapons on SlotItemView, `maxValue =
  TemplateDurability`, `_fillWear = (Template - Max)`) and `_E340.RealResistance(Durability,
  TemplateDurability, ...)` in ArmorComponent's damage maths. Broken = `ArmorComponent.IsDestroyed`
  (`Durability < 0.01`).
- **PlateGauge**: a child of `_infoPanel` named `PlateDurabilityGauge`, made once per view and
  kept through the view pool; UpdateInfo re-points it or deactivates it. Polls its item every
  0.25 s (unscaled) while active to catch damage taken with the inventory open. Only the backing
  panel is a raycast target (for the hover, off with "Hover the gauge for details"); it has no
  click/drag handlers, so those bubble up to the ItemView. Letters use the font of
  `BottomPanel.ItemValue` (the game's own). Sprites are procedural, point-filtered (Sprites.cs).
- **Placement**: bottom-left of the view's rect, pushed right past any active small Image under
  BottomPanel in the left half (insured icon, repair buff, quest marker, mod icons; other mods'
  icons too), and shrunk (down to 0.6) rather than run into `ItemValue`'s rendered text.
- Setting change -> `FindObjectsOfType<GridItemView>()` and `UpdateInfo()` on each active one.
- **PlateTooltip** (Joel asked 2026-10-27 for a fuller hover panel in the vanilla style): a
  prefix on `MultiLineTooltip.Show(_F1DC)` (found by shape: one param whose type has a `Lines`
  field; prefix takes `object __0`) replaces `Lines` (readonly, set by reflection) with: vanilla
  header row, separator (null row), one row per plate slot in gauge order incl. empty, separator,
  every soft insert, separator, "Plates fitted n/m". Row = Label "class icon + slot
  LocalizedName()", Value "dur/full pct%" in the ramp colour, "Broken", "Empty", "max N" wear.
  Icon from the public static `FormatArmorClassIcon(int)` found by name over all types. Only
  when `_subject` (set by a GridItemView.OnPointerEnter prefix, cleared on exit) reads as
  gauge-worthy armor, so soft vests, helmets and every other MultiLineTooltip are untouched.
  The gauge opens it itself through the `(CompoundItem, bool)` constructor, as vanilla's
  `GridItemView._E018` does. A failure in PlateTooltip.Apply only disables the tooltip.
  English only for the few words it adds (Empty, Broken, max, Plates fitted).

## Vanilla facts this rests on (read 2026-10-27 from `ilspycmd -p` of C:\HUH 4.1.5)

- Vanilla already has the hover breakdown, but **only on hovering the durability numbers**
  (`BottomPanel.ValuePointerEventsProxy` -> `_E018`): `MultiLineTooltip` with
  `_F1DC(CompoundItem, onlyMoveAble:false)` when the item is examined and has plates: header,
  then locked (soft) slots, then plates, each "class icon, slot name, Durability/MaxDurability".
  Only filled slots. `MultiLineRow.Show(null)` draws a separator line; Label and Value both go
  through `.Localized()`, which passes unknown text through (vanilla feeds it formatted text).
- Game strings are encrypted (`_F2C3._E000(n)`); loading Assembly-CSharp outside the game to
  decrypt them failed (type load), so vanilla's exact row format string is not known.
- The armor value text on the icon is the sum of every RepairableComponent in children
  (`Durability/MaxDurability`), bottom-right.
- `GridItemView.AddCustomObjectToInfoPanel` exists (destroys the object on dispose) but is not
  used: the gauge must outlive pool reuse.
- Prefab positions of InsuredIcon / RepairBuffIcon / ItemViewStats were **not** read (no UnityPy
  on the work machine), hence the runtime avoidance.

## What the first raid test has to look at

1. `BepInEx\LogOutput.log` has `Plate Durability UI 0.1.0 loaded.`, and no "Plate gauge failed".
2. Stash: a plate carrier with all plates, one with a plate pulled (dashed), a rig with plates, a
   plain rig (no gauge), a soft vest (no gauge), a helmet (no gauge).
3. Equipped body armor and rig slots: gauge bottom-left, not over the vanilla numbers.
4. An insured carrier: the gauge must sit right of the insured icon, not on it.
5. Take hits in raid with the inventory open: bars slide down. A plate shot to 0: striped.
6. Repair at a trader: bars rise, a dark wear cap appears on top.
7. Drag the item, examine an unknown carrier: gauge hides with the numbers.
8. F12 toggles take effect at once on open views.
9. With UI Fixes on (it patches item views heavily).
10. Turn on **Log armor readings** and check a modded carrier/plate pack if one is installed.
11. Tooltip: hover the gauge and the numbers on a carrier with an empty slot and a repaired
    plate. Rows aligned like vanilla? Class icons there? "Plates fitted" red when short? Hover a
    soft vest and a helmet: vanilla rows. No "Plate tooltip failed" in the log.
12. Clicking, dragging, right-clicking and Ctrl/Shift/Alt-clicking on the gauge itself must act
    on the item exactly as clicking anywhere else on it.
13. Armor in a raid crate: hidden until searched, gauge once searched (if the model is examined).
