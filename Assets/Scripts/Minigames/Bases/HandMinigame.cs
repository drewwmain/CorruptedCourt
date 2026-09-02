using UnityEngine;

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
///  - <see cref="MouseWorld"/> - the mouse projected in front of the camera.
///
/// CONVENTION: subclasses override <see cref="OnMinigameUpdate"/> /
/// <see cref="OnMinigameLateUpdate"/> / <see cref="OnMinigameFixedUpdate"/>. They must NOT declare
/// their own Update()/LateUpdate()/FixedUpdate() - that would hide the base loop.
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

    protected Camera cam;
    protected MinigameHandRig Hand { get; private set; }

    private Vector3 walkAnchor;
    private float rmbDownTime;
    private bool rmbDragged;
    private bool wasMenuPaused;

    // --- lifecycle ---------------------------------------------------------------------------

    protected override void OnMinigameBegin()
    {
        cam = (player != null && player.PlayerCamera != null)
            ? player.PlayerCamera.GetComponent<Camera>()
            : Camera.main;

        if (player == null || cam == null)
        {
            Debug.LogWarning($"[{GetType().Name}] missing player or camera - cancelling.");
            CancelMinigame();
            return;
        }

        Hand = new MinigameHandRig(player, cam);
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

    /// <summary>Un-freeze the player, re-lock the cursor, release the hand rig.</summary>
    protected void RestorePlayer()
    {
        if (Hand != null) Hand.End();
        if (player != null) player.SetControlsLocked(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // --- Unity loop -> template methods ------------------------------------------------------

    private void Update()
    {
        if (player == null || cam == null) return;

        // Settings / pause menu: freeze the whole minigame so the mouse stops driving the arm.
        if (MinigameInput.Suppressed) { wasMenuPaused = true; return; }
        if (wasMenuPaused)
        {
            wasMenuPaused = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        HandleLook();
        HandleFootwork();
        OnMinigameUpdate();
    }

    private void LateUpdate()
    {
        if (player == null) return;
        OnMinigameLateUpdate();
    }

    private void FixedUpdate()
    {
        if (player == null) return;
        OnMinigameFixedUpdate();
    }

    // --- shared handlers -------------------------------------------------------------------------

    /// <summary>Hold RMB to pan the body (and optionally pitch); a quick no-drag tap cancels.</summary>
    protected virtual void HandleLook()
    {
        if (MinigameInput.SecondaryDown) { rmbDownTime = Time.time; rmbDragged = false; }

        if (MinigameInput.SecondaryHeld)
        {
            Vector2 d = MinigameInput.MouseDelta;
            if (Mathf.Abs(d.x) > 0.001f)
            {
                rmbDragged = true;
                player.MinigameLookYaw(d.x * rmbLookSensitivity);
            }
            if (rmbAllowPitch && Mathf.Abs(d.y) > 0.001f)
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
        if (walkRadius <= 0f) return;
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
}
