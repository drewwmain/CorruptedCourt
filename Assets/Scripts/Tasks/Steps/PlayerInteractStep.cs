using UnityEngine;
using UnityEngine.Serialization;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
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

        [Tooltip("Optional: what the TARGET player must be holding. Leave empty for no constraint at " +
                 "all on the target's hands (NOT \"target must be empty\") - absorbed from the deleted " +
                 "MutualPlayerInteractStep.")]
        public ItemDefinition targetRequiredItem;

        [Tooltip("State flags the target's item must carry. None = any state.")]
        public ItemState targetRequiredState = ItemState.None;

        // [Obsolete] identity moved to targetRequiredItem + targetRequiredState.
        [FormerlySerializedAs("targetRequiredItemName")]
        [Tooltip("[DEPRECATED] Old string identity. Assign 'Target Required Item' instead.")]
        public string legacyTargetRequiredItemName;

        public override string GetObjectiveText()
        {
            string text = requiredItem == null
                ? "Interact with another court member"
                : $"Use <color=#5DADE2>{requiredItem.displayName}</color> on another player";

            if (targetRequiredItem != null)
                text += $" holding <color=#5DADE2>{targetRequiredItem.displayName}</color>";

            return text;
        }

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
        {
            if (targetInteractable == null) return false;

            // Verify the target is actually another player
            PlayerController targetPlayer = targetInteractable.GetComponent<PlayerController>();
            if (targetPlayer == null) return false;

            // My-side check: empty-handed when requiredItem == null, else must match.
            if (requiredItem == null)
            {
                if (player.GetHeldItem() != null) return false;
            }
            else
            {
                PickupItem heldItem = player.GetHeldItem();
                if (heldItem == null || !heldItem.Matches(requiredItem, requiredState)) return false;
            }

            // Target-side check: only constrained when targetRequiredItem is assigned. Null means no
            // constraint at all on the target's hands - old PlayerInteractStep tasks never needed to
            // say anything about the target, so this stays permissive by default.
            if (targetRequiredItem != null)
            {
                PickupItem targetHeldItem = targetPlayer.GetHeldItem();
                if (targetHeldItem == null || !targetHeldItem.Matches(targetRequiredItem, targetRequiredState)) return false;
            }

            return true;
        }

        public override string GetConfigurationWarning()
        {
            if (requiredItem == null && !string.IsNullOrEmpty(legacyRequiredHeldItemName))
                return $"legacyRequiredHeldItemName '{legacyRequiredHeldItemName}' is set but Required Item is not - assign the ItemDefinition.";
            if (targetRequiredItem == null && !string.IsNullOrEmpty(legacyTargetRequiredItemName))
                return $"legacyTargetRequiredItemName '{legacyTargetRequiredItemName}' is set but Target Required Item is not - assign the ItemDefinition.";
            return null;
        }
    }
}
