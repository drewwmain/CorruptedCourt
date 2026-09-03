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

    public TaskStepRuntime(TaskStep template)
    {
        Template = template;
    }
}
