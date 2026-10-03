using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Cheeze.Managed;
using HarmonyLib;
using Nivalis;
using Nivalis.Apartment;
using Nivalis.Boat;
using Nivalis.GhostSystem;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.Locale;
using Nivalis.UI;
using UnityEngine;

namespace NivalisBoatDecor;

[BepInPlugin(Id, "Boat Decor", "0.1.3")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "cheeze.nivalis.boatdecor";
    private const string SupportedBinary = "0DA6AAC5209F504DA743ABD7926F6F528010E2CA7B884B4A20F5198F42F1A26D";
    internal static Plugin Instance = null!;
    private ConfigEntry<bool> enabled = null!;
    private readonly RelativeAttachmentStore attachments = new();
    private readonly HashSet<string> authorized = new(StringComparer.Ordinal);
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelativeAttachment> pendingPlacement = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IntPtr View, IntPtr Boat, bool Editable)> locks = new(StringComparer.Ordinal);
    private readonly SaveSnapshotLoad loading = new();
    private SaveSnapshotStore snapshots = null!;
    private PlacementModeUi? editor;
    private PlacementSystem? placement;
    private ItemStack? selecting;
    private int selectionCount;
    private HoldableEntity? pendingOwnedEntity;
    private int pendingOwnedInstance;
    private IntPtr pendingOwnedType;
    private string? pendingLoad;
    private IntPtr world;
    private bool ready, editFault, previewRunning, surfaceRunning, storeRunning, hintFault;
    private int boatHintMode = -1;
    private IntPtr hintHud;
    private const int BoatSurfaceBit = 1 << 22; // Inspected PlayerBoat hull/deck collider layer.
    private Il2CppSystem.Action<HoldableEntity> placed = null!;
    private Il2CppSystem.Action afterLoad = null!;

    public override void Load()
    {
        Instance = this;
        enabled = Config.Bind("General", "Enabled", true, "Allow owned furniture to be placed on the docked player boat. Restart after changing.");
        if (!enabled.Value) return;
        var harmony = new Harmony(Id);
        try
        {
            using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
            using var sha = SHA256.Create();
            if (Convert.ToHexString(sha.ComputeHash(stream)) != SupportedBinary)
                throw new NotSupportedException("Boat Decor requires the inspected build 25680465; no hooks installed on another binary.");
            ManagedApiCompatibility.Validate(typeof(Plugin).Assembly);
            snapshots = new SaveSnapshotStore(Path.Combine(Paths.ConfigPath, Id, "attachments"));
            Patch(harmony, typeof(PlacementModeUi), "OnPlacementMode", nameof(OpenBoatEditor));
            Patch(harmony, typeof(PlacementModeUi), "Close", nameof(BeforeEditorClose), nameof(EditorClosed));
            Patch(harmony, typeof(PlacementModeUi), "ClearSelection", nameof(ClearBoatSelection));
            Patch(harmony, typeof(PlacementModeUi), "OnStore", nameof(StoreBoatSelection));
            Patch(harmony, typeof(PlacementModeUi), "OnObjectPickup", null, nameof(AfterBoatPickup));
            Patch(harmony, typeof(HeadsUpDisplayUI), "RefreshFurnitureHotkeys", null, nameof(BoatHints));
            Patch(harmony, typeof(HeadsUpDisplayUI), "Update", null, nameof(UpdateBoatHints));
            Patch(harmony, typeof(PlacementSystem), "DoPlacement", nameof(BeforePlacement));
            Patch(harmony, typeof(HoldableEntity), "PickUp", nameof(BeforePickup));
            Patch(harmony, typeof(FurnitureObject), "set_CurrentProperty", null, nameof(AfterFurnitureLock));
            Patch(harmony, typeof(PlacementModeUi), "SelectItem", nameof(BeforeSelect), nameof(AfterSelect));
            Patch(harmony, typeof(PlayerObjectHolder), "HoldObject", null, nameof(AfterHold));
            Patch(harmony, typeof(PlacementSystem), "UpdateHeldObjectPlacementPosition", nameof(BoatPreview));
            Patch(harmony, typeof(PlacementSetting), "CheckSurfaceValid", nameof(BoatSurfaceMask));
            foreach (string name in new[] { "CheckValidProperty", "CheckParentValid", "CheckParentMessageValid" })
                Patch(harmony, typeof(HoldableEntity), name, nameof(AllowBoatSurface));
            Patch(harmony, typeof(FurnitureObject), "SetLock", null, nameof(AfterFurnitureLock));
            Patch(harmony, typeof(FurnitureObject), "get_IsIllegal", nameof(OwnedBoatFurniture));
            Patch(harmony, typeof(PlayerObjectHolder), "StoreEntity", nameof(BeforeStore), nameof(AfterStore));
            Patch(harmony, typeof(SerializationManager), "Save", nameof(BeforeSave), nameof(AfterSave));
            Patch(harmony, typeof(SerializationManager), "Load", nameof(BeforeLoad));
            Patch(harmony, typeof(SerializationManager), "Clear", null, nameof(ClearWorld));
            placed = (Il2CppSystem.Action<HoldableEntity>)(Action<HoldableEntity>)OnPlaced;
            afterLoad = (Il2CppSystem.Action)(Action)AfterLoaded;
            PlacementSystem.OnObjectPlace.Add(placed);
            SerializationManager.OnPostLoad.Add(afterLoad);
            AddComponent<BoatDecorUpdate>();
            ready = true;
            Log.LogInfo("Boat Decor 0.1.3 ready. Board and dock your boat, then use the normal decorating shortcut. Own an apartment for the native furniture menu. Runtime placement and save/reload validation required.");
        }
        catch (Exception e)
        {
            ready = false;
            harmony.UnpatchSelf();
            Log.LogError("Boat Decor could not initialize; inactive. " + e);
        }
    }

    private static void Patch(Harmony h, Type type, string name, string? prefix = null, string? postfix = null)
    {
        var method = AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
        h.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(Plugin), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
    }
    private bool CanEdit => ready && !editFault && !loading.Active && !loading.Quarantined;
    private static PlayerManager.Player? Player => Singleton<PlayerManager>.InstanceExist() ? Singleton<PlayerManager>.Instance.LocalPlayer : null;
    private static Ghost? EntityGhost(HoldableEntity entity) => entity.GetComponent<IGhostView>()?.BaseGhost;
    private bool Known(HoldableEntity entity)
    {
        PromoteOwnedSelection();
        var ghost = EntityGhost(entity);
        return ghost != null && (authorized.Contains(ghost.Id) ||
            (attachments.TryGet(ghost.Id, out var a) && a!.PrefabId == ghost.PrefabId));
    }
    private void ClearOwnedSelection()
    {
        pendingOwnedEntity = null;
        pendingOwnedInstance = 0;
        pendingOwnedType = IntPtr.Zero;
    }
    private void PromoteOwnedSelection()
    {
        if (!CanEdit) { ClearOwnedSelection(); return; }
        try
        {
            if (pendingOwnedEntity == null) { ClearOwnedSelection(); return; }
            var held = Player?.Character?.ObjectHolder?.HeldObject;
            if (held == null || held.Pointer != pendingOwnedEntity.Pointer ||
                held.GetInstanceID() != pendingOwnedInstance || held.ItemType.Pointer != pendingOwnedType)
            { ClearOwnedSelection(); return; }
            // SelectItem instantiates and holds the view before Unity Start creates
            // its ghost. Preserve the proven inventory receipt across that boundary.
            var ghost = EntityGhost(held);
            if (ghost == null || !ghost.IsValid) return;
            if (ghost.IsBakedInstance) { ClearOwnedSelection(); return; }
            authorized.Add(ghost.Id);
            ClearOwnedSelection();
            Log.LogInfo("Owned furniture initialized for boat placement: " + ghost.Id);
        }
        catch (Exception e)
        {
            // A dying preview must never stop existing decorations following the boat.
            ClearOwnedSelection();
            EditFailure(e);
        }
    }
    private static BoatController? EditableBoat()
    {
        var boat = BoatController.Instance;
        var g = boat == null ? null : boat.MyGhost;
        var character = Player?.Character;
        // BoatGhost.Driven records first use; cockpit exit never clears it.
        // DrivenBoat is the character's current helm assignment and clears on exit.
        return g != null && g.Unlocked && g.Boarded && g.Docked &&
            character != null && character.DrivenBoat == null ? boat : null;
    }
    private void Report(string key, string message)
    {
        if (reported.Add(key)) Log.LogWarning(message);
    }
    private void EditFailure(Exception e)
    {
        editFault = true;
        Report("edit", "Boat editing disabled until reload after an error; existing attachments continue syncing. " + e);
    }

    private static bool OpenBoatEditor(PlacementModeUi __instance)
    {
        var m = Instance;
        if (__instance.IsOpen) return true;
        if (!m.CanEdit)
        {
            m.Report("menu-unavailable", "Boat menu unavailable: editing is not ready, loading, or disabled after an error. Check earlier Boat Decor messages.");
            return true;
        }
        try
        {
            if (EditableBoat() == null)
            {
                var g = BoatController.Instance?.MyGhost;
                if (g != null && g.Boarded)
                    m.Report("menu-boat-state", "Boat menu blocked: requires an unlocked, docked boat and a player who has left the helm. " +
                        "Unlocked=" + g.Unlocked + ", Docked=" + g.Docked +
                        ", Character=" + (Player?.Character != null) +
                        ", AtHelm=" + (Player?.Character?.DrivenBoat != null));
                return true;
            }
            var p = Player;
            if (p == null || p.Character == null || p.IsHoldingObject || p.Character.MyGhost.Sitting)
            {
                m.Report("menu-player-state", "Boat menu blocked: stand up and finish holding or placing the current object first.");
                return true;
            }
            var input = Singleton<PlayerInputManager>.Instance;
            if (input.PreventAllMenuShortcuts.Value || input.PreventGameplayMenuShortcuts.Value)
            {
                m.Report("menu-shortcuts", "Boat menu blocked by the game's menu shortcut lock.");
                return true;
            }
            var context = OwnedApartment();
            if (context == null)
            {
                m.Report("home", "The native furniture menu needs an owned apartment as its category context.");
                return true;
            }
            // The native menu lists the player's inventory. The apartment supplies
            // category filters only: no rental state or property inventory is altered.
            m.editor = __instance;
            __instance.Open(context, null, null);
            m.Log.LogInfo("Boat furniture menu opened using player-owned inventory.");
            return false;
        }
        catch (Exception e) { m.EditFailure(e); return true; }
    }

    private static BaseProperty? OwnedApartment()
    {
        var p = Player;
        if (p == null) return null;
        var properties = new Il2CppSystem.Collections.Generic.List<BaseProperty>();
        p.GetOwnedProperties(properties);
        foreach (var property in properties)
            if (property != null && property.TryCast<Apartment>() != null && property.PlayerOwned) return property;
        return null;
    }
    private bool OwnsHeldBoatItem()
    {
        if (!CanEdit || EditableBoat() == null) return false;
        var entity = Player?.Character?.ObjectHolder?.HeldObject;
        if (entity == null || !entity.ItemType.IsFurniture || !Known(entity) || !CanEdit) return false;
        var ghost = EntityGhost(entity);
        return ghost != null && ghost.IsValid && !ghost.IsBakedInstance &&
            (authorized.Contains(ghost.Id) ||
             (attachments.TryGet(ghost.Id, out var a) && a!.ParentId == EditableBoat()!.MyGhost.Id));
    }
    // null: leave native behavior alone; false: native storage failed, retain UI/item.
    private bool? StoreHeldBoatItem()
    {
        if (storeRunning) return false;
        try
        {
            if (!OwnsHeldBoatItem()) return null;
            storeRunning = true;
            bool result = Player!.Character.ObjectHolder.StoreEntity();
            if (result) Log.LogInfo("Boat furniture returned to player inventory by native storage.");
            else Report("store-refused", "Native storage refused this boat item; it remains held. Place it again or retry storage.");
            return result;
        }
        catch (Exception e) { EditFailure(e); return false; }
        finally { storeRunning = false; }
    }
    private static bool BeforeEditorClose() => Instance.StoreHeldBoatItem() != false;
    private static bool ClearBoatSelection() => Instance.StoreHeldBoatItem() == null;
    private static bool StoreBoatSelection(PlacementModeUi __instance)
    {
        if (!__instance.IsOpen) return true;
        var result = Instance.StoreHeldBoatItem();
        if (result == null) return true;
        if (result == true) __instance.Close();
        return false;
    }
    private static void AfterBoatPickup(PlacementModeUi __instance, HoldableEntity obj)
    {
        var m = Instance;
        try
        {
            if (__instance.IsOpen || !m.OwnsHeldBoatItem()) return;
            var held = Player?.Character?.ObjectHolder?.HeldObject;
            if (obj == null || held == null || held.Pointer != obj.Pointer) return;
            var context = OwnedApartment();
            if (context == null) return;
            m.editor = __instance;
            __instance.OpenPickup(context, obj.ItemType);
            __instance._currentlyPlacingObject = obj;
        }
        catch (Exception e) { m.EditFailure(e); }
    }
    private int BoatHintMode()
    {
        if (!CanEdit || hintFault || EditableBoat() == null) return 0;
        var p = Player;
        if (p?.Character == null || p.Character.MyGhost.Sitting) return 0;
        if (p.IsHoldingObject) return editor != null && editor.IsOpen && OwnsHeldBoatItem() ? 2 : 0;
        if (!p.Character.Interaction.CanInteract) return 0;
        var input = Singleton<PlayerInputManager>.Instance;
        return input.PreventAllMenuShortcuts.Value || input.PreventGameplayMenuShortcuts.Value ? 0 : 1;
    }
    private static void BoatHints(HeadsUpDisplayUI __instance)
    {
        var m = Instance;
        if (m.hintFault) return;
        try
        {
            int mode = m.BoatHintMode();
            if (mode == 0) return;
            if (mode == 1) __instance.placementModeHotkey.SetActive(true);
            else __instance.storeHotkey.SetActive(true);
            __instance._areItemHotkeysActive = true;
            __instance.ToggleHotkeys(true);
        }
        catch (Exception e) { m.hintFault = true; m.Report("hud", "Boat hints disabled after an error: " + e); }
    }
    private static void UpdateBoatHints(HeadsUpDisplayUI __instance)
    {
        var m = Instance;
        if (!m.ready || m.hintFault) return;
        try
        {
            int mode = m.BoatHintMode();
            if (mode == m.boatHintMode && m.hintHud == __instance.Pointer) return;
            m.boatHintMode = mode;
            m.hintHud = __instance.Pointer;
            // Native refresh restores ordinary hints when boat eligibility ends.
            __instance.RefreshFurnitureHotkeys();
        }
        catch (Exception e) { m.hintFault = true; m.Report("hud", "Boat hints disabled after an error: " + e); }
    }

    private static bool BeforeSelect(PlacementModeUi __instance, ItemStack itemToSelect)
    {
        var m = Instance;
        if (m.StoreHeldBoatItem() == false) return false;
        m.selecting = null;
        m.ClearOwnedSelection();
        if (!m.CanEdit || m.editor == null || m.editor.Pointer != __instance.Pointer) return true;
        try
        {
            if (EditableBoat() == null || itemToSelect == null || !itemToSelect.Type.IsFurniture) return true;
            var p = Player;
            if (p == null) return true;
            if (p.Inventory.Items.ContainsSpecificStack(itemToSelect) && itemToSelect.StackCount > 0)
            { m.selecting = itemToSelect; m.selectionCount = itemToSelect.StackCount; }
        }
        catch (Exception e) { m.EditFailure(e); return false; }
        return true;
    }
    private static void EditorClosed(PlacementModeUi __instance)
    { if (!__instance.IsOpen && Instance.editor?.Pointer == __instance.Pointer) Instance.editor = null; }
    private static void AfterSelect() => Instance.selecting = null;
    private static void AfterHold(HoldableEntity entity, bool __result)
    {
        var m = Instance;
        if (!m.CanEdit || !__result || m.selecting == null) return;
        try
        {
            // Trust only a successful native removal of one owned item followed by
            // a successful native HoldObject of that exact item type.
            if (m.selecting.StackCount != m.selectionCount - 1 || entity.ItemType.Pointer != m.selecting.Type.Pointer) return;
            m.pendingOwnedEntity = entity;
            m.pendingOwnedInstance = entity.GetInstanceID();
            m.pendingOwnedType = entity.ItemType.Pointer;
            m.PromoteOwnedSelection();
            if (m.pendingOwnedEntity != null)
                m.Report("ghost-wait", "Owned furniture receipt retained; waiting for native ghost initialization.");
        }
        catch (Exception e) { m.EditFailure(e); }
    }
    private static bool BoatPreview(PlacementSystem __instance, Ray ray, ref bool __result)
    {
        var m = Instance;
        if (!m.CanEdit || m.previewRunning) return true;
        try
        {
            var entity = Player?.Character?.ObjectHolder?.HeldObject;
            if (EditableBoat() == null || entity == null || !m.Known(entity) || !m.CanEdit) return true;
            var settings = entity.PlacementSettings;
            int oldHit = __instance.hitLayer.value, oldPlace = settings.placeLayer.value;
            var previous = m.placement;
            try
            {
                m.previewRunning = true;
                m.placement = __instance;
                // The hull/deck is layer22, excluded from native placement masks.
                // Extend the queries for this call; permission still checks the hit's
                // exact boat identity. Finally restores the shared settings asset.
                __instance.hitLayer = oldHit | BoatSurfaceBit;
                settings.placeLayer = oldPlace | BoatSurfaceBit;
                __result = __instance.UpdateHeldObjectPlacementPosition(ray);
                return false;
            }
            finally
            {
                __instance.hitLayer = oldHit;
                settings.placeLayer = oldPlace;
                m.placement = previous;
                m.previewRunning = false;
            }
        }
        catch (Exception e) { m.EditFailure(e); __result = false; return false; }
    }
    private static bool BoatSurfaceMask(PlacementSetting __instance, Il2CppSystem.Nullable<RaycastHit> hit, ref bool __result)
    {
        var m = Instance;
        if (!m.CanEdit || m.surfaceRunning || m.placement == null) return true;
        try
        {
            var entity = Player?.Character?.ObjectHolder?.HeldObject;
            if (entity == null || entity.PlacementSettings.Pointer != __instance.Pointer || !m.ValidBoatTarget(entity, m.placement)) return true;
            var mask = __instance.surfaceMask;
            if (mask == null || !mask.HasValue) return true;
            int old = mask.Value.value;
            try
            {
                m.surfaceRunning = true;
                mask._value = old | BoatSurfaceBit;
                __result = __instance.CheckSurfaceValid(hit);
                return false;
            }
            finally { mask._value = old; m.surfaceRunning = false; }
        }
        catch (Exception e) { m.EditFailure(e); __result = false; return false; }
    }
    private bool IsBoatSurface(Collider collider, BoatController boat)
    {
        if (collider == null) return false;
        if (collider.transform.IsChildOf(boat.transform)) return true;
        var support = collider.GetComponentInParent<HoldableEntity>();
        var ghost = support == null ? null : EntityGhost(support);
        return ghost != null && attachments.TryGet(ghost.Id, out var a) &&
            a!.PrefabId == ghost.PrefabId && a.ParentId == boat.MyGhost.Id && !support!.IsHeld;
    }
    private bool ValidBoatTarget(HoldableEntity entity, PlacementSystem system)
    {
        var boat = EditableBoat();
        var p = Player;
        return boat != null && p?.Character?.ObjectHolder?.HeldObject?.Pointer == entity.Pointer &&
            entity.ItemType.IsFurniture && Known(entity) && CanEdit && IsBoatSurface(system._hit.collider, boat) &&
            (system._hit.point - boat.transform.position).sqrMagnitude <= 900f;
    }
    private static bool AllowBoatSurface(HoldableEntity __instance, ref bool __result)
    {
        var m = Instance;
        if (!m.CanEdit || m.placement == null) return true;
        try
        {
            if (!m.ValidBoatTarget(__instance, m.placement)) return true;
            var g = EntityGhost(__instance);
            if (g == null || (!m.attachments.TryGet(g.Id, out _) && m.attachments.Count >= RelativeAttachmentStore.MaximumEntries))
            { m.Report("capacity", "Boat decoration limit reached (128). Store an item before adding another."); return true; }
            if (m.reported.Add("preview-ok")) m.Log.LogInfo("Native boat-surface placement checks reached for owned furniture; collider=" + m.placement._hit.collider.name + ", layer=" + m.placement._hit.collider.gameObject.layer);
            __result = true;
            return false;
        }
        catch (Exception e) { m.EditFailure(e); return true; }
    }
    private static void AfterFurnitureLock(FurnitureObject __instance)
    {
        var m = Instance;
        if (!m.CanEdit) return;
        try
        {
            var e = __instance.HoldableEntity;
            var g = e == null ? null : EntityGhost(e);
            if (g != null && m.attachments.TryGet(g.Id, out var a) && a!.PrefabId == g.PrefabId && EditableBoat()?.MyGhost.Id == a.ParentId)
                __instance.ReleaseLock();
        }
        catch (Exception e) { m.EditFailure(e); }
    }
    private static bool OwnedBoatFurniture(FurnitureObject __instance, ref bool __result)
    {
        var m = Instance;
        if (!m.ready || m.loading.Active) return true;
        try
        {
            var e = __instance.HoldableEntity;
            var g = e == null ? null : EntityGhost(e);
            if (g == null || !m.attachments.TryGet(g.Id, out var a) || a!.PrefabId != g.PrefabId) return true;
            __result = false;
            return false;
        }
        catch (Exception e) { m.EditFailure(e); return true; }
    }
    private static bool BeforePickup(HoldableEntity __instance, ref bool __result)
    {
        var m = Instance;
        if (!m.ready || m.loading.Active) return true;
        try
        {
            var ghost = EntityGhost(__instance);
            if (ghost == null || !m.attachments.TryGet(ghost.Id, out var a) || a!.PrefabId != ghost.PrefabId) return true;
            if (EditableBoat()?.MyGhost.Id == a.ParentId) return true;
            __result = false;
            return false;
        }
        catch (Exception e) { m.EditFailure(e); __result = false; return false; }
    }
    private static bool BeforePlacement(PlacementSystem __instance, bool isKinematic)
    {
        var m = Instance;
        if (!m.ready || m.loading.Active) return true;
        try
        {
            var entity = Player?.Character?.ObjectHolder?.HeldObject;
            if (entity == null) return true;
            var ghost = EntityGhost(entity);
            if (ghost == null) return true;
            m.pendingPlacement.Remove(ghost.Id);
            if (!m.CanEdit || !isKinematic || !m.ValidBoatTarget(entity, __instance)) return true;
            var boat = EditableBoat()!;
            var local = Quaternion.Inverse(boat.transform.rotation) * (entity.transform.position - boat.transform.position);
            var rot = Quaternion.Inverse(boat.transform.rotation) * entity.transform.rotation;
            var candidate = new RelativeAttachment(ghost.Id, ghost.PrefabId, boat.MyGhost.Id,
                local.x, local.y, local.z, rot.x, rot.y, rot.z, rot.w);
            // Validate final pose and capacity before native placement commits.
            var check = new RelativeAttachmentStore();
            check.Put(candidate);
            if (!m.attachments.TryGet(ghost.Id, out _) && m.attachments.Count >= RelativeAttachmentStore.MaximumEntries) return false;
            m.pendingPlacement[ghost.Id] = candidate;
            return true;
        }
        catch (Exception e) { m.EditFailure(e); return false; }
    }
    private void OnPlaced(HoldableEntity entity)
    {
        if (!ready || loading.Active) return;
        try
        {
            var ghost = EntityGhost(entity);
            if (ghost == null || !Known(entity)) return;
            if (!pendingPlacement.Remove(ghost.Id, out var candidate))
            {
                SetBoatCollisions(entity, BoatController.Instance, false);
                attachments.Remove(ghost.Id); authorized.Remove(ghost.Id); locks.Remove(ghost.Id);
                entity.Furniture?.RestoreLock();
                return;
            }
            attachments.Put(candidate);
            SetBoatCollisions(entity, BoatController.Instance, true);
            authorized.Remove(ghost.Id);
            if (entity.Rigidbody != null) { entity.Rigidbody.isKinematic = true; }
            entity.Furniture?.ReleaseLock();
            Log.LogInfo($"Boat decoration attached: {ghost.Id}; count={attachments.Count}.");
        }
        catch (Exception e) { EditFailure(e); }
    }
    private static void BeforeStore(PlayerObjectHolder __instance, ref string? __state)
    {
        __state = null;
        if (!Instance.ready) return;
        try
        {
            var e = __instance.HeldObject;
            if (e != null)
            {
                __state = EntityGhost(e)?.Id;
                if (__state != null && Instance.attachments.TryGet(__state, out _))
                {
                    SetBoatCollisions(e, BoatController.Instance, false);
                    Instance.locks.Remove(__state);
                }
            }
        }
        catch (Exception e) { Instance.EditFailure(e); }
    }
    private static void AfterStore(bool __result, string? __state)
    {
        if (!__result) return;
        Instance.ClearOwnedSelection();
        if (__state == null) return;
        Instance.attachments.Remove(__state);
        Instance.authorized.Remove(__state);
        Instance.pendingPlacement.Remove(__state);
        Instance.locks.Remove(__state);
    }

    internal void Tick()
    {
        if (!ready || loading.Active) return;
        try
        {
            if (!GhostManager.HasInstance || !GhostManager.Instance.IsSystemInitialized) return;
            var current = GhostManager.Instance.Pointer;
            if (world != IntPtr.Zero && world != current) ResetRecords();
            world = current;
            PromoteOwnedSelection();
            SyncAttachments();
        }
        catch (Exception e) { Report("sync", "Boat decoration synchronization paused: " + e); }
    }
    private void SyncAttachments()
    {
        if (!GhostManager.HasInstance) return;
        var manager = GhostManager.Instance;
        foreach (var a in attachments.Entries)
        {
            try
            {
                var ghost = manager.GetGhostById(a.Id);
                var boat = manager.GetGhostById(a.ParentId)?.TryCast<BoatGhost>();
                if (ghost == null || boat == null || !ghost.IsValid || !boat.IsValid || ghost.PrefabId != a.PrefabId) continue;
                var held = ghost.TryCast<HoldableGhost>();
                if (held == null || held.IsHeld) continue;
                IGhostView? view = null;
                HoldableEntity? entity = null;
                if (ghost.TryGetLoadedView(out var loadedView) && loadedView != null)
                {
                    view = loadedView;
                    entity = view.Transform.GetComponent<HoldableEntity>();
                    if (entity != null && (entity.IsHeld || entity.IsBeingPlaced)) continue;
                }
                var controller = BoatController.Instance;
                bool liveBoat = controller != null && controller.MyGhost?.Id == boat.Id;
                Vector3 origin = liveBoat ? controller!.transform.position : boat.Position;
                Quaternion rotation = liveBoat ? controller!.transform.rotation : boat.Rotation;
                Vector3 pos = origin + rotation * new Vector3(a.X, a.Y, a.Z);
                Quaternion rot = rotation * new Quaternion(a.QX, a.QY, a.QZ, a.QW);
                if (ghost.SceneIndex != boat.SceneIndex)
                    GhostManager.TransferGhostToScene(ghost, boat.SceneIndex, new PositionRotation(pos, rot));
                else
                {
                    if ((ghost.Position - pos).sqrMagnitude > 0.000001f) ghost.Position = pos;
                    ghost.Rotation = rot;
                }
                // Scene transfer can replace or pool the view; reacquire it.
                if (!ghost.TryGetLoadedView(out view) || view == null) continue;
                view.Transform.SetPositionAndRotation(pos, rot);
                entity = view.Transform.GetComponent<HoldableEntity>();
                if (entity == null || entity.IsHeld || entity.IsBeingPlaced) continue;
                bool editable = EditableBoat()?.MyGhost.Id == boat.Id;
                var stamp = (entity.Pointer, !liveBoat ? IntPtr.Zero : controller!.Pointer, editable);
                if (!locks.TryGetValue(a.Id, out var oldStamp) || oldStamp != stamp)
                {
                    SetBoatCollisions(entity, liveBoat ? controller : null, true);
                    entity.Furniture?.RestoreLock();
                    if (editable) entity.Furniture?.ReleaseLock();
                    locks[a.Id] = stamp;
                }
                var body = entity.Rigidbody;
                if (body != null) { body.isKinematic = true; }
            }
            catch (Exception e) { Report(a.Id, "Could not update boat decoration " + a.Id + ": " + e.Message); }
        }
    }
    private static void SetBoatCollisions(HoldableEntity entity, BoatController? boat, bool ignore)
    {
        if (boat == null) return;
        // Independent kinematic decorations must not push the boat's dynamic hull.
        // Keep collisions with the player and other furniture unchanged.
        foreach (var decorationCollider in entity.GetComponentsInChildren<Collider>(true))
            foreach (var boatCollider in boat.GetComponentsInChildren<Collider>(true))
                if (decorationCollider != null && boatCollider != null && decorationCollider != boatCollider)
                    Physics.IgnoreCollision(decorationCollider, boatCollider, ignore);
    }
    private sealed class SaveContext(string name) { public readonly string Name = name; public string? Snapshot; }
    private static void BeforeSave(string saveName, ref SaveContext? __state)
    {
        var m = Instance;
        if (!m.ready || m.loading.Active || m.loading.Quarantined) return;
        __state = new SaveContext(saveName);
        try { m.Tick(); __state.Snapshot = m.attachments.Snapshot(); }
        catch (Exception e) { m.Report("save", "Boat attachment snapshot failed: " + e); }
    }
    private static void AfterSave(bool __result, SaveContext? __state)
    {
        if (!__result || __state?.Snapshot == null) return;
        try
        {
            Instance.snapshots.Write(SavePath(__state.Name), __state.Snapshot);
            Instance.Log.LogInfo("Boat attachments saved for the completed game snapshot.");
        }
        catch (Exception e) { Instance.Report("save", "Game saved, but boat attachment metadata did not. Keep the previous save and its sidecar. " + e); }
    }
    private static string SavePath(string name)
    {
        if (Path.GetFileName(name) != name) throw new IOException("Save name is not a filename.");
        string? result = null;
        foreach (string filename in new[] { name, name + ".sav" })
        {
            string path = Path.Combine(Application.persistentDataPath, filename);
            if (!File.Exists(path)) continue;
            if (result != null) throw new IOException("Ambiguous save path.");
            result = path;
        }
        return result ?? throw new FileNotFoundException("Game save not found.");
    }
    private static void BeforeLoad(string saveName)
    {
        var m = Instance;
        if (!m.ready) return;
        m.loading.Begin();
        m.ResetRecords();
        m.pendingLoad = null;
        try
        {
            m.pendingLoad = SavePath(saveName);
            string hash = SaveSnapshotStore.Hash(m.pendingLoad);
            m.loading.Stage(hash, m.snapshots.Read(hash));
        }
        catch (Exception e) { m.Report("load", "Boat attachment metadata unavailable: " + e); }
    }
    private void AfterLoaded()
    {
        if (!ready) return;
        try
        {
            string? json = loading.Complete(pendingLoad == null ? null : SaveSnapshotStore.Hash(pendingLoad));
            if (json != null) attachments.Restore(json);
            world = GhostManager.HasInstance ? GhostManager.Instance.Pointer : IntPtr.Zero;
            Log.LogInfo("Boat attachment snapshot restored: " + attachments.Count + " records.");
        }
        catch (Exception e) { Report("load", "Boat attachments were not restored: " + e); }
        finally { loading.Complete(null); pendingLoad = null; }
    }
    private void ResetRecords()
    {
        attachments.Clear(); authorized.Clear(); reported.Clear(); pendingPlacement.Clear(); locks.Clear();
        ClearOwnedSelection();
        boatHintMode = -1; hintHud = IntPtr.Zero; hintFault = false;
        editor = null; placement = null; selecting = null; world = IntPtr.Zero; editFault = false;
    }
    private static void ClearWorld() { if (Instance.ready) Instance.ResetRecords(); }
}

public sealed class BoatDecorUpdate : MonoBehaviour
{
    public BoatDecorUpdate(IntPtr pointer) : base(pointer) { }
    public void LateUpdate() => Plugin.Instance.Tick();
}
