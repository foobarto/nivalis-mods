# Contributor and agent instructions

Keep each mod under `mods/<ModName>/` and reusable C# helpers under
`shared/managed/`. Preserve plugin GUIDs, configuration paths, and receipt schema
compatibility unless an explicit migration is included.

- Use only original source and documentation. Do not commit game binaries,
  generated IL2CPP bindings, loader assemblies, saves, receipt histories, logs,
  personal paths, credentials, or private session notes. Keep build outputs ignored.
- Run the affected mod's checks. Build the plugin against a local game installation
  when changing game-facing code. Keep game assemblies as local dependencies.
- Record compatibility and behavior changes in the mod documentation and changelog.
  Separate logic checks, plugin loading, live gameplay, finance UI, and save/reload
  evidence. Do not claim a new game hash is verified from compilation alone.
- Before enabling hooks after an update, validate regenerated managed API bindings.
  Failed initialization must leave callbacks inactive and native gameplay available.
  API checks do not prove native behavior or prevent native crashes.
- Verify item identities and ownership before and after mutations. A failure can
  follow a committed harvest; never assume an exception means nothing changed.
  Recoverable unit failures should be isolated only when native post-state is known.
- Produce and planting material can be the same item type. Count shared identities
  once, verify actual planting consumption, and transfer only surviving fresh items.
- Keep committed receipt reporting independent from a sourcing-only failure. Do not
  fabricate missing history or alter the native save format to store optional reports.
- Preserve vendor fallback, inventory capacity checks, and same-crop replanting.
  Review substantial changes independently for ownership, compatibility, regressions,
  and test gaps before publishing.
