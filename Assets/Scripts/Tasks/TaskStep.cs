using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
    /// <summary>
    /// Base for one step of a task. This is authoring data - it lives inside
    /// <see cref="TaskData.stepTemplates"/> and is never mutated at runtime. Anything a step needs to
    /// remember while a player works through it (progress flags, a generated code) belongs on
    /// <see cref="TaskStepRuntime"/>, handed in via the runtime-aware overloads below.
    /// </summary>
    [System.Serializable]
    public abstract class TaskStep
    {
        [Tooltip("Optional: Assign a Minigame UI Prefab here. If assigned, interacting will launch this minigame instead of instantly completing the step.")]
        public GameObject minigamePrefab;

        /// <summary>
        /// Returns the UI text displayed to the player for this specific step.
        /// </summary>
        public abstract string GetObjectiveText();

        /// <summary>
        /// Runtime-aware objective text. <paramref name="runtime"/> carries this step's per-player state.
        /// The default ignores it; only steps that keep runtime state override this.
        /// </summary>
        public virtual string GetObjectiveText(TaskStepRuntime runtime) => GetObjectiveText();

        /// <summary>
        /// Evaluates whether the player's current action fulfills the requirements of this step.
        /// </summary>
        /// <param name="player">Reference to the player attempting the action.</param>
        /// <param name="targetInteractable">Optional target (Station, Item, or Player) being interacted with.</param>
        /// <returns>True if the step conditions are met.</returns>
        public abstract bool CheckCompletion(PlayerController player, GameObject targetInteractable = null);

        /// <summary>
        /// Runtime-aware completion check. <paramref name="runtime"/> carries this step's per-player,
        /// per-attempt state. The default ignores it and forwards to the stateless overload; only steps
        /// that need runtime state override this.
        /// </summary>
        public virtual bool CheckCompletion(PlayerController player, GameObject targetInteractable, TaskStepRuntime runtime)
            => CheckCompletion(player, targetInteractable);

        /// <summary>
        /// Context-aware completion check. The default forwards to the (player, target, runtime)
        /// overload above using <see cref="TaskEvalContext.Target"/> - existing steps that only care
        /// about Interact-shaped evaluation need no changes. Override this instead when a step must
        /// react to an event with no interaction target (e.g. <see cref="TaskEvalReason.EmotePerformed"/>).
        /// </summary>
        public virtual bool CheckCompletion(TaskEvalContext ctx, TaskStepRuntime runtime)
            => CheckCompletion(ctx.Player, ctx.Target, runtime);

        /// <summary>
        /// Editor-only sanity check, run for every step by <see cref="TaskData.OnValidate"/>. Return a
        /// message to flag mis-authored data (e.g. a legacy identity string is filled in but the typed
        /// <see cref="ItemDefinition"/> reference that replaced it is not); return null when the step is
        /// configured correctly.
        /// </summary>
        public virtual string GetConfigurationWarning() => null;

        /// <summary>
        /// The <see cref="TaskLocation"/> for an interacted object: on the object itself, else its
        /// nearest ancestor (some stations put the collider on a child). Null when neither carries one.
        /// Replaces the old <c>gameObject.name.Contains(id)</c> station matching.
        /// </summary>
        protected static TaskLocation ResolveLocation(GameObject go)
        {
            if (go == null) return null;
            TaskLocation loc = go.GetComponent<TaskLocation>();
            if (loc == null) loc = go.GetComponentInParent<TaskLocation>();
            return loc;
        }
    }
}
