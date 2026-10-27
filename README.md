# Plate Durability UI

See the condition of every armor plate at a glance, right on the item icon.

Body armor and armored rigs get a small gauge in the corner of their icon: one bar per plate
slot, always in the order front, back, left, right, and a thin bar underneath for the built-in
soft armor. A worn, broken or missing plate shows the moment you look at the item, in your
stash, on your character, in a corpse or at a trader. No hovering, no opening the item.

## Reading the gauge

- **Bar height and colour** follow the plate's durability exactly: green when new, amber at
  half, red when nearly gone, with every shade in between.
- **Striped red** is a broken plate: in the slot, at zero, protecting nothing.
- **Dashed outline** is an empty plate slot.
- **Dark cap on top of a bar** is durability lost to repairs for good, the same idea as the wear
  segment on the weapon durability bar.
- **Thin bar at the bottom** is the weakest of the soft armor inserts (front, back, sides,
  collar, groin).

## The plate tooltip

Point at the gauge (or at the durability numbers, as in vanilla) for the game's own plate
tooltip, filled out: every plate slot in front, back, left, right order, empty ones included,
then every soft insert, each with its armor class, exact durability and a percentage in the
gauge's colour. Broken plates and durability lost to repairs are called out, and the last row
tells you how many plate slots are filled.

## In raid

When a plate in the armor or rig you are wearing is shot to zero, the game's own message in the
top right tells you which one: "Front plate broken (Kirasa-N)". Plates that were already broken
when the raid started stay quiet.

## Settings

Press F12 in game (BepInEx Configuration Manager).

| Setting | Default | |
| --- | --- | --- |
| Show plate gauge | on | |
| On body armor | on | |
| On armored rigs | on | |
| In the stash and inventory | on | Everywhere that is not a trader or the flea market. |
| At traders | on | |
| On the flea market | on | |
| Show soft armor bar | on | |
| Show slot letters | on | A label under each bar. |
| Slot labels | Letters | Letters: F, B, L, R. ArmorClass: each plate's class. |
| Detailed plate tooltip | on | Off: the vanilla tooltip rows. |
| Hover the gauge for details | on | Off: only the durability numbers open the tooltip, as in vanilla. |
| Full bar means | OriginalMaximum | OriginalMaximum: a full bar is the plate as new, the scale the game's protection maths uses. CurrentMaximum: full is the maximum left after repairs. |
| Show repair wear | on | The dark cap for durability lost to repairs. |
| Hide on healthy armor | off | Only show the gauge once something needs attention. |
| Healthy above (%) | 90 | Used by the setting above. |
| Style | Smoke | Smoke, Classic, Bare, Silver or Slim. |
| Corner | BottomLeft | BottomLeft, TopLeft or TopRight. It keeps clear of the game's icons and text there. |
| Size | 1.0 | It also shrinks by itself on small items. |
| Animate changes | on | Bars slide when a plate is hit, repaired or swapped. |
| Message when a plate breaks | on | The raid message above. |
| Log armor readings | off | For bug reports: writes each armor's slots to `BepInEx\LogOutput.log`. |

## Install

Unzip over your SPT folder so `PlateDurabilityUI.dll` lands in `BepInEx\plugins`. Client only,
no server part. Built for SPT 4.1.x.

## Good to know

- **Modded plates and armor work.** The gauge reads whatever plate slots an item has, the same
  way the game decides what is armor, so plates and carriers from other mods show up without
  any patch or list.
- Armor found in raid works the same: in crates, bags, corpses and stashes. As in vanilla, an
  item shows nothing until the container is searched, and no numbers (so no gauge) until you
  have examined that model.
- **Works next to Armor Class Icon.** The gauge sits beside its class badges, never on them.
- Helmets are left out for now.
- Unexamined items get no gauge, the same as their numbers show "?".
- Nothing about the items changes. The gauge only reads them.

## Building

```
scripts\pack.ps1 -SPTPath H:\SPT4.1.X            # build and zip into dist\
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install   # also copy into BepInEx\plugins
```
