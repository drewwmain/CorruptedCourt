using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

// ---------------------------------------------------
// 1. ACQUIRE ITEM (Standard or Heavy)
// ---------------------------------------------------
[System.Serializable]
public class AcquireItemStep : TaskStep
{
    [Tooltip("The exact itemName to look for.")]
    public string requiredItemName;

    public override string GetObjectiveText()
    {
        return $"Find and pick up: <color=#5DADE2>{requiredItemName}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        // Counts the item whether it's in the active hand or the off-hand.
        return player.IsHoldingItemNamed(requiredItemName);
    }
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
        return player.currentZoneID == targetZoneID;
    }
}

// ---------------------------------------------------
// 3. TASK STATION INTERACTION (Standard & Group)
// ---------------------------------------------------
[System.Serializable]
public class StationInteractStep : TaskStep
{
    [Tooltip("The ID or Name of the station to interact with.")]
    public string targetStationID;

    [Tooltip("Set to > 1 if multiple players must interact simultaneously.")]
    public int requiredSimultaneousPlayers = 1;

    public override string GetObjectiveText()
    {
        if (requiredSimultaneousPlayers > 1)
            return $"Gather {requiredSimultaneousPlayers} players at the <color=#F4D03F>{targetStationID}</color>";

        return $"Interact with the <color=#F4D03F>{targetStationID}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        // Check if the object we interacted with matches the target station
        if (targetInteractable.name.Contains(targetStationID))
        {
            // If it's a group task, check proximity of other players
            if (requiredSimultaneousPlayers > 1)
            {
                int playersNearby = 0;
                Collider[] hits = Physics.OverlapSphere(targetInteractable.transform.position, player.InteractionRange, player.CharacterLayer);

                foreach (var hit in hits)
                {
                    if (hit.GetComponent<PlayerController>() != null) playersNearby++;
                }

                return playersNearby >= requiredSimultaneousPlayers;
            }

            return true; // Standard single-player interaction successful
        }
        return false;
    }
}

// ---------------------------------------------------
// 4. PLAYER INTERACTION
// ---------------------------------------------------
[System.Serializable]
public class PlayerInteractStep : TaskStep
{
    [Tooltip("Leave blank if you must approach empty-handed.")]
    public string requiredHeldItemName;

    public override string GetObjectiveText()
    {
        if (string.IsNullOrEmpty(requiredHeldItemName))
            return "Interact with another court member";

        return $"Use <color=#5DADE2>{requiredHeldItemName}</color> on another player";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        // Verify the target is actually another player
        PlayerController targetPlayer = targetInteractable.GetComponent<PlayerController>();
        if (targetPlayer == null) return false;

        // Empty-handed check
        if (string.IsNullOrEmpty(requiredHeldItemName))
        {
            return player.GetHeldItem() == null;
        }
        // Specific item check
        else
        {
            var heldItem = player.GetHeldItem();
            return heldItem != null && heldItem.itemName == requiredHeldItemName;
        }
    }
}

// ---------------------------------------------------
// 5. DATA RETRIEVAL (Generate & Input)
// ---------------------------------------------------
[System.Serializable]
public class DataRetrievalStep : TaskStep
{
    public string sourceStationID;
    public string inputStationID;

    // We now store the generated code directly inside the step itself!
    [HideInInspector] public bool hasCode = false;
    [HideInInspector] public string generatedCode = "";

