using UnityEngine;
using System.Collections.Generic;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Gameplay
{
    // Owns this player's task list: the active/assigned task lists, assigning and completing tasks, the
    // waypoint-refresh broadcast, and the repeated "evaluate every active task against a target" loop
    // that used to be copy-pasted across PlayerController's interaction paths.
    // Extracted from PlayerController - see PlayerController.cs for player.TaskBook, the single access
    // point external code now uses (RoleManager, TaskManager, MatchManager, TaskDepositStation,
    // the task step types (Tasks/Steps/), WaypointManager, UIManager, TaskInstance, and PlayerInventory
    // all go through it).
    [RequireComponent(typeof(PlayerController))]
    public class PlayerTaskBook : MonoBehaviour
    {
        [Header("Tasks")]
        public bool showWaypoints = false;
        // Per-player runtime task state. TaskInstance is a plain class (not a ScriptableObject), so these
        // are runtime-only - Unity does not serialize them and they never appear in the Inspector.
        public List<TaskInstance> activeTasks = new List<TaskInstance>();
        // The visual state list that never shrinks, keeping UI numbers synced
        public List<TaskInstance> allAssignedTasks = new List<TaskInstance>();

        // The sibling PlayerController on this same GameObject.
        private PlayerController player;

        void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        void OnEnable()
        {
            GameEvents.PlayerZoneChanged += HandleZoneChanged;
        }

        void OnDisable()
        {
            GameEvents.PlayerZoneChanged -= HandleZoneChanged;
        }

        // TaskZone raises PlayerZoneChanged for whichever player crossed its trigger - filter to this
        // component's own player before evaluating, since GameEvents is a shared static bus.
        private void HandleZoneChanged(PlayerController p)
        {
            if (p != player) return;
            EvaluateActiveTasks(new TaskEvalContext { Player = player, Reason = TaskEvalReason.ZoneChanged });
        }

        public void AssignTasks(List<TaskInstance> newTasks)
        {
            activeTasks.Clear();
            activeTasks.AddRange(newTasks);

            allAssignedTasks.Clear();
            allAssignedTasks.AddRange(newTasks);

            // No InitializeTask() call - each TaskInstance is constructed fresh (step index 0, steps
            // deep-copied and un-completed), so a new instance IS the initialization.

            RefreshLocalWaypoints(); // raises LocalTasksChanged; the HUD + waypoints react. No-op if not local.
        }

        // Called when the player successfully interacts with a task station
        public void RemoveCompletedTask(TaskInstance completedTask)
        {
            if (activeTasks.Contains(completedTask))
            {
                // Remove it from active logic, but keep it in allAssignedTasks!
                activeTasks.Remove(completedTask);

                RefreshLocalWaypoints();
            }
        }

        // Signals that the local player's task list / current step changed. The HUD text and the
        // on-screen waypoints are views that subscribe to GameEvents.LocalTasksChanged - this method
        // must NOT reference UIManager or WaypointManager.
        public void RefreshLocalWaypoints()
        {
            if (!player.IsLocal) return;
            GameEvents.RaiseLocalTasksChanged(player);
        }

        // Evaluates every active task's current step against `target`, completing (via TaskManager) any
        // that finish. This is the loop that used to be copy-pasted across PerformInteraction,
        // TryProximityDeposit, HandlePlayerInteraction, and ResolveStandaloneItemMinigame.
        //   stopOnMinigame: bail out of the loop early once a step's evaluation opens a minigame (mirrors
        //     the original "if (isPlayingMinigame) break;" guards - not every original call site had this).
        //   skipMinigame: forwarded to TaskStep.EvaluateCurrentStep - true when a minigame already ran and
        //     this is just advancing state afterward (see ResolveStandaloneItemMinigame).
        // Returns true if at least one task step completed.
        public bool EvaluateActiveTasks(TaskEvalContext ctx, bool stopOnMinigame = false)
        {
            bool anyCompleted = false;
            for (int i = activeTasks.Count - 1; i >= 0; i--)
            {
                TaskInstance task = activeTasks[i];
                if (task.EvaluateCurrentStep(ctx))
                {
                    if (TaskManager.Instance != null) TaskManager.Instance.CompleteTask(player, task);
                    anyCompleted = true;
                }

                if (stopOnMinigame && player.isPlayingMinigame) break;
            }
            return anyCompleted;
        }

        // Interact-shaped convenience overload, kept for every pre-B1 call site (PlayerInteractor,
        // PlayerController). Builds a TaskEvalContext with Reason = Interact and forwards.
        public bool EvaluateActiveTasks(GameObject target, bool stopOnMinigame = false, bool skipMinigame = false)
            => EvaluateActiveTasks(new TaskEvalContext
            {
                Player = player,
                Reason = TaskEvalReason.Interact,
                Target = target,
                SkipMinigame = skipMinigame
            }, stopOnMinigame);

        // Runs CheckForTaskRegression on every active task - used wherever an item leaves the player's
        // hands outside of a task step completing (dropped, thrown, or borrowed for a minigame), since
        // that can un-satisfy an AcquireItemStep that was already marked done.
        public void CheckRegressionForAll()
        {
            foreach (TaskInstance task in activeTasks) task.CheckForTaskRegression(player);
        }
    }
}
