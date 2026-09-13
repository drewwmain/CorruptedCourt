using UnityEngine;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Base for "both players guide a hand to a shared point": handshake (empty hands), cheers
    /// (glass rims meet). A session-owned anchor spawns at the midpoint between the initiator and
    /// their partner; both guide a hand to it, the initiator confirms with a left click, and a
    /// partner who never reaches simply times out - a silent, free refusal, nothing changed.
    ///
    /// Duel is explicitly NOT built on this base (mouse-swing arm control, three-hit counter, no
    /// shared anchor) - it composes <see cref="PartnerHandMinigame"/> directly instead.
    /// </summary>
    public abstract class MutualReachMinigame : PartnerHandMinigame
    {
        [Header("Mutual reach")]
        [Tooltip("Height above the midpoint between the two players the anchor sits at.")]
        public float anchorHeight = 1.1f;
        [Tooltip("How close the guided hand must get to the anchor to count as 'reached'.")]
        public float reachTolerance = 0.15f;
        [Tooltip("Seconds to wait for both sides to reach and confirm before the session cancels.")]
        public float consentTimeout = 6f;

        private GameObject anchorObject;
        private float timeoutAt;
        private bool initiatorConfirmed;
        private bool partnerReaching;

        /// <summary>The shared world point both hands guide to this session. Null before begin / after end.</summary>
        protected Transform Anchor => anchorObject != null ? anchorObject.transform : null;

        protected override void OnPartnerHandBegin()
        {
            Vector3 mid = Vector3.Lerp(player.transform.position, Link.Partner.transform.position, 0.5f);
            mid.y += anchorHeight;

            anchorObject = new GameObject("MutualReachAnchor");
            anchorObject.transform.position = mid;

            timeoutAt = Time.time + consentTimeout;
            initiatorConfirmed = false;
            partnerReaching = false;

            OnMutualReachBegin();
        }

        protected override void OnMinigameUpdate()
        {
            if (Anchor == null) return;

            Vector3 reachPoint = MouseWorld();
            Hand.ReachToward(reachPoint);

            if (!initiatorConfirmed && MinigameInput.PrimaryDown
                && Vector3.Distance(reachPoint, Anchor.position) <= reachTolerance)
            {
                initiatorConfirmed = true;
            }

            if (!partnerReaching && Link.PartnerAccepted)
            {
                Link.ReachToward(Anchor.position);
                partnerReaching = true;
            }

            if (initiatorConfirmed && Link.PartnerAccepted)
            {
                OnReachConfirmed();
                CompleteMinigame();
                return;
            }

            if (Time.time >= timeoutAt)
            {
                // Refusal is silent and free - no log, no partial credit, nothing changed.
                CancelMinigame();
            }
        }

        protected override void OnMinigameEnd(bool won)
        {
            base.OnMinigameEnd(won); // PartnerHandMinigame: HandMinigame.RestorePlayer() + Link.Dismiss()
            if (anchorObject != null) Destroy(anchorObject);
            anchorObject = null;
        }

        // --- hooks for concrete minigames -----------------------------------------------------------

        /// <summary>Runs once, after the anchor exists and the timeout is armed.</summary>
        protected virtual void OnMutualReachBegin() { }

        /// <summary>Runs once both sides have reached and confirmed, just before <see cref="MinigameBase.CompleteMinigame"/>.</summary>
        protected virtual void OnReachConfirmed() { }
    }
}
