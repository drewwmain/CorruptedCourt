using System.Collections.Generic;
using UnityEngine;
using TMPro;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Minigames;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.UI
{
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

        // Shared empty list so the "clear all markers" path allocates nothing.
        private static readonly List<TaskInstance> noTasks = new List<TaskInstance>();

        // Reused by Update() every frame so the per-frame marker pass allocates nothing. Both are
        // cleared, refilled, and fully consumed within a single Update() call.
        private readonly List<MarkerDrawData> drawList = new List<MarkerDrawData>();
        private readonly Dictionary<Transform, int> targetStackCounts = new Dictionary<Transform, int>();
        // Cached so Sort() doesn't allocate a Comparison delegate each frame.
        private static readonly System.Comparison<MarkerDrawData> byDistanceDescending =
            (a, b) => b.distance.CompareTo(a.distance);

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        void OnEnable()
        {
            GameEvents.LocalTasksChanged += OnLocalTasksChanged;
            GameEvents.MeetingWaypointSet      += SetMeetingWaypoint;
            GameEvents.MeetingWaypointCleared  += ClearMeetingWaypoint;
            GameEvents.SpymasterWaypointsShown += ShowSpymasterWaypoints;
        }

        void OnDisable()
        {
            GameEvents.LocalTasksChanged -= OnLocalTasksChanged;
            GameEvents.MeetingWaypointSet      -= SetMeetingWaypoint;
            GameEvents.MeetingWaypointCleared  -= ClearMeetingWaypoint;
            GameEvents.SpymasterWaypointsShown -= ShowSpymasterWaypoints;
        }

        // The local player's task list / current step changed. Rebuild that player's on-screen markers -
        // this is what PlayerController.RefreshLocalWaypoints used to call directly.
        private void OnLocalTasksChanged(PlayerController player)
        {
            if (player == null) return;

            if (player.TaskBook.showWaypoints)
            {
                if (player.PlayerCamera != null) playerCamera = player.PlayerCamera.GetComponent<Camera>();
                UpdateWaypoints(player, player.TaskBook.allAssignedTasks);
            }
            else
            {
                UpdateWaypoints(player, noTasks); // clears any active markers
            }
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

                if (!localPlayer.TaskBook.activeTasks.Contains(task)) continue;

                // Hide this task's waypoint for the minigame's whole session - start to true end
                // (FinishMinigame/CancelMinigame clear activeMinigameTask). Deliberately NOT gated on
                // isPlayingMinigame: a HandMinigame-based deposit hands the player back to normal controls
                // (isPlayingMinigame false) the instant the item is released, well before the drop's
                // outcome - hang / seat / miss-and-retry - is known, and the waypoint should stay hidden
                // through that whole window, not flicker back on while the item is still falling.
                if (localPlayer.activeMinigameTask == task) continue;

                // Every step now answers for itself via GetObjectiveTarget (ARCHITECTURE.md §12) instead
                // of this manager pattern-matching each step type - see TaskStep.GetObjectiveTarget and
                // its per-step overrides in Tasks/Steps/.
                TaskStep activeStep = task.GetCurrentStep();
                if (activeStep == null) continue; // Skip if the task is completely finished

                ObjectiveTarget target = task.GetCurrentObjectiveTarget(localPlayer);

                if (target.Transform != null)
                {
                    activeWaypoints[task] = new List<RectTransform>();
                    taskTargets[task] = new List<Transform>();

                    GameObject marker = Instantiate(waypointPrefab, waypointContainer);
                    activeWaypoints[task].Add(marker.GetComponent<RectTransform>());
                    taskTargets[task].Add(target.Transform);

                    TextMeshProUGUI label = marker.GetComponentInChildren<TextMeshProUGUI>();
                    if (label != null)
                    {
                        label.text = taskNumber.ToString();
                    }
                }
            }
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
                drawList.Clear();

                // 1. Gather all active markers and calculate their distance
                foreach (var kvp in activeWaypoints)
                {
                    TaskInstance task = kvp.Key;
                    List<RectTransform> markers = kvp.Value;
                    List<Transform> targets = taskTargets[task];

                    for (int i = 0; i < markers.Count; i++)
                    {
                        if (markers[i] == null) continue;

                        // The target it was pointing at was destroyed (dropped item despawned, sabotage,
                        // etc.) - hide the marker now instead of leaving it frozen at its last screen
                        // position until the next full rebuild (LocalTasksChanged) happens to fire.
                        if (targets[i] == null)
                        {
                            markers[i].gameObject.SetActive(false);
                            continue;
                        }

                        float dist = Vector3.Distance(playerCamera.transform.position, targets[i].position);
                        drawList.Add(new MarkerDrawData { marker = markers[i], target = targets[i], distance = dist });
                    }
                }

                // 2. Sort the list from Furthest to Closest (Descending order)
                drawList.Sort(byDistanceDescending);

                // We track how many markers are pointing at the exact same Transform this frame
                targetStackCounts.Clear();

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
}
