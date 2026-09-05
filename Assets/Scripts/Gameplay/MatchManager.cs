using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Gameplay
{
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

        [Header("Debug")]
        [Tooltip("Escape hatch: re-check win conditions every frame like the old build did. Off = they are " +
                 "only re-checked on the events that can change the outcome (a death, a completed task, a " +
                 "vote tally, a stage change).")]
        [SerializeField] private bool debugPollWinConditions = false;

        // Court members currently outside the meeting room. Maintained by PlayerZoneChanged / PlayerGhosted
        // while the pre-meeting scramble is live, instead of scanning every player every frame.
        private readonly HashSet<PlayerController> absentPlayers = new HashSet<PlayerController>();
        // Refilled (never reallocated) each time we broadcast the absent list. Subscribers (UIManager)
        // consume it synchronously, so reusing the buffer is safe.
        private readonly List<string> absentNamesBuffer = new List<string>();
        // Last whole-second value pushed to the transition-timer view, so we raise the event (and its
        // string build on the UI side) once per second instead of every frame.
        private int lastTransitionSecondShown = -1;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        void OnEnable()
        {
            GameEvents.PlayerZoneChanged += OnPlayerZoneChanged;
            GameEvents.PlayerGhosted += OnPlayerGhosted;
            GameEvents.CourtProgressChanged += OnCourtProgressChanged;
        }

        void OnDisable()
        {
            GameEvents.PlayerZoneChanged -= OnPlayerZoneChanged;
            GameEvents.PlayerGhosted -= OnPlayerGhosted;
            GameEvents.CourtProgressChanged -= OnCourtProgressChanged;
        }

        void Start()
        {
            ChangeState(MatchState.Initialization);
        }

        void Update()
        {
            HandleStateTimers();

            // Win conditions are event-driven now (see OnPlayerGhosted / OnCourtProgressChanged /
            // ChangeState / TransitionToNextPhase). This is only for debugging a suspected missed trigger.
            if (debugPollWinConditions) CheckWinConditions();
        }

        private void ChangeState(MatchState newState)
        {
            currentState = newState;

            // Views (transition panel, etc.) subscribe to this - MatchManager no longer pokes them directly.
            GameEvents.RaiseMatchStateChanged(currentState);

            switch (currentState)
            {
                case MatchState.Initialization:
                    Log.Game("--- MATCH STARTING: Initialization Phase ---");
                    currentStage = 1;

                    if (RoleManager.Instance != null) RoleManager.Instance.AssignAllRoles();

                    // Roles are set - size the Court meter to the lobby now, once per match, before any
                    // win check can run in a non-Initialization state.
                    if (TaskManager.Instance != null) TaskManager.Instance.InitializeCourtMeter();

                    GameEvents.RaiseVotingPanelHidden();
                    GameEvents.RaiseGameOverHidden();

                    ChangeState(MatchState.ActionStage);
                    break;

                case MatchState.ActionStage:
                    Log.Game($"--- STAGE {currentStage}: Action Stage Started! ---");
                    currentTimer = actionDuration;

                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;

                    // TELEPORT REMOVED: Players must now run from the meeting room to their tasks!

                    if (TaskManager.Instance != null) TaskManager.Instance.AssignTasksForNewStage();
                    break;

                // --- NEW: THE 20 SECOND TRANSITION PHASE ---
                case MatchState.TransitionToMeeting:
                    Log.Game($"--- ROUND OVER: 20 Seconds to reach the {meetingZoneID}! ---");
                    currentTimer = transitionDuration;

                    // Prime the timer view once, then HandleStateTimers only re-raises on each new second.
                    lastTransitionSecondShown = Mathf.CeilToInt(transitionDuration);
                    GameEvents.RaiseTransitionTimerTicked(currentTimer);

                    // Snapshot who is out of the room, then keep it live off zone-change events.
                    RebuildAbsentSet();
                    BroadcastAbsentPlayers();

                    // Ask the view to draw a marker at the meeting room
                    if (meetingRoomTransform != null)
                    {
                        GameEvents.RaiseMeetingWaypointSet(meetingRoomTransform);
                    }
                    break;

                case MatchState.MeetingPhase:
                    Log.Game($"--- STAGE {currentStage}: Meeting Phase Started! ---");
                    currentTimer = meetingDuration;

                    // 1. Turn off the meeting waypoint (the transition panel hides off MatchStateChanged)
                    GameEvents.RaiseMeetingWaypointCleared();

                    // 2. One final scan locks in the absent players for the meeting phase. From here the
                    //    list is frozen (zone changes during the meeting no longer move it), matching the
                    //    old behaviour where live tracking only ran during TransitionToMeeting.
                    RebuildAbsentSet();

                    // 3. Trigger the meeting flow; the absent list goes out as an event for the view.
                    if (VotingManager.Instance != null) VotingManager.Instance.StartMeeting();
                    BroadcastAbsentPlayers();
                    break;

                case MatchState.GameOver:
                    currentTimer = 0f;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;

                    GameEvents.RaiseGameOverShown(winningTeam, winReason);
                    break;
            }

            // A stage / phase change can itself be a win trigger (e.g. tasks finished right as the meeting
            // ends). Cheap, and CheckWinConditions no-ops for Initialization / GameOver.
            CheckWinConditions();
        }

        private void HandleStateTimers()
        {
            // We removed MatchState.ActionStage from this check so the round lasts forever
            // until tasks are done or the King triggers the Gallows!
            if (currentState != MatchState.MeetingPhase && currentState != MatchState.TransitionToMeeting)
                return;

            currentTimer -= Time.deltaTime;

            // Update the on-screen countdown, but only when the displayed whole-second actually changes -
            // the old code raised this (and rebuilt a string on the UI side) every single frame.
            if (currentState == MatchState.TransitionToMeeting)
            {
                int secondsLeft = Mathf.CeilToInt(Mathf.Max(0f, currentTimer));
                if (secondsLeft != lastTransitionSecondShown)
                {
                    lastTransitionSecondShown = secondsLeft;
                    GameEvents.RaiseTransitionTimerTicked(currentTimer);
                }
            }

            if (currentTimer <= 0f)
            {
                TransitionToNextPhase();
            }
        }

        // --- NEW: KING'S GALLOWS TRIGGER ---
        public void TriggerGallowsMeeting()
        {
            // Only allow this if we are actively playing the game
            if (currentState == MatchState.ActionStage)
            {
                Log.Game("<color=#F1C40F>--- THE KING HAS CALLED FOR AN EXECUTION! ---</color>");
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

        // --- ABSENT-PLAYER TRACKING (event-driven; see OnPlayerZoneChanged / OnPlayerGhosted) ---

        private bool IsAbsent(PlayerController p)
            => p != null && !p.Vitals.isGhost && p.Vitals.currentZoneID != meetingZoneID;

        private void RebuildAbsentSet()
        {
            absentPlayers.Clear();
            if (RoleManager.Instance == null) return;
            foreach (PlayerController p in RoleManager.Instance.allPlayers)
                if (IsAbsent(p)) absentPlayers.Add(p);
        }

        // Reconciles one player's membership in the absent set. Returns true if the set actually changed.
        private bool UpdateAbsentMembership(PlayerController p)
        {
            if (p == null) return false;
            return IsAbsent(p) ? absentPlayers.Add(p) : absentPlayers.Remove(p);
        }

        private void BroadcastAbsentPlayers()
        {
            absentNamesBuffer.Clear();
            foreach (PlayerController p in absentPlayers)
                if (p != null) absentNamesBuffer.Add(p.gameObject.name);
            GameEvents.RaiseAbsentPlayersChanged(absentNamesBuffer);
        }

        private void OnPlayerZoneChanged(PlayerController p)
        {
            // Live tracking only runs during the pre-meeting scramble (matches the old build).
            if (currentState != MatchState.TransitionToMeeting) return;
            if (UpdateAbsentMembership(p)) BroadcastAbsentPlayers();
        }

        private void OnPlayerGhosted(PlayerController p)
        {
            // A ghost is never "absent" - drop them from the scramble list if we're tracking it.
            if (currentState == MatchState.TransitionToMeeting && UpdateAbsentMembership(p))
                BroadcastAbsentPlayers();

            CheckWinConditions();
        }

        private void OnCourtProgressChanged(float current, float max)
        {
            CheckWinConditions();
        }

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
                if (RoleManager.Instance.currentKing.Vitals.isGhost)
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

                    if (!player.Vitals.isGhost)
                    {
                        // Population win conditions count strictly by Faction. A Corrupted player who
                        // was appointed Kingsguard still counts here as Corrupted.
                        if (player.Vitals.faction == Faction.Corrupted)
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
}
