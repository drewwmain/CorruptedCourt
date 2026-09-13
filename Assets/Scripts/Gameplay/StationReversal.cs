using UnityEngine;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Player-facing "undo" for a satisfied station (ARCHITECTURE.md §8.2): resets a lit candle,
    /// straightened painting, etc. back to Pending. Sits beside TaskStationState. Uncredited and
    /// unrestricted (R1/R4) - TryRevert only ever touches station condition, never a player's
    /// TaskStepRuntime/step index, so this can never un-complete anyone's task.
    ///
    /// Instant press, not hold: no hold-to-interact mechanism exists anywhere in this codebase
    /// (PlayerController.OnInteract only reacts to the press event and ignores release entirely) and
    /// building one was ruled out of scope for this phase - see IMPLEMENTATION_PROMPTS.md B7.
    /// PlayerInteractor.ResolveInteractable checks IsActive and only routes here ahead of the
    /// station's own IInteractable while a reversible station is actually Satisfied.
    ///
    /// For a deposit station, reversal is retrieval - TaskDepositStation already has stage-gated
    /// retrieval (retrievableFromStage, RetrieveTestMode), so this stays inactive there entirely
    /// rather than competing with that existing path (its own TaskStationState.TryRevert is a no-op
    /// for deposit stations anyway - see TaskStationState's class summary).
    /// </summary>
    [RequireComponent(typeof(TaskStationState))]
    public class StationReversal : MonoBehaviour, IInteractable
    {
        private TaskStationState state;
        private TaskDepositStation depositStation;

        /// <summary>True while this should intercept interaction instead of the station's own
        /// IInteractable. Read by PlayerInteractor.ResolveInteractable.</summary>
        public bool IsActive => depositStation == null && state != null
                                 && state.PlayerReversible
                                 && state.Current == TaskStationState.Condition.Satisfied;

        private void Awake()
        {
            state = GetComponent<TaskStationState>();
            depositStation = GetComponent<TaskDepositStation>();
        }

        public string GetInteractionPrompt()
            => $"Press <color=#F4D03F>[E]</color> to {state.RevertVerb} the {state.LocationID}";

        public void OnInteract(GameObject interactor)
        {
            state.TryRevert(interactor.GetComponent<PlayerController>());
        }
    }
}
