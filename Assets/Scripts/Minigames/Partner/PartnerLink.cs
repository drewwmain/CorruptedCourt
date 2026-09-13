using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// The partner-resolution half of <see cref="PartnerMinigame"/>, extracted so
    /// <see cref="PartnerHandMinigame"/> can compose it too without inheriting from
    /// PartnerMinigame - MinigameBase's single-inheritance hierarchy can't give one subclass both
    /// HandMinigame's plumbing and PartnerMinigame's (Minigames/ARCHITECTURE.md §10.2). Moved from
    /// PartnerMinigame almost verbatim; behavior is unchanged.
    /// </summary>
    public class PartnerLink
    {
        private PlayerController initiator;
        private DummyPartner dummy;

        /// <summary>The other participant (real player or stand-in).</summary>
        public PlayerController Partner { get; private set; }

        /// <summary>True when <see cref="Partner"/> is an AI stand-in rather than a real player.</summary>
        public bool PartnerIsDummy { get; private set; }

        /// <summary>True once the dummy has auto-accepted (or immediately for a cooperating real player).</summary>
        public bool PartnerAccepted => PartnerIsDummy ? (dummy != null && dummy.HasAccepted) : true;

        /// <summary>
        /// Resolves the partner: the real player <paramref name="initiator"/> aimed at
        /// (<see cref="MinigameContext.PartnerPlayer"/>), or a spawned <see cref="DummyPartner"/>
        /// stand-in for solo testing. <see cref="Partner"/> is null when neither is available.
        /// </summary>
        public void Resolve(MinigameContext ctx, PlayerController initiator, GameObject dummyPrefab, float distance)
        {
            this.initiator = initiator;
            Partner = PartnerResolver.Resolve(ctx, initiator, dummyPrefab, distance, out dummy);
            PartnerIsDummy = dummy != null;
        }

        /// <summary>Starts the dummy's auto-accept countdown. No-op for a real partner.</summary>
        public void BeginDummyAutoAccept(float delaySeconds)
        {
            if (dummy != null) dummy.BeginAutoAccept(delaySeconds);
        }

        /// <summary>Turn the initiator's body to face the partner (yaw only).</summary>
        public void FaceEachOther()
        {
            if (initiator == null || Partner == null) return;

            Vector3 dir = Partner.transform.position - initiator.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                initiator.transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>Play the same animation trigger on both sides (handshake, cheers clink, ...).</summary>
        public void Mirror(string animTrigger)
        {
            if (PartnerIsDummy && dummy != null) dummy.Play(animTrigger);
            // TODO(P5): real remote players get the trigger over the network / via their PlayerController.
        }

        /// <summary>Point the partner's hand at a world position (mutual-reach minigames - B11). No-op
        /// for a real partner; nothing to drive for a remote hand yet (see <see cref="Mirror"/>).</summary>
        public void ReachToward(Vector3? worldPoint)
        {
            if (PartnerIsDummy && dummy != null) dummy.ReachToward(worldPoint);
        }

        /// <summary>Removes the dummy stand-in, if any. Call when the session ends.</summary>
        public void Dismiss()
        {
            if (dummy != null) dummy.Dismiss();
        }
    }
}
