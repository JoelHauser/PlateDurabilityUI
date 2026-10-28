# Plate Durability UI -- working notes for Claude

Body armor and armored rigs get a small gauge on their item icon: one bar per plate slot (front,
back, left, right), slot letters, and a thin bar for the weakest soft insert. Client only, one
BepInEx plugin. Repo https://github.com/JoelHauser/PlateDurabilityUI.git. Idea from UI Fixes
issue tyfon7/UIFixes#112, whose author called a per-plate overlay out of scope.

Joel's brief (2026-10-27): it must look like it shipped with EFT, polished, and work with plates
added by any other mod. Helmets are out for v1. Design mockup (bars style chosen, continuous
colour ramp, icon only): https://claude.ai/artifact/XZUGkBPaihU8Y9xcCn8RfJ

## State as it stands

**0.0.2 released 2026-10-27 as a GitHub pre-release** (built on the home machine against
`H:\SPT4.1.X`, installed there). In it, beyond 0.0.1: the Armor Class Icon placement fix, five
styles, per-screen switches, corner choice, class labels and the raid break message. Joel ran
the styles in game and said "looks good". **Not yet run in game:** the corners, per-screen
switches, class labels and the raid message (tests 15-18 below). The 0.0.1 pre-release zip
still has a README inside; 0.0.2's does not.

**0.0.3 released 2026-10-28 as a GitHub pre-release**, built on the work machine with
`scripts\pack.ps1 -SPTPath <mirror>`, where the mirror is `BepInEx\core` and
`EscapeFromTarkov_Data\Managed` copied from `C:\HUH` with Assembly-CSharp patched by SPT's
HDiffPatch delta (`C:\HUH\SPT_Runtime\SPT_Data\Launcher\Patches\SPT-core\...\Assembly-CSharp.dll.delta`,
applied with the NuGet package SharpHDiffPatch.Core) and the original kept as `.spt-bak`.
`ilspycmd -il` of the result showed no `_E…`/`_F…` names. In it: the tooltip fix (test 19), the tooltip no longer tied to "Show plate
gauge", the plugin switching itself off when its patches fail, and cleanup. Never run in game.

The source names **no obfuscated type and no member that is only public after SPT's
publicizer**, which is why the same source compiles against both the unpatched (`_E371`,
`_EEA7`, ...) and the SPT-remapped assembly. Keep it that way: reach obfuscated types only
through named members (`Slot`, `ArmorComponent`, `ArmorHolderComponent`, `RepairableComponent`,
`CompoundItem.Slots/Grids`, `view.ItemContext.ViewType`) and `IEnumerable<Slot>` covariance;
remapped types by reflection (`NotificationManager`); private-in-vanilla fields with
`AccessTools.FieldRefAccess` (`GridItemView.Caption/TagName`). `Player.Equipment` failed this
(not public in vanilla): use `player.Profile.Inventory.Equipment`.

