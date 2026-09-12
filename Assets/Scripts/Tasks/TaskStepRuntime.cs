
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

        public TaskStepRuntime(TaskStep template)
        {
            Template = template;
        }
    }
}
