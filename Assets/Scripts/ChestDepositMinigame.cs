using UnityEngine;

/// <summary>
/// Chest deposit minigame (dowry_deposit): open the lid, then drop the held item inside.
///
/// Flow:
///  1. The held item shifts to the LEFT hand; the right hand is freed.
///  2. The right hand reaches to the chest LID (mouse aims). LEFT-CLICK near it to grab.
///  3. Move the mouse UP to swing the lid open. Once it's open past the threshold:
///  4. The item shifts back to the RIGHT hand. Aim it at the chest opening (a DropSlot) and
///     LEFT-CLICK to let go. Land it in the opening to win; a miss leaves it loose to pick up
///     and retry (the lid stays open).
///
/// Hold RIGHT-CLICK + move the mouse to look around; a quick right-click tap cancels (before the
/// item is released). Launched by TaskDepositStation when its Deposit Minigame Prefab carries this
/// component. Extends ItemDepositMinigame so success advances the DepositItemStep.
/// </summary>
public class ChestDepositMinigame : ItemDepositMinigame
{
    [Header("Lid")]
    [Tooltip("Leave EMPTY - resolved at runtime by name: a child of the station named '...Hinge' (preferred - the pivot to rotate), else the first child with 'lid' in its name.")]
    public Transform lid;
    [Tooltip("The hinge's LOCAL rotation (Euler) when the lid is fully OPEN. To find it: select LidHinge in the Scene, rotate it until the lid stands open the way you want, and copy its Rotation values here. The CLOSED pose is captured automatically at the start.")]
    public Vector3 lidOpenLocalEuler = new Vector3(-95f, 0f, 0f);
    [Range(0.5f, 1f)]
    [Tooltip("Fraction of the way open the lid must reach to count as open.")]
    public float lidOpenThreshold = 0.9f;
    [Tooltip("How much 'open progress' (0..1) each unit of upward mouse movement adds.")]
    public float lidOpenSensitivity = 0.2f;
    [Tooltip("Seconds for the lid to swing shut on its own once the item is deposited. 0 = snap shut instantly.")]
    public float lidCloseTime = 0.4f;
    [Tooltip("How close the right hand must get to the lid grab point to grab it.")]
    public float lidGrabDistance = 0.5f;
    [Tooltip("Leave EMPTY - resolved at runtime: a child of the station with 'grab' in its name (put it at the lid's front edge so the hand follows it up). Falls back to the lid/hinge origin.")]
    public Transform lidGrabPoint;

    [Header("Reach / look / footwork")]
    [Tooltip("Distance in front of the camera the hand reaches to follow the mouse.")]
    public float reachDistance = 1.1f;
    [Tooltip("Hold RIGHT-CLICK + move the mouse to look around while playing.")]
    public float rmbLookSensitivity = 3f;
    [Tooltip("A right-click held shorter than this, with no mouse movement, cancels the minigame.")]
    public float rmbTapCancelTime = 0.2f;
    [Tooltip("While your controls are locked for the minigame, WASD lets you shuffle this many metres from where you started (e.g. step in closer to reach into the chest). 0 = locked in place.")]
    public float walkRadius = 1.5f;
    [Tooltip("Shrinks the player's collision radius to this while the minigame runs, so they can stand right against the chest and reach the opening. The chest stays solid - they still can't clip through it. 0 = leave the radius alone.")]
    public float minigamePlayerRadius = 0.12f;

    [Header("Item in hand")]
    public Vector3 carryLocalPos = Vector3.zero;
    public Vector3 carryLocalEuler = Vector3.zero;

    [Header("Drop into chest")]
    [Tooltip("Seconds to wait for a released item to settle before judging the outcome.")]
    public float settleTime = 1.2f;
    [Tooltip("Once the item has PHYSICALLY touched the chest, it registers on the nearest free DropSlot within this distance.")]
    public float catchRadius = 0.4f;
    [Tooltip("After you let go, the item is steered sideways onto the target slot's column as it falls, so you don't have to release dead-centre over the chest. Metres/sec of horizontal correction. 0 = drop straight down from where you released.")]
    public float dropFunnelSpeed = 3f;

    [Header("Debug")]
    public bool debugMinigame = true;

    private enum Phase { ReachLid, OpenLid, AimItem, ClosingLid }

