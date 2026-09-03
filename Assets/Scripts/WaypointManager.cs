using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class WaypointManager : MonoBehaviour
{
    public static WaypointManager Instance { get; private set; }

    [Header("References")]
    public Camera playerCamera; 
    public GameObject waypointPrefab; 
    public RectTransform waypointContainer;
    public Camera uiCamera;

    [Header("Scaling Settings")]
    [Tooltip("The distance at which the marker is its normal 1x size.")]
    public float baseDistance = 5f; 
    [Tooltip("The absolute smallest the marker can get when far away.")]
    public float minScale = 0.1f; 
    [Tooltip("The absolute largest the marker can get when very close.")]
    public float maxScale = 1.5f; 
    [Tooltip("How many pixels to push overlapping waypoints upward.")]
    public float verticalStackSpacing = 120f; // NEW: Increased default to clear your circle graphic
    
    [Header("Meeting Waypoint")]
    public GameObject meetingWaypointPrefab; // Uses a different icon/color!
    private Transform currentMeetingTarget;
    private RectTransform activeMeetingMarker;

    // NEW: Updated dictionary structures to hold multiple targets/markers per TaskInstance
    private Dictionary<TaskInstance, List<RectTransform>> activeWaypoints = new Dictionary<TaskInstance, List<RectTransform>>();
    private Dictionary<TaskInstance, List<Transform>> taskTargets = new Dictionary<TaskInstance, List<Transform>>();

    // NEW: A simple struct to help us sort markers by distance before drawing them
    private struct MarkerDrawData
    {
        public RectTransform marker;
        public Transform target;
        public float distance;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void UpdateWaypoints(PlayerController localPlayer, List<TaskInstance> currentTasks)
    {
        // 1. Clear out all existing lists of markers
        foreach (var markerList in activeWaypoints.Values)
        {
            if (markerList != null)
            {
                foreach (var wp in markerList)
                {
                    if (wp != null) Destroy(wp.gameObject);
                }
            }
        }
        activeWaypoints.Clear();
        taskTargets.Clear();

        if (currentTasks == null || localPlayer == null) return;

        for (int i = 0; i < currentTasks.Count; i++)
        {
            TaskInstance task = currentTasks[i];
            if (task == null || task.Definition == null) continue;
            int taskNumber = i + 1; // Start at 1 instead of 0 for the player UI

            if (!localPlayer.activeTasks.Contains(task)) continue;

            // Hide this task's waypoint while its minigame UI is open.
            if (localPlayer.isPlayingMinigame && localPlayer.activeMinigameTask == task) continue;

            // --- NEW: FETCH THE ACTIVE STEP ---
            TaskStep activeStep = task.GetCurrentStep();
            if (activeStep == null) continue; // Skip if the task is completely finished

            List<Transform> targetsForThisTask = new List<Transform>();

            // --- NEW: POLYMORPHIC EVALUATION USING PATTERN MATCHING ---
            
            // 1. Acquire Item Step
            if (activeStep is AcquireItemStep acquireStep)
            {
                Transform t = FindItemByName(acquireStep.requiredItemName);
                if (t != null) targetsForThisTask.Add(t);
            }
            // 2. Navigate Step
            else if (activeStep is NavigateStep navigateStep)
            {
                Transform t = FindLocationByID(navigateStep.targetZoneID);
                if (t != null) targetsForThisTask.Add(t);
            }
            // 3. Station Interact Step
            else if (activeStep is StationInteractStep stationStep)
            {
                Transform t = FindLocationByID(stationStep.targetStationID);
                if (t != null) targetsForThisTask.Add(t);
            }
            // 4. Player Interact Step
            else if (activeStep is PlayerInteractStep playerStep)
            {
                // If they need an item and don't have it (in either hand), point to the item on the floor first
                if (!string.IsNullOrEmpty(playerStep.requiredHeldItemName) &&
                    !localPlayer.IsHoldingItemNamed(playerStep.requiredHeldItemName))
                {
                    Transform t = FindItemByName(playerStep.requiredHeldItemName);
                    if (t != null) targetsForThisTask.Add(t);
                }
                else // They have the item (or don't need one), so point to matching players
                {
                    targetsForThisTask.AddRange(FindPlayersWithSameTask(localPlayer, task.Definition.taskID));
                }
            }
            // 5. Data Retrieval Step
            else if (activeStep is DataRetrievalStep dataStep)
            {
                // Dynamically point to the source OR the input based on the step's internal boolean
                string targetID = dataStep.hasCode ? dataStep.inputStationID : dataStep.sourceStationID;
                Transform t = FindLocationByID(targetID);
                if (t != null) targetsForThisTask.Add(t);
            }
            
            // --- NEW: DEPOSIT ITEM STEP ---
            else if (activeStep is DepositItemStep depositStep)
            {
                // Point directly to the target deposit station (e.g., "Armory")
                Transform t = FindLocationByID(depositStep.targetStationID);
                if (t != null) targetsForThisTask.Add(t);
            }
            
            // --- NEW: PROCESS ITEM STEP ---
            else if (activeStep is ProcessItemStep processStep)
            {
                // For processing, we first check if they need to go to a station.
                // If it's not a station, we check if it's an item on the floor.
                Transform t = FindLocationByID(processStep.targetStationOrItemName);
                if (t == null) t = FindItemByName(processStep.targetStationOrItemName);
                
                // (If the item is already in their hand, this naturally returns null and hides the waypoint, which is correct!)
                if (t != null) targetsForThisTask.Add(t);
            }
            

            // --- Spawning Logic (Remains identical!) ---
            if (targetsForThisTask.Count > 0)
            {
                activeWaypoints[task] = new List<RectTransform>();
                taskTargets[task] = new List<Transform>();

                foreach (Transform targetTransform in targetsForThisTask)
                {
                    GameObject marker = Instantiate(waypointPrefab, waypointContainer);
                    activeWaypoints[task].Add(marker.GetComponent<RectTransform>());
                    taskTargets[task].Add(targetTransform);

                    TextMeshProUGUI label = marker.GetComponentInChildren<TextMeshProUGUI>();
                    if (label != null) 
                    {
                        label.text = taskNumber.ToString();
                    }
                }
            }
        }
    }
    
    // Helper method to locate the item in the 3D world
    private Transform FindItemByName(string nameToFind)
    {
        if (string.IsNullOrEmpty(nameToFind)) return null;

        Transform fallbackSpawner = null;

        foreach (PickupItem item in PickupItem.AllItems)
        {
            // Only point to the item if it's actually sitting in the world (not held by someone else)
            if (item.itemName == nameToFind && item.transform.parent == null) 
            {
                // PRIORITY: If we find a dropped clone (not an infinite spawner), point to this immediately!
                if (!item.isInfiniteSource)
                {
                    return item.transform;
                }
                
                // Otherwise, remember this spawner in case we don't find any dropped clones
                if (fallbackSpawner == null)
                {
                    fallbackSpawner = item.transform;
                }
            }
        }
        
        // If no dropped clones were found on the ground, point to the infinite spawner
        return fallbackSpawner;
    }

    // NEW: Helper method to find all other living players in the lobby with the exact same task
    private List<Transform> FindPlayersWithSameTask(PlayerController localPlayer, string taskIDToMatch)
    {
        List<Transform> matchingPlayers = new List<Transform>();

        if (RoleManager.Instance != null)
        {
            foreach (PlayerController player in RoleManager.Instance.allPlayers)
            {
                // Ignore ourselves and ghosts
                if (player == localPlayer || player.isGhost) continue;

                // Check if they have the exact same task (compare the shared Definition's ID,
                // never the per-player instance reference).
                foreach (TaskInstance task in player.activeTasks)
                {
                    if (task != null && task.Definition != null && task.Definition.taskID == taskIDToMatch)
                    {
                        matchingPlayers.Add(player.transform);
                        break;
                    }
                }
            }
        }

        return matchingPlayers;
    }

    void Update()
    {
        // 1. We must have a camera to draw UI, so return if it's missing
        if (playerCamera == null) return;

        // ==========================================
        // SYSTEM B: THE TASK WAYPOINTS (Run first so Meeting marker draws over them)
        // ==========================================
        // Only run this loop if the player actually has active tasks to point to
        if (activeWaypoints.Count > 0)
        {
            List<MarkerDrawData> drawList = new List<MarkerDrawData>();

            // 1. Gather all active markers and calculate their distance
            foreach (var kvp in activeWaypoints)
            {
                TaskInstance task = kvp.Key;
                List<RectTransform> markers = kvp.Value;
                List<Transform> targets = taskTargets[task];

                for (int i = 0; i < markers.Count; i++)
                {
                    if (targets[i] == null || markers[i] == null) continue;

                    float dist = Vector3.Distance(playerCamera.transform.position, targets[i].position);
                    drawList.Add(new MarkerDrawData { marker = markers[i], target = targets[i], distance = dist });
                }
            }

            // 2. Sort the list from Furthest to Closest (Descending order)
            drawList.Sort((a, b) => b.distance.CompareTo(a.distance));

            // We track how many markers are pointing at the exact same Transform this frame
            Dictionary<Transform, int> targetStackCounts = new Dictionary<Transform, int>();

            // 3. Draw the sorted markers
            foreach (var data in drawList)
            {
                RectTransform marker = data.marker;
                Transform target = data.target;
                float distance = Mathf.Max(0.1f, data.distance); // Prevent division by zero

                Vector3 screenPos = playerCamera.WorldToScreenPoint(target.position);

                if (screenPos.z < 0)
                {
                    marker.gameObject.SetActive(false); 
                }
                else
                {
                    marker.gameObject.SetActive(true);

                    // 1. Calculate scale inversely proportional to distance
                    float scale = baseDistance / distance;
                    scale = Mathf.Clamp(scale, minScale, maxScale);
                    
                    // 2. Apply the scale to the UI RectTransform
                    marker.localScale = new Vector3(scale, scale, scale);

                    // --- THE OFFSET STACKING LOGIC ---
                    // Check if we have already drawn a marker for this exact target this frame
                    if (!targetStackCounts.ContainsKey(target))
                    {
                        targetStackCounts[target] = 0;
                    }
                    
                    int stackIndex = targetStackCounts[target];
                    
                    // Increment the count so the next marker pointing here gets pushed up higher
                    targetStackCounts[target]++; 

                    float verticalOffset = verticalStackSpacing * scale * stackIndex;
                        screenPos.y += verticalOffset;
                        // --------------------------------------

                        // --- NEW: SCREEN SPACE CAMERA FIX ---
                        if (uiCamera != null)
                        {
                            // Translates the raw pixels into the exact 3D world space of your UI Camera
                            RectTransformUtility.ScreenPointToWorldPointInRectangle(waypointContainer, screenPos, uiCamera, out Vector3 uiWorldPos);
                            marker.position = uiWorldPos;
                        }
                        else
                        {
                            // Fallback just in case you ever switch back to Overlay
                            marker.position = screenPos;
                        }

                        // --- NEW: Z-DEPTH RENDERING FIX ---
                        marker.SetAsLastSibling();
                }
            }
        }

        // ==========================================
        // SYSTEM A: THE MEETING WAYPOINT
        // ==========================================
        // Draw the Meeting Waypoint last so it always sits on top of tasks!
        if (currentMeetingTarget != null && activeMeetingMarker != null)
        {
            Vector3 screenPos = playerCamera.WorldToScreenPoint(currentMeetingTarget.position);
            
            if (screenPos.z < 0)
            {
                activeMeetingMarker.gameObject.SetActive(false);
            }
            else
            {
                activeMeetingMarker.gameObject.SetActive(true);
                
                // --- NEW: SCREEN SPACE CAMERA FIX ---
                if (uiCamera != null)
                {
                    RectTransformUtility.ScreenPointToWorldPointInRectangle(waypointContainer, screenPos, uiCamera, out Vector3 uiWorldPos);
                    activeMeetingMarker.position = uiWorldPos;
                }
                else
                {
                    activeMeetingMarker.position = screenPos;
                }
               
                float distance = Vector3.Distance(playerCamera.transform.position, currentMeetingTarget.position);
                distance = Mathf.Max(0.1f, distance); 
                float scale = baseDistance / distance;
                scale = Mathf.Clamp(scale, minScale, maxScale);
                activeMeetingMarker.localScale = new Vector3(scale, scale, scale);
                
                // Force the urgent meeting marker to the very front
                activeMeetingMarker.SetAsLastSibling();
            }
        }
    }

    private Transform FindLocationByID(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        // 1. Check physical Stations/TaskLocations first
        foreach (TaskLocation loc in TaskLocation.AllLocations)
        {
            if (loc.locationID == id) return loc.transform;
        }

        // 2. NEW: Check invisible TaskZones (Rooms)
        foreach (TaskZone zone in TaskZone.AllZones)
        {
            if (zone.zoneID == id) return zone.transform;
        }

        return null;
    }

    public void SetMeetingWaypoint(Transform meetingTransform)
    {
        currentMeetingTarget = meetingTransform;
        
        if (activeMeetingMarker == null && meetingWaypointPrefab != null)
        {
            GameObject marker = Instantiate(meetingWaypointPrefab, waypointContainer);
            activeMeetingMarker = marker.GetComponent<RectTransform>();
        }
    }

    public void ClearMeetingWaypoint()
    {
        currentMeetingTarget = null;
        if (activeMeetingMarker != null)
        {
            Destroy(activeMeetingMarker.gameObject);
            activeMeetingMarker = null;
        }
    }

    // --- NEW: SPYMASTER LEDGER UI ---
    public void ShowSpymasterWaypoints(List<Transform> targets, float duration)
    {
        StartCoroutine(SpymasterRoutine(targets, duration));
    }

    private System.Collections.IEnumerator SpymasterRoutine(List<Transform> targets, float duration)
    {
        // 1. Create temporary red markers for the VIPs
        List<RectTransform> spyMarkers = new List<RectTransform>();
        
        foreach (Transform target in targets)
        {
            if (waypointPrefab != null)
            {
                GameObject marker = Instantiate(waypointPrefab, waypointContainer);
                RectTransform rect = marker.GetComponent<RectTransform>();
                
                // Color the text and icon red to indicate it's an enemy track
                TextMeshProUGUI label = marker.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = "VIP";
                    label.color = Color.red; 
                }
                
                UnityEngine.UI.Image img = marker.GetComponentInChildren<UnityEngine.UI.Image>();
                if (img != null) img.color = Color.red;
                
                spyMarkers.Add(rect);
            }
        }

        float timer = 0f;

        // 2. Update their screen positions every frame for the duration
        while (timer < duration)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (i >= spyMarkers.Count || targets[i] == null || spyMarkers[i] == null) continue;

                Vector3 screenPos = playerCamera.WorldToScreenPoint(targets[i].position);
                
                if (screenPos.z < 0)
                {
                    spyMarkers[i].gameObject.SetActive(false);
                }
                else
                {
                    spyMarkers[i].gameObject.SetActive(true);
                    
                    // --- NEW: SCREEN SPACE CAMERA FIX ---
                    if (uiCamera != null)
                    {
                        RectTransformUtility.ScreenPointToWorldPointInRectangle(waypointContainer, screenPos, uiCamera, out Vector3 uiWorldPos);
                        spyMarkers[i].position = uiWorldPos;
                    }
                    else
                    {
                        spyMarkers[i].position = screenPos;
                    }
                    
                    // Keep them small so they don't block the screen
                    spyMarkers[i].localScale = new Vector3(minScale, minScale, minScale);
                }
            }

            timer += Time.deltaTime;
            yield return null; // Wait for the next frame
        }

        // 3. Time is up! Destroy the temporary markers
        foreach (RectTransform marker in spyMarkers)
        {
            if (marker != null) Destroy(marker.gameObject);
        }
    }
}