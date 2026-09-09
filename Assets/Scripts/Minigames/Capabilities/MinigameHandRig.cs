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

        // Persistent additive world offset re-applied to every reach-target write (see SetReachCompensation).
        private Vector3 reachCompensation;

        // Last reach target actually written (WITHOUT reachCompensation) + whether one exists yet. The
        // block clamp measures this frame's motion against it.
        private Vector3 lastReachTarget;
        private bool hasLastReachTarget;

        // Optional plane the reach target may not advance INTO - set by HandMinigame while the grip
        // constraint reports itself blocked. Motion across the plane and back out of it is untouched.
        private bool reachClampActive;
        private Vector3 reachClampNormal;

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
            reachCompensation = Vector3.zero;
            lastReachTarget = Vector3.zero;
            hasLastReachTarget = false;
            reachClampActive = false;
            reachClampNormal = Vector3.zero;
            if (player == null) return;
            // No scripted finger-curl until a caller sets one - the left-mouse-button auto-curl is the
            // default. A HandMinigame with autoCurlFromPrimary = false re-pins it open right after this.
            player.ClearMinigameHandGrip();
            player.hangReachActive = true;
            player.hangReachRotWeight = 0f;
        }

        /// <summary>Stop driving the hand; the arm blends back to its animated pose.</summary>
        public void End()
        {
            reachCompensation = Vector3.zero;
            lastReachTarget = Vector3.zero;
            hasLastReachTarget = false;
            reachClampActive = false;
            reachClampNormal = Vector3.zero;
            if (player == null) return;
            player.ClearMinigameHandGrip();
            player.hangReachActive = false;
            player.hangReachRotWeight = 0f;
        }

        /// <summary>Point the hand at a world position (plus the current <see cref="SetReachCompensation"/> offset).</summary>
        public void ReachToward(Vector3 worldPos)
        {
            if (player == null) return;
            Vector3 target = ApplyReachClamp(worldPos);
            lastReachTarget = target;
            hasLastReachTarget = true;
            player.hangReachPos = target + reachCompensation;
        }

        /// <summary>Point the hand at the mouse, projected <paramref name="distance"/> m in front of the camera (plus the current <see cref="SetReachCompensation"/> offset).</summary>
        public void AimFromMouse(float distance)
        {
            if (player == null || cam == null) return;
            Vector3 mp = MinigameInput.MouseScreenPosition;
            mp.z = distance;
            Vector3 target = ApplyReachClamp(cam.ScreenToWorldPoint(mp));
            lastReachTarget = target;
            hasLastReachTarget = true;
            player.hangReachPos = target + reachCompensation;
        }

        /// <summary>
        /// Persistent additive world-space offset layered on top of every <see cref="ReachToward"/> /
        /// <see cref="AimFromMouse"/> target until changed or cleared - it does NOT replace the per-frame
        /// reach target the minigame sets. Used by <see cref="MinigameGripConstraint"/> to cancel the
        /// grip-point drift its cosmetic wrist rotation would otherwise cause (the wrist joint is not at
        /// the grip point). Reset to zero by <see cref="Begin"/> / <see cref="End"/>. Pass Vector3.zero to
        /// stop compensating.
        /// </summary>
        public void SetReachCompensation(Vector3 worldOffset)
        {
            reachCompensation = worldOffset;
        }

        /// <summary>
        /// Constrain subsequent <see cref="ReachToward"/> / <see cref="AimFromMouse"/> targets so they
        /// may not advance any further along <c>-worldNormal</c> (into a surface the held item is jammed
        /// against) than they already have. Motion across the surface and back out of it passes through
        /// untouched, and the target is never pulled back. Driven by <c>HandMinigame</c> from
        /// <see cref="MinigameGripConstraint.IsBlocked"/>; a near-zero normal releases it, as does
        /// <see cref="ClearReachClamp"/>. Reset by <see cref="Begin"/> / <see cref="End"/>.
        /// </summary>
        public void SetReachClamp(Vector3 worldNormal)
        {
            if (worldNormal.sqrMagnitude < 1e-8f) { reachClampActive = false; return; }
            reachClampNormal = worldNormal.normalized;
            reachClampActive = true;
        }

        /// <summary>Release the constraint set by <see cref="SetReachClamp"/>.</summary>
        public void ClearReachClamp()
        {
            reachClampActive = false;
        }

        // Remove the part of this frame's motion (target - lastReachTarget) that heads into the clamp
        // plane; keep tangential + outward motion. No-op until a target has been recorded and a clamp is
        // active. A hard limit, never eased - the hand stops the same frame the clamp engages.
        private Vector3 ApplyReachClamp(Vector3 target)
        {
            if (!reachClampActive || !hasLastReachTarget) return target;
            Vector3 delta = target - lastReachTarget;
            float into = Vector3.Dot(delta, reachClampNormal);
            if (into < 0f) delta -= into * reachClampNormal;
            return lastReachTarget + delta;
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
        /// Finger-curl amount, 0 = open .. 1 = fist. Overrides PlayerIKRig's default "curl while the left
        /// mouse button is held during a minigame" behaviour until <see cref="End"/> (or the owning
        /// HandMinigame handing control back). Use in minigames where the left button means something
        /// else - bow draw, drag-to-grab, pour, duel swing - and in HandMinigame subclasses that set
        /// autoCurlFromPrimary = false. No effect while the rig's Minigame Hand Grip toggle is off.
        /// </summary>
        public void SetGrip(float amount01)
        {
            if (player != null) player.SetMinigameHandGrip(amount01);
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
