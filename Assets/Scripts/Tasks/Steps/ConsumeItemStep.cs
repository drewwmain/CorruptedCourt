using UnityEngine;
using UnityEngine.Serialization;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Minigames;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 7. CONSUME ITEM (Eat / Drink - destroys item)
    // ---------------------------------------------------
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
            if (requiredItem == null) return false;

            PickupItem heldItem = player.GetHeldItem();
            if (heldItem == null) return false;

            // Existing behaviour: the held item itself IS the required identity (unchanged - every live
            // task still uses this path today).
            if (heldItem.Matches(requiredItem, requiredState))
            {
                // If a minigame is attached it plays out the eating/drinking and consumes the item itself
                // (see ConsumeItemMinigame). Here we only confirm the player is holding the right thing.
                if (minigamePrefab != null) return true;

                // No minigame: consume it right away.
                GameObject objToDestroy = heldItem.gameObject;
                player.ClearHeldItem();
                Object.Destroy(objToDestroy);
                return true;
            }

            // New: a held container's payload can also satisfy this (eat the cake off the plate). The
            // container itself is NOT destroyed - only one serving is consumed from its payload.
            ItemPayload payload = heldItem.GetComponent<ItemPayload>();
            if (payload != null && payload.contents == requiredItem && payload.count > 0)
            {
                if (minigamePrefab != null) return true;

                payload.count--;
                if (payload.count <= 0) payload.contents = null;
                return true;
            }

            return false;
        }

        public override string GetConfigurationWarning()
            => requiredItem == null && !string.IsNullOrEmpty(legacyRequiredItemName)
                ? $"legacyRequiredItemName '{legacyRequiredItemName}' is set but Required Item is not - assign the ItemDefinition."
                : null;

        // New capability - WaypointManager's old if-else chain never handled this step at all, so
        // consume tasks had no waypoint before B16.
        public override ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime)
        {
            if (requiredItem == null) return default;
            if (player.IsHoldingItem(requiredItem, requiredState)) return default; // already holding it - nothing to point at, just consume it

            return ItemSourceResolver.Instance != null
                ? ItemSourceResolver.Instance.Resolve(new ItemIdentity { definition = requiredItem, state = requiredState }, player.transform.position)
                : default;
        }
    }
}
