using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Base for every minigame where the player drives their RIGHT HAND with the mouse: deposits,
    /// tool-use, dragging, pouring, consuming, drawing a bow, playing an instrument.
    ///
    /// It owns the plumbing the current deposit minigames each copy by hand:
    ///  - freezes the player and frees the cursor on begin, restores both on end,
    ///  - a <see cref="MinigameHandRig"/> for the reach IK + item attach,
    ///  - the settings-menu pause,
    ///  - hold-RIGHT-CLICK to look around (+ a quick tap to cancel),
    ///  - WASD footwork leashed to where the player started,
    ///  - <see cref="MouseWorld"/> - the mouse projected in front of the camera,
    ///  - an optional <see cref="MinigameGripConstraint"/> that keeps the held item from visibly
    ///    penetrating world geometry while aiming.
    ///
    /// ============================================================================================
    /// CONVENTION - READ BEFORE ADDING A SUBCLASS: this class declares Update()/LateUpdate()/
    /// FixedUpdate() ITSELF (see below) to drive the shared look/footwork/menu-pause plumbing every
    /// frame. A subclass must NEVER also declare its own Update()/LateUpdate()/FixedUpdate() - override
    /// OnMinigameUpdate() / OnMinigameLateUpdate() / OnMinigameFixedUpdate() instead.
    ///
    /// WHY THIS IS A HARD RULE, NOT A STYLE PREFERENCE: Update() here is a private method, and Unity's
    /// message dispatch finds "magic methods" like Update() by reflecting over the WHOLE type hierarchy,
    /// not by normal C# virtual-call resolution. A private method can't be overridden, so a subclass
    /// Update() would NOT replace this one - Unity would call BOTH every frame (this class's, driving
    /// the shared plumbing, AND the subclass's, running in parallel with no coordination). That is a
    /// silent double-update bug, not a compile error, which is exactly what made the pre-refactor
    /// SwordHangMinigame / ChestDepositMinigame duplication so easy to get subtly wrong - this base
    /// exists so there is exactly one place that owns the frame loop.
    /// ============================================================================================
    /// </summary>
    public abstract class HandMinigame : MinigameBase
    {
        [Header("Hand reach / look / footwork")]
        [Tooltip("Distance in front of the camera the hand reaches to follow the mouse.")]
        public float reachDistance = 1.2f;
        [Tooltip("WASD shuffle radius from where the player started. 0 = locked in place.")]
        public float walkRadius = 1.25f;
        [Tooltip("Hold RIGHT-CLICK + move the mouse to look around. Higher = faster.")]
        public float rmbLookSensitivity = 3f;
        [Tooltip("A right-click held shorter than this, with no mouse movement, cancels the minigame.")]
        public float rmbTapCancelTime = 0.2f;
        [Tooltip("Let the WASD look/footwork also tilt the camera vertically while looking around.")]
        public bool rmbAllowPitch = true;

        [Header("Grip constraint")]
        [Tooltip("Rotate the held item about its grip pivot so it never visibly enters world geometry " +
                 "while aiming. Also needs the item's PickupItem > Grip Constraint > Enable Grip Constraint. " +
                 "No effect while off.")]
        public bool useGripConstraint = false;
        [Tooltip("Set to the GripContact layer ONLY - the world colliders the held item must not pass through.")]
        public LayerMask gripContactMask;
        [Tooltip("When the grip constraint mirrors part of its correction onto the wrist, also shift the hand " +
                 "reach target so the held item's grip point stays visually pinned. The wrist joint is not at " +
                 "the grip point, so rotating the wrist would otherwise slide the item a few cm. Turn off to " +
                 "A/B the drift.")]
        public bool compensateGripDrift = true;
        [Tooltip("Seconds of exponential smoothing on the COSMETIC wrist give only (0 = off/instant). Softens " +
                 "how quickly the arm yields and returns during grip contact. Never affects where the held " +
                 "item rests - the item's clearance pose is always applied instantly.")]
        public float wristDamping = 0f;

        protected Camera cam;
        protected MinigameHandRig Hand { get; private set; }

        /// <summary>
        /// Optional predictive anti-penetration solve for the held item during the aiming phase. Built in
        /// <see cref="OnMinigameBegin"/> next to <see cref="Hand"/> (which it drives for the cosmetic
        /// wrist mirror); bound to the in-hand <c>Context.HeldItem</c> and Solved each frame right after
        /// <see cref="OnMinigameLateUpdate"/>; released by <see cref="RestorePlayer"/>. Inert (Solve is a
        /// safe no-op) until bound. Gated by <see cref="useGripConstraint"/> plus the item's own
        /// <c>enableGripConstraint</c>. The wrist mirror it writes through <see cref="Hand"/> lands on the
        /// hand one frame later (OnAnimatorIK has already run by LateUpdate) and is purely cosmetic - the
        /// item itself is always fully clear on the frame of contact.
        /// </summary>
        protected MinigameGripConstraint Grip { get; private set; }

        // This family drives its own leashed WASD shuffle (HandleFootwork) - keep PlayerController's
        // normal movement path off so the player isn't Move()d twice per frame.
        public override bool AllowsPlayerMovement => false;

        private Vector3 walkAnchor;
        private float rmbDownTime;
        private bool rmbDragged;
        private bool wasMenuPaused;
        private PickupItem gripBoundItem;

        // --- lifecycle ---------------------------------------------------------------------------

        protected override void OnMinigameBegin()
        {
            cam = (player != null && player.PlayerCamera != null)
                ? player.PlayerCamera.GetComponent<Camera>()
                : Camera.main;

            if (player == null || cam == null)
            {
                Log.Warn($"[{GetType().Name}] missing player or camera - cancelling.");
                CancelMinigame();
                return;
            }

            Hand = new MinigameHandRig(player, cam);
            Grip = new MinigameGripConstraint(gripContactMask, Hand);
            walkAnchor = player.transform.position;

            player.SetControlsLocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Hand.Begin();
            OnHandBegin();
        }

        protected override void OnMinigameEnd(bool won)
        {
            RestorePlayer();
        }

        /// <summary>
        /// Un-freeze the player, re-lock the cursor, release the hand rig. Also leaves the active registry
        /// (see MinigameBase.LeaveActiveRegistry) - a subclass that calls this mid-lifecycle (e.g. the
        /// instant a deposit item is released, well before its outcome is known) hands the player fully
        /// back to normal controls, so MinigameBase.IsAnyActive must go false too, not just controlsLocked.
        /// If the minigame later resumes restricting the player (a retry re-aims), call
        /// MinigameBase.RejoinActiveRegistry() from wherever it re-locks controls.
        /// </summary>
        protected void RestorePlayer()
        {
            if (Hand != null) Hand.End();
            // The grip constraint must go inert the instant control is handed back: a subclass calls
            // RestorePlayer the moment a deposit item is released, and GuidedDrop owns the item from then.
            if (Grip != null) Grip.End();
            gripBoundItem = null;
            if (player != null) player.SetControlsLocked(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            LeaveActiveRegistry();
        }

        /// <summary>
        /// Re-centre the WASD footwork leash on the player's CURRENT position. The anchor is normally set
        /// once in <see cref="OnMinigameBegin"/>; call this from a subclass that restarts its aiming phase
        /// somewhere new (e.g. after walking over to retrieve a missed item) and wants the leash to follow.
        /// </summary>
        protected void ReanchorFootwork()
        {
            if (player != null) walkAnchor = player.transform.position;
        }

        // --- Unity loop -> template methods -------------------------------------------------------
        // DO NOT declare Update()/LateUpdate()/FixedUpdate() in any subclass - see the class comment.

        private void Update()
        {
            if (player == null || cam == null) return;

            // Settings / pause menu: freeze the whole minigame so the mouse stops driving the arm.
            if (MinigameInput.Suppressed) { wasMenuPaused = true; return; }
            if (wasMenuPaused)
            {
                wasMenuPaused = false;
                if (WantsFreeCursor)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            HandleLook();
            HandleFootwork();
            OnMinigameUpdate();
        }

        private void LateUpdate()
        {
            if (player == null) return;
            OnMinigameLateUpdate();

            // The grip constraint solves AFTER OnMinigameLateUpdate(): a subclass (SwordHangMinigame)
            // writes the held item's world rotation in there, and THAT write is the rest pose
            // Grip.Solve() reads and corrects. Solving first would just be overwritten this frame.
            MaintainGripConstraint();
            if (Grip != null)
            {
                Grip.compensateGripDrift = compensateGripDrift; // Inspector A/B toggle, read live each frame
                Grip.wristDamping = wristDamping;               // feel knob for the cosmetic wrist give only
                Grip.Solve();
            }
        }

        private void FixedUpdate()
        {
            if (player == null) return;
            OnMinigameFixedUpdate();
        }

        // Bind the grip constraint to the minigame's held item while it is actually in the hand (the
        // aiming phase), and unbind when it leaves: released -> GuidedDrop owns it; re-picked-up for a
        // retry -> rebind. RestorePlayer() also ends it, synchronously, the moment the item is released;
        // this lazy check is what re-Begins on a retry without the subclass having to call in.
        private void MaintainGripConstraint()
        {
            if (Grip == null) return;

            PickupItem target = null;
            if (useGripConstraint && Context != null && Context.HeldItem != null
                && player != null && player.GetHeldItem() == Context.HeldItem)
            {
                target = Context.HeldItem;
            }

            if (target == gripBoundItem) return;

            if (gripBoundItem != null) Grip.End();
            gripBoundItem = null;

            if (target == null) return;

            GripConstraintSettings gripSettings = target.GripConstraint;
            if (gripSettings == null || !gripSettings.enableGripConstraint) return;

            Grip.Begin(target);
            gripBoundItem = target;
        }

        // --- shared handlers -------------------------------------------------------------------------

        /// <summary>Hold RMB to pan the body (and optionally pitch); a quick no-drag tap cancels.</summary>
        protected virtual void HandleLook()
        {
            if (!LookActive) return;

            if (MinigameInput.SecondaryDown) { rmbDownTime = Time.time; rmbDragged = false; }

            if (MinigameInput.SecondaryHeld)
            {
                Vector2 d = MinigameInput.MouseDelta;
                if (Mathf.Abs(d.x) > 0.001f)
                {
                    rmbDragged = true;
                    player.MinigameLookYaw(d.x * rmbLookSensitivity);
                }
                if (rmbAllowPitch && !SuppressPitchLook && Mathf.Abs(d.y) > 0.001f)
                {
                    rmbDragged = true;
                    player.MinigameLookPitch(d.y * rmbLookSensitivity);
                }
            }

            if (MinigameInput.SecondaryUp && !rmbDragged
                && Time.time - rmbDownTime <= rmbTapCancelTime && AllowTapCancel())
            {
                CancelMinigame();
            }
        }

        /// <summary>WASD shuffle, leashed to <see cref="walkRadius"/> of the start position.</summary>
        protected virtual void HandleFootwork()
        {
            if (!FootworkActive || walkRadius <= 0f) return;
            Vector2 step = MinigameInput.MoveAxis;
            if (step.sqrMagnitude > 0f) player.MinigameWalk(step, walkAnchor, walkRadius);
        }

        /// <summary>The mouse position projected <see cref="reachDistance"/> m in front of the camera.</summary>
        protected Vector3 MouseWorld()
        {
            Vector3 mp = MinigameInput.MouseScreenPosition;
            mp.z = reachDistance;
            return cam.ScreenToWorldPoint(mp);
        }

        // --- hooks for concrete minigames -----------------------------------------------------------

        /// <summary>Runs once, after the player is frozen and the hand rig is live.</summary>
        protected virtual void OnHandBegin() { }

        /// <summary>Per-frame logic. Runs after the look / footwork handlers.</summary>
        protected virtual void OnMinigameUpdate() { }

        /// <summary>After the animator/IK have posed the hand this frame (item pose locks, etc.).</summary>
        protected virtual void OnMinigameLateUpdate() { }

        /// <summary>Physics-step logic (guided-drop funnel, tilt-to-pour, draw force, ...).</summary>
        protected virtual void OnMinigameFixedUpdate() { }

        /// <summary>Return false while a quick RMB tap must NOT cancel (e.g. after the item is released).</summary>
        protected virtual bool AllowTapCancel() => true;

        /// <summary>
        /// Master gate for <see cref="HandleLook"/> - override to fully suppress RMB-look once a subclass
        /// phase no longer wants it (e.g. once the held item has been released and normal player controls
        /// have already taken back over, or while a drop is resolving/settling).
        /// </summary>
        protected virtual bool LookActive => true;

        /// <summary>Master gate for <see cref="HandleFootwork"/> - see <see cref="LookActive"/>.</summary>
        protected virtual bool FootworkActive => true;

        /// <summary>
        /// Suppress the RMB-drag vertical PITCH specifically (yaw still applies) - e.g. while a phase is
        /// already using vertical mouse movement to drive something else (swinging a lid open).
        /// </summary>
        protected virtual bool SuppressPitchLook => false;

        /// <summary>
        /// Whether re-closing the settings menu should re-free the cursor. False once a subclass phase has
        /// already handed normal control back to the player (so the cursor should stay locked/hidden as if
        /// in ordinary gameplay) until it explicitly frees the cursor again itself (e.g. a retry restart).
        /// </summary>
        protected virtual bool WantsFreeCursor => true;
    }
}
