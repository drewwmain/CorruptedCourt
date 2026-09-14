using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 3. TASK STATION INTERACTION (Standard & Group)
    // ---------------------------------------------------
    [System.Serializable]
    public class StationInteractStep : TaskStep
    {
        [Tooltip("The locationID of the station to interact with (exact match against TaskLocation.locationID).")]
        public string targetStationID;

        [Tooltip("Set to > 1 if multiple players must interact simultaneously.")]
        public int requiredSimultaneousPlayers = 1;

        [Tooltip("When true, the station's TaskStationState must be Pending - re-lighting an already-lit " +
                 "candle (or similar) does nothing. Requires a TaskStationState on the station.")]
        public bool requiresStationPending;

        public override string GetObjectiveText()
        {
            if (requiredSimultaneousPlayers > 1)
                return $"Gather {requiredSimultaneousPlayers} players at the <color=#F4D03F>{targetStationID}</color>";

            return $"Interact with the <color=#F4D03F>{targetStationID}</color>";
        }

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
        {
            if (targetInteractable == null) return false;

            // The object we interacted with must BE (or sit under) the target station.
            TaskLocation loc = ResolveLocation(targetInteractable);
            if (loc == null || loc.locationID != targetStationID) return false;

            TaskStationState state = (requiresStationPending || requiredSimultaneousPlayers > 1)
                ? loc.GetComponent<TaskStationState>()
                : null;

            if (requiresStationPending)
            {
                if (state == null || state.Current != TaskStationState.Condition.Pending) return false;
            }

            // If it's a group task, check actual seat occupancy rather than momentary proximity.
            if (requiredSimultaneousPlayers > 1)
            {
                if (state == null)
                {
                    Log.Warn($"[StationInteractStep] '{targetStationID}' needs a TaskStationState for its multi-player seat check.");
                    return false;
                }

                // No hold-to-interact mechanic exists anywhere in this codebase to release a seat on
                // button-up, so lazily re-validate before claiming: release anyone no longer actually
                // near the station, then try to claim this press's seat.
                state.PruneStaleSeats(player.Interactor.InteractionRange);
                state.TryClaimSeat(player);

                return state.SeatsFull;
            }

            return true; // Standard single-player interaction successful
        }

        public override ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime)
        {
            TaskStationState station = StationRegistry.FindNearestPending(targetStationID, player.transform.position);
            if (station != null) return new ObjectiveTarget { Transform = station.transform, IsDirect = true };

            // No TaskStationState registered here at all - fall back to a plain location lookup rather
            // than claiming R4's "everything's satisfied" for a station that isn't tracked that way.
            if (StationRegistry.ByLocation(targetStationID).Count == 0)
            {
                Transform loc = FindLocationOrZone(targetStationID);
                return loc != null ? new ObjectiveTarget { Transform = loc, IsDirect = true } : default;
            }

            return new ObjectiveTarget
            {
                Transform = null,
                Hint = $"Every {targetStationID} is already done - find or ask for one to be reset."
            };
        }
    }
}
