using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Base for "do something WITH another court member, using the hand rig": duel is the motivating
    /// case (a HandMinigame that also needs a resolved partner). Composes the same
    /// <see cref="PartnerLink"/> that <see cref="PartnerMinigame"/> uses, rather than inheriting from
    /// it - MinigameBase's single-inheritance hierarchy can't give one subclass both
    /// <see cref="HandMinigame"/>'s plumbing and <see cref="PartnerMinigame"/>'s body
    /// (Minigames/ARCHITECTURE.md §10.2).
    /// </summary>
    public abstract class PartnerHandMinigame : HandMinigame
    {
        [Header("Partner")]
        [Tooltip("Spawned as the stand-in partner when the player isn't aimed at a real court member (solo testing).")]
        public GameObject dummyPartnerPrefab;
        [Tooltip("How far in front of the initiator the stand-in partner is placed.")]
        public float dummyPartnerDistance = 1.4f;

        /// <summary>Partner resolution, shared with <see cref="PartnerMinigame"/>.</summary>
        protected PartnerLink Link { get; } = new PartnerLink();

        protected override void OnHandBegin()
        {
            Link.Resolve(Context, player, dummyPartnerPrefab, dummyPartnerDistance);

            if (Link.Partner == null)
            {
                Log.Warn($"[{GetType().Name}] no partner and no dummy prefab - cancelling.");
                CancelMinigame();
                return;
            }

            Link.FaceEachOther();
            OnPartnerHandBegin();
        }

        protected override void OnMinigameEnd(bool won)
        {
            base.OnMinigameEnd(won); // HandMinigame.RestorePlayer()
            Link.Dismiss();
        }

        /// <summary>Runs once, after the player is frozen, the hand rig is live, and the partner is resolved.</summary>
        protected virtual void OnPartnerHandBegin() { }
    }
}
