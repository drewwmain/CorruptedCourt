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
        /// targetStationID) that were still Pending when this player's current attempt at the step
        /// began. Null until the step's first evaluation snapshots it. Exists so a station already
        /// Satisfied before the attempt started - including by this same player's own earlier task,
        /// since nothing decays (R5) - can never satisfy a fresh attempt.
        /// </summary>
        public HashSet<TaskDepositStation> EligibleDepositStations;

        public TaskStepRuntime(TaskStep template)
        {
            Template = template;
        }
    }
}
