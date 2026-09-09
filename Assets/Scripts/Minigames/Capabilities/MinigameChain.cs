using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// An optional follow-up minigame that auto-starts when the owning minigame completes, so a
    /// two-step task can play its second beat without the player re-interacting. The Cheers
    /// PartnerMinigame chains straight into the Drink ConsumeMinigame this way (ARCHITECTURE.md §5).
    ///
    /// Held as a <c>[SerializeField]</c> field on a concrete minigame, which exposes it through the
    /// <see cref="MinigameBase.Chain"/> override. <see cref="MinigameBase.CompleteMinigame"/> calls
    /// <see cref="LaunchNext"/> after it has finished the current step (<c>player.FinishMinigame</c>),
    /// so the task's *current* step is already the one the next minigame should satisfy - the fresh
    /// context carries the same player, held item, and <see cref="TaskInstance"/>.
    ///
    /// Not a MonoBehaviour - a minigame owns one as a field. An unassigned
    /// <see cref="nextMinigamePrefab"/> makes <see cref="LaunchNext"/> a no-op.
    ///
    /// NOTE: <c>player.FinishMinigame</c> also auto-advances any follow-up step that is already
    /// satisfied by player state, and that can itself launch the next step's own
    /// <c>minigamePrefab</c>. <see cref="LaunchNext"/> yields (no-op) when a minigame is already
    /// active for exactly that reason - so wire the follow-up step to be launched by EITHER its own
    /// <c>minigamePrefab</c> OR this chain, not both.
    /// </summary>
    [System.Serializable]
    public class MinigameChain
    {
        [Tooltip("The minigame spawned when the owner completes. Leave empty for no chain.")]
        public MinigameBase nextMinigamePrefab;

        [Tooltip("Seconds to wait after the owner completes before the next minigame takes over. " +
                 "0 = immediately. During any wait the player has normal control (no minigame is active).")]
        public float startDelaySeconds = 0f;

        /// <summary>
        /// Spawn <see cref="nextMinigamePrefab"/> and set it up with a fresh <see cref="MinigameContext"/>
        /// (same <paramref name="player"/>, their currently held item, and <paramref name="task"/>). Call
        /// from the owning minigame's success path - <see cref="MinigameBase.ChainNextIfSet"/> does this.
        /// No-op when no prefab is assigned, there is no player, or another minigame is already active
        /// (the follow-up step's own minigamePrefab was launched by FinishMinigame's auto-advance).
        /// </summary>
        public void LaunchNext(PlayerController player, TaskInstance task)
        {
            if (nextMinigamePrefab == null || player == null) return;
            if (MinigameBase.IsAnyActive) return;

            MinigameBase next = Object.Instantiate(nextMinigamePrefab);

            MinigameContext context = new MinigameContext(player, task)
            {
                // ConsumeMinigame and the other HandMinigame families read Context.HeldItem and do NOT
                // fall back to player.GetHeldItem(), so carry it explicitly - the player is still holding
                // whatever they carried into the first minigame.
                HeldItem = player.GetHeldItem()
            }.Resolve();

            if (startDelaySeconds > 0f)
                next.StartCoroutine(SetupAfterDelay(next, context, startDelaySeconds));
            else
                next.SetupMinigame(context);
        }

        // Runs on the freshly spawned minigame's own GameObject, so if it is destroyed before the delay
        // elapses (scene unload, match end) the pending setup dies with it instead of leaking.
        private static System.Collections.IEnumerator SetupAfterDelay(MinigameBase minigame,
                                                                      MinigameContext context, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (minigame != null) minigame.SetupMinigame(context);
        }
    }
}
