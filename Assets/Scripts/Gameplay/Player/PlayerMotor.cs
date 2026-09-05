using UnityEngine;
using CorruptedCourt.Core;
using UnityEngine.InputSystem;

namespace CorruptedCourt.Gameplay
{
    // Owns locomotion: grounded movement, gravity, jumping, sprinting, crouching (including the
    // crouch height / camera-Y lerp), the lean input blend, constrained minigame walking, and
    // teleportation. Extracted from PlayerController - see PlayerController.cs for the thin
    // forwarders other scripts still call (CharController, MinigameWalk, TeleportTo, ...).
    //
    // Must live on the same GameObject as PlayerController and PlayerInput: PlayerInput's "Send
    // Messages" behaviour calls OnJump/OnSprint/OnCrouch/OnLean on every component on that
    // GameObject, so this component receives them directly, alongside PlayerController's own
    // input callbacks.
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float sprintSpeed = 8f;
        [SerializeField] private float crouchSpeed = 2.5f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float jumpHeight = 1.5f;

        [Header("Smooth Crouch Settings")]
        [SerializeField] private float crouchTransitionSpeed = 10f;

        [Header("Lean (hold Ctrl to bend forward at the hips - reach low stations)")]
        [Tooltip("How fast the lean blends in/out. The bend angle and camera arc themselves are still " +
                 "tuned on PlayerController (leanAngle, leanViewPitch, leanHipLocalY, ...) - only the " +
                 "0-1 blend fraction those read every frame lives here.")]
        [SerializeField] private float leanSpeed = 8f;

        // The sibling PlayerController on this same GameObject. Resolved in Awake - never null once
        // RequireComponent has done its job.
        private PlayerController player;
        private CharacterController characterController;

        /// <summary>Read-through for code that needs the raw CharacterController (e.g. deposit
        /// minigames temporarily shrinking its radius). PlayerController.CharController forwards here.</summary>
        public CharacterController CharController => characterController;

        // --- Crouch state ---
        private bool isCrouching;
        private float targetHeight;
        private float targetCameraY;
        private float standingCameraY = 0.8f;
        private float crouchingCameraY = 0.5f;
        private float originalHeight;
        private float crouchHeight;

        // --- Jump / gravity / sprint state ---
        private Vector3 velocity;
        private bool isGrounded;
        private bool isSprinting;

        // --- Lean input blend ---
        private bool isLeaning;
        private float leanBlend;

        /// <summary>0 = standing straight, 1 = fully leaned. PlayerController's ApplyLeanCameraArc /
        /// ApplyLeanSpineBend read this every LateUpdate to pose the camera and spine.</summary>
        public float LeanBlend => leanBlend;

        // The camera's un-leaned local offset, driven by the crouch lerp. PlayerController's
        // ApplyLeanCameraArc arcs the camera forward from this clean base each frame so the lean
        // can't accumulate.
        private Vector3 camBaseLocalXZ;
        private float camBaseY;
        public Vector3 CamBaseLocalXZ => camBaseLocalXZ;
        public float CamBaseY => camBaseY;

        void Awake()
        {
            characterController = GetComponent<CharacterController>();
            player = GetComponent<PlayerController>();

            originalHeight = characterController.height;
            crouchHeight = originalHeight / 2f;
            targetHeight = originalHeight;
            targetCameraY = standingCameraY;

            // Cache the camera's resting local offset so the lean arc can always be computed from a
            // clean base (never from the already-arced position - that runs away).
            Transform cam = player != null ? player.PlayerCamera : null;
            if (cam != null)
            {
                camBaseLocalXZ = new Vector3(cam.localPosition.x, 0f, cam.localPosition.z);
                camBaseY = cam.localPosition.y;
            }
        }

        #region Input Action Callbacks

        public void OnJump(InputValue value)
        {
            // Block jumping entirely if holding a heavy item (either hand).
            if (player != null && player.IsHoldingHeavyItem())
            {
                if (value.isPressed) Log.Game("Cannot jump while carrying a heavy item!");
                return;
            }
            if (value.isPressed && isGrounded && !isCrouching)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }

        public void OnSprint(InputValue value)
        {
            // value.isPressed is true when you press and hold the key down
            // value.isPressed becomes false the exact moment you let go of the key
            isSprinting = value.isPressed;
            if (value.isPressed) Log.Game("Sprinting");
            else Log.Game("Walking");
        }

        public void OnCrouch(InputValue value)
        {
            Log.Game("Crouched");
            if (!value.isPressed) return;

            isCrouching = !isCrouching;

            targetHeight = isCrouching ? crouchHeight : originalHeight;
            targetCameraY = isCrouching ? crouchingCameraY : standingCameraY;
        }

        // [Ctrl]: hold to bend forward at the hips so you can reach a low deposit station.
        public void OnLean(InputValue value)
        {
            isLeaning = value.isPressed;
        }

        #endregion

