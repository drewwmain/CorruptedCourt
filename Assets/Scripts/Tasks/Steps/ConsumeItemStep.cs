using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Minigames;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 7. CONSUME ITEM (Eat / Drink - destroys item)
    // ---------------------------------------------------
    [MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: null)]
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
}
