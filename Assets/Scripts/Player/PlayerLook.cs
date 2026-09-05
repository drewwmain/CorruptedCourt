using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    // Owns camera look: mouse-look rotation, the pitch clamp, the minigame free-look yaw clamp, and the
    // lean camera arc. Extracted from PlayerController - see PlayerController.cs for the thin forwarders
    // other scripts still call (MinigameLookYaw, MinigameLookPitch, PointCameraAt).
    //
    // Must live on the same GameObject as PlayerController and PlayerMotor (back-references below resolve
    // via GetComponent and assume that).
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerLook : MonoBehaviour
    {
        [Header("Look Settings")]
        [SerializeField] private float mouseSensitivity = 0.1f;
        [SerializeField] private float upperLookLimit = 80f;
        [SerializeField] private float lowerLookLimit = -80f;

        [Tooltip("How far left or right (in degrees) a player can look while playing a minigame.")]
        public float minigameLookLimit = 90f;

        [Header("Lean camera arc (hold Ctrl to bend forward at the hips - reach low stations)")]
        [Tooltip("Bend angle at the hips when fully leaned. The camera swings forward+down on this same arc.")]
        [SerializeField] private float leanAngle = 45f;
        [Tooltip("Height of the hip pivot in the player's LOCAL space (the camera arcs around this point). Lower it if the camera doesn't move far enough forward.")]
        [SerializeField] private float leanHipLocalY = -0.1f;
        [Tooltip("How much the camera pitches while fully leaned, in degrees. Positive = look down, NEGATIVE = tilt UP (keeps your hands in frame while aiming in a minigame). Independent of the body-bend angle.")]
        [SerializeField] private float leanViewPitch = -5f;
        [Range(0f, 1f)]
        [Tooltip("While a minigame drives the right hand, the camera's FORWARD lean travel is scaled by this (the drop is kept). Lower = the hand's aim target stays within arm's reach.")]
        [SerializeField] private float leanMinigameForwardFactor = 0.2f;

        /// <summary>Exposed for PlayerController.ApplyLeanSpineBend, which bends the spine by this same angle
        /// (the spine bend itself stays on PlayerController - it must run after the Animator evaluates the
        /// frame's pose, in LateUpdate).</summary>
        public float LeanAngle => leanAngle;

        // The sibling PlayerController and PlayerMotor on this same GameObject.
        private PlayerController player;
        private PlayerMotor motor;

        private float verticalRotation = 0f;
        private float currentMinigameYaw = 0f; // Tracks how far we have turned during minigame free-look
        private Quaternion minigameStartBodyRotation;
        private float minigameStartVerticalRotation;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            motor = GetComponent<PlayerMotor>();
        }

        // Clamps and applies a new camera pitch in ONE place. This is the whole point of this extraction:
        // HandleRotation, MinigameLookPitch, and PointCameraAt below all funnel through here instead of
        // each separately clamping verticalRotation and writing playerCamera.localRotation. The one
        // outside caller (PlayerController.StartMinigame's camera-snap block) reaches it through the
        // public SnapPitch gateway just below, and EndMinigameLook (also below) reuses it too.
        private void SetPitch(float degrees)
        {
            verticalRotation = Mathf.Clamp(degrees, lowerLookLimit, upperLookLimit);
            Transform cam = player != null ? player.PlayerCamera : null;
            if (cam != null) cam.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        }

        /// <summary>Public gateway to SetPitch for callers outside this component. PlayerController's
        /// StartMinigame computes its own raw (unclamped) target pitch per minigame-target scenario
        /// (Player / Item / Station) and hands the result here instead of duplicating the clamp + apply.</summary>
        public void SnapPitch(float degrees) => SetPitch(degrees);

        public void HandleRotation(Vector2 lookInput)
        {
            bool isPlayingMinigame = player != null && player.isPlayingMinigame;
            bool isMinigameLooking = player != null && player.isMinigameLooking;

            // --- 1. HORIZONTAL ROTATION (Body) ---
            if (isPlayingMinigame && isMinigameLooking)
            {
                // Add the mouse input to our tracker
                currentMinigameYaw += lookInput.x * mouseSensitivity;

                // Clamp it so they can't turn past the limit (e.g., -90 to 90 degrees)
                currentMinigameYaw = Mathf.Clamp(currentMinigameYaw, -minigameLookLimit, minigameLookLimit);

                // Apply the clamped rotation relative to their original starting angle
                transform.rotation = minigameStartBodyRotation * Quaternion.Euler(0f, currentMinigameYaw, 0f);
            }
            else
            {
                // Normal FPS free rotation
                transform.Rotate(Vector3.up * lookInput.x * mouseSensitivity);
            }

            // --- 2. VERTICAL ROTATION (Camera Pitch) ---
            // (This remains exactly the same since it's already perfectly clamped!)
            SetPitch(verticalRotation - lookInput.y * mouseSensitivity);
        }

        // Horizontal-only look for placement minigames. The minigame calls this while the player holds
        // RMB, passing an already-scaled yaw in degrees. Turns the body (and the camera with it) so they
        // can pan across a wide target; the mouse's vertical axis stays free for aiming.
        // (Renamed from nothing - PlayerController.MinigameLookYaw forwards here, unchanged signature.)
        public void MinigameLookYaw(float degrees)
        {
            transform.Rotate(0f, degrees, 0f, Space.World);
        }

        // Vertical look for placement minigames. Positive `degrees` = look up (matches mouse-up). Pitch is
        // clamped to the same limits as normal FPS look and applied straight to the camera.
        // (PlayerController.MinigameLookPitch forwards here.)
        public void MinigameLookPitch(float degrees)
        {
            SetPitch(verticalRotation - degrees);
        }

        // Aim the body + camera at a world point (used when a placement minigame starts).
        // (PlayerController.PointCameraAt forwards here.)
        public void PointCameraAt(Vector3 worldPoint)
        {
            Transform cam = player != null ? player.PlayerCamera : null;
            if (cam == null) return;

            Vector3 dir = worldPoint - cam.position;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(flat);

            float pitch = Quaternion.LookRotation(dir).eulerAngles.x;
            if (pitch > 180f) pitch -= 360f;
            SetPitch(pitch);
        }

        /// <summary>Called once, at the end of PlayerController.StartMinigame, after it has finished
        /// snapping the body/camera to the target - captures the "home" pose so EndMinigameLook can snap
        /// back to it when the player releases free-look.</summary>
        public void CaptureMinigameLookHome()
        {
            minigameStartBodyRotation = transform.rotation;
            minigameStartVerticalRotation = verticalRotation;
        }

        /// <summary>Called when RMB is pressed to start free-looking during an open minigame
        /// (PlayerController.OnStrangle's minigame-camera-look interception).</summary>
        public void BeginMinigameLook()
        {
            currentMinigameYaw = 0f;
        }

        /// <summary>Called when RMB is released, ending free-look during an open minigame - snaps the body
        /// and camera back to the pose CaptureMinigameLookHome saved.</summary>
        public void EndMinigameLook()
        {
            transform.rotation = minigameStartBodyRotation;
            SetPitch(minigameStartVerticalRotation);
        }

        // Pivots the camera forward + down around a hip point, matching a bend at the hips, so it clears
        // the body instead of clipping straight down through it. Computed from the CLEAN base (camBase* on
        // PlayerMotor) set by PlayerMotor.HandleCrouchTransition, never from the current position - so it
        // can't run away. PlayerController.Update calls this every frame, right after HandleRotation (see
        // the comment there for why this must happen in Update and not LateUpdate).
        public void ApplyLeanCameraArc()
        {
            Transform cam = player != null ? player.PlayerCamera : null;
            if (motor == null || cam == null || motor.LeanBlend <= 0.001f) return;

            float theta = leanAngle * motor.LeanBlend;
            Vector3 baseLocal = new Vector3(motor.CamBaseLocalXZ.x, motor.CamBaseY, motor.CamBaseLocalXZ.z);
            Vector3 pivot = new Vector3(baseLocal.x, leanHipLocalY, baseLocal.z);

            Vector3 arm = Quaternion.AngleAxis(theta, Vector3.right) * (baseLocal - pivot); // swing forward + down
            // During a minigame the camera pushing forward drags the hand's IK target out of arm's reach
            // (it's measured from the camera), so keep the drop but cut most of the forward travel.
            if (player != null && player.hangReachActive) arm.z *= leanMinigameForwardFactor;
            cam.localPosition = pivot + arm;
            // View pitch is its own tunable (can be negative = tilt up) so the hands stay in frame.
            cam.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f)
                                * Quaternion.AngleAxis(leanViewPitch * motor.LeanBlend, Vector3.right);
        }
    }
}
