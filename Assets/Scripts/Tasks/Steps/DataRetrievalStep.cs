using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 5. DATA RETRIEVAL (Generate & Input)
    // ---------------------------------------------------
    [MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: null)]
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

                    Debug.Log($"[Task System] Code {runtime.GeneratedCode} acquired from {sourceStationID}!");

                    // Force the waypoints (and the code popup) to update to the new destination
                    player.TaskBook.RefreshLocalWaypoints();

                    return false; // Return false because the step isn't fully complete until they input it!
                }
            }
            else
            {
                // Part 2: Inputting the code
                if (loc.locationID == inputStationID)
                {
                    // Unlock the mouse so they can click the keypad UI
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;

                    return true; // Step fully completed!
                }
            }

            return false;
        }
    }
}
