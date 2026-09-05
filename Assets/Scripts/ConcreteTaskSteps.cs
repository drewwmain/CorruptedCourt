using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

// ---------------------------------------------------
// 1. ACQUIRE ITEM (Standard or Heavy)
// ---------------------------------------------------
[System.Serializable]
public class AcquireItemStep : TaskStep
{
    [Tooltip("The item to find and pick up.")]
    public ItemDefinition requiredItem;

    [Tooltip("State flags the item must carry (e.g. Processed for a polished sword). None = any state.")]
    public ItemState requiredState = ItemState.None;

    // [Obsolete] identity moved to requiredItem + requiredState. Kept so un-migrated assets load and
    // OnValidate can flag them; the migration tool clears it.
    [FormerlySerializedAs("requiredItemName")]
    [Tooltip("[DEPRECATED] Old string identity. Assign 'Required Item' instead.")]
    public string legacyRequiredItemName;

    public override string GetObjectiveText()
    {
        string what = requiredItem != null ? requiredItem.displayName : legacyRequiredItemName;
        return $"Find and pick up: <color=#5DADE2>{what}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        // Counts the item whether it's in the active hand or the off-hand.
        return requiredItem != null && player.IsHoldingItem(requiredItem, requiredState);
    }

    public override string GetConfigurationWarning()
        => requiredItem == null && !string.IsNullOrEmpty(legacyRequiredItemName)
            ? $"legacyRequiredItemName '{legacyRequiredItemName}' is set but Required Item is not - assign the ItemDefinition."
            : null;
}

// ---------------------------------------------------
// 2. NAVIGATE TO ZONE
// ---------------------------------------------------
[System.Serializable]
public class NavigateStep : TaskStep
{
    [Tooltip("The ID of the room the player must enter.")]
    public string targetZoneID;

    public override string GetObjectiveText()
    {
        return $"Travel to the <color=#F4D03F>{targetZoneID}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        return player.Vitals.currentZoneID == targetZoneID;
    }
}

// ---------------------------------------------------
// 3. TASK STATION INTERACTION (Standard & Group)
// ---------------------------------------------------
[System.Serializable]
public class StationInteractStep : TaskStep
{
    [Tooltip("The locationID of the station to interact with (exact match against TaskLocation.locationID).")]
    public string targetStationID;

    [Tooltip("Set to > 1 if multiple players must interact simultaneously.")]
    public int requiredSimultaneousPlayers = 1;

    // Shared scratch buffer for the group-proximity check. CheckCompletion runs on the main thread
    // and consumes the hits immediately, so one static buffer is safe.
    private static readonly Collider[] proximityBuffer = new Collider[16];

    public override string GetObjectiveText()
    {
        if (requiredSimultaneousPlayers > 1)
            return $"Gather {requiredSimultaneousPlayers} players at the <color=#F4D03F>{targetStationID}</color>";

        return $"Interact with the <color=#F4D03F>{targetStationID}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        // The object we interacted with must BE (or sit under) the target station.
        TaskLocation loc = ResolveLocation(targetInteractable);
        if (loc == null || loc.locationID != targetStationID) return false;

        // If it's a group task, check proximity of other players.
        if (requiredSimultaneousPlayers > 1)
        {
            int playersNearby = 0;
            int hitCount = Physics.OverlapSphereNonAlloc(
                targetInteractable.transform.position, player.Interactor.InteractionRange, proximityBuffer, player.Interactor.CharacterLayer);

            for (int i = 0; i < hitCount; i++)
            {
                if (proximityBuffer[i] != null && proximityBuffer[i].GetComponent<PlayerController>() != null)
                    playersNearby++;
            }

            return playersNearby >= requiredSimultaneousPlayers;
        }

        return true; // Standard single-player interaction successful
    }
}

// ---------------------------------------------------
// 4. PLAYER INTERACTION
// ---------------------------------------------------
[System.Serializable]
public class PlayerInteractStep : TaskStep
{
    [Tooltip("Leave empty to approach empty-handed. Otherwise the item that must be held.")]
    public ItemDefinition requiredItem;

    [Tooltip("State flags the held item must carry. None = any state.")]
    public ItemState requiredState = ItemState.None;

    // [Obsolete] identity moved to requiredItem + requiredState.
    [FormerlySerializedAs("requiredHeldItemName")]
    [Tooltip("[DEPRECATED] Old string identity. Assign 'Required Item' instead.")]
    public string legacyRequiredHeldItemName;

