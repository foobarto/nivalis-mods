# Boat Decor

Decorate your boat in **Nivalis Nights** with furniture you already own. Use the
normal furniture menu, placement, rotation, pickup and storage controls.

Version **0.1.3** is the first public test release.

## Features

- Place owned furniture on the docked player boat, including suitable surfaces
  on previously attached decorations.
- Pick up and rearrange decorations, or cancel/store them back into player inventory.
- Native decorating and store hints retain the game's bindings and localization.
- Native collision and surface rules remain active. Up to 128 decorations can be attached.
- Attachment tracking is designed to keep furniture aligned with the moving boat.
- No free furniture, replacement game assets or native save-format changes.

The mod does not supply business, farming or rental systems on the boat.
Keep the helm, doors, bed and walkways accessible.

## Requirements and compatibility

- Nivalis Nights **Steam build 25680465**.
- **BepInEx 6 Unity IL2CPP, Windows x64**, tested with **6.0.0-be.788**.
- An unlocked boat and an owned apartment. The apartment supplies furniture-menu
  category filters; furniture comes from the player's inventory.

The plugin accepts only GameAssembly.dll SHA256
`0da6aac5209f504da743abd7926f6f528010e2ca7b884b4a20f5198f42f1a26d`.
It refuses other binaries until compatibility is checked. Passing managed API
checks alone does not establish support for a new game or loader version.
Farm First Manager Supply is not required.

## Installation

1. Back up your saves. Close the game.
2. Install the required BepInEx loader using the
   [official IL2CPP guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)
   and [bleeding-edge downloads](https://builds.bepinex.dev/projects/bepinex_be).
   Launch once to generate bindings, then close the game.
3. Extract the mod ZIP into the game directory containing `Nivalis Nights.exe`.
   The DLL belongs at `BepInEx/plugins/NivalisBoatDecor/NivalisBoatDecor.dll`.
4. Launch and check `BepInEx/LogOutput.log` for `Boat Decor 0.1.3 ready`.

For Steam/Proton, the Windows loader needs the native `winhttp` override:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

The package contains only this mod, its documentation and license. BepInEx must
be installed separately. To disable the mod, set `General.Enabled = false` in
`BepInEx/config/cheeze.nivalis.boatdecor.cfg` and restart.

## Use

1. Put purchased furniture in your player inventory.
2. Dock and board the boat, then leave the helm. Engine-off at sea does not count
   as docked for editing.
3. Use the normal decorating shortcut shown in the HUD.
4. Select an item, aim at a suitable surface, rotate and place it normally.
5. Pick up an attached item to rearrange it. Use store or cancel/close placement
   to return it to inventory. If native storage fails, it remains held and the
   menu stays open.

Placement, pickup and rearrangement require the boat to be docked. The apartment
is a menu context only: no furniture is taken from a furnished apartment.

## Saves, backups and removal

Native saves contain furniture objects and their world positions. Extra attachment
metadata is stored under:

`BepInEx/config/cheeze.nivalis.boatdecor/attachments/`

Back up this directory together with your saves. Metadata is matched to the exact
completed save contents. Older saves can use their matching metadata; missing,
conflicting or malformed metadata is not applied to unrelated saves. Steam Cloud
does not automatically synchronize this BepInEx folder.

Before uninstalling or disabling, return all added furniture to inventory, save
and exit. Remove only `BepInEx/plugins/NivalisBoatDecor/`. Retain metadata if older
decorated saves are still needed. Without the mod or matching metadata, furniture
may remain at saved world positions and will not follow the boat. A pre-mod save
backup provides a clean rollback.

## Validation and limitations

Confirmed in-game: loader startup, furniture menu, deck placement, rearrangement,
pickup, cancel/store back to inventory, and decorating/store hints.

**Sailing, boat recall, scene transitions and save/reload attachment persistence
have not yet completed end-to-end gameplay validation.** This is an early test
release. Test one inexpensive item through those actions before furnishing a
boat extensively; keep paired save and metadata backups.

The plugin builds against the supported game and loader without warnings. Its
52 pure checks cover attachment validation and exact-save snapshot handling; these
checks do not simulate Unity physics, UI or native save callbacks.

## Build and checks

Install the .NET 10 SDK. The plugin targets the loader's .NET 6 runtime and uses
local game/loader assemblies after BepInEx has generated bindings:

```sh
dotnet build mods/NivalisBoatDecor/NivalisBoatDecor.csproj -c Release -p:GamePath="/path/to/Nivalis Nights"
dotnet run --project mods/NivalisBoatDecor/tests/Tests.csproj -c Release
```

Output: `mods/NivalisBoatDecor/bin/Release/net6.0/NivalisBoatDecor.dll`.
Do not redistribute the locally referenced game or loader assemblies.

Shared metadata helpers live in `shared/managed/`. Boat-specific ownership checks,
scoped placement masks, relative transforms and native UI hooks stay in the plugin.

## License

Original code is available under the repository's MIT license. See
[CHANGELOG.md](CHANGELOG.md) for versions and [NEXUS_DESCRIPTION.md](NEXUS_DESCRIPTION.md)
for the listing description.
