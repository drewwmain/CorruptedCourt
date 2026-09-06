using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
    public class TaskManager : MonoBehaviour
    {
        public static TaskManager Instance { get; private set; }

        [Header("Global Court Meter")]
        public float currentCourtProgress = 0f;

        // Sized once at match start by InitializeCourtMeter() from lobby size, task tiers and
        // targetStages - it is NOT an authored value. The 100f is only a pre-match fallback so the
        // win check never compares against 0 before Initialization has run.
        [System.NonSerialized] public float maxCourtProgress = 100f;

        [Tooltip("Points a completed task adds to the Court meter, indexed by (taskTier - 1): " +
                 "element 0 = tier 1, element 1 = tier 2, element 2 = tier 3. A tier with no weight " +
                 "authored falls back to 1.")]
        public float[] tierWeights = { 1f, 2f, 4f };

        [Tooltip("How many full stages of assignments the Court is expected to finish to fill the " +
                 "meter. Used once at match start to size the target; never recomputed when players die.")]
        public int targetStages = 3;

        [Header("Task Generation")]
        public int tasksPerStage = 3;

        // Drag and drop all your created TaskData ScriptableObjects here in the Inspector
        public List<TaskData> allPossibleTasks = new List<TaskData>();

        [Header("Match History")]
        public List<TaskData> completedTasksHistory = new List<TaskData>();

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // Called once by MatchManager at match start (Initialization), AFTER RoleManager has assigned
        // factions/titles. Resets progress to 0 and sizes the Court meter to the lobby:
        //   target = eligibleCourtCount * tasksPerStage * averageTierWeight * targetStages
        // eligibleCourtCount = players who can receive tasks (not a ghost, not the King) - it mirrors
        // the test in AssignTasksForNewStage. The target is frozen here and deliberately NOT
        // recomputed when players die, so a shrinking Court has to work harder rather than less.
        public void InitializeCourtMeter()
        {
            currentCourtProgress = 0f;

            if (RoleManager.Instance == null)
            {
                Log.Warn($"[TaskManager] InitializeCourtMeter: no RoleManager - keeping fallback maxCourtProgress={maxCourtProgress}.");
                GameEvents.RaiseCourtProgressChanged(currentCourtProgress, maxCourtProgress);
                return;
            }

            int eligibleCourtCount = 0;
            foreach (PlayerController player in RoleManager.Instance.allPlayers)
            {
                if (player == null) continue;
                // Same "receives tasks" rule as AssignTasksForNewStage: ghosts and the King are out.
                // Corrupted players are counted here - they are handed tasks (to blend in) even though
                // their completions never credit the meter.
                if (player.Vitals.isGhost || player.Vitals.courtTitle == CourtTitle.King) continue;
                eligibleCourtCount++;
            }

            float averageTierWeight = TierWeightAverage();

            if (eligibleCourtCount <= 0 || tasksPerStage <= 0 || targetStages <= 0 || averageTierWeight <= 0f)
            {
                Log.Warn($"[TaskManager] InitializeCourtMeter: cannot size the meter (eligibleCourtCount={eligibleCourtCount}, " +
                         $"tasksPerStage={tasksPerStage}, targetStages={targetStages}, avgTierWeight={averageTierWeight}). " +
                         $"Keeping fallback maxCourtProgress={maxCourtProgress}.");
                GameEvents.RaiseCourtProgressChanged(currentCourtProgress, maxCourtProgress);
                return;
            }

            float perStageTheoreticalMax = eligibleCourtCount * tasksPerStage * averageTierWeight;
            maxCourtProgress = Mathf.Ceil(perStageTheoreticalMax * targetStages);

            Log.Game($"--- COURT METER SIZED: target {maxCourtProgress} " +
                     $"(eligibleCourt {eligibleCourtCount} x tasksPerStage {tasksPerStage} x avgTierWeight {averageTierWeight:0.###} x targetStages {targetStages}); " +
                     $"per-stage theoretical max {perStageTheoreticalMax:0.###} ---");

            GameEvents.RaiseCourtProgressChanged(currentCourtProgress, maxCourtProgress);
        }

        // Points for one completed task, from its tier. An unset/legacy tier (0) or an out-of-range
        // value is clamped into 1..3; a tier with no weight authored falls back to 1.
        private float TierWeight(TaskData definition)
        {
            int tier = Mathf.Clamp(definition != null ? definition.taskTier : 1, 1, 3);
            if (tierWeights == null || tierWeights.Length < tier)
            {
                Log.Warn($"[TaskManager] tierWeights has no entry for tier {tier} (length {(tierWeights != null ? tierWeights.Length : 0)}). Using weight 1.");
                return 1f;
            }
            return tierWeights[tier - 1];
        }

        // Mean weight across the authored tiers (1..3), used to size the meter target at match start.
        private float TierWeightAverage()
        {
            if (tierWeights == null || tierWeights.Length == 0) return 1f;
            int n = Mathf.Min(tierWeights.Length, 3); // only tiers 1..3 exist
            float sum = 0f;
            for (int i = 0; i < n; i++) sum += tierWeights[i];
            return sum / n;
        }

        // Called by the MatchManager at the start of EVERY Action Stage
        public void AssignTasksForNewStage()
        {
            if (RoleManager.Instance == null) return;
            Log.Game("--- TASK MANAGER: Distributing new tasks for the Action Stage ---");

            // Track what we auto-spawn this stage to prevent giving out 5 swords if 5 people get the Duel task
            HashSet<ItemDefinition> spawnedItemsThisStage = new HashSet<ItemDefinition>();

            foreach (PlayerController player in RoleManager.Instance.allPlayers)
            {
                if (player == null) continue;

                // Ghosts and the King do not receive tasks (whether the player receives tasks is a title
                // concern - a Corrupted Kingsguard still gets tasks, they just don't credit the meter).
                if (player.Vitals.isGhost || player.Vitals.courtTitle == CourtTitle.King)
                {
                    player.TaskBook.AssignTasks(new List<TaskInstance>()); // Empty list
                    continue;
                }

                // Generate a random subset of fresh per-player task instances
                List<TaskInstance> playerTasks = GenerateRandomTasks(tasksPerStage);
                player.TaskBook.AssignTasks(playerTasks);

                // --- PREREQUISITE AUTO-SPAWN LOGIC ---
                foreach (TaskInstance task in playerTasks)
                {
                    if (task == null || task.Definition == null) continue;
                    TaskData def = task.Definition;

                    // If this task required a past event, and the Court FAILED to do it...
                    if (def.prerequisiteTask != null && !completedTasksHistory.Contains(def.prerequisiteTask))
                    {
                        if (def.autoSpawnItemPrefab != null && def.autoSpawnItemPrefab.definition != null
                            && !string.IsNullOrEmpty(def.autoSpawnLocationID))
                        {
                            // Check if we already spawned this item for another player's task this round
                            if (spawnedItemsThisStage.Contains(def.autoSpawnItemPrefab.definition)) continue;

                            // Find the required Task Deposit Station in the world
                            foreach (TaskLocation location in TaskLocation.AllLocations)
                            {
                                if (location == null) continue;

                                if (location.locationID == def.autoSpawnLocationID)
                                {
                                    TaskDepositStation station = location.GetComponent<TaskDepositStation>();
                                    if (station != null)
                                    {
                                        // Find the first empty slot in the station's grid
                                        for (int i = 0; i < station.depositedItemSlots.Length; i++)
                                        {
                                            if (station.depositedItemSlots[i] == null)
                                            {
                                                // Instantiate the required item (identity rides on 'definition', which Instantiate copies)
                                                PickupItem spawnedItem = Instantiate(def.autoSpawnItemPrefab);

                                                // Flag this as a normal item, not an infinite spawner, so the UI prioritizes it!
                                                spawnedItem.isInfiniteSource = false;

                                                // Grab the exact drop slot Transform and tell the item to deposit
                                                Transform exactGridSlot = station.GetDropSlot(i);
                                                spawnedItem.PlaceInStation(exactGridSlot, station);

                                                // Register it in the station's memory
                                                station.depositedItemSlots[i] = spawnedItem;
                                                spawnedItemsThisStage.Add(spawnedItem.definition);

                                                Log.Game($"[TaskManager] Auto-spawned {spawnedItem.DisplayName} at {location.locationID} because prerequisite was failed.");
                                                break; // Successfully spawned, move to next task
                                            }
                                        }
                                    }
                                    break; // We found the right location, no need to keep checking other rooms
                                }
                            }
                        }
                    }
                }
            }

            // After EVERY player in the lobby has been handed their tasks, refresh the UI
            // so it can accurately scan the dummy players for matching multiplayer tasks!
            if (PlayerController.Local != null) PlayerController.Local.TaskBook.RefreshLocalWaypoints();
        }

        private List<TaskInstance> GenerateRandomTasks(int amount)
        {
            List<TaskInstance> generatedTasks = new List<TaskInstance>();

            // We only want to hand out standard Court tasks to do, so we filter out Sabotages
            List<TaskData> pool = new List<TaskData>();
            foreach (var task in allPossibleTasks)
            {
                if (task == null) continue;

                if (!task.isSabotage)
                {
                    // Check if MatchManager exists and if the task has allowed stages assigned
                    if (MatchManager.Instance != null && task.allowedStages != null && task.allowedStages.Count > 0)
                    {
                        // Only add the task to the pool if the current stage is in its allowed list
                        if (task.allowedStages.Contains(MatchManager.Instance.currentStage))
                        {
                            pool.Add(task);
                        }
                    }
                    else
                    {
                        // If no round data is set, assume it can spawn anytime
                        pool.Add(task);
                    }
                }
            }

            for (int i = 0; i < amount; i++)
            {
                if (pool.Count == 0) break;

                int randomIndex = Random.Range(0, pool.Count);
                // A fresh instance IS the initialization - no InitializeTask() call needed.
                generatedTasks.Add(new TaskInstance(pool[randomIndex]));

                // Remove it from the temporary pool so they don't get the exact same task twice in one stage
                pool.RemoveAt(randomIndex);
            }

            return generatedTasks;
        }

        public void CompleteTask(PlayerController player, TaskInstance task)
        {
            if (task == null || player.Vitals.isGhost || !player.TaskBook.activeTasks.Contains(task)) return;

            TaskData definition = task.Definition;

            // Remove the task from the player's personal list
            player.TaskBook.RemoveCompletedTask(task);

            // NOTE: Corrupted players can "do" tasks to blend in, but they DO NOT fill the meter!
            // Meter credit is a faction concern, so a Corrupted-aligned Kingsguard is filtered here too.
            if (player.Vitals.faction == Faction.Corrupted)
            {
                Log.Game($"{player.gameObject.name} (Corrupted) faked task: {(definition != null ? definition.taskName : "<unknown>")}. Meter unchanged.");
                return;
            }

            // Add progress for every Court-faction player, weighted by the completed task's tier
            currentCourtProgress += TierWeight(definition);

            // Clamp the progress so it never exceeds the computed target
            currentCourtProgress = Mathf.Clamp(currentCourtProgress, 0f, maxCourtProgress);

            // Add to history so future rounds know it was completed! History holds the shared asset
            // (Definition), which is what the prerequisite auto-spawn logic checks against.
            if (definition != null && !completedTasksHistory.Contains(definition))
            {
                completedTasksHistory.Add(definition);
            }

            // Announce the new progress - the court meter is a view that subscribes to this.
            GameEvents.RaiseCourtProgressChanged(currentCourtProgress, maxCourtProgress);

            Log.Game($"Court Task Completed: {(definition != null ? definition.taskName : "<unknown>")}! Global Meter: {currentCourtProgress} / {maxCourtProgress}");
        }

        // Lowers the global Court meter (a Corrupted sabotage effect - see SabotageManager's Poison the
        // Feast). Clamped at 0; never touches the frozen target. Raises the progress event so the meter
        // view and the win check react.
        public void DrainCourtProgress(float amount)
        {
            if (amount <= 0f) return;

            currentCourtProgress = Mathf.Clamp(currentCourtProgress - amount, 0f, maxCourtProgress);
            GameEvents.RaiseCourtProgressChanged(currentCourtProgress, maxCourtProgress);
        }
    }
}
