using System.Collections.Generic;
using UnityEngine;

public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Global Court Meter")]
    public float currentCourtProgress = 0f;
    public float maxCourtProgress = 100f;
    public float pointsPerTask = 5f; // How much % the meter fills per completed task

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

    // Called by the MatchManager at the start of EVERY Action Stage
    public void AssignTasksForNewStage()
    {
        if (RoleManager.Instance == null) return;
        Debug.Log("--- TASK MANAGER: Distributing new tasks for the Action Stage ---");
        
        // Track what we auto-spawn this stage to prevent giving out 5 swords if 5 people get the Duel task
        HashSet<string> spawnedItemsThisStage = new HashSet<string>();

        foreach (PlayerController player in RoleManager.Instance.allPlayers)
        {
            // Ghosts and the King do not receive tasks
            if (player.isGhost || player.currentRole == PlayerRole.King)
            {
                player.AssignTasks(new List<TaskData>()); // Empty list
                continue;
            }
            
            // Generate a random subset of tasks for this player
            List<TaskData> playerTasks = GenerateRandomTasks(tasksPerStage);
            player.AssignTasks(playerTasks);

            // --- PREREQUISITE AUTO-SPAWN LOGIC ---
            foreach (TaskData task in playerTasks)
            {
                // If this task required a past event, and the Court FAILED to do it...
                if (task.prerequisiteTask != null && !completedTasksHistory.Contains(task.prerequisiteTask))
                {
                    if (task.autoSpawnItemPrefab != null && !string.IsNullOrEmpty(task.autoSpawnLocationID))
                    {
                        // Check if we already spawned this item for another player's task this round
                        if (spawnedItemsThisStage.Contains(task.autoSpawnItemPrefab.itemName)) continue;

                        // Find the required Task Deposit Station in the world
                        foreach (TaskLocation location in TaskLocation.AllLocations)
                        {
                            if (location.locationID == task.autoSpawnLocationID)
                            {
                                TaskDepositStation station = location.GetComponent<TaskDepositStation>();
                                if (station != null)
                                {
                                    // Find the first empty slot in the station's grid
                                    for (int i = 0; i < station.depositedItemSlots.Length; i++)
                                    {
                                        if (station.depositedItemSlots[i] == null)
                                        {
                                            // Instantiate the required item
                                            PickupItem spawnedItem = Instantiate(task.autoSpawnItemPrefab);
                                            
                                            // Clean "(Clone)" string so it matches task requirements
                                            spawnedItem.itemName = task.autoSpawnItemPrefab.itemName; 
                                            
                                            // Flag this as a normal item, not an infinite spawner, so the UI prioritizes it!
                                            spawnedItem.isInfiniteSource = false;
                                            
                                            // Grab the exact drop slot Transform and tell the item to deposit
                                            Transform exactGridSlot = station.GetDropSlot(i);
                                            spawnedItem.PlaceInStation(exactGridSlot, station);

                                            // Register it in the station's memory
                                            station.depositedItemSlots[i] = spawnedItem;
                                            spawnedItemsThisStage.Add(spawnedItem.itemName);
                                            
                                            Debug.Log($"[TaskManager] Auto-spawned {spawnedItem.itemName} at {location.locationID} because prerequisite was failed.");
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
        GameObject localPlayerObj = GameObject.Find("Player");
        if (localPlayerObj != null)
        {
            PlayerController localPlayer = localPlayerObj.GetComponent<PlayerController>();
            if (localPlayer != null) localPlayer.RefreshLocalWaypoints();
        }
    }

    private List<TaskData> GenerateRandomTasks(int amount)
    {
        List<TaskData> generatedTasks = new List<TaskData>();
        
        // We only want to hand out standard Court tasks to do, so we filter out Sabotages
        List<TaskData> pool = new List<TaskData>();
        foreach (var task in allPossibleTasks)
        {
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
            generatedTasks.Add(pool[randomIndex]);
            
            // Remove it from the temporary pool so they don't get the exact same task twice in one stage
            pool.RemoveAt(randomIndex); 
        }

        return generatedTasks;
    }

    public void CompleteTask(PlayerController player, TaskData task)
    {
        if (player.isGhost || !player.activeTasks.Contains(task)) return;

        // Remove the task from the player's personal list
        player.RemoveCompletedTask(task);

        // NOTE: Corrupted players can "do" tasks to blend in, but they DO NOT fill the meter!
        if (player.currentRole == PlayerRole.Corrupted)
        {
            Debug.Log($"{player.gameObject.name} (Corrupted) faked task: {task.taskName}. Meter unchanged.");
            return;
        }

        // Add progress for Court and Kingsguard players
        currentCourtProgress += pointsPerTask;
        
        // Clamp the progress so it doesn't exceed 100%
        currentCourtProgress = Mathf.Clamp(currentCourtProgress, 0f, maxCourtProgress);

        // Add to history so future rounds know it was completed!
        if (!completedTasksHistory.Contains(task))
        {
            completedTasksHistory.Add(task);
        }

        // Update the visual meter
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateGlobalMeter(currentCourtProgress, maxCourtProgress);
        }

        Debug.Log($"Court Task Completed: {task.taskName}! Global Meter: {currentCourtProgress}% / {maxCourtProgress}%");
    }
}