    public override string GetObjectiveText()
    {
        if (requiredItem == null)
            return "Interact with another court member";

        return $"Use <color=#5DADE2>{requiredItem.displayName}</color> on another player";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        // Verify the target is actually another player
        PlayerController targetPlayer = targetInteractable.GetComponent<PlayerController>();
        if (targetPlayer == null) return false;

        // Empty-handed check
        if (requiredItem == null)
        {
            return player.GetHeldItem() == null;
        }

        // Specific item check (active hand, mirroring the old behaviour)
        PickupItem heldItem = player.GetHeldItem();
        return heldItem != null && heldItem.Matches(requiredItem, requiredState);
    }

    public override string GetConfigurationWarning()
        => requiredItem == null && !string.IsNullOrEmpty(legacyRequiredHeldItemName)
            ? $"legacyRequiredHeldItemName '{legacyRequiredHeldItemName}' is set but Required Item is not - assign the ItemDefinition."
            : null;
}

// ---------------------------------------------------
// 5. DATA RETRIEVAL (Generate & Input)
// ---------------------------------------------------
[System.Serializable]
public class DataRetrievalStep : TaskStep
{
    [Tooltip("locationID of the station the code is retrieved from.")]
    public string sourceStationID;
    [Tooltip("locationID of the station the code is entered at.")]
    public string inputStationID;

    // hasCode / generatedCode used to live here and were serialized onto the shared asset - a
    // per-player, per-attempt value stored on one shared object. They now live on TaskStepRuntime,
    // passed into the runtime-aware overloads below.

    public override string GetObjectiveText() => GetObjectiveText(null);

    public override string GetObjectiveText(TaskStepRuntime runtime)
    {
        bool hasCode = runtime != null && runtime.HasCode;
        if (!hasCode)
            return $"Retrieve the code from the <color=#F4D03F>{sourceStationID}</color>";

        return $"Input code <color=#E74C3C>'{runtime.GeneratedCode}'</color> at the <color=#F4D03F>{inputStationID}</color>";
    }

    // Two-phase flow with per-player state, so it always runs through the runtime-aware overload;
    // the stateless predicate can never complete it on its own.
    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null) => false;

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable, TaskStepRuntime runtime)
    {
        if (targetInteractable == null || runtime == null) return false;

        TaskLocation loc = ResolveLocation(targetInteractable);
        if (loc == null) return false;

        if (!runtime.HasCode)
        {
            // Part 1: Getting the code
            if (loc.locationID == sourceStationID)
            {
                runtime.HasCode = true;
                runtime.GeneratedCode = Random.Range(100, 999).ToString(); // Generate the code
                runtime.CodeRevealPending = true; // the view shows the popup off the tasks-changed signal

                Debug.Log($"[Task System] Code {runtime.GeneratedCode} acquired from {sourceStationID}!");

                // Force the waypoints (and the code popup) to update to the new destination
                player.TaskBook.RefreshLocalWaypoints();

                return false; // Return false because the step isn't fully complete until they input it!
            }
        }
        else
        {
            // Part 2: Inputting the code
            if (loc.locationID == inputStationID)
            {
                // Unlock the mouse so they can click the keypad UI
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                return true; // Step fully completed!
            }
        }

        return false;
    }
}

// ---------------------------------------------------
// 6. DEPOSIT ITEM
// ---------------------------------------------------
[System.Serializable]
public class DepositItemStep : TaskStep
{
    [Tooltip("The item that must be deposited.")]
    public ItemDefinition requiredItem;

    [Tooltip("State flags the deposited item must carry (e.g. Processed, DepositedContainer). None = any state.")]
    public ItemState requiredState = ItemState.None;

    [Tooltip("The locationID of the TaskDepositStation (exact match against TaskLocation.locationID).")]
    public string targetStationID;

    [SerializeField]
    [Tooltip("When true, the step only advances for the player who actually made the deposit. " +
             "Default false preserves the old 'any matching item in the slot' behaviour.")]
    private bool requireOwnDeposit = false;

    // [Obsolete] identity moved to requiredItem + requiredState.
    [FormerlySerializedAs("requiredItemName")]
    [Tooltip("[DEPRECATED] Old string identity. Assign 'Required Item' instead.")]
    public string legacyRequiredItemName;

