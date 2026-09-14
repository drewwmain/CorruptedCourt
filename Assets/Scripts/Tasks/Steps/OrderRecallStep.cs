using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // ORDER RECALL (read a fixed order at one station, reproduce it at another)
    // ---------------------------------------------------
    [System.Serializable]
    public class OrderRecallStep : TaskStep
    {
        [Tooltip("locationID of the station that shows the fixed order (the Ledger).")]
        public string sourceStationID;

        [Tooltip("locationID of the station where the recalled order is submitted (the Confirmation Box).")]
        public string inputStationID;

        [Tooltip("Expected order length, for objective text only - the Ledger's own order (whatever " +
                 "length it actually generated) is always what's copied and checked.")]
        public int sequenceLength = 3;

        public override string GetObjectiveText() => GetObjectiveText(null);

        public override string GetObjectiveText(TaskStepRuntime runtime)
        {
            if (runtime != null && runtime.HasObserved)
                return $"Recall the order at the <color=#F4D03F>{inputStationID}</color>";
            return $"Read the order at the <color=#F4D03F>{sourceStationID}</color>";
        }

        // Multi-phase, like the deleted DataRetrievalStep - can never complete through the stateless
        // predicate. All real logic is in the runtime-aware overload below; final completion happens
        // externally, when the order-recall UI panel calls TaskInstance.CompleteActiveStep() directly
        // after validating a correct submission (see UIManager's order-recall panel).
        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null) => false;

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable, TaskStepRuntime runtime)
        {
            if (targetInteractable == null || runtime == null) return false;

            TaskLocation loc = ResolveLocation(targetInteractable);
            if (loc == null) return false;

            if (loc.locationID == sourceStationID)
            {
                if (!runtime.HasObserved)
                {
                    LedgerStation ledger = loc.GetComponent<LedgerStation>();
                    if (ledger == null) ledger = loc.GetComponentInParent<LedgerStation>();
                    if (ledger == null || ledger.Sequence == null) return false;

                    runtime.ObservedSequence = new List<int>(ledger.Sequence);
                    runtime.HasObserved = true;
                    runtime.SequenceRevealPending = true;
                    player.TaskBook.RefreshLocalWaypoints();
                }
                return false;
            }

            if (loc.locationID == inputStationID && runtime.HasObserved)
            {
                runtime.InputPending = true;
                player.TaskBook.RefreshLocalWaypoints();
            }

            return false;
        }

        public override ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime)
        {
            string targetID = (runtime != null && runtime.HasObserved) ? inputStationID : sourceStationID;
            Transform t = FindLocationOrZone(targetID);
            return t != null ? new ObjectiveTarget { Transform = t, IsDirect = true } : default;
        }

        public override string GetConfigurationWarning()
        {
            if (string.IsNullOrEmpty(sourceStationID)) return "Source Station ID (the Ledger) is not set.";
            if (string.IsNullOrEmpty(inputStationID)) return "Input Station ID (the Confirmation Box) is not set.";
            return null;
        }
    }
}
