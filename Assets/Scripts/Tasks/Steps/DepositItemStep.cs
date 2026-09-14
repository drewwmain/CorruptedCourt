using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 6. DEPOSIT ITEM
    // ---------------------------------------------------
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
        [Tooltip("When true, the step only advances for the player who actually made the deposit, at a " +
                 "station that was still Pending when THIS attempt began. Default true (R3) - a task " +
                 "should only opt back into shared credit deliberately.")]
        private bool requireOwnDeposit = true;

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
            => CheckCompletion(player, targetInteractable, null);

        // Snapshots eligible stations at task assignment - see TaskStepRuntime.EligibleDepositStations
        // for why this must happen here (eagerly, before the player can act) and not lazily on the
        // step's first evaluation.
        public override void OnRuntimeCreated(TaskStepRuntime runtime)
        {
            if (requireOwnDeposit) runtime.EligibleDepositStations = SnapshotEligibleStations();
        }

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable, TaskStepRuntime runtime)
        {
            if (requiredItem == null) return false; // un-migrated step: nothing to match against

            // Scoped to stations that were still Pending at task assignment (R3/R4) - only when
            // requireOwnDeposit is enforced. Populated eagerly by OnRuntimeCreated; the lazy fallback
            // here is just a safety net (e.g. a runtime built some other way) and should normally be a
            // no-op since OnRuntimeCreated already ran.
            HashSet<TaskDepositStation> eligible = null;
            if (requireOwnDeposit && runtime != null)
            {
                if (runtime.EligibleDepositStations == null)
                    runtime.EligibleDepositStations = SnapshotEligibleStations();
                eligible = runtime.EligibleDepositStations;
            }

            // The step is complete once an eligible station holds an item that matches requiredItem +
            // requiredState. If requireOwnDeposit is set, only THIS player's deposit counts.
            foreach (TaskLocation location in TaskLocation.AllLocations)
            {
                if (location == null || location.locationID != targetStationID) continue;

                TaskDepositStation station = location.GetComponent<TaskDepositStation>();
                if (station == null) station = location.GetComponentInParent<TaskDepositStation>();
                if (station == null || station.depositedItemSlots == null) continue;

                // Not captured when this attempt began (had a TaskStationState and was already
                // Satisfied at that moment) - can't count, even for my own past deposit.
                if (eligible != null && !eligible.Contains(station)) continue;

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

        // Captures which TaskDepositStation instances at targetStationID count for this task, as of
        // right now (called from OnRuntimeCreated, at task assignment). A station with a
        // TaskStationState is included only if it's Pending at that moment; a station with none yet
        // (not every TaskDepositStation prefab has one) is included unconditionally, so it keeps
        // working exactly as before - protected only by requireOwnDeposit, not this scoping.
        private HashSet<TaskDepositStation> SnapshotEligibleStations()
        {
            var result = new HashSet<TaskDepositStation>();

            foreach (TaskLocation location in TaskLocation.AllLocations)
            {
                if (location == null || location.locationID != targetStationID) continue;

                TaskDepositStation station = location.GetComponent<TaskDepositStation>();
                if (station == null) station = location.GetComponentInParent<TaskDepositStation>();
                if (station == null) continue;

                TaskStationState state = station.GetComponent<TaskStationState>();
                if (state == null || state.Current == TaskStationState.Condition.Pending)
                    result.Add(station);
            }

            return result;
        }

        public override string GetConfigurationWarning()
        {
            if (requiredItem == null && !string.IsNullOrEmpty(legacyRequiredItemName))
                return $"legacyRequiredItemName '{legacyRequiredItemName}' is set but Required Item is not - assign the ItemDefinition.";

            // TODO(B18): TaskDataValidator should warn here when requireOwnDeposit is explicitly
            // false - per ARCHITECTURE.md §III.4, the permissive form should be a deliberate
            // authoring choice, not an inherited default.
            return null;
        }

        public override ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime)
        {
            TaskStationState station = StationRegistry.FindNearestPending(targetStationID, player.transform.position);
            if (station != null) return new ObjectiveTarget { Transform = station.transform, IsDirect = true };

            // No TaskStationState registered at this locationID at all - not every deposit target has
            // one yet (B9/B19) - so fall back to a plain location lookup rather than reporting R4's
            // "everything's satisfied" hint for a station that was never tracked in the first place.
            if (StationRegistry.ByLocation(targetStationID).Count == 0)
            {
                Transform loc = FindLocationOrZone(targetStationID);
                return loc != null ? new ObjectiveTarget { Transform = loc, IsDirect = true } : default;
            }

            // Registered stations exist here, but none are Pending right now (R4).
            return new ObjectiveTarget
            {
                Transform = null,
                Hint = $"Every {targetStationID} is already filled - find or ask for one to be reset."
            };
        }
    }
}
