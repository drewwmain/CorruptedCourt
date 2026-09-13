using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Base for "do something WITH another court member": handshake, cheers, dance, conversation, duel.
    ///
    /// On begin it resolves a <see cref="Partner"/> - the real player the initiator aimed at, or (in a
    /// solo test) a <see cref="DummyPartner"/> spawned in front of them that plays its half of the
    /// animation and auto-accepts. Subclasses implement the actual success test in
    /// <see cref="OnMinigameUpdate"/> and call <see cref="CompleteMinigame"/>.
    ///
    /// The partner-resolution work itself lives in <see cref="PartnerLink"/> so
    /// <see cref="PartnerHandMinigame"/> can reuse it without also inheriting from this class -
    /// MinigameBase's single-inheritance hierarchy can't give one subclass both HandMinigame's
    /// plumbing and this class's body (Minigames/ARCHITECTURE.md §10.2).
    /// </summary>
    public abstract class PartnerMinigame : MinigameBase
    {
        [Header("Partner")]
        [Tooltip("Spawned as the stand-in partner when the player isn't aimed at a real court member (solo testing).")]
        public GameObject dummyPartnerPrefab;
        [Tooltip("How far in front of the initiator the stand-in partner is placed.")]
        public float dummyPartnerDistance = 1.4f;
        [Tooltip("Seconds before the stand-in partner auto-accepts, so solo tests always complete.")]
        public float dummyAutoAcceptDelay = 1.25f;

        private readonly PartnerLink link = new PartnerLink();

        /// <summary>The other participant (real player or stand-in).</summary>
        protected PlayerController Partner => link.Partner;

        /// <summary>True when <see cref="Partner"/> is an AI stand-in rather than a real player.</summary>
        protected bool PartnerIsDummy => link.PartnerIsDummy;

        /// <summary>True once the dummy has auto-accepted (or immediately for a cooperating real player).</summary>
        protected bool PartnerAccepted => link.PartnerAccepted;

        protected override void OnMinigameBegin()
        {
            link.Resolve(Context, player, dummyPartnerPrefab, dummyPartnerDistance);

            if (link.Partner == null)
            {
                Log.Warn($"[{GetType().Name}] no partner and no dummy prefab - cancelling.");
                CancelMinigame();
                return;
            }

            link.FaceEachOther();
            if (link.PartnerIsDummy) link.BeginDummyAutoAccept(dummyAutoAcceptDelay);
            OnPartnerBegin();
        }

        protected override void OnMinigameEnd(bool won)
        {
            link.Dismiss();
        }

        private void Update()
        {
            if (player == null || link.Partner == null) return;
            OnMinigameUpdate();
        }

        /// <summary>Turn the initiator's body to face the partner (yaw only).</summary>
        protected void FacePartner() => link.FaceEachOther();

        /// <summary>Play the same animation trigger on both sides (handshake, cheers clink, ...).</summary>
        protected void MirrorOnPartner(string animTrigger) => link.Mirror(animTrigger);

        // --- hooks --------------------------------------------------------------------------------
        protected virtual void OnPartnerBegin() { }
        protected virtual void OnMinigameUpdate() { }
    }
}
