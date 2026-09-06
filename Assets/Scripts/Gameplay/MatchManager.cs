using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using CorruptedCourt.Core;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Gameplay
{
    /// <summary>How far an absent court member stood from the meeting room when the pre-meeting
    /// scramble began. Shown next to their name in the meeting roll-call so a missing player who
    /// started on the far side of the map reads as "had a long way to come" rather than as a bare
    /// accusation.</summary>
    public enum ScrambleStartDistance
    {
        Nearby,
        AcrossTheMap
    }

    /// <summary>One entry in the meeting roll-call's absent list: a living court member who was not
    /// in the meeting room, plus how far from it they started the pre-meeting scramble.</summary>
    public struct AbsentMember
    {
        public readonly string Name;
        public readonly ScrambleStartDistance StartDistance;

        public AbsentMember(string name, ScrambleStartDistance startDistance)
        {
            Name = name;
            StartDistance = startDistance;
        }
    }

    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        public enum MatchState
        {
            Initialization,
            ActionStage,
            TransitionToMeeting, // The pre-meeting scramble phase (length scales with map size - G3.1)
            MeetingPhase,
            GameOver
        }

        [Header("Match Settings")]
        public MatchState currentState;
        public int currentStage = 1;

        [Header("Timers")]
        public float meetingDuration = 120f;
        public float actionDuration = 300f;
        private float currentTimer;

        [Header("Pre-Meeting Scramble (G3.1)")]
        [Tooltip("Assumed jog speed, in world units per second, used to size the scramble window: the " +
                 "timer is the meeting-room-to-farthest-task distance divided by this, plus the buffer " +
                 "below. Set it near the player's un-penalised walk speed; lower it to be more generous.")]
        [SerializeField] private float scrambleTravelSpeed = 4f;

        [Tooltip("Flat seconds added on top of the computed jog time so players can react to the round " +
                 "ending, round a corner, or unstick from geometry.")]
        [SerializeField] private float scrambleBufferSeconds = 4f;

        [Tooltip("Fallback scramble length, in seconds, used only when the map span can't be measured " +
                 "(no meeting-room reference, or no task locations registered yet).")]
        [FormerlySerializedAs("transitionDuration")]
        [SerializeField] private float scrambleDurationFallback = 20f;

        [Tooltip("A player who began the scramble at least this fraction of the measured map span away " +
                 "from the meeting room is shown as absent 'from across the map' rather than 'nearby'.")]
        [Range(0f, 1f)]
        [SerializeField] private float acrossMapDistanceFraction = 0.5f;

        [Header("Meeting Settings")]
        [Tooltip("The Zone ID of the meeting room (matches the TaskZone script on the room's collider)")]
        public string meetingZoneID = "MeetingRoom";
        [Tooltip("The physical center of the meeting room for the UI Waypoint")]
        public Transform meetingRoomTransform;

        [Header("Corpses")]
        [Tooltip("Body left behind when a player dies, so the Court has physical evidence a murder " +
                 "happened. Must carry a Corpse component and sit on the Interactable layer. Leave empty " +
                 "to disable corpse spawning.")]
        public GameObject corpsePrefab;

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
        // Refilled (never reallocated) each time we broadcast the roll-call. Subscribers (UIManager)
        // consume them synchronously, so reusing the buffers is safe.
        private readonly List<AbsentMember> absentBuffer = new List<AbsentMember>();
        private readonly List<string> deadNamesBuffer = new List<string>();
        // Last whole-second value pushed to the transition-timer view, so we raise the event (and its
        // string build on the UI side) once per second instead of every frame.
        private int lastTransitionSecondShown = -1;

        // Meeting-room-to-farthest-content distance measured at the start of the current scramble.
        // <= 0 means it couldn't be measured (no meeting-room reference); the timer then uses
        // scrambleDurationFallback and every absent player buckets as "nearby".
        private float scrambleMapSpan;

        // How far each living player stood from the meeting room the instant the scramble began.
        // Cleared and refilled by BeginScrambleMeasurements; read by BucketFor when the roll-call is
        // broadcast so an absent player is shown with how far they had to come.
        private readonly Dictionary<PlayerController, float> scrambleStartDistance =
            new Dictionary<PlayerController, float>();

        /// <summary>Seconds of play since the match left Initialization. Stamped onto a Corpse at the
        /// moment of death so a body report can say when the kill happened. Pure runtime state.</summary>
        public float MatchTime { get; private set; }

        // --- Most recent body report (Corpse.OnInteract -> TriggerReportedBodyMeeting). A report has no
        //     condemned defendant, so this is separate from VotingManager.condemnedPlayer. Recorded for
        //     the meeting flow / UI. Cleared when a fresh action stage begins. ---
        public PlayerController LastBodyReportReporter { get; private set; }
        public PlayerController LastBodyReportVictim { get; private set; }
        public string LastBodyReportZoneID { get; private set; }
        public bool HasPendingBodyReport { get; private set; }

        // Which kind of meeting the current transition is heading into. Set by whichever trigger fired,
        // read once when MeetingPhase opens and handed to VotingManager.StartMeeting.
        private VotingManager.MeetingKind pendingMeetingKind = VotingManager.MeetingKind.GallowsTrial;

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
            // Match clock: advances during live play (not while the match is booting or finished).
            if (currentState != MatchState.Initialization && currentState != MatchState.GameOver)
                MatchTime += Time.deltaTime;

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

            // Heavy-item movement penalties (half speed, no jump) are lifted for exactly the pre-meeting
            // scramble, and any heavy/haul item in hand is dropped on the leading edge - players doing
            // haul tasks must be able to reach the meeting room in the transition window (G3.1).
            SetScrambleGraceForAll(currentState == MatchState.TransitionToMeeting);

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

                    // A new action stage clears any body report the last meeting was called on.
                    HasPendingBodyReport = false;
                    LastBodyReportReporter = null;
                    LastBodyReportVictim = null;
                    LastBodyReportZoneID = null;

                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;

                    // TELEPORT REMOVED: Players must now run from the meeting room to their tasks!

                    if (TaskManager.Instance != null) TaskManager.Instance.AssignTasksForNewStage();
                    break;

                // --- THE PRE-MEETING SCRAMBLE (length scales with map size - see G3.1) ---
                case MatchState.TransitionToMeeting:
                    // Measure the map and each player's distance from the meeting room BEFORE sizing
                    // the timer or building the roll-call, so all three read the same snapshot.
                    BeginScrambleMeasurements();
                    currentTimer = ComputeScrambleDuration();

                    Log.Game($"--- ROUND OVER: {Mathf.CeilToInt(currentTimer)} seconds to reach the {meetingZoneID}! ---");

                    // Prime the timer view once, then HandleStateTimers only re-raises on each new second.
                    lastTransitionSecondShown = Mathf.CeilToInt(currentTimer);
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
                    if (VotingManager.Instance != null) VotingManager.Instance.StartMeeting(pendingMeetingKind);
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
                pendingMeetingKind = VotingManager.MeetingKind.GallowsTrial;
                ChangeState(MatchState.TransitionToMeeting);
            }
        }

        // --- CORPSES ---

        // Called from PlayerVitals.BecomeGhost the instant a player dies. Drops a reportable body at
        // the victim's position/rotation carrying who they were, the room they died in, and the
        // match-clock time of death. Skipped outside live play (an on-stage gallows execution during
        // the meeting needs no body report).
        public void SpawnCorpse(PlayerController victim)
        {
            if (victim == null || corpsePrefab == null) return;
            if (currentState != MatchState.ActionStage && currentState != MatchState.TransitionToMeeting) return;

            GameObject corpseObj = Instantiate(corpsePrefab, victim.transform.position, victim.transform.rotation);

            Corpse corpse = corpseObj.GetComponent<Corpse>();
            if (corpse != null)
            {
                string zone = victim.Vitals != null ? victim.Vitals.currentZoneID : "";
                corpse.Initialize(victim, zone, MatchTime);
            }
            else
            {
                Log.Warn("[MatchManager] corpsePrefab has no Corpse component - the body cannot be reported.");
            }
        }

        // Entry point for a corpse report (Corpse.OnInteract). Unlike TriggerGallowsMeeting there is no
        // condemned defendant yet - it opens an Inquest, which nominates one. Records who reported whom
        // and where, raises the meeting-open announcement, then reuses the existing pre-meeting flow.
        // Returns false (and does nothing) if a meeting is already under way, so the body stays reportable.
        public bool TriggerReportedBodyMeeting(PlayerController reporter, PlayerController victim, string deathZoneID)
        {
            if (currentState != MatchState.ActionStage) return false;

            LastBodyReportReporter = reporter;
            LastBodyReportVictim = victim;
            LastBodyReportZoneID = deathZoneID;
            HasPendingBodyReport = true;

            string reporterName = reporter != null ? reporter.DisplayName : "Someone";
            string victimName = victim != null ? victim.DisplayName : "an unknown court member";
            string zoneName = string.IsNullOrEmpty(deathZoneID) ? "an unknown location" : deathZoneID;

            Log.Game($"<color=#E74C3C>--- BODY REPORTED: {reporterName} found {victimName} in {zoneName}! ---</color>");

            pendingMeetingKind = VotingManager.MeetingKind.Inquest;
            ChangeState(MatchState.TransitionToMeeting);

            // Raised after the state change so the view has already reset its meeting panels for the
            // new meeting before it paints the announcement over them.
            GameEvents.RaiseMeetingAnnouncement($"{reporterName} reported {victimName}'s body in {zoneName}.");
            return true;
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

        // --- ROLL-CALL TRACKING (event-driven; see OnPlayerZoneChanged / OnPlayerGhosted) ---
        // "Absent" is only living members out of the room. Dead members are reported separately (see
        // BroadcastAbsentPlayers) so a living player can account for every name in the lobby: anyone
        // not in the absent list and not in the dead list is present in the room.

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
            absentBuffer.Clear();
            foreach (PlayerController p in absentPlayers)
                if (p != null) absentBuffer.Add(new AbsentMember(p.DisplayName, BucketFor(p)));

            // Dead members, rebuilt from the lobby each broadcast - a ghost is never in absentPlayers,
            // so without this list a murdered player would just vanish from the roll-call.
            deadNamesBuffer.Clear();
            if (RoleManager.Instance != null)
            {
                foreach (PlayerController p in RoleManager.Instance.allPlayers)
                    if (p != null && p.Vitals != null && p.Vitals.isGhost)
                        deadNamesBuffer.Add(p.DisplayName);
            }

            GameEvents.RaiseAbsentPlayersChanged(absentBuffer, deadNamesBuffer);
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

        // --- PRE-MEETING SCRAMBLE FAIRNESS (G3.1) ---
        // The scramble is the only time the Corrupted know a meeting is coming (a Royal triggers it),
        // so an innocent hauling a chest at the map's edge must not be flagged "Absent" as a bare
        // accusation. We: (1) lift the heavy-item movement penalty and drop heavy/haul items for the
        // duration, (2) size the timer to how big the map actually is, and (3) tag each absent player
        // with how far from the room they started so the UI can show it.

        // Toggles the heavy-item movement grace on every lobby member. On the leading edge this also
        // force-drops any heavy/haul item in hand (see PlayerController.SetScrambleGrace).
        private void SetScrambleGraceForAll(bool active)
        {
            if (RoleManager.Instance == null) return;
            foreach (PlayerController p in RoleManager.Instance.allPlayers)
            {
                if (p == null) continue;
                p.SetScrambleGrace(active);
            }
        }

        // Snapshots the map span and every living player's distance from the meeting room at the
        // instant the scramble begins. Everything downstream (timer length, absent buckets) reads
        // this snapshot so the numbers stay consistent for the whole transition.
        private void BeginScrambleMeasurements()
        {
            scrambleStartDistance.Clear();

            Vector3 meetingPos = ResolveMeetingRoomPosition(out bool haveMeetingPos);
            scrambleMapSpan = MeasureMapSpan(meetingPos, haveMeetingPos);

            if (!haveMeetingPos || RoleManager.Instance == null) return;

            foreach (PlayerController p in RoleManager.Instance.allPlayers)
            {
                if (p == null || p.Vitals == null || p.Vitals.isGhost) continue;
                scrambleStartDistance[p] = Vector3.Distance(p.transform.position, meetingPos);
            }
        }

        // Timer length = how far the farthest content sits from the meeting room, divided by the
        // assumed jog speed, plus a flat buffer. Falls back to the serialized flat value when the
        // map can't be measured.
        private float ComputeScrambleDuration()
        {
            if (scrambleMapSpan > 0f && scrambleTravelSpeed > 0.01f)
                return scrambleMapSpan / scrambleTravelSpeed + scrambleBufferSeconds;
            return scrambleDurationFallback;
        }

        // Farthest a player could reasonably have to travel to reach the meeting room: the distance
        // from the room to the most distant registered task location. That's where the stage's
        // demanding content sits, and players walk to it straight out of the meeting room (no
        // respawn), so it is the real edge of the playable area. Returns <= 0 when nothing
        // measurable exists.
        private float MeasureMapSpan(Vector3 meetingPos, bool haveMeetingPos)
        {
            if (!haveMeetingPos) return 0f;

            float span = 0f;

            foreach (TaskLocation loc in TaskLocation.AllLocations)
            {
                if (loc == null) continue;
                float d = Vector3.Distance(loc.transform.position, meetingPos);
                if (d > span) span = d;
            }

            return span;
        }

        // Where the meeting room is, for the scramble timer and the per-player start-distance buckets.
        // Prefers the explicit centre transform; falls back to a TaskZone whose zoneID matches
        // meetingZoneID so the feature still works when only the trigger volume is wired.
        private Vector3 ResolveMeetingRoomPosition(out bool resolved)
        {
            if (meetingRoomTransform != null)
            {
                resolved = true;
                return meetingRoomTransform.position;
            }

            foreach (TaskZone zone in TaskZone.AllZones)
            {
                if (zone != null && zone.zoneID == meetingZoneID)
                {
                    resolved = true;
                    return zone.transform.position;
                }
            }

            resolved = false;
            return Vector3.zero;
        }

        // Buckets one absent player by how far they stood from the meeting room when the scramble
        // began. Unknown distance (no meeting-room reference / player joined mid-scramble) -> Nearby,
        // so the UI falls back to a plain "absent" rather than a false "across the map".
        private ScrambleStartDistance BucketFor(PlayerController p)
        {
            if (p == null || scrambleMapSpan <= 0f) return ScrambleStartDistance.Nearby;
            if (!scrambleStartDistance.TryGetValue(p, out float startDistance)) return ScrambleStartDistance.Nearby;

            return startDistance >= scrambleMapSpan * acrossMapDistanceFraction
                ? ScrambleStartDistance.AcrossTheMap
                : ScrambleStartDistance.Nearby;
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
