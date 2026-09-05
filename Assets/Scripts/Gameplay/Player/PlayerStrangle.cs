using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    // Owns the Corrupted strangle mechanic: the reach-and-hunt / locked-struggle state machine, the
    // slow orbit that keeps the strangler chest-to-chest with the victim, this player's neck anchor
    // (so a strangler can aim at it), and the two-handed reach IK. Extracted from PlayerVitals - see
    // PlayerVitals.cs for player.Vitals.isStrangling / .GetNeckWorldPosition(), the forwarders external
    // code still uses, and PlayerController for the OnStrangle input routing and PlayerIKHelper for the
    // OnAnimatorIK hook.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerStrangle : MonoBehaviour
    {
        [Header("Corrupted Combat")]
        public float strangleCooldown = 4f;
        private float lastStrangleTime = -4f;
        public float strangleHoldTime = 1.5f; // How long they must hold the button
        private Coroutine strangleCoroutine;  // Tracks the active struggle

        [Header("Corrupted Strangle Lock")]
        [Tooltip("Centre-to-centre distance the strangler settles to once locked. Bodies overlap so the arms reach the neck - every collider on the victim is ignored during the strangle.")]
        public float strangleGrabDistance = 0.55f;
        [Tooltip("How close (centre-to-centre) you must physically get for the strangle to START. Must be reachable while the capsules still collide, so keep it ~1.1+.")]
        public float strangleConnectDistance = 1.15f;
        [Tooltip("Degrees per second the strangler can shuffle side-to-side around the victim's neck. Keep low for a slow drag.")]
        public float strangleOrbitSpeed = 55f;
        [Tooltip("Units/sec the strangler repositions to keep the grab (also the speed it pulls into the grab). Low = a sprinting victim can pull free.")]
        public float strangleFollowSpeed = 2.5f;
        [Tooltip("Extra distance beyond the grab distance the victim must open up before the grip breaks.")]
        public float strangleBreakSlack = 1.4f;
        [Tooltip("Fallback only: height above this player's transform for the neck point when no neck/head bone is found.")]
        public float strangleNeckHeight = 0.85f;
        [Tooltip("Sideways gap between the two reaching hands.")]
        public float strangleHandSpread = 0.08f;
        [Tooltip("Pulls the hand target toward the strangler so the hands close on the FRONT of the throat.")]
        public float strangleHandInset = 0.12f;
        [Tooltip("Raises (+) or lowers (-) the hand grip point relative to the neck/head bone.")]
        public float strangleHandHeightOffset = -0.12f;
        [Tooltip("How far in front of the camera the hands reach while HUNTING for a target (no victim yet).")]
        public float strangleReachDistance = 0.9f;
        [Tooltip("Euler tweak for the reaching hands' rotation - adjust until the palms face the throat on your rig.")]
        public Vector3 strangleHandRotationOffset = Vector3.zero;
        [Tooltip("Roll (deg) applied opposite on each hand so the palms turn vertical and face INWARD, cupping the neck. 0 = flat; flip the sign to face them outward.")]
        public float strangleHandRoll = 90f;

        public bool isStrangling { get; private set; }          // locked onto a victim (movement suspended)
        public bool isReachingToStrangle { get; private set; }  // button held, arms out, hunting for a target
        private PlayerController strangleVictim;
        private bool strangleButtonHeld = false;
        private float strangleHandIKWeight = 0f;
        private Transform cachedNeckAnchor; // this player's neck/head bone, resolved once for strangle aim + IK
        private Collider[] strangleIgnoredColliders; // victim colliders temporarily ignored so we can close in

        // The sibling components on this same GameObject.
        private PlayerController player;
        private PlayerVitals vitals;
        private PlayerPowerUps powerUps;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            vitals = GetComponent<PlayerVitals>();
            powerUps = GetComponent<PlayerPowerUps>();
        }

        /// <summary>Cancels a latched strangle-button hold, e.g. when a blocking menu opens.</summary>
        public void CancelStrangleButton() => strangleButtonHeld = false;

        // --- DEDICATED RIGHT CLICK, non-minigame branch (Corrupted only): reach out to strangle.
        // Routed from PlayerController.OnStrangle once it's ruled out the minigame-camera-look
        // interception. The Royal arrest branch on the same button lives in PlayerVitals.HandleArrestInput. ---
        public void HandleStrangleInput(bool isPressed, PickupItem heldItem, PickupItem leftHeldItem)
        {
            if (vitals.isGhost || vitals.currentRole != PlayerRole.Corrupted) return;

            if (isPressed)
            {
                if (heldItem != null || leftHeldItem != null || powerUps.HasActivePowerUp)
                {
                    Log.Game("You cannot strangle someone while holding an item or power-up!");
                    return;
                }
                if (Time.time < lastStrangleTime + strangleCooldown)
                {
                    Log.Game("Strangulation is on cooldown!");
                    return;
                }
                strangleButtonHeld = true;
                if (strangleCoroutine == null) strangleCoroutine = StartCoroutine(StrangleRoutine());
            }
            else
            {
                // The routine watches this flag, then unwinds the lock and restores movement itself.
                strangleButtonHeld = false;
            }
        }

        private System.Collections.IEnumerator StrangleRoutine()
        {
            Log.Game($"{gameObject.name} reaches out to strangle...");

            // --- PHASE 1: REACH & HUNT ---
            // Arms extend forward (IK) while the player moves and aims normally. Each frame we look for a
            // valid victim in front, but the strangle only STARTS once we've closed to arm's length -
            // i.e. the reaching hands are actually at the victim's neck.
            isReachingToStrangle = true;
            PlayerController targetVictim = null;

            while (strangleButtonHeld && targetVictim == null)
            {
                if (vitals.isGhost || vitals.IsStunned || vitals.isArrested || vitals.IsBeingPushed) break;
                if (player.GetHeldItem() != null || player.GetLeftHeldItem() != null || powerUps.HasActivePowerUp) break;

                // Grabs only when we've actually closed to arm's length in front of a valid victim.
                targetVictim = FindStrangleVictim();
                if (targetVictim != null) break;

                yield return null; // keep reaching; free movement/aim
            }

            if (targetVictim == null)
            {
                // Button released (or interrupted) without grabbing anyone - just drop the arms.
                isReachingToStrangle = false;
                strangleCoroutine = null;
                yield break;
            }

            Log.Game($"Grabbed {targetVictim.gameObject.name}! Hold the button for {strangleHoldTime}s...");

            // --- PHASE 2: LOCKED STRUGGLE ---
            BeginStrangleLock(targetVictim);

            float timer = 0f;
            while (strangleButtonHeld && timer < strangleHoldTime)
            {
                if (vitals.isGhost || vitals.IsStunned || vitals.isArrested || vitals.IsBeingPushed)
                    break;

                if (strangleVictim == null || strangleVictim.Vitals.isGhost)
                {
                    Log.Game("Target is already dead!");
                    break;
                }

                // Slow orbit + follow, and re-face the victim
                UpdateStrangleLock();

                // Cancel if the victim opens up more distance than the grip allows (e.g. sprints off)
                if (Vector3.Distance(transform.position, strangleVictim.transform.position) > strangleGrabDistance + strangleBreakSlack)
                {
                    Log.Game($"{strangleVictim.gameObject.name} broke free from your grasp!");
                    break;
                }

                timer += Time.deltaTime;
                yield return null; // Wait for the next frame
            }

            // --- EXECUTION ---
            if (timer >= strangleHoldTime && strangleVictim != null && !strangleVictim.Vitals.isGhost)
            {
                // Court members can grab and hold someone, but their strangle never kills.
                if (vitals.currentRole == PlayerRole.Court)
                {
                    Log.Game($"{gameObject.name} strangled {strangleVictim.gameObject.name} - but Court members deal no damage.");
                }
                else
                {
                    Log.Game($"Successfully strangled {strangleVictim.gameObject.name}!");
                    strangleVictim.Vitals.TakeDamage(1);
                }
                lastStrangleTime = Time.time; // Apply the full cooldown
            }
            else if (!strangleButtonHeld)
            {
                Log.Game("Strangulation cancelled! You let go too early.");
            }

            // Release the lock and hand control back to normal movement
            EndStrangleLock();
            isReachingToStrangle = false;
            strangleCoroutine = null;
        }

        // The strangle only connects with the player the crosshair is actually on (Interactor.TargetPlayer
        // - the same thing that turns the crosshair green) and only once close enough.
        private PlayerController FindStrangleVictim()
        {
            PlayerController v = player.Interactor.TargetPlayer;
            if (v == null || v == player || v.Vitals.isGhost || v.Vitals.currentRole == PlayerRole.Corrupted) return null;

            Vector3 flat = v.transform.position - transform.position;
            flat.y = 0f;
            if (flat.magnitude > strangleConnectDistance) return null; // still too far to grab

            return v;
        }

        // World point the strangler's hands reach for: the victim's actual neck/head bone when available.
        private Vector3 StrangleNeckPoint()
        {
            if (strangleVictim != null) return strangleVictim.Vitals.GetNeckWorldPosition();
            // Post-strangle IK fade-out with no victim: keep the reach close to the body, not the sky.
            return transform.position + transform.forward * 0.35f + Vector3.up * strangleNeckHeight;
        }

        // Resolves (and caches) this player's neck/head bone so a strangler can lock its hands there.
        // Works for humanoid rigs and for the generic Synty rig via the bones FirstPersonHeadHider already points at.
        public Vector3 GetNeckWorldPosition()
        {
            if (cachedNeckAnchor == null)
            {
                Animator animator = player.PlayerAnimator;
                if (animator != null && animator.isHuman)
                {
                    // Head bone sits at the jaw/throat line - a better strangle target than the low Neck bone.
                    cachedNeckAnchor = animator.GetBoneTransform(HumanBodyBones.Head)
                                    ?? animator.GetBoneTransform(HumanBodyBones.Neck);
                }

                if (cachedNeckAnchor == null)
                {
                    FirstPersonHeadHider hider = GetComponentInChildren<FirstPersonHeadHider>(true);
                    if (hider != null) cachedNeckAnchor = hider.headBone != null ? hider.headBone : hider.neckBone;
                }
            }

            if (cachedNeckAnchor != null) return cachedNeckAnchor.position;
            return transform.position + Vector3.up * strangleNeckHeight; // last-resort estimate
        }

        // Enter the locked struggle. No teleport - the two capsules are set to ignore each other so
        // UpdateStrangleLock can pull the strangler in from connect range to the (much closer) grab
        // distance over a fraction of a second. Movement/look are suspended while isStrangling.
        private void BeginStrangleLock(PlayerController victim)
        {
            strangleVictim = victim;
            isStrangling = true;
            isReachingToStrangle = false;

            // Ignore EVERY collider on the victim (capsule + any body/mesh colliders) so nothing stops
            // the strangler closing chest-to-chest.
            if (player.CharController != null)
            {
                strangleIgnoredColliders = victim.GetComponentsInChildren<Collider>();
                foreach (Collider col in strangleIgnoredColliders)
                    if (col != null && col.enabled) Physics.IgnoreCollision(player.CharController, col, true);
            }

            // Turn to face the victim immediately; the pull-in and orbit are handled every frame.
            Vector3 neck = StrangleNeckPoint();
            Vector3 faceDir = new Vector3(neck.x - transform.position.x, 0f, neck.z - transform.position.z);
            if (faceDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(faceDir);

            // Camera pitch is left exactly where the player was aiming when they grabbed - no forced snap.
        }

        // Runs every frame of the struggle: slow side-to-side shuffle around the neck + face the victim.
        private void UpdateStrangleLock()
        {
            if (strangleVictim == null || player.CharController == null) return;

            Vector3 neck = StrangleNeckPoint();
            Vector3 center = new Vector3(neck.x, transform.position.y, neck.z);

            // Current angle of the strangler around the neck
            Vector3 offset = transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.0001f) offset = -transform.forward;
            float angle = Mathf.Atan2(offset.z, offset.x);

            // A/D shuffles slowly around the circle
            angle -= player.MoveInput.x * strangleOrbitSpeed * Mathf.Deg2Rad * Time.deltaTime;

            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 targetPos = center + dir * strangleGrabDistance;

            // Reposition slowly, so a sprinting victim outruns the grip
            Vector3 newPos = Vector3.MoveTowards(transform.position, targetPos, strangleFollowSpeed * Time.deltaTime);
            Vector3 delta = newPos - transform.position;
            delta.y = -2f * Time.deltaTime; // small downward bias to stay grounded

            if (player.CharController.enabled) player.CharController.Move(delta);

            // Keep facing the victim's neck
            Vector3 faceDir = center - transform.position;
            faceDir.y = 0f;
            if (faceDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(faceDir);
        }

        private void EndStrangleLock()
        {
            if (player.CharController != null && strangleIgnoredColliders != null)
            {
                foreach (Collider col in strangleIgnoredColliders)
                    if (col != null) Physics.IgnoreCollision(player.CharController, col, false);
            }
            strangleIgnoredColliders = null;

            isStrangling = false;
            strangleVictim = null;
        }

        // Called from PlayerIKHelper.OnAnimatorIK - drives both hands out for a strangle:
        // to the victim's throat when locked, or straight ahead where the player is aiming while hunting.
        public void ApplyStrangleIK(int layerIndex)
        {
            Animator animator = player.PlayerAnimator;
            if (animator == null) return;

            bool armsOut = isStrangling || isReachingToStrangle;
            float target = armsOut ? 1f : 0f;
            strangleHandIKWeight = Mathf.Lerp(strangleHandIKWeight, target, Time.deltaTime * player.ikBlendSpeed);

            if (strangleHandIKWeight <= 0.01f)
            {
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
                return;
            }

            Vector3 grip;
            if (strangleVictim != null)
            {
                grip = strangleVictim.Vitals.GetNeckWorldPosition();
                // Pull toward the strangler so the hands close on the FRONT of the throat, not the bone pivot.
                Vector3 toMe = transform.position - grip;
                toMe.y = 0f;
                if (toMe.sqrMagnitude > 0.0001f) grip += toMe.normalized * strangleHandInset;
            }
            else
            {
                // Hunting for a target: reach straight out along the aim.
                grip = player.PlayerCamera.position + player.PlayerCamera.forward * strangleReachDistance;
            }

            // Drop (or raise) the grip so the hands sit on the throat rather than up at the jaw.
            grip += Vector3.up * strangleHandHeightOffset;

            // Stable reach direction (flattened) so the hands hold one orientation instead of following the walk cycle.
            Vector3 reachDir = grip - player.PlayerCamera.position;
            reachDir.y = 0f;
            if (reachDir.sqrMagnitude < 0.0001f) reachDir = transform.forward;
            reachDir.Normalize();

            // Base "reach forward" orientation, then roll each hand the opposite way so the palms stand
            // vertical (perpendicular to the ground), cupping in toward the neck rather than lying flat.
            Quaternion baseRot = Quaternion.LookRotation(reachDir, Vector3.up) * Quaternion.Euler(strangleHandRotationOffset);
            // Roll each hand so the PALMS turn inward toward each other (cupping the neck).
            Quaternion rightRot = baseRot * Quaternion.Euler(0f, 0f, -strangleHandRoll);
            Quaternion leftRot = baseRot * Quaternion.Euler(0f, 0f, strangleHandRoll);

            Vector3 apart = transform.right * strangleHandSpread;
            Vector3 elbowBase = grip - reachDir * 0.35f + Vector3.down * 0.15f;

            ApplyOneStrangleHand(animator, AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, grip + apart, rightRot, elbowBase + transform.right * 0.3f);
            ApplyOneStrangleHand(animator, AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, grip - apart, leftRot, elbowBase - transform.right * 0.3f);
        }

        private void ApplyOneStrangleHand(Animator animator, AvatarIKGoal goal, AvatarIKHint elbow, Vector3 handPos, Quaternion handRot, Vector3 elbowPos)
        {
            animator.SetIKPositionWeight(goal, strangleHandIKWeight);
            animator.SetIKPosition(goal, handPos);
            animator.SetIKRotationWeight(goal, strangleHandIKWeight);
            animator.SetIKRotation(goal, handRot);
            animator.SetIKHintPositionWeight(elbow, strangleHandIKWeight * 0.5f);
            animator.SetIKHintPosition(elbow, elbowPos);
        }
    }
}
