using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Root of every minigame. Owns the shared lifecycle and a global "is any minigame running"
    /// registry. Subclasses pick a family (PanelMinigame / HandMinigame / PartnerMinigame / ...) rather
    /// than extending this directly. See Assets/Scripts/Minigames/ARCHITECTURE.md.
    /// </summary>
    public abstract class MinigameBase : MonoBehaviour
    {
        // --- global registry: the single source of truth for "the player is in a minigame" -----------
        private static readonly HashSet<MinigameBase> active = new HashSet<MinigameBase>();

        private static void Prune() { active.RemoveWhere(m => m == null); }

        /// <summary>Every minigame that has run SetupMinigame and not yet ended. Self-prunes destroyed entries.</summary>
        public static IReadOnlyCollection<MinigameBase> ActiveMinigames { get { Prune(); return active; } }

        /// <summary>True while any minigame is open. Replaces scattered isPlayingMinigame / hangReachActive checks.</summary>
        public static bool IsAnyActive { get { Prune(); return active.Count > 0; } }

        /// <summary>The most recently started still-open minigame, or null.</summary>
        public static MinigameBase Current
        {
            get { Prune(); foreach (MinigameBase m in active) return m; return null; }
        }

        /// <summary>
        /// Whether the player keeps normal WASD locomotion while this minigame is open. True for pure
        /// button / UI minigames (cut cake, eat, dummy, emote wheel) - the player can walk around
        /// freely. <see cref="HandMinigame"/> overrides this to false: it runs its OWN leashed footwork
        /// (<c>HandleFootwork</c>), and letting the normal path also move the player would Move() them
        /// twice per frame.
        /// </summary>
        public virtual bool AllowsPlayerMovement => true;

        // --- per-instance state ---------------------------------------------------------------------
        protected PlayerController player;
        protected TaskInstance activeTask;

        /// <summary>
        /// One player taking part in this minigame session and the task (if any) it's judged against
        /// for them. A null Task means this participant has no matching task for this minigame -
        /// FinishMinigame(null) still runs for them on completion, which resolves any OTHER task they
        /// hold generically instead of auto-completing this one (R1: any action is open to anyone;
        /// only credit for the specific task is gated).
        /// </summary>
        public struct MinigameParticipant
        {
            public PlayerController Player;
            public TaskInstance Task;
        }

        /// <summary>
        /// Every player this session credits on completion. Seeded with one entry (the launching
        /// player/task) by SetupMinigame, so all of today's single-player minigames are unaffected;
        /// <see cref="AddParticipant"/> grows it for shared sessions (handshake, cheers, duel, chess -
        /// B10b/B11). Nothing adds a second participant yet.
        /// </summary>
        protected readonly List<MinigameParticipant> participants = new List<MinigameParticipant>();

        /// <summary>Richer launch payload. Null when launched via the legacy 2-arg SetupMinigame.</summary>
        public MinigameContext Context { get; protected set; }

        /// <summary>
        /// Legacy entry point: injects the player and task the moment the minigame spawns. Still the
        /// actual implementation - SetupMinigame(MinigameContext) forwards to this after stashing the
        /// context, so both launch paths (PlayerController.StartMinigame and
        /// TaskDepositStation.LaunchDepositMinigame) funnel through here.
        /// </summary>
        public virtual void SetupMinigame(PlayerController playerRef, TaskInstance task)
        {
            player = playerRef;
            activeTask = task;
            if (playerRef != null)
            {
                // Every launch path sets this so WaypointManager can hide this task's marker while any
                // minigame is open, regardless of which of the two launchers started it.
                playerRef.activeMinigameTask = task;
                participants.Add(new MinigameParticipant { Player = playerRef, Task = task });
            }
            active.Add(this);
            OnMinigameBegin();
        }

        /// <summary>
        /// Preferred entry point once a launcher builds a MinigameContext. Forwards to the legacy
        /// overload after stashing the context, so migration can be done file by file.
        /// </summary>
        public virtual void SetupMinigame(MinigameContext context)
        {
            Context = context;
            SetupMinigame(context != null ? context.Player : null,
                          context != null ? context.Task : null);
        }

        /// <summary>Adds another player to this shared session (B10b/B11 - handshake, cheers, duel,
        /// chess). No-op for a null player or one already in <see cref="participants"/>.</summary>
        public void AddParticipant(PlayerController p, TaskInstance task)
        {
            if (p == null) return;

            foreach (MinigameParticipant existing in participants)
            {
                if (existing.Player == p) return;
            }

            participants.Add(new MinigameParticipant { Player = p, Task = task });
        }

        /// <summary>Call when the player successfully finishes the minigame's action.</summary>
        public virtual void CompleteMinigame()
        {
            active.Remove(this);
            OnMinigameEnd(true);

            // Every participant is credited independently - the minigame's own win/lose outcome is
            // irrelevant to who gets credit (R7); a participant with no matching task still runs
            // FinishMinigame(null), which resolves any OTHER task they hold instead of this one (R1).
            foreach (MinigameParticipant participant in participants)
            {
                if (participant.Player != null) participant.Player.FinishMinigame(participant.Task);
            }

            Destroy(gameObject);
        }

        /// <summary>Call if the player closes the minigame early or is interrupted (attacked, Esc).</summary>
        public virtual void CancelMinigame()
        {
            active.Remove(this);
            OnMinigameEnd(false);

            if (player != null)
            {
                player.CancelMinigame();
            }

            Destroy(gameObject);
        }

        // --- hooks for subclasses ------------------------------------------------------------------

        /// <summary>Runs right after player / task / context are injected. Override for setup.</summary>
        protected virtual void OnMinigameBegin() { }

        /// <summary>Runs as the minigame closes. <paramref name="won"/> = it reached its success state.</summary>
        protected virtual void OnMinigameEnd(bool won) { }

        // --- mid-lifecycle registry control ---------------------------------------------------------
        // A HandMinigame-style subclass can hand control back to the player WELL BEFORE its outcome is
        // known - e.g. the instant a deposit item is released, while it's still falling/settling/awaiting
        // a miss retry. IsAnyActive (hence PlayerController.isPlayingMinigame) needs to go false for that
        // whole window too, or the player is stuck unable to move: PlayerController.Update() only takes
        // the normal-controls branch when isPlayingMinigame is false. CompleteMinigame/CancelMinigame
        // already remove the minigame for its true end; these are for the earlier, mid-lifecycle handback.

        /// <summary>
        /// Leaves the active registry without ending the minigame. Call when a subclass hands control back
        /// to the player mid-lifecycle (see HandMinigame.RestorePlayer). Call <see cref="RejoinActiveRegistry"/>
        /// if the minigame goes back to restricting the player afterwards (e.g. a retry re-locks controls).
        /// Harmless to call again from CompleteMinigame/CancelMinigame's own removal - HashSet.Remove on a
        /// non-member is a no-op.
        /// </summary>
        protected void LeaveActiveRegistry() => active.Remove(this);

        /// <summary>Re-enters the active registry after <see cref="LeaveActiveRegistry"/> - see there.</summary>
        protected void RejoinActiveRegistry() => active.Add(this);
    }
}
