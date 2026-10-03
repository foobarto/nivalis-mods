# Boat Decor

Make your boat feel like home. Boat Decor lets you furnish the player boat with
items you already own, using Nivalis Nights' familiar decorating controls.

Add seating, lights, tables and other furniture to suitable boat surfaces. Rotate
and rearrange items as usual, then pick them up and use store or cancel placement
to put them back in your inventory. The mod also shows the native decorating and
store hints, using your existing key bindings.

## Features

- Decorate the docked player boat with purchased furniture from your inventory.
- Use the normal furniture menu, placement, rotation, pickup and storage controls.
- Place items on suitable surfaces, including previously attached decorations.
- Attach up to 128 decorations while retaining the game's collision and surface checks.
- No free items or changes to furniture prices.
- Designed to keep attached furniture aligned with the boat as it moves.

This mod adds decoration support. It does not turn the boat into a business,
farm or rentable apartment. Leave space around the helm, doors, bed and walkways.

## Requirements

- Nivalis Nights, Steam build **25680465**.
- **BepInEx 6 Unity IL2CPP for Windows x64**, tested with **6.0.0-be.788**.
- An unlocked boat and an owned apartment.

An apartment is required because the native furniture menu uses its category
filters. All items come from your player inventory; the mod does not take
furniture from your apartment. Farm First Manager Supply is not required.

This release is tied to the inspected game binary and disables itself on an
unsupported build. Other loader versions have not been verified.

## Installation

1. Back up your game saves and close the game.
2. Install BepInEx 6 Unity IL2CPP for Windows x64. Launch the game once to let
   BepInEx generate its bindings, then close it.
3. Extract the Boat Decor ZIP into the directory containing Nivalis Nights.exe.
4. Confirm the DLL is at BepInEx/plugins/NivalisBoatDecor/NivalisBoatDecor.dll.
5. Launch the game.

BepInEx is a separate requirement and is not included. For Steam/Proton, use the
loader's native winhttp override in Steam launch options:

`WINEDLLOVERRIDES="winhttp=n,b" %command%`

Loader setup: https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html

Loader downloads: https://builds.bepinex.dev/projects/bepinex_be

## How to decorate

Put furniture in your player inventory, dock your boat, board it and leave the
helm. Press your normal decorating shortcut, select an item and place it on a
suitable surface. Pick up a decoration to move it, or use store/cancel to return
it to inventory.

Editing requires docking. Stopping at sea with the engine off does not count as
docked. No new hotkeys are required.

## Early-release status

Version **0.1.3** has been tested in-game for menu opening, placement on the deck,
rearrangement, pickup, returning items to inventory, and the decorating/store hints.

**Sailing, boat recall, scene transitions and save/reload attachment persistence
have not yet completed end-to-end gameplay testing.** Start with one inexpensive
item and check those actions before decorating extensively. Keep a pre-mod backup.

## Saves and backups

Furniture objects are saved by the game. The mod stores additional boat attachment
metadata in BepInEx/config/cheeze.nivalis.boatdecor/attachments/.

Back up that folder together with your saves. Steam Cloud does not automatically
include this BepInEx folder. Attachment metadata is matched to the exact save
contents; missing or mismatched metadata will not be applied to another save.

## Uninstallation

Return every added decoration to inventory, save, then close the game. Remove
BepInEx/plugins/NivalisBoatDecor/. Keep the attachment metadata if you still need
older decorated saves.

Without the mod or matching metadata, furniture can remain at its saved world
position and will not follow the boat. Restore a pre-mod save for a clean rollback.

## Source and bug reports

Source code and releases: https://github.com/foobarto/nivalis-mods

When reporting a problem, include your game build, BepInEx version, whether the
boat was docked, the item involved, and the relevant Boat Decor log messages.
Remove personal information before sharing logs.
