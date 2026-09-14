using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Base for a two-seat shared session at one station - <c>game_play</c> (chess) is the motivating
    /// case (Tasks/ARCHITECTURE.md §10.1, Part VI item 1). The first player to interact claims a seat
    /// and starts the session; a second player joins the SAME session via <see cref="TryJoin"/>,
    /// claiming the other seat and being credited alongside whoever's already there
    /// (<c>MinigameBase.AddParticipant</c>, B10a).
    ///
    /// Ending is generic on purpose: whatever terminal condition a concrete subclass detects
    /// (checkmate, stalemate, resignation, a seat emptying, ...) just calls the inherited
    /// <c>CompleteMinigame()</c> / <c>CancelMinigame()</c> - every participant added via
    /// AddParticipant/TryJoin is credited regardless of the outcome (R7: "either player leaving the
    /// board" still credits both, per the chess ruling). This base's only job is the seat plumbing:
    /// claiming one on begin/join, and releasing every participant's seat on end so an abandoned
    /// session never leaves the table permanently occupied. The actual terminal-condition logic
    /// (chess rules) is B20 content, not this base.
    /// </summary>
    public abstract class SharedStationMinigame : MinigameBase
    {
        /// <summary>The station this session is seated at. Null if OnMinigameBegin couldn't find/claim one.</summary>
        protected TaskStationState Station { get; private set; }

        protected override void OnMinigameBegin()
        {
            Station = Context != null && Context.Target != null
                ? Context.Target.GetComponentInParent<TaskStationState>()
                : null;

            if (Station == null || !Station.TryClaimSeat(player))
            {
                Log.Warn($"[{GetType().Name}] no station / no free seat - cancelling.");
                CancelMinigame();
                return;
            }

            OnSharedBegin();
        }

        /// <summary>
        /// A second player joins this already-running session: claims the station's other seat and is
        /// credited alongside whoever's already here. False if the station has no free seat left.
        /// </summary>
        public bool TryJoin(PlayerController p, TaskInstance task)
        {
            if (Station == null || p == null || !Station.TryClaimSeat(p)) return false;
            AddParticipant(p, task);
            OnParticipantJoined(p);
            return true;
        }

        protected override void OnMinigameEnd(bool won)
        {
            if (Station == null) return;
            foreach (MinigameParticipant participant in participants)
            {
                if (participant.Player != null) Station.ReleaseSeat(participant.Player);
            }
        }

        // --- hooks for concrete minigames -----------------------------------------------------------

        /// <summary>Runs once, after the first seat is claimed.</summary>
        protected virtual void OnSharedBegin() { }

        /// <summary>Runs each time <see cref="TryJoin"/> succeeds.</summary>
        protected virtual void OnParticipantJoined(PlayerController p) { }
    }
}
