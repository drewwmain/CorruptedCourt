using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// The behavioural stand-in a <see cref="PartnerMinigame"/> talks to when there's no real second
    /// player (solo testing). Plays its half of the shared animation and auto-"accepts" after a delay so
    /// handshake / cheers / dance / duel all complete single-player.
    ///
    /// Put this on a simple humanoid prefab (a spare Synty character). Pair it with the existing
    /// <c>DummyTestHelper</c> if the stand-in needs to be holding something (wine glass, sword).
    /// </summary>
    public class DummyPartner : MonoBehaviour
    {
        private Animator anim;
        private float acceptAt = -1f;

        private Vector3? reachTarget;
        private float reachWeight;

        /// <summary>True once the stand-in has "agreed" to the interaction.</summary>
        public bool HasAccepted { get; private set; }

        private void Awake()
        {
            anim = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (!HasAccepted && acceptAt >= 0f && Time.time >= acceptAt)
                HasAccepted = true;
        }

        // Requires the Animator Controller's base layer to have "IK Pass" enabled - Unity does not
        // call OnAnimatorIK otherwise, silently.
        private void OnAnimatorIK(int layerIndex)
        {
            if (anim == null) return;

            float targetWeight = reachTarget.HasValue ? 1f : 0f;
            reachWeight = Mathf.MoveTowards(reachWeight, targetWeight, Time.deltaTime * 4f);

            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, reachWeight);
            if (reachTarget.HasValue) anim.SetIKPosition(AvatarIKGoal.RightHand, reachTarget.Value);
        }

        /// <summary>Start the countdown to auto-accept.</summary>
        public void BeginAutoAccept(float delaySeconds)
        {
            acceptAt = Time.time + Mathf.Max(0f, delaySeconds);
        }

        /// <summary>Play an animation trigger on the stand-in (mirrors the initiator's clip).</summary>
        public void Play(string trigger)
        {
            if (anim != null && !string.IsNullOrEmpty(trigger)) anim.SetTrigger(trigger);
        }

        /// <summary>Hold a looping pose (dance / conversation idle).</summary>
        public void HoldPose(string boolParam, bool on)
        {
            if (anim != null && !string.IsNullOrEmpty(boolParam)) anim.SetBool(boolParam, on);
        }

        /// <summary>
        /// Points the stand-in's right hand toward a world point (mutual-reach minigames - handshake,
        /// cheers; B11 drives this each frame toward the shared anchor). Pass null to release the reach
        /// and let the hand relax back to its normal animated pose. Requires a Humanoid Animator with
        /// "IK Pass" enabled on its base layer (see the class summary).
        /// </summary>
        public void ReachToward(Vector3? worldPoint)
        {
            reachTarget = worldPoint;
        }

        /// <summary>
        /// Plays an emote's animator trigger on the stand-in. A stub for now - B14 gives emote
        /// performance its real meaning (timing, looping, category matching); this just fires the clip.
        /// </summary>
        public void PerformEmote(EmoteDefinition emote)
        {
            if (emote != null) Play(emote.animTrigger);
        }

        /// <summary>Remove the stand-in when the minigame ends.</summary>
        public void Dismiss()
        {
            Destroy(gameObject);
        }
    }
}
