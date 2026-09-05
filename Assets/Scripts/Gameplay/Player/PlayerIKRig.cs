using UnityEngine;
using System.Collections.Generic;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    // Owns the player's IK rig: the minigame hand-follows-mouse IK, the two-handed haul pose, and the
    // finger-grip curl. Extracted from PlayerController - see PlayerController.cs for the thin
    // forwarding accessors PlayerIKHelper, MinigameHandRig, and PlayerInventory still call
    // (RightHandBone, haulActive, ikBlendSpeed, hangReachActive/Pos/Rot/RotWeight).
    //
    // PlayerIKHelper.OnAnimatorIK only fires on the component sitting alongside the Animator (a CHILD
    // GameObject, "CharacterVisuals") - never on this component. That's unaffected by this extraction:
    // PlayerIKHelper already reached PlayerController's IK methods through a plain cross-hierarchy field
    // reference, not same-GameObject SendMessage magic, so it now reaches ApplyMinigameIK / ApplyHaulIK
    // here the same way. PlayerIKRig itself stays on the root, alongside PlayerController / Motor / Look
    // / Inventory - it does NOT need to move to the Animator's GameObject.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerIKRig : MonoBehaviour
    {
        [Header("Hand Bone")]
        [SerializeField] private Transform rightHandBone;  // The actual rig hand bone (Hand_R) - items glued here follow the animated / IK'd arm

        /// <summary>Falls back to the Animator's humanoid right-hand bone if no override is assigned.
        /// PlayerController.RightHandBone forwards here.</summary>
        public Transform RightHandBone => rightHandBone != null ? rightHandBone
            : (player != null && player.PlayerAnimator != null && player.PlayerAnimator.isHuman
                ? player.PlayerAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null);

        [Header("Minigame Hand Grip")]
        [Tooltip("While a minigame is open, curl the right-hand fingers into a grab while the LEFT MOUSE BUTTON is held and open them when it's released - a visual 'reach in and place / grab' gesture.")]
        [SerializeField] private bool minigameHandGrip = true;
        [Tooltip("Finger base bones under the right hand to curl (e.g. Finger_01_R, IndexFinger_01_R). Leave empty to auto-detect from RightHandBone's children by name.")]
        [SerializeField] private Transform[] rightHandFingerBones;
        [Tooltip("Right thumb base bone (e.g. Thumb_01_R). Leave empty to auto-detect.")]
        [SerializeField] private Transform rightHandThumbBone;
        [Tooltip("Degrees each finger joint curls TOWARD THE PALM at a full grab. The curl axis is derived from the rig automatically, so this just needs to be positive.")]
        [SerializeField] private float fingerCurlDegrees = 60f;
        [Tooltip("Degrees the thumb curls toward the fingers at a full grab.")]
        [SerializeField] private float thumbCurlDegrees = 35f;
        [Tooltip("Optional. Leave at (0,0,0) to auto-derive each finger's curl axis from the rig. Set a bone-local axis here only if the auto curl still looks wrong.")]
        [SerializeField] private Vector3 fingerCurlAxisOverride = Vector3.zero;
        [Tooltip("How fast the hand opens / closes (higher = snappier).")]
        [SerializeField] private float handGripSpeed = 16f;
        [Tooltip("Minimum time the hand stays clenched after a left-click, so a quick click (not just a hold) still shows a clear grab-and-release.")]
        [SerializeField] private float handGripPulseTime = 0.18f;

        private float handGrip01;      // current fist amount: 0 = open, 1 = closed
        private float handGripTarget;  // where handGrip01 is heading this frame
        private float gripHoldUntil;   // Time.time until which the grip stays closed after a click
        private readonly List<Transform> gripFingerBones = new List<Transform>();
        private readonly List<Quaternion> gripFingerDefaults = new List<Quaternion>();
        private readonly List<Quaternion> gripFingerCurls = new List<Quaternion>();   // full-grab local delta per finger bone
        private readonly List<Transform> gripThumbBones = new List<Transform>();
        private readonly List<Quaternion> gripThumbDefaults = new List<Quaternion>();
        private readonly List<Quaternion> gripThumbCurls = new List<Quaternion>();
        private bool gripBonesCollected;

        // --- Sword-hang minigame: reach the right hand toward a target while it holds an item ---
        // Routed through MinigameHandRig (Assets/Scripts/Minigames/Capabilities/MinigameHandRig.cs) -
        // that class (and PlayerLook's read of hangReachActive) talks to these via the forwarding
        // properties on PlayerController, unaware they live here now.
        [HideInInspector] public bool hangReachActive;
        [HideInInspector] public Vector3 hangReachPos;
        [HideInInspector] public Quaternion hangReachRot = Quaternion.identity;
        [HideInInspector] public float hangReachRotWeight; // 0 = keep held pose, 1 = fully align to hangReachRot
        /// <summary>Blend weight for the hang-reach IK. No PlayerController forwarder - only
        /// PlayerController.ApplyHangReachIK (which stays there) reads/writes this, via ikRig directly.</summary>
        public float hangReachWeight;

        // --- Two-handed haul: both hands lock onto grip points on a carried heavy item ---
        [HideInInspector] public bool haulActive;
        [HideInInspector] public Vector3 haulLeftHandPos, haulRightHandPos;
        [HideInInspector] public Vector3 haulLeftElbowPos, haulRightElbowPos;
        [HideInInspector] public Quaternion haulLeftHandRot = Quaternion.identity, haulRightHandRot = Quaternion.identity;
        [HideInInspector] public bool haulUseRotation;
        private float haulIKWeight;

        [Header("Minigame IK Tracking")]
        [Tooltip("How far in front of the camera the hand should hover while playing.")]
        public float minigameIKDepth = 0.6f;
        [Tooltip("How fast the hand raises and lowers when opening/closing a minigame. Also shared by " +
                 "PlayerStrangle's ApplyStrangleIK / PlayerController's ApplyHangReachIK blends via the " +
                 "ikBlendSpeed forwarding property.")]
        public float ikBlendSpeed = 8f;

        [Tooltip("Tweak this to rotate the hand bone so the palm faces inward.")]
        public Vector3 rightHandRotationOffset = new Vector3(0f, 0f, 90f);

        private float currentIKWeight = 0f;
        private Vector3 ikTargetPosition;

        // The sibling PlayerController and PlayerInventory on this same GameObject.
        private PlayerController player;
        private PlayerInventory inventory;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            inventory = GetComponent<PlayerInventory>();
        }

        // Finds the right-hand finger + thumb bones to curl for the minigame grab gesture (inspector
        // overrides if given, else RightHandBone's children by name), then works out a per-bone curl
        // rotation that swings each fingertip TOWARD THE PALM, so the grab direction can't be inverted.
        // Called once from PlayerController.Awake() (after it resolves the Animator), and retried lazily
        // from ApplyHandGripPose if the rig wasn't ready yet.
        public void CollectHandGripBones()
        {
            if (gripBonesCollected) return;

            gripFingerBones.Clear(); gripFingerDefaults.Clear(); gripFingerCurls.Clear();
            gripThumbBones.Clear();  gripThumbDefaults.Clear();  gripThumbCurls.Clear();

            void Add(Transform bone, bool isThumb)
            {
                if (bone == null) return;
                var bones = isThumb ? gripThumbBones : gripFingerBones;
                var defs  = isThumb ? gripThumbDefaults : gripFingerDefaults;
                if (bones.Contains(bone)) return;
                bones.Add(bone);
                defs.Add(bone.localRotation);
                // Include the next segment down (e.g. Finger_02_R) so the curl reads as a fist, not a twitch.
                if (bone.childCount > 0)
                {
                    Transform seg = bone.GetChild(0);
                    if (!bones.Contains(seg)) { bones.Add(seg); defs.Add(seg.localRotation); }
                }
            }

            bool haveOverride = (rightHandFingerBones != null && rightHandFingerBones.Length > 0) || rightHandThumbBone != null;
            if (haveOverride)
            {
                if (rightHandFingerBones != null)
                    foreach (Transform b in rightHandFingerBones) Add(b, false);
                Add(rightHandThumbBone, true);
            }
            else
            {
                Transform hand = RightHandBone;
                if (hand == null) return; // rig not ready yet - retried next frame from ApplyHandGripPose
                foreach (Transform child in hand.GetComponentsInChildren<Transform>())
                {
                    if (child == hand) continue;
                    string n = child.name.ToLowerInvariant();
                    bool isThumb = n.Contains("thumb");
                    bool isFinger = n.Contains("finger") || n.Contains("index") || n.Contains("pinky")
                                    || n.Contains("middle") || n.Contains("ring");
                    // Only the root segment of each finger - Add() pulls in its first child itself.
                    bool isRootSegment = n.Contains("01") || n.Contains("_1") || (!n.Contains("02") && !n.Contains("03"));
                    if (isThumb && isRootSegment) Add(child, true);
                    else if (isFinger && isRootSegment) Add(child, false);
                }
            }

            // Second pass: derive the curl direction now that every bone (and the thumb/finger we use as
            // the "toward the palm" reference) is known.
            foreach (Transform b in gripFingerBones) gripFingerCurls.Add(ComputeCurlDelta(b, false));
            foreach (Transform b in gripThumbBones)  gripThumbCurls.Add(ComputeCurlDelta(b, true));

            gripBonesCollected = gripFingerBones.Count > 0 || gripThumbBones.Count > 0;
        }

        // A bone-local rotation of `degrees` about the axis that rotates this bone's tip toward the palm.
        // The axis sign is chosen by actually testing the rotation, so "which way is inward" is never a
        // guess - set fingerCurlAxisOverride only if a rig defeats even this.
        private Quaternion ComputeCurlDelta(Transform bone, bool isThumb)
        {
            float deg = isThumb ? thumbCurlDegrees : fingerCurlDegrees;
            if (bone == null) return Quaternion.identity;
            if (!isThumb && fingerCurlAxisOverride.sqrMagnitude > 1e-6f)
                return Quaternion.AngleAxis(deg, fingerCurlAxisOverride.normalized);

            Transform hand = RightHandBone;
            Transform child = bone.childCount > 0 ? bone.GetChild(0) : null;
            if (hand == null || child == null)
                return Quaternion.AngleAxis(deg, Vector3.forward); // best-effort: local Z

            Vector3 tipLocal = bone.InverseTransformDirection((child.position - bone.position).normalized);

            // What the curl should bend toward: fingers fold toward the thumb, the thumb folds toward the fingers.
            Transform refBone = isThumb
                ? (gripFingerBones.Count > 0 ? gripFingerBones[0] : null)
                : (gripThumbBones.Count > 0 ? gripThumbBones[0] : rightHandThumbBone);
            Vector3 refWorld = refBone != null ? (refBone.position - bone.position)
                                               : (isThumb ? hand.forward : -hand.up);
            Vector3 refLocal = bone.InverseTransformDirection(refWorld.normalized);

            Vector3 axis = Vector3.Cross(tipLocal, refLocal);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward;
            axis.Normalize();

            // Flip the axis if +deg would swing the tip AWAY from the palm reference.
            Vector3 rotatedTip = Quaternion.AngleAxis(deg, axis) * tipLocal;
            if (Vector3.Dot(rotatedTip, refLocal) < Vector3.Dot(tipLocal, refLocal)) axis = -axis;

            return Quaternion.AngleAxis(deg, axis);
        }

        // Writes the curled finger pose over whatever the animator produced this frame. Skips entirely
        // when the hand is fully open so idle animation keeps full control of the fingers. Called from
        // PlayerController.LateUpdate - must run after the Animator has posed this frame, and still fires
        // while a minigame has controls locked (LateUpdate isn't gated by Update's early return).
        public void ApplyHandGripPose()
        {
            if (!minigameHandGrip) return;
            if (!gripBonesCollected) CollectHandGripBones();

            bool isPlayingMinigame = player != null && player.isPlayingMinigame;

            // Curl toward a fist while the LEFT MOUSE BUTTON is held during ANY minigame, open otherwise.
            // isPlayingMinigame is a read-through of MinigameBase.IsAnyActive (ARCHITECTURE.md P3), so this
            // covers every minigame regardless of launch path - StartMinigame ones (cake, consume, ...) and
            // the deposit ones (sword rack, dowry chest) alike. Used to need `|| hangReachActive` to cover
            // the deposit case, since TaskDepositStation.LaunchDepositMinigame never set isPlayingMinigame.
            // The deposit minigames act on the mouse-DOWN (drop / grab the lid), so a real hold never
            // happens - latch a short pulse on the click so the grab is always visible.
            if (isPlayingMinigame && Input.GetMouseButtonDown(0)) gripHoldUntil = Time.time + handGripPulseTime;
            bool wantGrip = isPlayingMinigame && (Input.GetMouseButton(0) || Time.time < gripHoldUntil);
            handGripTarget = wantGrip ? 1f : 0f;

            handGrip01 = Mathf.MoveTowards(handGrip01, handGripTarget, Time.deltaTime * handGripSpeed);
            if (handGrip01 <= 0.0005f && handGripTarget <= 0.0005f) return;

            float t = Mathf.SmoothStep(0f, 1f, handGrip01);

            for (int i = 0; i < gripFingerBones.Count && i < gripFingerCurls.Count; i++)
                if (gripFingerBones[i] != null)
                    gripFingerBones[i].localRotation = gripFingerDefaults[i] * Quaternion.Slerp(Quaternion.identity, gripFingerCurls[i], t);

            for (int i = 0; i < gripThumbBones.Count && i < gripThumbCurls.Count; i++)
                if (gripThumbBones[i] != null)
                    gripThumbBones[i].localRotation = gripThumbDefaults[i] * Quaternion.Slerp(Quaternion.identity, gripThumbCurls[i], t);
        }

        // Called from PlayerController.Update() every frame (so the hand follows the mouse the instant a
        // minigame opens).
        public void UpdateMinigameIKTarget()
        {
            Transform cam = player != null ? player.PlayerCamera : null;
            bool isPlayingMinigame = player != null && player.isPlayingMinigame;
            bool isMinigameLooking = player != null && player.isMinigameLooking;

            if (isPlayingMinigame && cam != null && !isMinigameLooking)
            {
                // 1. Get the raw 2D pixel coordinate of the mouse on the screen
                Vector3 mouseScreenPos = Input.mousePosition;

                // 2. Set the Z axis to define how deep into the 3D world we want to project
                mouseScreenPos.z = minigameIKDepth;

                // 3. Convert that screen pixel + depth into a physical 3D world coordinate
                ikTargetPosition = cam.GetComponent<Camera>().ScreenToWorldPoint(mouseScreenPos);
            }
        }

        // Called from PlayerIKHelper.OnAnimatorIK (PlayerIKHelper sits on the Animator's GameObject and
        // reaches this component via a plain cross-hierarchy reference - see the class comment above).
        public void ApplyMinigameIK(int layerIndex)
        {
            Animator animator = player != null ? player.PlayerAnimator : null;
            if (animator == null) return;

            bool isPlayingMinigame = player != null && player.isPlayingMinigame;
            float targetWeight = isPlayingMinigame ? 1f : 0f;
            currentIKWeight = Mathf.Lerp(currentIKWeight, targetWeight, Time.deltaTime * ikBlendSpeed);

            if (currentIKWeight > 0.01f)
            {
                // --- RIGHT HAND (Always tracks the Mouse in all 3 scenarios) ---
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, currentIKWeight);
                animator.SetIKPosition(AvatarIKGoal.RightHand, ikTargetPosition);

                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, currentIKWeight);
                Quaternion handOffset = Quaternion.Euler(rightHandRotationOffset);
                animator.SetIKRotation(AvatarIKGoal.RightHand, player.PlayerCamera.transform.rotation * handOffset);

                // --- LEFT HAND (Dynamically changes based on target) ---
                if (player.currentMinigameTargetType == MinigameTargetType.Player)
                {
                    // SCENARIO 1: PLAYER - Keep the left arm resting naturally by their side
                    animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                    animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                }
                else if (player.currentMinigameTargetType == MinigameTargetType.Item)
                {
                    // SCENARIO 2: ITEM - Raise left hand to hold the item in the center of the screen
                    if (player.LeftHandSocket != null)
                    {
                        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, currentIKWeight);
                        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, currentIKWeight); // Turn on rotation!

                        animator.SetIKPosition(AvatarIKGoal.LeftHand, player.LeftHandSocket.position);

                        // Match the hand bone rotation to the socket's rotation so you can fix weird wrist twists!
                        animator.SetIKRotation(AvatarIKGoal.LeftHand, player.LeftHandSocket.rotation);
                    }
                }
                else
                {
                    // SCENARIO 3: STATION - Slam left hand onto the center of the table
                    Transform station = player.ActiveMinigameStation;
                    if (station != null)
                    {
                        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, currentIKWeight);

                        Vector3 leftHandTarget = station.position;
                        Collider stationCol = station.GetComponent<Collider>();
                        if (stationCol != null)
                        {
                            leftHandTarget = stationCol.bounds.center;
                        }

                        animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandTarget);
                        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f); // Default to idle rotation
                    }
                }
            }
            else
            {
                // Release control of both hands back to the standard idle/walking animations
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);

                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            }
        }

        // Keeps haulActive in sync with what's held, re-poses the item, and refreshes the grip-point +
        // elbow-hint positions. Hands are auto-assigned by which side of the player each grip is on.
        // Called from PlayerController.Update().
        public void UpdateHaulIKTarget()
        {
            PickupItem held = inventory != null ? inventory.GetHeldItem() : null;
            bool hauling = held != null && held.haulWithBothHands;
            haulActive = hauling;
            if (!hauling) return;

            PickupItem hi = held;
            inventory.PoseHaulItem();

            Transform gpA = hi.leftGripPoint;
            Transform gpB = hi.rightGripPoint;
            Vector3 posA = gpA != null ? gpA.position : hi.transform.TransformPoint(Vector3.left * 0.35f);
            Vector3 posB = gpB != null ? gpB.position : hi.transform.TransformPoint(Vector3.right * 0.35f);
            Quaternion rotA = gpA != null ? gpA.rotation : Quaternion.identity;
            Quaternion rotB = gpB != null ? gpB.rotation : Quaternion.identity;

            // Whichever grip is further to the player's LEFT gets the left hand - so the arms can't cross
            // no matter how the item is rotated.
            bool aIsLeft = Vector3.Dot(posA - transform.position, transform.right)
                         <= Vector3.Dot(posB - transform.position, transform.right);

            haulLeftHandPos = aIsLeft ? posA : posB;
            haulRightHandPos = aIsLeft ? posB : posA;
            haulLeftHandRot = aIsLeft ? rotA : rotB;
            haulRightHandRot = aIsLeft ? rotB : rotA;
            haulUseRotation = gpA != null && gpB != null;

            // Push each elbow out past its own hand (player-relative) and drop it below - arms wing out.
            haulLeftElbowPos = haulLeftHandPos - transform.right * hi.haulElbowOut - Vector3.up * hi.haulElbowDrop;
            haulRightElbowPos = haulRightHandPos + transform.right * hi.haulElbowOut - Vector3.up * hi.haulElbowDrop;
        }

        // Called from PlayerIKHelper.OnAnimatorIK - pins BOTH hands to the grip points of a hauled item
        // so the player looks like they're carrying it. Fed each frame by UpdateHaulIKTarget().
        public void ApplyHaulIK(int layerIndex)
        {
            Animator animator = player != null ? player.PlayerAnimator : null;
            if (animator == null) return;

            float target = haulActive ? 1f : 0f;
            haulIKWeight = Mathf.Lerp(haulIKWeight, target, Time.deltaTime * ikBlendSpeed);
            if (haulIKWeight <= 0.01f)
            {
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
                return;
            }

            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, haulIKWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, haulLeftHandPos);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, haulIKWeight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, haulRightHandPos);

            // Elbow hints keep the arms bowed outward instead of crossing in front of the chest.
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, haulIKWeight);
            animator.SetIKHintPosition(AvatarIKHint.LeftElbow, haulLeftElbowPos);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, haulIKWeight);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, haulRightElbowPos);

            if (haulUseRotation)
            {
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, haulIKWeight);
                animator.SetIKRotation(AvatarIKGoal.LeftHand, haulLeftHandRot);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, haulIKWeight);
                animator.SetIKRotation(AvatarIKGoal.RightHand, haulRightHandRot);
            }
        }
    }
}
