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
    
    [HideInInspector] public int currentStepIndex = 0;

    /// <summary>
    /// Resets progress (typically called at match/stage start when tasks are assigned).
    /// </summary>
    public void InitializeTask()
    {
        currentStepIndex = 0;
        foreach (var step in steps)
        {
            if (step != null) step.isCompleted = false;
        }
    }

    /// <summary>
    /// Gets the current active step, or null if the entire task is complete.
    /// </summary>
    public TaskStep GetCurrentStep()
    {
        if (steps != null && currentStepIndex < steps.Count)
        {
            return steps[currentStepIndex];
        }
        return null;
    }

    /// <summary>
    /// Checks if the overall task is fully finished.
    /// </summary>
    public bool IsTaskComplete()
    {
        return steps == null || currentStepIndex >= steps.Count;
    }

    /// <summary>
    /// Evaluates the active step against the player's action. 
    /// Advances currentStepIndex if successful.
    /// </summary>
    /// <returns>True if the overall task is now complete.</returns>
    public bool EvaluateCurrentStep(PlayerController player, GameObject targetInteractable = null, bool skipMinigame = false)
    {
        if (IsTaskComplete()) return true;

        TaskStep activeStep = GetCurrentStep();
        if (activeStep != null && activeStep.CheckCompletion(player, targetInteractable))
        {
            // --- MINIGAME INTERCEPTION ---
            // Skipped when the caller already ran the minigame (e.g. a held item's own minigame).
            if (!skipMinigame && activeStep.minigamePrefab != null)
            {
                // CHANGED: We now pass the targetInteractable GameObject directly!
                player.StartMinigame(activeStep.minigamePrefab, this, targetInteractable);
                return false; // Return false because the step is NOT complete yet!
            }

            // --- NORMAL INSTANT COMPLETION ---
            CompleteActiveStep();
        }

        return IsTaskComplete();
    }

    /// <summary>
    /// Helper method called instantly by normal tasks, or called later by a Minigame upon success.
    /// </summary>
    public void CompleteActiveStep()
    {
        TaskStep activeStep = GetCurrentStep();
        if (activeStep != null)
        {
            activeStep.isCompleted = true;
            currentStepIndex++;
            Debug.Log($"[Task System] Advanced '{taskName}' to step {currentStepIndex}/{steps.Count}");
        }
    }

    /// <summary>
    /// Helper to fetch current objective text for the UI.
    /// </summary>
    public string GetCurrentObjectiveText()
    {
        TaskStep activeStep = GetCurrentStep();
        if (activeStep != null)
        {
            return activeStep.GetObjectiveText();
        }
        return "Completed";
    }

    /// <summary>
    /// Checks if any previously completed steps (like acquiring an item) are broken, and reverts progress if necessary.
    /// </summary>
    public void CheckForTaskRegression(PlayerController player)
    {
        if (steps == null || steps.Count == 0) return;

        // Loop through all steps that have already been completed
        for (int i = 0; i < currentStepIndex; i++)
        {
            TaskStep step = steps[i];
            
            // Specifically check if an AcquireItemStep has been invalidated because we dropped/threw the item
            if (step is AcquireItemStep acquireStep)
            {
                // The required item counts whether it's in the active hand or the off-hand.
                bool stillHolding = player.IsHoldingItemNamed(acquireStep.requiredItemName);

                if (!stillHolding)
                {
                    // Regression triggered! Roll back to this step's index
                    acquireStep.ResetStep();
                    currentStepIndex = i;
                    
                    Debug.Log($"[Task System] Task '{taskName}' regressed to step {currentStepIndex} because required item '{acquireStep.requiredItemName}' was dropped/thrown.");
                    
                    // Refresh UI waypoints immediately
                    player.RefreshLocalWaypoints();
                    break; // Only roll back to the earliest broken step
                }
            }
        }
    }
}