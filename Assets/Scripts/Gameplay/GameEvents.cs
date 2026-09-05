using System;
using System.Collections.Generic;
using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// One-way gameplay -> view signalling. Gameplay code raises; view code (UIManager, WaypointManager)
    /// subscribes. Nothing in here references UnityEngine.UI or a view type, so the dependency only
    /// points one way.
    ///
    /// These are <b>static</b> events. With "Enter Play Mode -> Reload Domain" disabled they are NOT
    /// cleared between Play sessions, so handlers from destroyed objects would pile up and fire against
    /// dead references. <see cref="ClearAll"/> wipes every invocation list and runs automatically on
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> - before any scene object's
    /// Awake/OnEnable - so every Play session starts clean. (Subscribers must still unsubscribe in
    /// OnDisable; that is what keeps scene reloads mid-session safe.)
    /// </summary>
    public static class GameEvents
    {
        /// <summary>The local player's assigned/active task list or current step changed. Carries the local player.</summary>
        public static event Action<PlayerController> LocalTasksChanged;
        public static void RaiseLocalTasksChanged(PlayerController local)
        {
            LocalTasksChanged?.Invoke(local);
        }

        /// <summary>Global court-task progress changed. (current, max).</summary>
        public static event Action<float, float> CourtProgressChanged;
        public static void RaiseCourtProgressChanged(float current, float max)
        {
            CourtProgressChanged?.Invoke(current, max);
        }

        /// <summary>The match advanced to a new state. Carries the new state.</summary>
        public static event Action<MatchManager.MatchState> MatchStateChanged;
        public static void RaiseMatchStateChanged(MatchManager.MatchState state)
        {
            MatchStateChanged?.Invoke(state);
        }

        /// <summary>The set of court members not in the meeting room changed. Carries their display names.</summary>
        public static event Action<IReadOnlyList<string>> AbsentPlayersChanged;
        public static void RaiseAbsentPlayersChanged(IReadOnlyList<string> absentNames)
        {
            AbsentPlayersChanged?.Invoke(absentNames);
        }

        /// <summary>The Corrupted power-up inventory changed. Carries the 3-slot array.</summary>
        public static event Action<PowerUpData[]> CorruptedInventoryChanged;
        public static void RaiseCorruptedInventoryChanged(PowerUpData[] inventory) => CorruptedInventoryChanged?.Invoke(inventory);

        /// <summary>The highlighted Corrupted power-up slot changed. -1 = none.</summary>
        public static event Action<int> CorruptedSlotHighlighted;
        public static void RaiseCorruptedSlotHighlighted(int activeIndex) => CorruptedSlotHighlighted?.Invoke(activeIndex);

        /// <summary>Seconds left in the pre-meeting transition. Raised every frame while it runs.</summary>
        public static event Action<float> TransitionTimerTicked;
        public static void RaiseTransitionTimerTicked(float secondsRemaining) => TransitionTimerTicked?.Invoke(secondsRemaining);

        /// <summary>The match ended. Carries (winningTeam, reason).</summary>
        public static event Action<string, string> GameOverShown;
        public static void RaiseGameOverShown(string winner, string reason) => GameOverShown?.Invoke(winner, reason);

        /// <summary>A new match/round is starting - the game-over screen should hide.</summary>
        public static event Action GameOverHidden;
        public static void RaiseGameOverHidden() => GameOverHidden?.Invoke();

        /// <summary>The meeting phase began - the "open vote" affordance may appear.</summary>
        public static event Action VotingPhaseStarted;
        public static void RaiseVotingPhaseStarted() => VotingPhaseStarted?.Invoke();

        /// <summary>The voting panel should close (meeting ended, or match reset).</summary>
        public static event Action VotingPanelHidden;
        public static void RaiseVotingPanelHidden() => VotingPanelHidden?.Invoke();

        /// <summary>Show an on-screen waypoint to the meeting room. Carries its transform.</summary>
        public static event Action<Transform> MeetingWaypointSet;
        public static void RaiseMeetingWaypointSet(Transform meetingTransform) => MeetingWaypointSet?.Invoke(meetingTransform);

        /// <summary>Remove the meeting-room waypoint.</summary>
        public static event Action MeetingWaypointCleared;
        public static void RaiseMeetingWaypointCleared() => MeetingWaypointCleared?.Invoke();

        /// <summary>Spymaster: show temporary VIP-tracking waypoints. Carries (targets, duration).</summary>
        public static event Action<List<Transform>, float> SpymasterWaypointsShown;
        public static void RaiseSpymasterWaypointsShown(List<Transform> targets, float duration) => SpymasterWaypointsShown?.Invoke(targets, duration);

        /// <summary>The in-game settings / pause menu opened (true) or closed (false).
        /// <see cref="SettingsMenuOpen"/> caches the latest value for pull-style readers like MinigameInput.</summary>
        public static event Action<bool> SettingsMenuToggled;
        public static bool SettingsMenuOpen { get; private set; }
        public static void RaiseSettingsMenuToggled(bool open)
        {
            SettingsMenuOpen = open;
            SettingsMenuToggled?.Invoke(open);
        }

        /// <summary>
        /// Drops every subscriber from every event. Runs automatically before each Play session (see
        /// <see cref="ResetStaticsOnPlay"/>); call it directly only from tests.
        /// </summary>
        public static void ClearAll()
        {
            LocalTasksChanged = null;
            CourtProgressChanged = null;
            MatchStateChanged = null;
            AbsentPlayersChanged = null;
            CorruptedInventoryChanged = null;
            CorruptedSlotHighlighted = null;
            TransitionTimerTicked = null;
            GameOverShown = null;
            GameOverHidden = null;
            VotingPhaseStarted = null;
            VotingPanelHidden = null;
            MeetingWaypointSet = null;
            MeetingWaypointCleared = null;
            SpymasterWaypointsShown = null;
            SettingsMenuToggled = null;
            SettingsMenuOpen = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlay()
        {
            ClearAll();
        }
    }
}
