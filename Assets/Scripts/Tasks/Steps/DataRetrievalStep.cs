using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 5. DATA RETRIEVAL (Generate & Input)
    // ---------------------------------------------------
    [System.Serializable]
    public class DataRetrievalStep : TaskStep
    {
        [Tooltip("locationID of the station the code is retrieved from.")]
        public string sourceStationID;
        [Tooltip("locationID of the station the code is entered at.")]
        public string inputStationID;

        // hasCode / generatedCode used to live here and were serialized onto the shared asset - a
        // per-player, per-attempt value stored on one shared object. They now live on TaskStepRuntime,
        // passed into the runtime-aware overloads below.

        public override string GetObjectiveText() => GetObjectiveText(null);

        public override string GetObjectiveText(TaskStepRuntime runtime)
        {
            bool hasCode = runtime != null && runtime.HasCode;
            if (!hasCode)
                return $"Retrieve the code from the <color=#F4D03F>{sourceStationID}</color>";

            return $"Input code <color=#E74C3C>'{runtime.GeneratedCode}'</color> at the <color=#F4D03F>{inputStationID}</color>";
        }

        // Two-phase flow with per-player state, so it always runs through the runtime-aware overload;
        // the stateless predicate can never complete it on its own.
        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null) => false;

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable, TaskStepRuntime runtime)
        {
            if (targetInteractable == null || runtime == null) return false;

            TaskLocation loc = ResolveLocation(targetInteractable);
            if (loc == null) return false;

            if (!runtime.HasCode)
            {
                // Part 1: Getting the code
                if (loc.locationID == sourceStationID)
                {
                    runtime.HasCode = true;
                    runtime.GeneratedCode = Random.Range(100, 999).ToString(); // Generate the code
                    runtime.CodeRevealPending = true; // the view shows the popup off the tasks-changed signal

                    Log.Game($"[Task System] Code {runtime.GeneratedCode} acquired from {sourceStationID}!");

                    // Force the waypoints (and the code popup) to update to the new destination
                    player.TaskBook.RefreshLocalWaypoints();

                    return false; // Return false because the step isn't fully complete until they input it!
                }
            }
            else
            {
                // Part 2: Inputting the code. Reaching the station opens the code-entry panel; the step
                // is NOT complete until the player submits the correct code (UIManager.SubmitDataCode
                // calls CompleteActiveStep). Signal the view the same way part 1 does - flag the runtime,
                // then let the tasks-changed broadcast open the panel. The step never touches the UI.
                if (loc.locationID == inputStationID)
                {
                    runtime.DataInputPending = true;

                    // Free the cursor so they can type into the panel.
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;

                    // Raises LocalTasksChanged -> the view opens the code-entry panel.
                    player.TaskBook.RefreshLocalWaypoints();

                    return false; // Not complete yet - a correct code entry finishes the step.
                }
            }

            return false;
        }
    }
}
