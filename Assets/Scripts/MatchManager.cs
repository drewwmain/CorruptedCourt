using System.Collections.Generic;
using UnityEngine;

public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance { get; private set; }
    
    public enum MatchState
    {
        Initialization,
        ActionStage,
        TransitionToMeeting, // NEW: The 20-second scramble phase
        MeetingPhase,
        GameOver
    }

    [Header("Match Settings")]
    public MatchState currentState;
    public int currentStage = 1;

    [Header("Timers")]
    public float transitionDuration = 20f; // NEW: Time to reach the meeting
    public float meetingDuration = 120f; 
    public float actionDuration = 300f;  
    private float currentTimer;

    [Header("Meeting Settings")]
    [Tooltip("The Zone ID of the meeting room (matches the TaskZone script on the room's collider)")]
    public string meetingZoneID = "MeetingRoom"; 
    [Tooltip("The physical center of the meeting room for the UI Waypoint")]
    public Transform meetingRoomTransform;

    [Header("Spawn Settings")]
    public Transform[] actionStageSpawnPoints;

    [Header("Game Over Data")]
    public string winningTeam = "None";
    public string winReason = "";

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        ChangeState(MatchState.Initialization);
    }

    void Update()
    {
        HandleStateTimers();
        CheckWinConditions();
    }

    private void ChangeState(MatchState newState)
    {
        currentState = newState;

        switch (currentState)
        {
            case MatchState.Initialization:
                Debug.Log("--- MATCH STARTING: Initialization Phase ---");
                currentStage = 1;
                
                if (RoleManager.Instance != null) RoleManager.Instance.AssignAllRoles();
                
                if (UIManager.Instance != null) 
                {
                    UIManager.Instance.HideVotingPanel();
                    UIManager.Instance.HideGameOverScreen();
                    UIManager.Instance.HideTransitionUI();
                }
                
                ChangeState(MatchState.ActionStage);
                break;

            case MatchState.ActionStage:
                Debug.Log($"--- STAGE {currentStage}: Action Stage Started! ---");
                currentTimer = actionDuration;
                
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                if (UIManager.Instance != null) UIManager.Instance.HideTransitionUI();
                
                // TELEPORT REMOVED: Players must now run from the meeting room to their tasks!
                
                if (TaskManager.Instance != null) TaskManager.Instance.AssignTasksForNewStage();
                break;

            // --- NEW: THE 20 SECOND TRANSITION PHASE ---
            case MatchState.TransitionToMeeting:
                Debug.Log($"--- ROUND OVER: 20 Seconds to reach the {meetingZoneID}! ---");
                currentTimer = transitionDuration;
                
                if (UIManager.Instance != null) 
                {
                    UIManager.Instance.ShowTransitionWarning();
                }

                // Tell the Waypoint Manager to draw a marker at the meeting room
                if (WaypointManager.Instance != null && meetingRoomTransform != null)
                {
                    WaypointManager.Instance.SetMeetingWaypoint(meetingRoomTransform);
                }
                break;

            case MatchState.MeetingPhase:
                Debug.Log($"--- STAGE {currentStage}: Meeting Phase Started! ---");
                currentTimer = meetingDuration;
                
                // 1. Turn off the waypoint and transition UI
                if (WaypointManager.Instance != null) WaypointManager.Instance.ClearMeetingWaypoint();
                if (UIManager.Instance != null) UIManager.Instance.HideTransitionUI();

                // 2. We do one final scan to lock in the absent players for the meeting phase
                List<string> finalAbsentPlayers = new List<string>();
                if (RoleManager.Instance != null)
                {
                    foreach (PlayerController player in RoleManager.Instance.allPlayers)
                    {
                        if (!player.isGhost && player.currentZoneID != meetingZoneID)
                        {
                            finalAbsentPlayers.Add(player.gameObject.name);
                        }
                    }
                }

                // 3. Trigger the meeting UI and pass the absent list to the Voting Panel
                if (VotingManager.Instance != null) VotingManager.Instance.StartMeeting();
                if (UIManager.Instance != null) UIManager.Instance.ShowAbsentMembers(finalAbsentPlayers);
                break;

            case MatchState.GameOver:
                currentTimer = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                if (UIManager.Instance != null)
                {
                    UIManager.Instance.ShowGameOverScreen(winningTeam, winReason);
                }
                break;
        }
    }

    private void HandleStateTimers()
    {
        // We removed MatchState.ActionStage from this check so the round lasts forever 
        // until tasks are done or the King triggers the Gallows!
        if (currentState == MatchState.MeetingPhase || currentState == MatchState.TransitionToMeeting)
        {
            currentTimer -= Time.deltaTime;

            // Constantly update the UI timer AND the Absent List if we are in the transition scramble
            if (currentState == MatchState.TransitionToMeeting && UIManager.Instance != null)
            {
                UIManager.Instance.UpdateTransitionTimer(currentTimer);

                // LIVE ABSENT TRACKING: Constantly scan the room as the clock ticks down
                List<string> liveAbsentPlayers = new List<string>();
                if (RoleManager.Instance != null)
                {
                    foreach (PlayerController player in RoleManager.Instance.allPlayers)
                    {
                        if (!player.isGhost && player.currentZoneID != meetingZoneID)
                        {
                            liveAbsentPlayers.Add(player.gameObject.name);
                        }
                    }
                }
                
                // Pushes the updated list to the screen every frame
                UIManager.Instance.ShowAbsentMembers(liveAbsentPlayers); 
            }

            if (currentTimer <= 0f)
            {
                TransitionToNextPhase();
            }
        }
    }

    // --- NEW: KING'S GALLOWS TRIGGER ---
    public void TriggerGallowsMeeting()
    {
        // Only allow this if we are actively playing the game
        if (currentState == MatchState.ActionStage)
        {
            Debug.Log("<color=#F1C40F>--- THE KING HAS CALLED FOR AN EXECUTION! ---</color>");
            ChangeState(MatchState.TransitionToMeeting);
        }
    }

    private void TransitionToNextPhase()
    {
        if (currentState == MatchState.ActionStage)
        {
            ChangeState(MatchState.TransitionToMeeting);
        }
        else if (currentState == MatchState.TransitionToMeeting)
        {
            ChangeState(MatchState.MeetingPhase);
        }
        else if (currentState == MatchState.MeetingPhase)
        {
            if (VotingManager.Instance != null) VotingManager.Instance.TallyVotes();

            CheckWinConditions();
            if (currentState == MatchState.GameOver) return;

            // --- CHANGED: INFINITE STAGE LOOP ---
            // The match continues indefinitely until a true win condition is met!
            currentStage++;
            ChangeState(MatchState.ActionStage);
        }
    }

    // TELEPORT METHOD REMOVED ENTIRELY 

    private void CheckWinConditions()
    {
        if (currentState == MatchState.GameOver || currentState == MatchState.Initialization) return;

        if (TaskManager.Instance != null && TaskManager.Instance.currentCourtProgress >= TaskManager.Instance.maxCourtProgress)
        {
            TriggerGameOver("Court", "All tasks were completed!");
            return;
        }

        if (RoleManager.Instance != null && RoleManager.Instance.currentKing != null)
        {
            if (RoleManager.Instance.currentKing.isGhost)
            {
                TriggerGameOver("Corrupted", "The King was eliminated!");
                return;
            }
        }

        // --- NEW: POPULATION WIN CONDITIONS ---
        if (RoleManager.Instance != null)
        {
            int aliveCorrupted = 0;
            int aliveCourt = 0;

            foreach (PlayerController player in RoleManager.Instance.allPlayers)
            {
                if (player == null) continue;

                if (!player.isGhost)
                {
                    if (player.currentRole == PlayerRole.Corrupted)
                        aliveCorrupted++;
                    else
                        aliveCourt++;
                }
            }

            // 1. If Corrupted numbers equal or exceed the remaining innocents
            if (aliveCorrupted >= aliveCourt && aliveCorrupted > 0)
            {
                TriggerGameOver("Corrupted", "The Corrupted have matched the Court's numbers!");
                return;
            }

            // 2. If all Corrupted are successfully executed on the Gallows
            if (aliveCorrupted == 0 && aliveCourt > 0)
            {
                TriggerGameOver("Court", "All Corrupted have been eliminated!");
                return;
            }
        }
    }

    public void TriggerGameOver(string winner, string reason)
    {
        if (currentState == MatchState.GameOver) return; 
        winningTeam = winner;
        winReason = reason;
        ChangeState(MatchState.GameOver);
    }
}