using System.Collections.Generic;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Tasks
{
    /// <summary>
    /// Per-player, per-attempt runtime state for one step of a <see cref="TaskInstance"/>. Never
    /// serialized - the <see cref="TaskStep"/> in <see cref="TaskData.stepTemplates"/> is immutable
    /// authoring data, so anything a step mutates while a player works through it lives here instead.
    /// </summary>
    public class TaskStepRuntime
    {
        /// <summary>The authoring step this state belongs to. Read-only template.</summary>
        public TaskStep Template { get; }

        /// <summary>
        /// DepositItemStep only (B9). The specific TaskDepositStation instances (matching the step's
        /// targetStationID) that were still Pending at task assignment - before the player had any
        /// chance to act on this task. Populated eagerly by DepositItemStep.OnRuntimeCreated, not
        /// lazily on evaluation: for an instant (non-minigame) deposit, TaskDepositStation.OnInteract
        /// places the item BEFORE CheckCompletion ever runs, so a lazy first-evaluation snapshot would
        /// see the station as already Satisfied by the very deposit it's supposed to credit. Exists so
        /// a station already Satisfied before the attempt started - including by this same player's
        /// own earlier task, since nothing decays (R5) - can never satisfy a fresh attempt.
        /// </summary>
        public HashSet<TaskDepositStation> EligibleDepositStations;

        /// <summary>OrderRecallStep only (B17). The fixed order copied from the LedgerStation the
        /// moment this player reads it. Null until <see cref="HasObserved"/>.</summary>
        public List<int> ObservedSequence;

        /// <summary>OrderRecallStep only. True once this player has read the Ledger this attempt.</summary>
        public bool HasObserved;

        /// <summary>OrderRecallStep only. One-shot: set the moment the order is first observed - the
        /// order-recall UI panel shows the reveal popup once, then clears this itself.</summary>
        public bool SequenceRevealPending;

        /// <summary>OrderRecallStep only. One-shot: set each time the player interacts with the input
        /// station having already observed the order. The order-recall UI panel opens the input popup
        /// off this and clears it (on submit, correct or not, or on cancel).</summary>
        public bool InputPending;

        public TaskStepRuntime(TaskStep template)
        {
            Template = template;
            template?.OnRuntimeCreated(this);
        }
    }
}
