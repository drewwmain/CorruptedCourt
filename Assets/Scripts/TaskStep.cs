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
}