    private PickupItem item;
    private TaskDepositStation chest;
    private Camera cam;
    private Transform rightHand;

    private Phase phase;
    private float lidOpen01;
    private Quaternion lidClosedLocalRot;
    private float lidCloseFrom;
    private float lidCloseTimer;

    private bool itemReleased;
    private bool resolving;
    private bool awaitingRetry;
    private bool touchedChest;
    private float settleTimer;
    private Transform dropTargetSlot; // the slot the released item is being funnelled toward

    private float rmbDownTime;
    private bool rmbDragged;
    private bool wasMenuPaused;
    private Vector3 walkAnchor;

    private RigidbodyConstraints savedConstraints;
    private float savedMaxDepen;
    private float savedMaxAngVel;
    private PhysicsMaterial savedMaterial;
    private PhysicsMaterial dropMaterial;
    private bool guidedDropActive;
    private Collider[] passableChestColliders;
    private float savedPlayerRadius = -1f;

    public override void BeginDeposit(PickupItem heldItem, TaskDepositStation station)
    {
        item = heldItem;
        chest = station;
        cam = (player != null && player.PlayerCamera != null) ? player.PlayerCamera.GetComponent<Camera>() : Camera.main;
        rightHand = player != null ? (player.RightHandBone != null ? player.RightHandBone : player.RightHandSocket) : null;

        if (item == null || chest == null || cam == null || rightHand == null) { CancelMinigame(); return; }

        // Prefab assets can't hold references to scene objects, so resolve the lid / grab point by
        // name at runtime. A child named "...Hinge" wins (that's the pivot to rotate); otherwise the
        // first child with "lid" in its name.
        if (lid == null) lid = FindChild(chest.transform, "hinge") ?? FindChild(chest.transform, "lid");
        if (lid == null) { Debug.LogWarning("[ChestDeposit] No lid/hinge assigned or found under the station - cannot run."); CancelMinigame(); return; }
        if (lidGrabPoint == null) lidGrabPoint = FindChild(chest.transform, "grab");
        lidClosedLocalRot = lid.localRotation;

        player.SetControlsLocked(true);
        walkAnchor = player.transform.position;
        // Let the player shuffle right up against the chest to reach over it. The chest's solid MESH
        // stays collidable (no clipping through it); only extra Box/Sphere/Capsule colliders - e.g. an
        // oversized interaction volume - are made passable so they can't hold the player back.
        SetChestPassable(true);
        // Slim the collision capsule so they can nose right up to the chest wall.
        if (minigamePlayerRadius > 0f && player.CharController != null)
        {
            savedPlayerRadius = player.CharController.radius;
            player.CharController.radius = Mathf.Min(minigamePlayerRadius, savedPlayerRadius);
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnterReachLidPhase();
    }

    private void SetChestPassable(bool passable)
    {
        if (passable) passableChestColliders = chest.GetComponentsInChildren<Collider>();
        if (passableChestColliders == null) return;

        Collider playerCC = player != null ? player.CharController : null;
        Collider itemCol = item != null ? item.GetComponent<Collider>() : null;

        bool anySolidBlocker = false;
        foreach (Collider c in passableChestColliders)
        {
            if (c == null || c.isTrigger) continue;
            bool isMesh = c is MeshCollider;
            if (isMesh) anySolidBlocker = true;

            // Player: keep the solid MESH collidable (can't clip through the chest body); only clear
            // away extra Box/Sphere/Capsule colliders that would hold them back.
            if (playerCC != null && !isMesh) Physics.IgnoreCollision(playerCC, c, passable);
            // Falling item: while the funnel is on, pass through ALL of the chest's colliders so it
            // can't snag on the rim or an inner wall - the drop is steered straight onto the target
            // slot instead, and the deposit still registers off RaycastTouchingChest() / the slot
            // arrival. With the funnel off (dropFunnelSpeed 0) leave the chest solid to the item.
            if (itemCol != null && (!passable || dropFunnelSpeed > 0f))
                Physics.IgnoreCollision(itemCol, c, passable);
        }

        if (passable && !anySolidBlocker)
            Debug.LogWarning("[ChestDeposit] The chest has no solid (non-trigger) MeshCollider - the " +
                             "player will clip straight through it. Uncheck 'Is Trigger' on the chest's Mesh Collider.");

        if (!passable) passableChestColliders = null;
    }

    private static Transform FindChild(Transform root, string keyword)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>())
            if (t != root && t.name.ToLower().Contains(keyword)) return t;
        return null;
    }

    // --- phases ---

    private void EnterReachLidPhase()
    {
        phase = Phase.ReachLid;
        MoveItemToHand(player.LeftHandSocket, false); // stow it in the off-hand
        player.hangReachActive = true;                // reach the empty right hand toward the mouse
        player.hangReachRotWeight = 0f;
    }

    private void EnterAimItemPhase()
    {
        phase = Phase.AimItem;
        itemReleased = false;
        awaitingRetry = false;
        MoveItemToHand(rightHand, true);             // back to the right hand to aim it
        player.hangReachActive = true;
        player.hangReachRotWeight = 0f;
    }

    private void MoveItemToHand(Transform parent, bool aimPose)
    {
        if (item == null || parent == null) return;
        item.transform.SetParent(parent, false);
        item.transform.localPosition = aimPose ? carryLocalPos : Vector3.zero;
        item.transform.localRotation = Quaternion.Euler(aimPose ? carryLocalEuler : Vector3.zero);

        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        Collider c = item.GetComponent<Collider>();
        if (c != null) c.enabled = false;
    }

    void Update()
    {
        if (item == null || player == null || cam == null || chest == null || lid == null) { FinishFail(); return; }

        if (UIManager.Instance != null && UIManager.Instance.IsSettingsOpen) { wasMenuPaused = true; return; }
        if (wasMenuPaused)
        {
            wasMenuPaused = false;
            if (!(phase == Phase.AimItem && itemReleased))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        HandleLook();
        HandleFootwork();

        switch (phase)
        {
            case Phase.ReachLid:   UpdateReachLid();   break;
            case Phase.OpenLid:    UpdateOpenLid();    break;
            case Phase.AimItem:    UpdateAimItem();    break;
            case Phase.ClosingLid: UpdateClosingLid(); break;
        }
    }

    // The item is in the chest - swing the lid shut on its own, then finish.
    private void UpdateClosingLid()
    {
        lidCloseTimer += Time.deltaTime;
        float t = lidCloseTime > 0f ? Mathf.Clamp01(lidCloseTimer / lidCloseTime) : 1f;
        lidOpen01 = Mathf.Lerp(lidCloseFrom, 0f, t);
        lid.localRotation = Quaternion.Slerp(lidClosedLocalRot, Quaternion.Euler(lidOpenLocalEuler), lidOpen01);

        if (t >= 1f)
        {
            lid.localRotation = lidClosedLocalRot;
            SetChestPassable(false); // restore collisions while the item ref is still valid
            item = null;
            CompleteMinigame();
        }
    }

    // WASD shuffle while controls are locked, leashed to walkRadius of the start spot. Skipped once
    // the item is released (normal controls are back for the throw).
    private void HandleFootwork()
    {
        if (walkRadius <= 0f || resolving) return;
        if (phase == Phase.AimItem && itemReleased) return;

        Vector2 step = new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
        if (step.sqrMagnitude > 0f) player.MinigameWalk(step, walkAnchor, walkRadius);
    }

    // Hold RMB to look around (yaw always, pitch except while swinging the lid). Quick tap cancels
    // while the item is still in hand.
    private void HandleLook()
    {
        if (resolving) return;

        if (Input.GetMouseButtonDown(1)) { rmbDownTime = Time.time; rmbDragged = false; }
        if (Input.GetMouseButton(1))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(dx) > 0.001f) { rmbDragged = true; player.MinigameLookYaw(dx * rmbLookSensitivity); }
            if (Mathf.Abs(dy) > 0.001f && phase != Phase.OpenLid)
            {
                rmbDragged = true;
                player.MinigameLookPitch(dy * rmbLookSensitivity);
            }
        }
        if (Input.GetMouseButtonUp(1) && !rmbDragged && Time.time - rmbDownTime <= rmbTapCancelTime && !itemReleased)
        {
            CancelMinigame();
        }
    }

    private Vector3 MouseWorld()
    {
        Vector3 mp = Input.mousePosition;
        mp.z = reachDistance;
        return cam.ScreenToWorldPoint(mp);
    }

    private Vector3 LidGrabWorld() => lidGrabPoint != null ? lidGrabPoint.position : lid.position;

    private void UpdateReachLid()
    {
        player.hangReachPos = MouseWorld();

        if (Input.GetMouseButtonDown(0) && Vector3.Distance(rightHand.position, LidGrabWorld()) <= lidGrabDistance)
        {
            phase = Phase.OpenLid;
            if (debugMinigame) Debug.Log("[ChestDeposit] grabbed the lid - move the mouse UP to open");
        }
    }

    private void UpdateOpenLid()
    {
        lidOpen01 = Mathf.Clamp01(lidOpen01 + Input.GetAxis("Mouse Y") * lidOpenSensitivity);
        // Blend the hinge from its captured closed pose to the authored open pose.
        lid.localRotation = Quaternion.Slerp(lidClosedLocalRot, Quaternion.Euler(lidOpenLocalEuler), lidOpen01);

        // Keep the hand on the lid grab point as it swings.
        player.hangReachPos = LidGrabWorld();

        if (lidOpen01 >= lidOpenThreshold)
        {
            if (debugMinigame) Debug.Log("[ChestDeposit] lid open - item back to the right hand, aim it into the chest");
            EnterAimItemPhase();
        }
    }

    private void UpdateAimItem()
    {
        if (!itemReleased)
        {
            player.hangReachPos = MouseWorld();
            if (Input.GetMouseButtonDown(0)) ReleaseItem();
            return;
        }

        if (resolving) return;

        // Picked the item back up while the minigame is still open - aim again (lid stays open).
        if (player.GetHeldItem() == item) { RestartAim(); return; }

        if (awaitingRetry)
        {
            if (Vector3.Distance(player.transform.position, item.transform.position) > 8f) FinishFail();
            return;
        }

        // Record the moment the falling item physically touches the chest.
        if (!touchedChest && RaycastTouchingChest()) touchedChest = true;

        // Register once it's lined up on a free slot: either after physically touching the chest, or -
        // when the funnel is steering it down a slot column - as soon as it drops to the slot's lip.
        bool linedUp = touchedChest || (dropTargetSlot != null && dropFunnelSpeed > 0f);
        if (linedUp && TrySeatInChest("contact")) return;

        settleTimer -= Time.deltaTime;
        Rigidbody rb = item.GetComponent<Rigidbody>();
        bool moving = rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.04f;
        if (settleTimer <= 0f && !moving) ResolveDrop();
    }

    // True when the item's pivot is physically resting on the chest - a short ray straight down finds
    // a chest collider within a few centimetres.
    private bool RaycastTouchingChest()
    {
        if (!Physics.Raycast(item.transform.position + Vector3.up * 0.03f, Vector3.down,
                             out RaycastHit h, 0.12f, ~0, QueryTriggerInteraction.Ignore)) return false;
        return h.collider.GetComponentInParent<TaskDepositStation>() == chest
               || h.collider.transform.IsChildOf(chest.transform);
    }

    private void ResolveDrop()
    {
        if (touchedChest && TrySeatInChest("settle")) return;

        if (debugMinigame)
            Debug.Log($"[ChestDeposit] MISS - touchedChest={touchedChest}. Leaving the item loose to retry.");
        awaitingRetry = true;
    }

    // Seats the item once it has been funnelled onto a free DropSlot's column and dropped to (or past)
    // the slot's lip: horizontal distance within catchRadius, and no more than catchRadius ABOVE it
    // (any depth below counts - a fast frame can carry the pivot past the slot). Returns true on seat.
    private bool TrySeatInChest(string via)
    {
        int slot = -1;
        float best = float.MaxValue;
        for (int i = 0; i < chest.SlotCount; i++)
        {
            if (!chest.IsSlotFree(i)) continue;
            Transform st = chest.GetDropSlot(i);
            if (st == null) continue;
            Vector3 d = item.transform.position - st.position;
            float horiz = new Vector2(d.x, d.z).magnitude;
            if (horiz > catchRadius || d.y > catchRadius) continue;
            float score = horiz + Mathf.Max(0f, d.y);
            if (score < best) { best = score; slot = i; }
        }
        if (slot < 0) return false;

        resolving = true;
        ConfigureGuidedDrop(false);
        chest.DepositIntoSlot(item, slot);
        if (debugMinigame) Debug.Log($"[ChestDeposit] seated in slot {slot} via {via} (dist {best:F2}). WIN");

        // Auto-close the lid, then finish. (item is kept referenced so Update keeps ticking; it's
        // nulled at the end of UpdateClosingLid.)
        lidCloseFrom = lidOpen01;
        lidCloseTimer = 0f;
        phase = Phase.ClosingLid;
        return true;
    }

    private void ReleaseItem()
    {
        itemReleased = true;
        awaitingRetry = false;
        touchedChest = false;
        settleTimer = settleTime;
        dropTargetSlot = NearestFreeSlot(); // funnel the fall toward whichever slot is nearest the release point

        player.hangReachActive = false;
        player.ClearHeldItem();
        item.DropInPlace();
        ConfigureGuidedDrop(true);

        // The item's collider was just re-enabled by DropInPlace, which can drop the ignore pairs set
        // while it was disabled - re-assert pass-through with the chest and the player here.
        Collider c = item.GetComponent<Collider>();
        if (c != null)
        {
            if (player.CharController != null) Physics.IgnoreCollision(c, player.CharController, true);
            if (dropFunnelSpeed > 0f)
                foreach (Collider cc in chest.GetComponentsInChildren<Collider>())
                    if (cc != null && !cc.isTrigger) Physics.IgnoreCollision(c, cc, true);
        }

        RestorePlayerControl();
    }

    // Nearest currently-free DropSlot to the item's present position (its release point).
    private Transform NearestFreeSlot()
    {
        Transform best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < chest.SlotCount; i++)
        {
            if (!chest.IsSlotFree(i)) continue;
            Transform st = chest.GetDropSlot(i);
            if (st == null) continue;
            float d = Vector3.Distance(item.transform.position, st.position);
            if (d < bestD) { bestD = d; best = st; }
        }
        return best;
    }

    // Steers the released item's horizontal position onto the target slot's column while gravity does
    // the falling, so releasing anywhere over the open chest still funnels the item down to the slot.
    void FixedUpdate()
    {
        if (phase != Phase.AimItem || !itemReleased || resolving || awaitingRetry) return;
        if (item == null || dropTargetSlot == null || dropFunnelSpeed <= 0f) return;

        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;

        Vector3 pos = rb.position;
        Vector3 toColumn = new Vector3(dropTargetSlot.position.x - pos.x, 0f, dropTargetSlot.position.z - pos.z);
        Vector3 wantHoriz = Vector3.ClampMagnitude(toColumn / Time.fixedDeltaTime, dropFunnelSpeed);

        Vector3 v = rb.linearVelocity;
        v.x = wantHoriz.x;
        v.z = wantHoriz.z;
        rb.linearVelocity = v;
    }

    private void RestartAim()
    {
        ConfigureGuidedDrop(false);
        itemReleased = false;
        resolving = false;
        awaitingRetry = false;
        touchedChest = false;
        dropTargetSlot = null;
        player.SetControlsLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        MoveItemToHand(rightHand, true);
        player.hangReachActive = true;
        player.hangReachRotWeight = 0f;
    }

    // --- guided drop: fall straight down, no spin, gentle depenetration ---
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
                dropMaterial = new PhysicsMaterial("ChestDrop")
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
                // Rotation is locked (no tumbling), but X/Z stay free so FixedUpdate can steer the
                // item horizontally onto the target slot as it falls.
                rb.constraints = savedConstraints | RigidbodyConstraints.FreezeRotation;
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
        ConfigureGuidedDrop(false);
        if (lid != null) lid.localRotation = lidClosedLocalRot;
        if (!itemReleased && item != null && player != null)
            item.AttachToHand(player.RightHandSocket); // give the item back as a normal held item
        RestorePlayerControl();
        base.CancelMinigame();
    }

    void OnDestroy()
    {
        SetChestPassable(false); // re-enable player <-> chest collision
        if (savedPlayerRadius > 0f && player != null && player.CharController != null)
            player.CharController.radius = savedPlayerRadius;
        if (dropMaterial != null) Destroy(dropMaterial);
    }
}
