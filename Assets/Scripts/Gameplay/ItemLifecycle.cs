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
    /// Why an item left a player's hand. Manual/Thrown consume the player's one outstanding-drop slot
    /// (see DroppedItemRegistry); Forced (the pre-meeting scramble) is exempt from that rule entirely.
    /// </summary>
    public enum DropCause { Manual, Thrown, Forced }

    /// <summary>
    /// Single surface for item spawn / hand-placement / state / payload / station-placement / drop /
    /// despawn (ARCHITECTURE.md §9.1). As of B4b, PickupItem/PlayerInventory/TaskDepositStation route
    /// their internal mutation logic through here instead of duplicating it inline - SetPayload has
    /// no adopted caller yet (that logic still lives in RoundRoleSwitch, untouched by B4b).
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

        // Places the item at the station's slot-index Transform (GetDropSlot is already public).
        // Does NOT touch depositedItemSlots / slotDepositors / lastDepositedSlot / the
        // StationReceivedDeposit event - those are TaskDepositStation-private bookkeeping only
        // TaskDepositStation.DepositIntoSlot can touch, and that method (as of B4b) calls THIS one
        // for the placement step - calling DepositIntoSlot from here instead would recurse straight
        // back into it.
        public static void PlaceInStation(PickupItem item, TaskDepositStation station, int slot)
        {
            if (item == null || station == null) return;
            item.PlaceInStation(station.GetDropSlot(slot), station);
        }

        // Mirrors the common core of PlayerInventory.DropHeldItemInPlace/DropHeavyItems: unparent,
        // re-enable physics, a small forward toss, then settle - all of which already lives in
        // PickupItem.DetachFromHand(). The player's own charge-based throw (ExecuteThrow) is a
        // distinct, input-driven mechanic (charge %, weight-scaled force, torque, ThrownProjectile)
        // that doesn't generalise to an arbitrary item + cause, so it is NOT replicated here - Thrown
        // is accepted so callers can express intent, but gets the same toss as Manual/Forced today.
        //
        // R5 (ARCHITECTURE.md §9.2, B5): a Manual/Thrown drop replaces the dropper's one outstanding
        // drop, despawning the old one with a visible fade rather than an instant pop. Forced drops
        // (the pre-meeting scramble) are exempt entirely - they neither replace nor become a tracked
        // drop, so DroppedItemRegistry is skipped altogether for that cause.
        public static void Drop(PlayerController dropper, PickupItem item, DropCause cause)
        {
            if (item == null) return;

            if (cause != DropCause.Forced && dropper != null && DroppedItemRegistry.Instance != null)
            {
                PickupItem outstanding = DroppedItemRegistry.Instance.OutstandingFor(dropper);
                if (outstanding != null && outstanding != item)
                    DroppedItemRegistry.Instance.DespawnWithFade(outstanding);

                DroppedItemRegistry.Instance.RegisterDrop(dropper, item, cause);
            }

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
