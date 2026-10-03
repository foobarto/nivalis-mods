# Managed game adapters

Reusable C# helpers for BepInEx/IL2CPP plugins. Projects link the relevant source
files directly; these helpers have no Unity, BepInEx or proprietary game references.

`ItemTransfer.cs` moves an existing item between two inventory adapters. It checks
actual ownership after mutations, recognizes an exception after a successful
commit, and restores the source after rejection. Ambiguous or unrecoverable
ownership stops the operation. It cannot make an uncooperative game API atomic or
guarantee recovery after a process crash.

`MoveAvailable` fills destination capacity with an existing batch and calls a
commit callback only after ownership has moved. Unaccepted items remain at source.

`SaveSnapshotStore.cs` keeps optional reporting data outside a game's save format,
bound to the exact completed save bytes. Snapshots are written atomically, preserve
older hashes, and invalidate conflicting histories for identical bytes.
`SaveSnapshotLoad` stages history until a matching load completes; overlapping
unattributable callbacks quarantine history for the rest of the session. Native
save and load completion must be verified by each game adapter.

`ManagedApiCompatibility.cs` resolves compiled IL member references against loaded
assemblies before hooks are installed. Missing bindings fail initialization early.
This checks managed API compatibility, not native method behavior or crash safety;
adapters must catch initialization errors and make residual callbacks inert.

`IdentitySnapshot.cs` captures unique native item identities and rejects duplicates
instead of silently merging references. Use the same check before and after a
native mutation so duplicate references cannot fake a valid ownership delta.

`HarvestRecovery.cs` attempts one native replant using real owned seeds and
classifies its observed result. Success requires the original crop growing and
exactly one previously owned seed removed. Missing seeds or a rejected attempt
with unchanged ownership are local outcomes; API/snapshot exceptions and uncertain
mutations propagate. Callers isolate known local outcomes without discarding
reporting state, and prevent repeated harvesting of an affected unit.

`HarvestBatch.cs` validates separate or shared crop/planting item types. Shared
types count native yield and planting returns together, then allow one verified
planting consumption. Delivery candidates include only newly created identities
still owned after replant, even when planting spends an original player item.
Count mismatches are local outcomes; ownership corruption is rejected.

First adapter: `mods/NivalisFarmSupply/Plugin.cs`. Checks live in that mod's
`tests` project and include partial mutation and restoration failure cases.

`RelativeAttachmentStore.cs` validates bounded object identities and relative poses
for moving-object decorations. Snapshot restore is transactional; malformed,
duplicate, nonfinite and oversized data is rejected. Boat Decor uses this with
exact-save snapshots; its tests cover both helpers.