    public override string GetObjectiveText()
    {
        string what = requiredItem != null ? requiredItem.displayName : legacyRequiredItemName;
        return $"Deposit the <color=#5DADE2>{what}</color> at the <color=#F4D03F>{targetStationID}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (requiredItem == null) return false; // un-migrated step: nothing to match against

        // The step is complete once the target station holds an item that matches requiredItem +
        // requiredState. If requireOwnDeposit is set, only THIS player's deposit counts.
        foreach (TaskLocation location in TaskLocation.AllLocations)
        {
            if (location == null || location.locationID != targetStationID) continue;

            TaskDepositStation station = location.GetComponent<TaskDepositStation>();
            if (station == null) station = location.GetComponentInParent<TaskDepositStation>();
            if (station == null || station.depositedItemSlots == null) continue;

            for (int i = 0; i < station.depositedItemSlots.Length; i++)
            {
                PickupItem item = station.depositedItemSlots[i];
                if (item == null) continue;

                if (requireOwnDeposit && station.GetSlotDepositor(i) != player) continue;

                if (item.Matches(requiredItem, requiredState)) return true;
            }
        }
        return false;
    }

    public override string GetConfigurationWarning()
        => requiredItem == null && !string.IsNullOrEmpty(legacyRequiredItemName)
            ? $"legacyRequiredItemName '{legacyRequiredItemName}' is set but Required Item is not - assign the ItemDefinition."
            : null;
}

// ---------------------------------------------------
// 7. CONSUME ITEM (Eat / Drink - destroys item)
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: "DepositItemStep/ConsumeItemStep")]
[System.Serializable]
public class ConsumeItemStep : TaskStep
{
    [Tooltip("The item that must be consumed / drunk.")]
    public ItemDefinition requiredItem;

    [Tooltip("State flags the held item must carry. None = any state.")]
    public ItemState requiredState = ItemState.None;

    // [Obsolete] identity moved to requiredItem + requiredState.
    [FormerlySerializedAs("requiredItemName")]
    [Tooltip("[DEPRECATED] Old string identity. Assign 'Required Item' instead.")]
    public string legacyRequiredItemName;

    public override string GetObjectiveText()
    {
        string what = requiredItem != null ? requiredItem.displayName : legacyRequiredItemName;
        return $"Consume or drink: <color=#5DADE2>{what}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        var heldItem = player.GetHeldItem();
        if (heldItem == null || requiredItem == null || !heldItem.Matches(requiredItem, requiredState)) return false;

        // If a minigame is attached it plays out the eating/drinking and consumes the item itself
        // (see ConsumeItemMinigame). Here we only confirm the player is holding the right thing.
        if (minigamePrefab != null) return true;

        // No minigame: consume it right away.
        GameObject objToDestroy = heldItem.gameObject;
        player.ClearHeldItem();
        Object.Destroy(objToDestroy);
        return true;
    }

    public override string GetConfigurationWarning()
        => requiredItem == null && !string.IsNullOrEmpty(legacyRequiredItemName)
            ? $"legacyRequiredItemName '{legacyRequiredItemName}' is set but Required Item is not - assign the ItemDefinition."
            : null;
}

// ---------------------------------------------------
// 8. PROCESS ITEM (Cut, Polish, Light)
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: "DepositItemStep/ProcessItemStep")]
[System.Serializable]
public class ProcessItemStep : TaskStep
{
    [Tooltip("locationID of the station where the item is processed (exact match). Use this OR Target Item.")]
    public string targetStationID;

    [Tooltip("The held / world PickupItem that must be processed (matched by ItemDefinition, not name). Use this OR Target Station ID.")]
    public ItemDefinition targetItem;

    [Tooltip("State flags the target item must carry to satisfy the step. None = any state.")]
    public ItemState targetItemState = ItemState.None;

    // [Obsolete] this used to be one string that meant EITHER a station name OR an item name, matched
    // by substring. Split into targetStationID (a locationID) and targetItem (an ItemDefinition).
    [FormerlySerializedAs("targetStationOrItemName")]
    [Tooltip("[DEPRECATED] Old station-or-item string. Assign Target Station ID or Target Item instead.")]
    public string legacyTargetStationOrItemName;

    public override string GetObjectiveText()
    {
        string what = targetItem != null ? targetItem.displayName
            : !string.IsNullOrEmpty(targetStationID) ? targetStationID
            : legacyTargetStationOrItemName;
        return $"Process or interact with: <color=#F4D03F>{what}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        // Station match: the interacted object is (or sits under) the target TaskLocation.
        if (!string.IsNullOrEmpty(targetStationID))
        {
            TaskLocation loc = ResolveLocation(targetInteractable);
            if (loc != null && loc.locationID == targetStationID) return true;
        }

        // Item match: the interacted object is (or sits under) a PickupItem of the target identity.
        if (targetItem != null)
        {
            PickupItem pi = targetInteractable.GetComponent<PickupItem>();
            if (pi == null) pi = targetInteractable.GetComponentInParent<PickupItem>();
            if (pi != null && pi.Matches(targetItem, targetItemState)) return true;
        }

        return false;
    }

