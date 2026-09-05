using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 8. PROCESS ITEM (Cut, Polish, Light)
    // ---------------------------------------------------
    [MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: null)]
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
}
