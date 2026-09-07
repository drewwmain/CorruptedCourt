using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Thin wrapper over the player's right-hand reach IK (<c>hangReach*</c> on PlayerController) plus
    /// item attach/detach and the finger-curl grip. Every minigame that drives the hand talks to this
    /// instead of poking PlayerController fields directly, so the "how the hand is controlled" details
    /// live in one place.
    ///
    /// Not a MonoBehaviour - a HandMinigame owns one as a field and calls <see cref="Begin"/> /
    /// <see cref="End"/> around its lifetime.
    /// </summary>
    public class MinigameHandRig
    {
        private readonly PlayerController player;
        private readonly Camera cam;

        public MinigameHandRig(PlayerController player, Camera cam)
        {
            this.player = player;
            this.cam = cam;
        }

        /// <summary>The rig bone items are parented to (Hand_R), or the socket as a fallback.</summary>
        public Transform HandBone =>
            player == null ? null : (player.RightHandBone != null ? player.RightHandBone : player.RightHandSocket);

        /// <summary>Start driving the right hand toward a reach target.</summary>
        public void Begin()
        {
            if (player == null) return;
            player.hangReachActive = true;
            player.hangReachRotWeight = 0f;
        }

        /// <summary>Stop driving the hand; the arm blends back to its animated pose.</summary>
        public void End()
        {
            if (player == null) return;
            player.hangReachActive = false;
            player.hangReachRotWeight = 0f;
        }

        /// <summary>Point the hand at a world position.</summary>
        public void ReachToward(Vector3 worldPos)
        {
            if (player == null) return;
            player.hangReachPos = worldPos;
        }

        /// <summary>Point the hand at the mouse, projected <paramref name="distance"/> m in front of the camera.</summary>
        public void AimFromMouse(float distance)
        {
            if (player == null || cam == null) return;
            Vector3 mp = MinigameInput.MouseScreenPosition;
            mp.z = distance;
            player.hangReachPos = cam.ScreenToWorldPoint(mp);
        }

        /// <summary>Optionally align the hand's rotation to <paramref name="worldRot"/> (0 = keep held pose).</summary>
        public void SetHandRotation(Quaternion worldRot, float weight)
        {
            if (player == null) return;
            player.hangReachRot = worldRot;
            player.hangReachRotWeight = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// Cosmetic-only follow-through: set the hand's IK rotation goal to the CURRENT hand pose rotated
        /// <paramref name="degrees"/> about the WORLD <paramref name="worldAxis"/>, blended in at
        /// <paramref name="weight01"/>. Writes the SAME hangReach rotation goal as
        /// <see cref="SetHandRotation"/>, so it is consumed by the NEXT OnAnimatorIK - one frame later.
        /// For visual give that is NOT part of any guarantee (see <see cref="MinigameGripConstraint"/>'s
        /// wrist mirror). Pass <paramref name="degrees"/> &lt;= 0 to hand the goal back to the animated pose.
        /// NOTE: the goal is re-derived from the (already IK-posed) hand each frame, so a caller that
        /// drives this every frame at a high weight settles a few degrees PAST <paramref name="degrees"/>
        /// (it converges - ratio = the IK weight - it does not run away). Use a sub-1 weight, or drive it
        /// only transiently, if that overshoot matters.
        /// </summary>
        public void MirrorHandRotation(float degrees, Vector3 worldAxis, float weight01)
        {
            if (player == null) return;
            Transform bone = HandBone;
            if (bone == null || degrees <= 0f || weight01 <= 0f || worldAxis.sqrMagnitude < 1e-8f)
            {
                player.hangReachRotWeight = 0f;   // nothing to mirror - back to the animated pose
                return;
            }
            player.hangReachRot = Quaternion.AngleAxis(degrees, worldAxis.normalized) * bone.rotation;
            player.hangReachRotWeight = Mathf.Clamp01(weight01);
        }

        /// <summary>
        /// Finger-curl amount, 0 = open .. 1 = fist. Currently PlayerController auto-drives the grip from
        /// the left mouse button while any minigame is open (see ApplyHandGripPose); this is the hook for
        /// minigames that want to script it explicitly once PlayerController exposes a setter.
        /// </summary>
        public void SetGrip(float amount01)
        {
            // TODO(P2): route to PlayerController.SetHandGrip(amount01) when that setter is added.
        }

        /// <summary>Glue an item to the hand bone at a local offset (kinematic, collider off).</summary>
        public void AttachItem(PickupItem item, Vector3 localPos, Vector3 localEuler)
        {
            Transform hand = HandBone;
            if (item == null || hand == null) return;

            item.transform.SetParent(hand, false);
            item.transform.localPosition = localPos;
            item.transform.localRotation = Quaternion.Euler(localEuler);

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Collider col = item.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        /// <summary>Let an item go where it currently sits (physics back on, becomes pick-up-able).</summary>
        public void DetachItem(PickupItem item)
        {
            if (item == null) return;
            item.DropInPlace();
        }
    }
}
