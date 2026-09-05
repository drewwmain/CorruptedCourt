using UnityEngine;
using UnityEngine.Serialization;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
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
}
