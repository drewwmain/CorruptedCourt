using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-player, runtime-only state for one assigned task.
///
/// <para>
/// <see cref="TaskData"/> is a <see cref="ScriptableObject"/> - a single shared asset - so its
/// <see cref="TaskData.stepTemplates"/> are immutable authoring data. A <see cref="TaskInstance"/>
/// owns its own step index plus one <see cref="TaskStepRuntime"/> per step, so each player advances
/// (and, for steps like <see cref="DataRetrievalStep"/>, accumulates per-attempt state)
/// independently. Two players handed the same <see cref="TaskData"/> no longer share a progress
/// counter.
/// </para>
/// </summary>
public class TaskInstance
{
    /// <summary>The authoring asset this instance was created from. Read-only template data.</summary>
    public TaskData Definition { get; }

    // One runtime wrapper per authoring step, in order. Built once in the constructor.
    private readonly List<TaskStepRuntime> stepRuntimes = new List<TaskStepRuntime>();

    /// <summary>Index of the active step. Advances as steps complete; equals the step count when done.</summary>
    public int CurrentStepIndex { get; private set; }

    /// <summary>True once every step has been completed.</summary>
    public bool IsComplete => CurrentStepIndex >= stepRuntimes.Count;

    public TaskInstance(TaskData definition)
    {
        Definition = definition;

        if (definition != null && definition.stepTemplates != null)
        {
            foreach (TaskStep template in definition.stepTemplates)
            {
                if (template == null) continue; // Inspector list can contain empty entries.
                stepRuntimes.Add(new TaskStepRuntime(template));
            }
        }

        CurrentStepIndex = 0;
    }

    /// <summary>The active step's authoring template, or null if the task is complete.</summary>
    public TaskStep GetCurrentStep()
        => CurrentStepIndex >= 0 && CurrentStepIndex < stepRuntimes.Count
            ? stepRuntimes[CurrentStepIndex].Template
            : null;

    /// <summary>The active step's per-player runtime state, or null if the task is complete.</summary>
    public TaskStepRuntime CurrentStepRuntime
        => CurrentStepIndex >= 0 && CurrentStepIndex < stepRuntimes.Count
            ? stepRuntimes[CurrentStepIndex]
            : null;

    /// <summary>
    /// Evaluates the active step against the player's action. Advances <see cref="CurrentStepIndex"/>
    /// if successful, including minigame interception.
    /// </summary>
    /// <returns>True if the overall task is now complete.</returns>
    public bool EvaluateCurrentStep(PlayerController player, GameObject targetInteractable = null, bool skipMinigame = false)
    {
        if (IsComplete) return true;

        TaskStep activeStep = GetCurrentStep();
        if (activeStep != null && activeStep.CheckCompletion(player, targetInteractable, CurrentStepRuntime))
        {
            // --- MINIGAME INTERCEPTION ---
            // Skipped when the caller already ran the minigame (e.g. a held item's own minigame).
            if (!skipMinigame && activeStep.minigamePrefab != null)
            {
                // Hand the minigame THIS instance, so its completion callback
                // (PlayerController.FinishMinigame) advances this player's copy, not the shared asset.
                player.StartMinigame(activeStep.minigamePrefab, this, targetInteractable);
                return false; // Return false because the step is NOT complete yet!
            }

            // --- NORMAL INSTANT COMPLETION ---
            CompleteActiveStep();
        }

        return IsComplete;
    }

    /// <summary>
    /// Marks the active step complete and advances. Called instantly by normal steps, or later by a
    /// Minigame upon success.
    /// </summary>
    public void CompleteActiveStep()
    {
        if (IsComplete) return;

        CurrentStepIndex++;
        Debug.Log($"[Task System] Advanced '{(Definition != null ? Definition.taskName : "<null>")}' to step {CurrentStepIndex}/{stepRuntimes.Count}");
    }

    /// <summary>Fetches current objective text for the UI.</summary>
    public string GetCurrentObjectiveText()
    {
        TaskStep activeStep = GetCurrentStep();
        if (activeStep != null)
        {
            return activeStep.GetObjectiveText(CurrentStepRuntime);
        }
        return "Completed";
    }

    /// <summary>
    /// Checks if any previously completed steps (like acquiring an item) are broken, and reverts
    /// progress if necessary.
    /// </summary>
    public void CheckForTaskRegression(PlayerController player)
    {
        if (stepRuntimes.Count == 0) return;

        // Loop through all steps that have already been completed
        for (int i = 0; i < CurrentStepIndex && i < stepRuntimes.Count; i++)
        {
            TaskStep step = stepRuntimes[i].Template;

            // Specifically check if an AcquireItemStep has been invalidated because we dropped/threw the item
            if (step is AcquireItemStep acquireStep)
            {
                // The required item counts whether it's in the active hand or the off-hand.
                bool stillHolding = player.IsHoldingItemNamed(acquireStep.requiredItemName);

                if (!stillHolding)
                {
                    // Regression triggered! Roll back to this step's index
                    CurrentStepIndex = i;

                    Debug.Log($"[Task System] Task '{(Definition != null ? Definition.taskName : "<null>")}' regressed to step {CurrentStepIndex} because required item '{acquireStep.requiredItemName}' was dropped/thrown.");

                    // Refresh UI waypoints immediately
                    player.RefreshLocalWaypoints();
                    break; // Only roll back to the earliest broken step
                }
            }
        }
    }
}
