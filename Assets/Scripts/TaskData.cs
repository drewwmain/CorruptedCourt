using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Authoring asset for one task: its identity, description, spawn rules, and the template list of
/// steps. Pure data - it holds NO per-player runtime state. Progress (step index, per-attempt
/// values like a retrieved code) lives on <see cref="TaskInstance"/>, one per player.
/// </summary>
[CreateAssetMenu(fileName = "New Task Data", menuName = "Social Deduction/Task Data")]
public class TaskData : ScriptableObject
{
    [Header("Basic Info")]
    public string taskID;
    public string taskName;
    [TextArea] public string taskDescription;

    [Header("Task Settings")]
    public bool isSabotage = false;

    [Header("Stage & Prerequisites")]
    [Tooltip("Which rounds/stages this task is allowed to spawn in (e.g., 1, 2, 3)")]
    public List<int> allowedStages = new List<int> { 1, 2, 3 };
    [Tooltip("If the prerequisite task was NOT completed, spawn this item to ensure the task is still possible")]
    public PickupItem autoSpawnItemPrefab;

    [Tooltip("The ID of the TaskDepositStation where the auto-spawned item will be injected (e.g., 'Armory')")]
    public string autoSpawnLocationID;

    [Tooltip("A task from a previous round that connects to this task (Optional)")]
    public TaskData prerequisiteTask;

    [Header("Modular Steps")]
    [Tooltip("Authoring template for this task's steps, in order. TaskInstance builds a per-player " +
             "runtime copy from this list; nothing mutates these entries while the game runs.")]
    [SerializeReference]
    [FormerlySerializedAs("steps")]
    public List<TaskStep> stepTemplates = new List<TaskStep>();

    // Editor-only sanity checks. Runs whenever the asset is edited or (re)loaded in the Editor.
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(taskID))
            Debug.LogWarning($"[TaskData] '{name}' has an empty taskID - it will not match any task step or waypoint lookup.", this);

        if (stepTemplates != null)
        {
            for (int i = 0; i < stepTemplates.Count; i++)
            {
                if (stepTemplates[i] == null)
                    Debug.LogWarning($"[TaskData] '{name}' stepTemplates[{i}] is empty (no step type selected).", this);
            }
        }
    }
}
