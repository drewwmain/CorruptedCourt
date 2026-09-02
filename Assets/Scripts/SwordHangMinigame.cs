using UnityEngine;

/// <summary>
/// Physical "hang the sword on the rack" deposit minigame.
///
/// The item is parented to the RIGHT HAND BONE (so it moves with the arm, including the reach IK)
/// and posed tip-down. The player is frozen and moves the mouse to reach the hand toward the notches.
///
/// LEFT CLICK releases the sword (physics on). After it settles, if it came to rest ON / between the
/// notches it snaps cleanly into that slot and the DepositItemStep completes. If it missed, the sword
/// is a loose pickup on the ground and the minigame stays open - walking over and picking the sword
/// back up (E) drops the player straight back into the aiming phase to try again.
/// RIGHT CLICK (before releasing) cancels and returns the sword to a normal grip.
///
/// Launched by TaskDepositStation when its "Deposit Minigame Prefab" is set. Prefab = an empty
/// GameObject with this component. Extends MinigameBase so success advances the DepositItemStep.
/// </summary>
public class SwordHangMinigame : ItemDepositMinigame
{
    public override void BeginDeposit(PickupItem heldItem, TaskDepositStation station) => BeginHang(heldItem, station);

    [Header("Sword grip while aiming")]
    [Tooltip("Local rotation of the sword on the hand bone at the start - set so the tip points at the ground.")]
    public Vector3 carryLocalEuler = new Vector3(180f, 0f, 0f);
    [Tooltip("Local position of the sword on the hand bone while aiming.")]
    public Vector3 carryLocalPos = new Vector3(0f, 0.05f, 0.05f);

    [Header("Reach")]
    [Tooltip("Distance in front of the camera the hand reaches to follow the mouse.")]
    public float reachDistance = 1.3f;

    [Header("Aiming footwork")]
    [Tooltip("While aiming, WASD lets the player shuffle this many metres from where they were placed so they can line up with the notch set they want. 0 = locked in place.")]
    public float walkRadius = 1.25f;
    [Tooltip("Hold RIGHT-CLICK and move the mouse to look around (pan horizontally, tilt vertically). Higher = faster.")]
    public float rmbLookSensitivity = 3f;
    [Tooltip("A right-click held shorter than this, with no mouse movement, cancels the minigame instead of panning.")]
    public float rmbTapCancelTime = 0.2f;

    [Header("Landing check")]
    [Tooltip("Seconds to wait for a released sword to settle before judging the outcome.")]
    public float settleTime = 1.2f;
    [Tooltip("Once the sword has PHYSICALLY touched the rack, it hangs on the nearest free DropSlot within this distance. Place each DropSlot where the sword's origin should rest when hung.")]
    public float catchRadius = 0.45f;
    [Tooltip("Log the landing numbers to the Console so you can tune catchRadius / DropSlot placement.")]
    public bool debugLanding = true;
    [Tooltip("After a miss the minigame waits for the player to pick the sword back up. If they walk this far from the dropped sword instead, the minigame gives up (they can still retry via E on the rack).")]
    public float abandonDistance = 8f;

    private PickupItem item;
    private TaskDepositStation rack;
    private Camera cam;
    private Transform hand;
    private bool released;
    private bool resolving;
    private bool awaitingRetry;
    private bool touchedRack;
    private float settleTimer;
    private RigidbodyConstraints savedConstraints;
    private float savedMaxDepen;
    private float savedMaxAngVel;
    private PhysicsMaterial savedMaterial;
    private PhysicsMaterial dropMaterial;
    private bool guidedDropActive;
    private bool wasMenuPaused;
    private Vector3 walkAnchor;
    private float rmbDownTime;
    private bool rmbDragged;

    public void BeginHang(PickupItem heldItem, TaskDepositStation targetRack)
    {
        item = heldItem;
        rack = targetRack;
        cam = (player != null && player.PlayerCamera != null) ? player.PlayerCamera.GetComponent<Camera>() : Camera.main;
        hand = player != null ? (player.RightHandBone != null ? player.RightHandBone : player.RightHandSocket) : null;

        if (item == null || rack == null || cam == null || hand == null) { CancelMinigame(); return; }

        if (player != null) player.SetControlsLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // The player walked up to the rack themselves - leave them exactly where they are.
        // They can shuffle with WASD (walkRadius) to line up the notch set they want.
        walkAnchor = player.transform.position;

        // Glue the sword to the hand BONE, tip-down.
        item.transform.SetParent(hand, false);
        item.transform.localPosition = carryLocalPos;
        item.transform.localRotation = Quaternion.Euler(carryLocalEuler);
        SetItemPhysics(false);

        player.hangReachActive = true;
        player.hangReachRotWeight = 0f;
    }

