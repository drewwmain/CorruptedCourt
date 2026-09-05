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
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlay()
        {
            ClearAll();
        }
    }
}