    public override string GetObjectiveText()
    {
        if (!hasCode)
            return $"Retrieve the code from the <color=#F4D03F>{sourceStationID}</color>";
        else
            return $"Input code <color=#E74C3C>'{generatedCode}'</color> at the <color=#F4D03F>{inputStationID}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        if (!hasCode)
        {
            // Part 1: Getting the code
            if (targetInteractable.name.Contains(sourceStationID))
            {
                hasCode = true;
                generatedCode = Random.Range(100, 999).ToString(); // Generate the code

                Debug.Log($"[Task System] Code {generatedCode} acquired from {sourceStationID}!");

                // Trigger the UI Popup
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.ShowDataCodePopup(generatedCode);
                }

                // Force the waypoints to update to the new destination
                player.RefreshLocalWaypoints();

                return false; // Return false because the step isn't fully complete until they input it!
            }
        }
        else
        {
            // Part 2: Inputting the code
            if (targetInteractable.name.Contains(inputStationID))
            {
                // Unlock the mouse so they can click the keypad UI
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // Trigger the Keypad UI
                if (UIManager.Instance != null)
                {
                    // Note: You may need to update your OpenDataInputPanel method to accept
                    // this specific step or code string instead of the old TaskData object!
                    // UIManager.Instance.OpenDataInputPanel(player, generatedCode);
                }

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
    [Tooltip("The exact itemName required to be deposited.")]
    public string requiredItemName;
    [Tooltip("The locationID of the TaskDepositStation.")]
    public string targetStationID;

    public override string GetObjectiveText()
    {
        return $"Deposit the <color=#5DADE2>{requiredItemName}</color> at the <color=#F4D03F>{targetStationID}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        // The step is complete once the target station holds an item whose NAME matches - no matter
        // who deposited it. If the station wants a processed item, only the renamed (processed) item
        // will match. Checks the station's acceptedItemName and (legacy) this step's requiredItemName.
        foreach (TaskLocation location in TaskLocation.AllLocations)
        {
            if (location == null || location.locationID != targetStationID) continue;

            TaskDepositStation station = location.GetComponent<TaskDepositStation>();
            if (station == null) continue;

            foreach (PickupItem item in station.depositedItemSlots)
            {
                if (item == null) continue;

                if (!string.IsNullOrEmpty(location.acceptedItemName) && item.itemName == location.acceptedItemName)
                    return true;
                if (!string.IsNullOrEmpty(requiredItemName) && item.itemName == requiredItemName)
                    return true;
            }
        }
        return false;
    }
}

// ---------------------------------------------------
// 7. CONSUME ITEM (Eat / Drink - destroys item)
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: "DepositItemStep/ConsumeItemStep")]
[System.Serializable]
public class ConsumeItemStep : TaskStep
{
    public string requiredItemName;

    public override string GetObjectiveText()
    {
        return $"Consume or drink: <color=#5DADE2>{requiredItemName}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        var heldItem = player.GetHeldItem();
        if (heldItem == null || heldItem.itemName != requiredItemName) return false;

        // If a minigame is attached it plays out the eating/drinking and consumes the item itself
        // (see ConsumeItemMinigame). Here we only confirm the player is holding the right thing.
        if (minigamePrefab != null) return true;

        // No minigame: consume it right away.
        GameObject objToDestroy = heldItem.gameObject;
        player.ClearHeldItem();
        Object.Destroy(objToDestroy);
        return true;
    }
}

// ---------------------------------------------------
// 8. PROCESS ITEM (Cut, Polish, Light)
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: "DepositItemStep/ProcessItemStep")]
[System.Serializable]
public class ProcessItemStep : TaskStep
{
    public string targetStationOrItemName;

    public override string GetObjectiveText()
    {
        return $"Process or interact with: <color=#F4D03F>{targetStationOrItemName}</color>";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;
        return targetInteractable.name.Contains(targetStationOrItemName);
    }
}

// ---------------------------------------------------
// 9. EQUIP CLOTHING STEP
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: "DepositItemStep/EquipClothingStep")]
[System.Serializable]
public class EquipClothingStep : TaskStep
{
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
    public string myRequiredItemName;

    [Tooltip("What the TARGET player must be holding to allow the interaction.")]
    public string targetRequiredItemName;

    public override string GetObjectiveText()
    {
        return $"Cross <color=#5DADE2>{myRequiredItemName}</color>s with another armed court member!";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        PlayerController targetPlayer = targetInteractable.GetComponent<PlayerController>();
        if (targetPlayer == null) return false;

        // 1. Check my hands
        var myItem = player.GetHeldItem();
        if (myItem == null || myItem.itemName != myRequiredItemName) return false;

        // 2. Check their hands
        var theirItem = targetPlayer.GetHeldItem();
        if (theirItem == null || theirItem.itemName != targetRequiredItemName) return false;

        return true;
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
        if (player.currentZoneID != targetZoneID) return false;

        int playersInRoom = 0;

        if (RoleManager.Instance != null)
        {
            foreach (PlayerController p in RoleManager.Instance.allPlayers)
            {
                if (!p.isGhost && p.currentZoneID == targetZoneID)
                {
                    playersInRoom++;
                }
            }
        }

        return playersInRoom >= requiredPlayerCount;
    }
}
