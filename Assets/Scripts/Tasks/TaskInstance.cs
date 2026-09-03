using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-player, runtime-only copy of a task's progress.
///
/// <para>
/// <see cref="TaskData"/> is a <see cref="ScriptableObject"/> — a single shared asset. Today it
/// mutates <c>currentStepIndex</c> at runtime and its <see cref="TaskStep"/> entries mutate
/// <c>isCompleted</c>, <c>hasCode</c>, and <c>generatedCode</c>. Two players handed the same
/// <see cref="TaskData"/> therefore share one progress counter — a correctness bug.
/// </para>
///
/// <para>
/// A <see cref="TaskInstance"/> owns its own deep-copied list of steps and its own step index, so
/// each player advances independently. This class is additive: nothing constructs it yet. The
/// <see cref="TaskData"/> type name is kept (as <see cref="Definition"/>) so no <c>.asset</c> files
/// break; its runtime state fields are stripped in a later pass.
/// </para>
/// </summary>
public class TaskInstance
{
    /// <summary>The authoring asset this instance was created from. Read-only template data.</summary>
    public TaskData Definition { get; }

    private readonly List<TaskStep> steps = new List<TaskStep>();

    /// <summary>Index of the active step. Advances as steps complete; equals <c>steps.Count</c> when done.</summary>
    public int CurrentStepIndex { get; private set; }

    /// <summary>All steps for this instance, in order. These are per-instance copies, safe to mutate.</summary>
    public IReadOnlyList<TaskStep> Steps => steps;

    /// <summary>True once every step has been completed.</summary>
    public bool IsComplete => CurrentStepIndex >= steps.Count;

    public TaskInstance(TaskData definition)
    {
        Definition = definition;

        if (definition != null && definition.steps != null)
        {
            foreach (TaskStep source in definition.steps)
            {
                if (source == null) continue; // Inspector list can contain empty entries.

                TaskStep copy = source.Clone();
                copy.isCompleted = false; // Start fresh regardless of stale state on the shared asset.
                steps.Add(copy);
            }
        }

        CurrentStepIndex = 0;
    }

    /// <summary>
    /// Gets the current active step, or null if the entire task is complete.
    /// </summary>
    public TaskStep GetCurrentStep()
    {
        if (CurrentStepIndex < steps.Count)
        {
            return steps[CurrentStepIndex];
        }
        return null;
    }

    /// <summary>
    /// Evaluates the active step against the player's action. Advances <see cref="CurrentStepIndex"/>
    /// if successful. Ported from <c>TaskData.EvaluateCurrentStep</c>, including minigame interception.
    /// </summary>
    /// <returns>True if the overall task is now complete.</returns>
    public bool EvaluateCurrentStep(PlayerController player, GameObject targetInteractable = null, bool skipMinigame = false)
    {
        if (IsComplete) return true;

        TaskStep activeStep = GetCurrentStep();
        if (activeStep != null && activeStep.CheckCompletion(player, targetInteractable))
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
    /// Minigame upon success. Ported from <c>TaskData.CompleteActiveStep</c>.
    /// </summary>
    public void CompleteActiveStep()
    {
        TaskStep activeStep = GetCurrentStep();
        if (activeStep != null)
        {
            activeStep.isCompleted = true;
            CurrentStepIndex++;
            Debug.Log($"[Task System] Advanced '{(Definition != null ? Definition.taskName : "<null>")}' to step {CurrentStepIndex}/{steps.Count}");
        }
    }

    /// <summary>
    /// Fetches current objective text for the UI. Ported from <c>TaskData.GetCurrentObjectiveText</c>.
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
    /// Checks if any previously completed steps (like acquiring an item) are broken, and reverts
    /// progress if necessary. Ported from <c>TaskData.CheckForTaskRegression</c>.
    /// </summary>
    public void CheckForTaskRegression(PlayerController player)
    {
        if (steps.Count == 0) return;

        // Loop through all steps that have already been completed
        for (int i = 0; i < CurrentStepIndex; i++)
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
