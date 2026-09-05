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
///
/// The falling-item physics (no-bounce, straight-drop) and the "has it touched the rack yet" check
/// are the GuidedDrop / StationContactProbe capabilities in Assets/Scripts/Minigames/Capabilities -
/// shared with ChestDepositMinigame (P1). The freeze / RMB-look / footwork / settings-pause / hand rig
/// plumbing is HandMinigame, via ItemDepositMinigame (P2) - this class keeps only the notch-slot pick
/// and the tip-down pose. See ARCHITECTURE.md.
/// </summary>
public class SwordHangMinigame : ItemDepositMinigame
{
    public override void BeginDeposit(PickupItem heldItem, TaskDepositStation station) => BeginHang(heldItem, station);

    [Header("Sword grip while aiming")]
    [Tooltip("Local rotation of the sword on the hand bone at the start - set so the tip points at the ground.")]
    public Vector3 carryLocalEuler = new Vector3(180f, 0f, 0f);
    [Tooltip("Local position of the sword on the hand bone while aiming.")]
    public Vector3 carryLocalPos = new Vector3(0f, 0.05f, 0.05f);

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
    private bool released;
    private bool resolving;
    private bool awaitingRetry;
    private bool touchedRack;
    private float settleTimer;
    private GuidedDrop.Handle dropHandle;

    // --- HandMinigame gates: the sword hands full normal control back to the player the instant it's
    // released (see ReleaseSword), so its own RMB-look / footwork / cursor-refree must stop then too -
    // otherwise they'd run alongside the player's now-active normal controls.
    protected override bool LookActive => !released;
    protected override bool FootworkActive => !released;
    protected override bool WantsFreeCursor => !released;

    public void BeginHang(PickupItem heldItem, TaskDepositStation targetRack)
    {
        item = heldItem;
        rack = targetRack;

        Transform handBone = Hand != null ? Hand.HandBone : null;
        if (item == null || rack == null || cam == null || handBone == null) { CancelMinigame(); return; }

        // Re-enter the busy registry: on the very first call this is a harmless re-add (SetupMinigame
        // already added it); on a retry restart (after ReleaseSword's RestorePlayer left it - see
        // MinigameBase.LeaveActiveRegistry) this is what makes the player "busy" again while re-aiming.
        RejoinActiveRegistry();

        player.SetControlsLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // The player walked up to the rack themselves - leave them exactly where they are. On a retry
        // restart (picked the sword back up somewhere else) this re-centres the WASD leash there too.
        ReanchorFootwork();

        // Glue the sword to the hand BONE, tip-down.
        item.transform.SetParent(handBone, false);
        item.transform.localPosition = carryLocalPos;
        item.transform.localRotation = Quaternion.Euler(carryLocalEuler);
        SetItemPhysics(false);

        Hand.Begin();
    }

    protected override void OnMinigameUpdate()
    {
        if (item == null || rack == null) { FinishFail(); return; }

        if (!released)
        {
            UpdateAiming();
            if (MinigameInput.PrimaryDown) ReleaseSword();
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
        if (!touchedRack && StationContactProbe.Resting(item.transform, rack.transform)) touchedRack = true;

        // It can only hang once it has actually touched the rack AND is lined up with a free slot.
        // That may be true the instant it lands, or after it has settled against the notches.
        if (touchedRack && TryHangOnSlot("contact")) return;

        settleTimer -= Time.deltaTime;
        Rigidbody rb = item.GetComponent<Rigidbody>();
        bool stillMoving = rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.04f;
        if (settleTimer <= 0f && !stillMoving) ResolveLanding();
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
        dropHandle?.End();
        dropHandle = null;
        rack.DepositIntoSlot(item, slot, player); // parents + poses it in the notch
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
        dropHandle?.End();
        dropHandle = null;
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
        Hand.ReachToward(MouseWorld());
        player.hangReachRotWeight = 0f;
    }

    // Runs after the animator/IK have posed the hand: force the sword's orientation so the tip
    // always points straight down, only yawing with the player so it looks natural as they turn.
    protected override void OnMinigameLateUpdate()
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

        player.ClearHeldItem();

        // Let go of the sword right where the hand is - it falls under physics and, crucially,
        // becomes pick-up-able again (DropInPlace resets isHeld).
        item.DropInPlace();

        // The fall: X/Z position and all rotation frozen so it drops straight down and stays
        // tip-down; no bounce, gentle depenetration. (GuidedDrop capability.)
        Rigidbody rb = item.GetComponent<Rigidbody>();
        Collider col = item.GetComponent<Collider>();
        dropHandle = GuidedDrop.Begin(rb, col, new GuidedDrop.Settings
        {
            maxAngularVelocity = 2.5f,
            maxDepenetrationVelocity = 0.5f,
            freezeRotation = true,
            freezeHorizontalPosition = true,
            funnelSpeed = 0f,
            materialName = "SwordDrop"
        });

        // Don't let it bounce off the player standing right there.
        if (col != null && player.CharController != null)
            Physics.IgnoreCollision(col, player.CharController, true);

        // The player can move again while the sword falls - hand normal control straight back
        // (RestorePlayer is safe to call again when the minigame later actually ends).
        RestorePlayer();
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

    private void AbortToHand()
    {
        dropHandle?.End();
        dropHandle = null;
        if (item != null && player != null) item.AttachToHand(player.RightHandSocket);
        base.CancelMinigame(); // restores the player via HandMinigame.OnMinigameEnd
    }

    private void FinishFail()
    {
        dropHandle?.End();
        dropHandle = null;
        RestorePlayer();
        Destroy(gameObject);
    }

    public override void CancelMinigame()
    {
        AbortToHand();
    }

    void OnDestroy()
    {
        dropHandle?.End();
        dropHandle = null;
    }
}
