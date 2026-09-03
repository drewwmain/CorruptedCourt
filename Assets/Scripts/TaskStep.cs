using UnityEngine;

[System.Serializable]
public abstract class TaskStep
{
    [HideInInspector] public bool isCompleted = false;

    [Tooltip("Optional: Assign a Minigame UI Prefab here. If assigned, interacting will launch this minigame instead of instantly completing the step.")]
    public GameObject minigamePrefab;

    /// <summary>
    /// Returns the UI text displayed to the player for this specific step.
    /// </summary>
    public abstract string GetObjectiveText();

    /// <summary>
    /// Evaluates whether the player's current action fulfills the requirements of this step.
    /// </summary>
    /// <param name="player">Reference to the player attempting the action.</param>
    /// <param name="targetInteractable">Optional target (Station, Item, or Player) being interacted with.</param>
    /// <returns>True if the step conditions are met.</returns>
    public abstract bool CheckCompletion(PlayerController player, GameObject targetInteractable = null);

    /// <summary>
    /// Optional: Reverses the step's completion state if the requirement is broken (e.g. dropping a required item).
    /// </summary>
    public virtual void ResetStep()
    {
        isCompleted = false;
    }

    /// <summary>
    /// Returns an independent copy of this step for a single player's <see cref="TaskInstance"/>.
    /// The default is a shallow <see cref="object.MemberwiseClone"/>, which is correct only while
    /// every field is a value type or a string (both copy by value) plus the shared
    /// <see cref="minigamePrefab"/> reference (which SHOULD stay shared — it points at an asset).
    /// Any subclass that adds a mutable reference-type field (a <c>List</c>, array, or class it
    /// writes to at runtime) MUST override this to deep-copy that field, or two players will alias
    /// the same object and corrupt each other's progress.
    /// </summary>
    public virtual TaskStep Clone() => (TaskStep)MemberwiseClone();
}