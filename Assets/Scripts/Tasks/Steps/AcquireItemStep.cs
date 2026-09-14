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

        [Tooltip("Optional: accept any member of this set instead of (or in addition to) Required Item " +
                 "- e.g. any Instrument.")]
        public ItemDefinitionSet requiredItemSet;

        // [Obsolete] identity moved to requiredItem + requiredState.
        [FormerlySerializedAs("requiredItemName")]
        [Tooltip("[DEPRECATED] Old string identity. Assign 'Required Item' instead.")]
        public string legacyRequiredItemName;

        public override string GetObjectiveText()
        {
            string what;
            if (requiredItem != null) what = requiredItem.displayName;
            else if (requiredItemSet != null) what = requiredItemSet.name;
            else what = legacyRequiredItemName;

            return $"Find and pick up: <color=#5DADE2>{what}</color>";
        }

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
        {
            // Counts the item whether it's in the active hand or the off-hand.
            if (requiredItem != null && player.IsHoldingItem(requiredItem, requiredState)) return true;

            if (requiredItemSet != null && requiredItemSet.members != null)
            {
                foreach (ItemDefinition member in requiredItemSet.members)
                {
                    if (member != null && player.IsHoldingItem(member, requiredState)) return true;
                }
            }

            // A held container's payload can also satisfy this step - e.g. a Quiver with Arrows in it
            // satisfies "acquire Arrow" - without the Quiver itself needing to BE that identity.
            if (PayloadMatches(player.GetHeldItem()) || PayloadMatches(player.GetLeftHeldItem())) return true;

            return false;
        }

        private bool PayloadMatches(PickupItem heldItem)
        {
            if (heldItem == null) return false;

            ItemPayload payload = heldItem.GetComponent<ItemPayload>();
            if (payload == null || payload.contents == null || payload.count <= 0) return false;

            if (requiredItem != null && payload.contents == requiredItem) return true;
            if (requiredItemSet != null && requiredItemSet.Contains(payload.contents)) return true;
            return false;
        }

        public override string GetConfigurationWarning()
            => requiredItem == null && requiredItemSet == null && !string.IsNullOrEmpty(legacyRequiredItemName)
                ? $"legacyRequiredItemName '{legacyRequiredItemName}' is set but Required Item is not - assign the ItemDefinition."
                : null;

        public override ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime)
        {
            if (ItemSourceResolver.Instance == null) return default;
            Vector3 from = player.transform.position;

            if (requiredItem != null)
                return ItemSourceResolver.Instance.Resolve(new ItemIdentity { definition = requiredItem, state = requiredState }, from);

            if (requiredItemSet != null && requiredItemSet.members != null)
            {
                foreach (ItemDefinition member in requiredItemSet.members)
                {
                    if (member == null) continue;
                    ObjectiveTarget target = ItemSourceResolver.Instance.Resolve(new ItemIdentity { definition = member, state = requiredState }, from);
                    if (target.Transform != null) return target;
                }
            }

            return default;
        }
    }
}