    public override string GetConfigurationWarning()
        => targetItem == null && string.IsNullOrEmpty(targetStationID) && !string.IsNullOrEmpty(legacyTargetStationOrItemName)
            ? $"legacyTargetStationOrItemName '{legacyTargetStationOrItemName}' is set but neither Target Station ID nor Target Item is - assign one."
            : null;
}

// ---------------------------------------------------
// 9. EQUIP CLOTHING STEP
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: "DepositItemStep/EquipClothingStep")]
[System.Serializable]
public class EquipClothingStep : TaskStep
{
    // NOTE: clothing has no ItemDefinition today, so this is the one step still matching on a
    // GameObject name. Migrate it when clothing gets typed identities (out of scope for phase 3b).
    public string clothingName;

    public override string GetObjectiveText()
    {
        return $"Put on the <color=#5DADE2>{clothingName}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        return targetInteractable != null && targetInteractable.name.Contains(clothingName);
    }
}

// ---------------------------------------------------
// 10. MUTUAL PLAYER INTERACT (Duels, Jousts)
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: "DepositItemStep/MutualPlayerInteractStep")]
[System.Serializable]
public class MutualPlayerInteractStep : TaskStep
{
    [Tooltip("What the player initiating the interaction must be holding.")]
    public ItemDefinition requiredItem;
    [Tooltip("State flags the initiator's item must carry. None = any state.")]
    public ItemState requiredState = ItemState.None;

    [Tooltip("What the TARGET player must be holding to allow the interaction.")]
    public ItemDefinition targetRequiredItem;
    [Tooltip("State flags the target's item must carry. None = any state.")]
    public ItemState targetRequiredState = ItemState.None;

    // [Obsolete] identity moved to the typed fields above.
    [FormerlySerializedAs("myRequiredItemName")]
    [Tooltip("[DEPRECATED] Old string identity. Assign 'Required Item' instead.")]
    public string legacyMyRequiredItemName;
    [FormerlySerializedAs("targetRequiredItemName")]
    [Tooltip("[DEPRECATED] Old string identity. Assign 'Target Required Item' instead.")]
    public string legacyTargetRequiredItemName;

    public override string GetObjectiveText()
    {
        string what = requiredItem != null ? requiredItem.displayName : legacyMyRequiredItemName;
        return $"Cross <color=#5DADE2>{what}</color>s with another armed court member!";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        PlayerController targetPlayer = targetInteractable.GetComponent<PlayerController>();
        if (targetPlayer == null) return false;

        if (requiredItem == null || targetRequiredItem == null) return false;

        // 1. Check my hands
        var myItem = player.GetHeldItem();
        if (myItem == null || !myItem.Matches(requiredItem, requiredState)) return false;

        // 2. Check their hands
        var theirItem = targetPlayer.GetHeldItem();
        if (theirItem == null || !theirItem.Matches(targetRequiredItem, targetRequiredState)) return false;

        return true;
    }

    public override string GetConfigurationWarning()
    {
        if (requiredItem == null && !string.IsNullOrEmpty(legacyMyRequiredItemName))
            return $"legacyMyRequiredItemName '{legacyMyRequiredItemName}' is set but Required Item is not - assign the ItemDefinition.";
        if (targetRequiredItem == null && !string.IsNullOrEmpty(legacyTargetRequiredItemName))
            return $"legacyTargetRequiredItemName '{legacyTargetRequiredItemName}' is set but Target Required Item is not - assign the ItemDefinition.";
        return null;
    }
}

// ---------------------------------------------------
// 11. GROUP NAVIGATE (Bedding Ceremony)
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: "DepositItemStep/GroupNavigateStep")]
[System.Serializable]
public class GroupNavigateStep : TaskStep
{
    [Tooltip("The ID of the room the crowd must gather in.")]
    public string targetZoneID;

    [Tooltip("How many total players must be in the room to complete the step.")]
    public int requiredPlayerCount;

    public override string GetObjectiveText()
    {
        return $"Gather {requiredPlayerCount} members of the court in the <color=#F4D03F>{targetZoneID}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (player.Vitals.currentZoneID != targetZoneID) return false;

        int playersInRoom = 0;

        if (RoleManager.Instance != null)
        {
            foreach (PlayerController p in RoleManager.Instance.allPlayers)
            {
                if (!p.Vitals.isGhost && p.Vitals.currentZoneID == targetZoneID)
                {
                    playersInRoom++;
                }
            }
        }

        return playersInRoom >= requiredPlayerCount;
    }
}
