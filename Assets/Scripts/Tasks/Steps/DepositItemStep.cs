using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 6. DEPOSIT ITEM
    // ---------------------------------------------------
    [MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: null)]
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
}
