using UnityEngine;
using System.Collections.Generic;

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
    [SerializeReference]
    public List<TaskStep> steps = new List<TaskStep>();

    // Legacy runtime progress field. Per-player progress now lives on TaskInstance (see
    // Assets/Scripts/Tasks/TaskInstance.cs); nothing reads this any more. Kept for now so no
    // TaskData .asset loses serialized data - it is stripped in a later pass.
    [HideInInspector] public int currentStepIndex = 0;
}