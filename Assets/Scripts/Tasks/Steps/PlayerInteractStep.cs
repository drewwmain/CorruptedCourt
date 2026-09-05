using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 4. PLAYER INTERACTION
    // ---------------------------------------------------
    [MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: null)]
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
}
