using UnityEngine;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Which hand an item is given to. No such enum existed anywhere in the codebase (PlayerInventory
    /// tracks "active"/"off" hand via two named fields, not an enum) - defined here per
    /// IMPLEMENTATION_PROMPTS.md B4a's instruction to confirm-or-define before assuming a signature.
    /// </summary>
    public enum Hand { Right, Left }

    /// <summary>
    /// Placeholder for the B5 DroppedItemRegistry phase's drop-slot rules. Defined here now only
    /// because ItemLifecycle.Drop's signature needs it; B5 owns whether it stays here or moves.
    /// </summary>
    public enum DropCause { Manual, Thrown, Forced }

    /// <summary>
    /// Single surface for item spawn / hand-placement / state / payload / station-placement / drop /
    /// despawn (ARCHITECTURE.md §9.1). B4a only: every method below calls into logic that already
    /// exists scattered across PickupItem, PlayerInventory and TaskDepositStation - nothing here is
    /// wired to a caller yet (that's B4b), and none of those files are touched by this phase.
    /// </summary>
    public static class ItemLifecycle
    {
        // Mirrors PickupItem.OnInteract's isInfiniteSource clone path, generalised: instantiate the
        // given prefab (not necessarily the item clicked) at an explicit pose and stamp an explicit
        // identity, rather than copying whatever the source object already was. Leaves
        // isInfiniteSource as authored on the prefab - that clone-specific override belongs to the
        // pickup interaction, not to a generic spawn.
        public static PickupItem Spawn(PickupItem prefab, ItemIdentity identity, Vector3 pos, Quaternion rot)
        {
            if (prefab == null) return null;

            PickupItem instance = Object.Instantiate(prefab, pos, rot);
            instance.definition = identity.definition;
            instance.state = identity.state;
            return instance;
        }

        // Mirrors ItemDepositMinigame.cs's "Item.AttachToHand(player.RightHandSocket)" - the existing
        // pattern for handing an item to a specific hand outside PlayerInventory's own auto-hand-
        // selection logic (EquipItem). Note this does not update PlayerInventory's private
        // currentlyHeldItem/leftHeldItem bookkeeping - neither does the existing call site it mirrors.
        public static void GiveToHand(PlayerController player, PickupItem item, Hand hand)
        {
            if (player == null || item == null) return;

            Transform socket = hand == Hand.Left ? player.LeftHandSocket : player.RightHandSocket;
            item.AttachToHand(socket);
        }

        // Generalises PickupItem.ProcessItem() / MarkAsDepositedContainer() / MarkAsSpent(), which all
        // do "state |= oneFlag" - into an add-and-remove form, since nothing today removes a flag.
        // item.state is a public field, so this is the same direct-mutation pattern those methods use.
        public static void SetState(PickupItem item, ItemState add, ItemState remove)
        {
            if (item == null) return;
            item.state = (item.state | add) & ~remove;
        }

        // Mirrors RoundRoleSwitch.TransferDepositedItems's get-or-add ItemPayload pattern (B3), as a
        // direct "set" rather than that method's per-item increment.
        public static void SetPayload(PickupItem item, ItemDefinition contents, int count)
        {
            if (item == null) return;

            ItemPayload payload = item.GetComponent<ItemPayload>();
            if (payload == null) payload = item.gameObject.AddComponent<ItemPayload>();

            payload.contents = contents;
            payload.count = count;
        }

        // TaskDepositStation.DepositIntoSlot(PickupItem, int) already IS this exact operation (places
        // the item in the slot's Transform, updates depositedItemSlots, raises StationReceivedDeposit) -
        // delegate straight to it rather than duplicating station-internal bookkeeping.
        public static void PlaceInStation(PickupItem item, TaskDepositStation station, int slot)
        {
            if (item == null || station == null) return;
            station.DepositIntoSlot(item, slot);
        }

        // Mirrors the common core of PlayerInventory.DropHeldItemInPlace/DropHeavyItems: unparent,
        // re-enable physics, a small forward toss, then settle - all of which already lives in
        // PickupItem.DetachFromHand(). The player's own charge-based throw (ExecuteThrow) is a
        // distinct, input-driven mechanic (charge %, weight-scaled force, torque, ThrownProjectile)
        // that doesn't generalise to an arbitrary item + cause, so it is NOT replicated here - Thrown
        // is accepted so callers can express intent, but gets the same toss as Manual/Forced today.
        // dropper/cause aren't used yet; both are here for the B5 DroppedItemRegistry hook.
        public static void Drop(PlayerController dropper, PickupItem item, DropCause cause)
        {
            if (item == null) return;
            item.DetachFromHand();
        }

        // Mirrors the Object.Destroy(item.gameObject) pattern already used by ConsumeItemStep,
        // ConsumeMinigame/ConsumeItemMinigame, and TaskDepositStation.SabotageMostRecentDeposit.
        public static void Despawn(PickupItem item)
        {
            if (item == null) return;
            Object.Destroy(item.gameObject);
        }
    }
}