**Check it on the home machine** without a work machine: build a copy of the sources against a
scratch "install" whose `Assembly-CSharp.dll` is `H:\SPT4.1.X\...\Assembly-CSharp.dll.spt-bak`,
with `-p:CompileCheckOnly=true` (copy the sources: pointing obj/out elsewhere from the repo
duplicates the generated AssemblyInfo). A DLL compiled that way carries obfuscated names and must
never ship.

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
- **Styles** (Joel asked 2026-10-27, after the 0.0.1 black panel "didn't look right" next to
  Armor Class Icon's badges, for all five mockups as an option; mockup canvas
  https://claude.ai/artifact/X93myK4EA4LrBdRcy9kDkb): `3. Look > Style` = Smoke (default, my
  pick, not Joel's), Classic (0.0.1), Bare, Silver, Slim. `GaugeLook.cs` holds each one's sizes,
  sprites and colours; `PlateGauge.UseLook/Restyle` apply them, cached per bar so a restyle only
  happens on change. Panel is a child of the body (so Silver's drop shadow can sit behind it);
  in Bare/Slim it is clear but still the raycast target for the hover. Letter shadows are a
  second TMP copy, not an underlay (no font material instance). Slim never draws letters. The
  tooltip numbers keep the Classic ramp in every style (`Sprites.Ramp`).
- **Options added in 0.0.2** (Joel 2026-10-27):
  - Per-screen switches by `view.ItemContext.ViewType`, grouped as Armor Class Icon does: flea =
    Ragfair/NewOffer, traders = TradingTrader/TradingSell/TradingPlayer, else inventory.
  - Corner: `PlaceTop` pushes the gauge down below every Image (not backdrops) and TMP text (by
    `textBounds`) in the top half over its columns: the info panel's graphics plus `Caption`
    (name) and `TagName`. `SettleLayout` rebuilds every layout group under the info panel and,
    for top corners, force-updates those texts' meshes first.
  - Slot labels: letters or `ArmorComponent.ArmorClass`.
  - **PlateAlerts**: polls `MainPlayer`'s ArmorVest and TacticalVest every 0.5 s through
    PlateReader (`anyArmor: true`, ignoring the gauge's rig/armor switches). A Fitted -> Broken
    change of a known plate (key item id + slot id) shows
    `NotificationManager.DisplayMessageNotification(..., Alert)`. A new MainPlayer clears what
    it has seen. Any exception turns it off for the session, logged once.
- **PlateGauge**: a child of `_infoPanel` named `PlateDurabilityGauge`, made once per view and
  kept through the view pool; UpdateInfo re-points it or deactivates it. Polls its item every
  0.25 s (unscaled) while active to catch damage taken with the inventory open. Only the backing
  panel is a raycast target (for the hover, off with "Hover the gauge for details"); it has no
  click/drag handlers, so those bubble up to the ItemView. Letters use the font of
  `BottomPanel.ItemValue` (the game's own). Sprites are procedural, point-filtered (Sprites.cs).
- **Placement**: bottom-left of the view's rect, pushed right past any active small Image under
  BottomPanel in the left half (mod icons, class icon; other mods' icons too), and shrunk (down to
  0.6) rather than run into `ItemValue`'s rendered text. The first Place after each Apply runs
  `LayoutRebuilder.ForceRebuildLayoutImmediate(BottomPanel)` and `ItemValue.ForceMeshUpdate()`
  first: those icons sit in layout groups that Unity only positions at canvas rebuild (after
  LateUpdate), so without it the gauge landed on them and jumped clear on the next 0.25 s poll.
- **Armor Class Icon (slpf/ArmorClassIcon) compatibility** (Joel asked 2026-10-27: start beside its
  badge, not on it and then move). Its badge is the game's own `ItemViewStats._armorClassIcon`
  turned on (plus clones in the same parent for multi-badge modes), set in both
  `ItemViewStats.SetStaticInfo` and `GridItemView.UpdateInfo` postfixes, both before our
  LateUpdate, so the panel scan finds it once the layout is forced. On views with no class icon
  slot it adds `ArmorClassBadge` Images straight under the view; Place avoids those too. No
  reference to the mod, no load-order dependency.
- Setting change -> `FindObjectsOfType<GridItemView>()` and `UpdateInfo()` on each active one.
- **PlateTooltip** (Joel asked 2026-10-27 for a fuller hover panel in the vanilla style): a
  prefix on `MultiLineTooltip.Show(_F1DC)` (found by shape: one param whose type has a `Lines`
  field; prefix takes `object __0`) replaces `Lines` (readonly, set by reflection) with: vanilla
  header row, separator (null row), one row per plate slot in gauge order incl. empty, separator,
  every soft insert, separator, "Plates fitted n/m". Row = Label "class icon + slot
  LocalizedName()", Value "dur/full pct%" in the ramp colour, "Broken", "Empty", "max N" wear.
  Icon from the public static `FormatArmorClassIcon(int)` found by name over all types. Only
  when `_subject` (set by a GridItemView.OnPointerEnter prefix, cleared on exit, and dropped if
  its view is no longer active or showing it: a view closed under the pointer never gets its
  exit) reads as gauge-worthy armor, **and** the Show's first row has the same Label as the
  first row `_F1DC(subject, false)` builds. `_F1DC` is shared: trader price/charisma, ammo
  penetration, skill-change costs, `TradingPlayerItemView`'s unsellable-parts list (same
  "label, '' + separator" shape, item name as header) and the flea's `onlyMoveAble: true`
  plates all go through the same Show and must stay vanilla. Independent of "Show plate gauge".
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
- Prefabs read 2026-10-27 with UnityPy from `H:\SPT4.1.X\EscapeFromTarkov_Data\resources.assets`
  (scripts resolve through `globalgamemanagers.assets`; MonoBehaviour typetrees are stripped, so
  only names and RectTransforms read). Grid, trading and slot item views alike: `Info Panel` >
  `ItemViewBottomLayoutGroup` (ItemViewBottomPanel, HorizontalLayoutGroup, offset 3,3) >
  `BottomLeftLayoutGroup` (VerticalLayoutGroup + ItemViewStats: WishlistIcon, **ArmorClassIcon**
  18x18, ModIconContainer, Special Icon, Inscription), `LyoutBreaker`, `BottomRightLayoutGroup`
  (VerticalLayoutGroup: RepairRequiredIcon, QuestItem, **InsuranceIcon, RepairBuffIcon**, Value).
  So insured/repair-buff icons are on the right, stacked above the numbers, not bottom-left.

## What the first raid test has to look at

1. `BepInEx\LogOutput.log` has `Plate Durability UI 0.0.3 loaded.`, and no "Plate gauge failed".
2. Stash: a plate carrier with all plates, one with a plate pulled (dashed), a rig with plates, a
   plain rig (no gauge), a soft vest (no gauge), a helmet (no gauge).
3. Equipped body armor and rig slots: gauge bottom-left, not over the vanilla numbers.
4. An insured carrier on a small rig: the insured icon stacks above the numbers on the right;
   the gauge must not run into it.
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
14. With Armor Class Icon: open the stash, scroll, open a trader. The gauge must appear right of
    the class badge at once, never on it first. Try its "All Plates" and "Lowest & Highest" modes.
15. Each Style from F12 with the inventory open: switches at once, on light and on black armor.
    Silver's rounded corners and shadow, Smoke/Bare letter shadows, Slim with no letters, the
    hover tooltip still opening from the gauge in Bare and Slim, empty and broken slots in each.
16. Corner TopLeft and TopRight: in the stash (under the item name top right, under a quick-slot
    number top left on an equipped rig), at a trader (under the price), on a tagged item. No
    jump on first draw.
17. The three per-screen switches, and Slot labels ArmorClass (class digits; "-" in an empty slot).
18. Raid: get a plate shot to zero while wearing it. One message "<slot> broken (<item>)", top
    right; none at raid start for armor already broken; none for a rig's plate when only body
    armor is on. No "Plate break messages are off" in the log.
19. Tooltips that share the window stay vanilla (fixed after 0.0.2): at a trader, hover your own
    carrier holding a part the trader will not buy (the "unsellable parts" list, item name on
    top), a trader price with a charisma discount, an ammo penetration tooltip; on the flea, the
    removable-plates tooltip. Sell a hovered carrier with Ctrl-click, then hover a price.