    void Update()
    {
        if (item == null || player == null || cam == null || rack == null) { FinishFail(); return; }

        // Esc / settings menu open: freeze the whole minigame so the mouse stops driving the arm
        // and the settle timer doesn't tick down behind the menu.
        if (UIManager.Instance != null && UIManager.Instance.IsSettingsOpen) { wasMenuPaused = true; return; }

        // Just came back from the settings menu during the aiming phase - the menu re-locks the
        // cursor on close, so put it back the way the minigame needs it.
        if (wasMenuPaused)
        {
            wasMenuPaused = false;
            if (!released)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        if (!released)
        {
            // RIGHT-CLICK: hold + move the mouse to look around (pan horizontally, tilt vertically).
            // A quick tap with no mouse movement still cancels the minigame (sword back to a grip).
            if (Input.GetMouseButtonDown(1)) { rmbDownTime = Time.time; rmbDragged = false; }
            if (Input.GetMouseButton(1))
            {
                float dx = Input.GetAxis("Mouse X");
                float dy = Input.GetAxis("Mouse Y");
                if (Mathf.Abs(dx) > 0.001f)
                {
                    rmbDragged = true;
                    player.MinigameLookYaw(dx * rmbLookSensitivity);
                }
                if (Mathf.Abs(dy) > 0.001f)
                {
                    rmbDragged = true;
                    player.MinigameLookPitch(dy * rmbLookSensitivity);
                }
            }
            if (Input.GetMouseButtonUp(1) && !rmbDragged && Time.time - rmbDownTime <= rmbTapCancelTime)
            {
                AbortToHand();
                return;
            }

            // Let the player shuffle their feet (WASD) to line up with the notch set they want.
            // The mouse is busy aiming the sword, so footwork is on the keyboard only.
            if (walkRadius > 0f)
            {
                Vector2 step = new Vector2(
                    (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                    (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
                if (step.sqrMagnitude > 0f) player.MinigameWalk(step, walkAnchor, walkRadius);
            }

            UpdateAiming();

            if (Input.GetMouseButtonDown(0)) ReleaseSword();
            return;
        }

        if (resolving) return;

        // Player picked the sword back up while the minigame is still open - straight back to aiming.
        if (player.GetHeldItem() == item)
        {
            RestartAiming();
            return;
        }

        if (awaitingRetry)
        {
            // Missed. The sword is loose; wait for the pickup above. If the player abandons it and
            // walks off, close the minigame (they can still retry with E on the rack).
            if (Vector3.Distance(player.transform.position, item.transform.position) > abandonDistance)
                FinishFail();
            return;
        }

        // Record the moment the falling sword physically touches the rack.
        if (!touchedRack && RaycastTouchingRack()) touchedRack = true;

        // It can only hang once it has actually touched the rack AND is lined up with a free slot.
        // That may be true the instant it lands, or after it has settled against the notches.
        if (touchedRack && TryHangOnSlot("contact")) return;

        settleTimer -= Time.deltaTime;
        Rigidbody rb = item.GetComponent<Rigidbody>();
        bool stillMoving = rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.04f;
        if (settleTimer <= 0f && !stillMoving) ResolveLanding();
    }

    // True when the sword's hilt (pivot) is physically resting on the rack - a short ray straight
    // down finds a rack collider within a few centimetres. Works with non-convex mesh colliders.
    // (A sword standing on its tip has its pivot a full blade-length up, so it does NOT count.)
    private bool RaycastTouchingRack()
    {
        if (!Physics.Raycast(item.transform.position + Vector3.up * 0.03f, Vector3.down,
                             out RaycastHit h, 0.12f, ~0, QueryTriggerInteraction.Ignore)) return false;
        return h.collider.GetComponentInParent<TaskDepositStation>() == rack
               || h.collider.transform.IsChildOf(rack.transform);
    }

    // If the sword's pivot is within catchRadius of a free DropSlot, hang it there. Returns true.
    private bool TryHangOnSlot(string via)
    {
        int slot = -1;
        float best = float.MaxValue;
        for (int i = 0; i < rack.SlotCount; i++)
        {
            if (!rack.IsSlotFree(i)) continue;
            Transform st = rack.GetDropSlot(i);
            if (st == null) continue;
            float d = Vector3.Distance(item.transform.position, st.position);
            if (d < best) { best = d; slot = i; }
        }
        if (slot < 0 || best > catchRadius) return false;

        resolving = true;
        ConfigureGuidedDrop(false);
        rack.DepositIntoSlot(item, slot); // parents + poses it in the notch
        if (debugLanding)
            Debug.Log($"[SwordHang] hung on slot {slot} via {via} (dist {best:F2} <= {catchRadius}).");
        item = null;
        CompleteMinigame();               // advances the DepositItemStep
        return true;
    }

    // The player grabbed the sword again mid-minigame - restart the aiming phase with the same
    // rack and task rather than ending.
    private void RestartAiming()
    {
        ConfigureGuidedDrop(false);
        released = false;
        resolving = false;
        awaitingRetry = false;
        touchedRack = false;
        BeginHang(item, rack);
    }

    private void UpdateAiming()
    {
        // The hand reaches to exactly where the mouse points - no assist. The player has to
        // physically line the sword up over a notch gap and be close enough to reach it.
        Vector3 mp = Input.mousePosition;
        mp.z = reachDistance;
        player.hangReachPos = cam.ScreenToWorldPoint(mp);
        player.hangReachRotWeight = 0f;
    }

    // Runs after the animator/IK have posed the hand: force the sword's orientation so the tip
    // always points straight down, only yawing with the player so it looks natural as they turn.
    void LateUpdate()
    {
        if (!released && item != null && player != null)
            item.transform.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f)
                                      * Quaternion.Euler(carryLocalEuler);
    }

    private void ReleaseSword()
    {
        released = true;
        awaitingRetry = false;
        touchedRack = false;
        settleTimer = settleTime;

        player.hangReachActive = false;
        player.ClearHeldItem();

        // Let go of the sword right where the hand is - it falls under physics and, crucially,
        // becomes pick-up-able again (DropInPlace resets isHeld).
        item.DropInPlace();

        // The fall: straight down (X/Z frozen) but rotation LEFT FREE (spin-capped) so contact with
        // the rack physically tips it. No bounce, gentle depenetration.
        ConfigureGuidedDrop(true);

        // Don't let it bounce off the player standing right there.
        Collider col = item.GetComponent<Collider>();
        if (col != null && player.CharController != null)
            Physics.IgnoreCollision(col, player.CharController, true);

        // The player can move again while the sword falls.
        RestorePlayerControl();
    }

    private void ResolveLanding()
    {
        // Settled. It only counts if it PHYSICALLY touched the rack and rests near a free slot.
        if (touchedRack && TryHangOnSlot("settle")) return;

        if (debugLanding)
            Debug.Log($"[SwordHang] MISS - touchedRack={touchedRack}. Leaving the sword loose to retry.");
        awaitingRetry = true;
    }

    // --- helpers ---

    // Toggle a "guided drop" on the sword's rigidbody: freeze X/Z position AND rotation so it falls
    // straight down and stays tip-down, with no bounce and gentle depenetration - so a contact with
    // the rack makes it rest, not fly off. Restores the originals when turned off.
    private void ConfigureGuidedDrop(bool on)
    {
        if (item == null) return;
        Rigidbody rb = item.GetComponent<Rigidbody>();
        Collider col = item.GetComponent<Collider>();

        if (on)
        {
            if (guidedDropActive) return;
            guidedDropActive = true;

            if (dropMaterial == null)
            {
                dropMaterial = new PhysicsMaterial("SwordDrop")
                {
                    bounciness = 0f,
                    dynamicFriction = 0.9f,
                    staticFriction = 0.9f,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    frictionCombine = PhysicsMaterialCombine.Maximum
                };
            }

            if (rb != null)
            {
                savedConstraints = rb.constraints;
                savedMaxDepen = rb.maxDepenetrationVelocity;
                savedMaxAngVel = rb.maxAngularVelocity;
                rb.constraints = RigidbodyConstraints.FreezePositionX
                                 | RigidbodyConstraints.FreezePositionZ
                                 | RigidbodyConstraints.FreezeRotation;
                rb.maxDepenetrationVelocity = 0.5f;
                rb.maxAngularVelocity = 2.5f;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            if (col != null)
            {
                savedMaterial = col.sharedMaterial;
                col.sharedMaterial = dropMaterial;
            }
        }
        else
        {
            if (!guidedDropActive) return;
            guidedDropActive = false;

            if (rb != null)
            {
                rb.constraints = savedConstraints;
                rb.maxDepenetrationVelocity = savedMaxDepen;
                rb.maxAngularVelocity = savedMaxAngVel;
            }
            if (col != null) col.sharedMaterial = savedMaterial;
        }
    }

    private void SetItemPhysics(bool loose)
    {
        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = !loose;
            rb.useGravity = loose;
            if (loose) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
        Collider col = item.GetComponent<Collider>();
        if (col != null) col.enabled = loose;
    }

    private void RestorePlayerControl()
    {
        if (player != null)
        {
            player.hangReachActive = false;
            player.hangReachRotWeight = 0f;
            player.SetControlsLocked(false);
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void AbortToHand()
    {
        ConfigureGuidedDrop(false);
        if (item != null && player != null) item.AttachToHand(player.RightHandSocket);
        RestorePlayerControl();
        base.CancelMinigame();
    }

    private void FinishFail()
    {
        ConfigureGuidedDrop(false);
        RestorePlayerControl();
        Destroy(gameObject);
    }

    public override void CompleteMinigame()
    {
        RestorePlayerControl();
        base.CompleteMinigame();
    }

    public override void CancelMinigame()
    {
        AbortToHand();
    }

    void OnDestroy()
    {
        if (dropMaterial != null) Destroy(dropMaterial);
    }
}