        /// <summary>Advances the lean blend by one frame. Called from PlayerController.Update() BEFORE
        /// the controls-locked / minigame gates, so you can still lean down to reach into a low chest
        /// during its minigame.</summary>
        public void UpdateLeanBlend()
        {
            bool ctrlHeld = isLeaning || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool wantLean = ctrlHeld && player != null && !player.Vitals.isStrangling && !player.Vitals.isArrested && !player.Vitals.IsStunned;
            leanBlend = Mathf.MoveTowards(leanBlend, wantLean ? 1f : 0f, Time.deltaTime * leanSpeed);
        }

        /// <summary>Cancels a latched sprint input, e.g. when a blocking menu opens.</summary>
        public void CancelSprint() => isSprinting = false;

        /// <summary>Cancels a latched lean input, e.g. when a blocking menu opens.</summary>
        public void CancelLeanInput() => isLeaning = false;

        /// <summary>Applies a one-off multiplier to walk speed (e.g. the ghost speed-up in BecomeGhost).</summary>
        public void ScaleWalkSpeed(float multiplier) => walkSpeed *= multiplier;

        public void HandleMovement(Vector2 moveInput)
        {
            isGrounded = characterController.isGrounded;
            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;
            }

            float currentSpeed = walkSpeed;

            // --- OVERRIDE MOVEMENT SPEED FOR HEAVY ITEMS, STUNS, & ARRESTS ---
            bool isHoldingHeavy = player != null && player.IsHoldingHeavyItem();

            // 1. Highest Priority: Stuns, Pushbacks, and Arrests completely lock voluntary movement
            if (player != null && (player.Vitals.IsStunned || player.Vitals.IsBeingPushed || player.Vitals.isArrested))
            {
                currentSpeed = 0f;
            }
            // 2. Second Priority: Heavy items and Dragging Prisoners halve speed and disable sprint/crouch speeds
            else if (isHoldingHeavy || (player != null && player.Vitals.isDraggingPrisoner))
            {
                currentSpeed = walkSpeed * 0.5f;
            }
            // 3. Normal movement logic
            else if (isCrouching)
            {
                currentSpeed = crouchSpeed;
            }
            else if (isSprinting)
            {
                currentSpeed = sprintSpeed;
            }

            // --- Combine movement and gravity into ONE vector ---
            Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
            Vector3 finalMovement = moveDirection * currentSpeed;

            // Calculate gravity
            velocity.y += gravity * Time.deltaTime;
            finalMovement.y = velocity.y; // Add the Y velocity to the final movement

            // Execute exactly ONE Move call so Unity calculates the velocity perfectly!
            characterController.Move(finalMovement * Time.deltaTime);
        }

        public void HandleCrouchTransition()
        {
            characterController.height = Mathf.Lerp(characterController.height, targetHeight, Time.deltaTime * crouchTransitionSpeed);

            // Drive the un-leaned base Y (crouch lerp), then set the camera to that clean base. The lean
            // arc is layered on top in PlayerController.LateUpdate, computed from this base - so it can't accumulate.
            camBaseY = Mathf.Lerp(camBaseY, targetCameraY, Time.deltaTime * crouchTransitionSpeed);

            Transform cam = player != null ? player.PlayerCamera : null;
            if (cam != null) cam.localPosition = new Vector3(camBaseLocalXZ.x, camBaseY, camBaseLocalXZ.z);
        }

        // Constrained walking for placement minigames (e.g. SwordHangMinigame). The minigame calls this
        // each frame with a raw move vector (x = strafe, y = forward) it read itself, so the player can
        // shuffle into line with a target while the mouse stays busy aiming. Runs at half walk speed and
        // leashes the player within `radius` metres of `anchor`. Works even while controlsLocked is true
        // because the minigame is driving it directly rather than the normal Update() path.
        // (Renamed from PlayerController.MinigameWalk, which now forwards here.)
        public void WalkConstrained(Vector2 move, Vector3 anchor, float radius)
        {
            if (characterController == null || !characterController.enabled) return;

            isGrounded = characterController.isGrounded;
            if (isGrounded && velocity.y < 0f) velocity.y = -2f;

            Vector3 dir = transform.right * move.x + transform.forward * move.y;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            Vector3 step = dir * (walkSpeed * 0.5f);
            velocity.y += gravity * Time.deltaTime;
            step.y = velocity.y;
            characterController.Move(step * Time.deltaTime);

            if (radius > 0f)
            {
                Vector3 offset = transform.position - anchor;
                offset.y = 0f;
                if (offset.magnitude > radius)
                {
                    Vector3 clamped = anchor + offset.normalized * radius;
                    clamped.y = transform.position.y;
                    characterController.enabled = false;
                    transform.position = clamped;
                    characterController.enabled = true;
                }
            }
        }

        public void TeleportTo(Transform targetTransform)
        {
            TeleportTo(targetTransform.position, targetTransform.rotation);
        }

        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            // Must disable CharacterController to physically move the player
            if (characterController != null) characterController.enabled = false;

            transform.position = position;
            transform.rotation = rotation;

            if (characterController != null) characterController.enabled = true;
        }
    }
}
