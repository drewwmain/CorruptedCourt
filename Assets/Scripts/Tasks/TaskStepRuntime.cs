
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

        // --- DataRetrievalStep: a code retrieved from one station and entered at another. Generated
        //     per player, per attempt - it must never touch the shared asset. ---

        /// <summary>True once this player has retrieved the code from the source station.</summary>
        public bool HasCode;

        /// <summary>The code this player retrieved. Empty until <see cref="HasCode"/> is set.</summary>
        public string GeneratedCode = "";

        /// <summary>
        /// Set by <see cref="DataRetrievalStep"/> the instant it generates the code; the view clears it
        /// once it has shown the "memorize this" popup. Lets the step hand the code to the UI without
        /// referencing it - it just flags the runtime and the tasks-changed signal does the rest. One-shot.
        /// </summary>
        public bool CodeRevealPending;

        public TaskStepRuntime(TaskStep template)
        {
            Template = template;
        }
    }
}
