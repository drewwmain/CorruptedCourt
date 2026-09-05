using UnityEngine;
using UnityEngine.InputSystem;

// 1. Define the roles globally so any script can use them
public enum PlayerRole
{
    None,
    King,
    Kingsguard,
    Court,
    Corrupted
}

public enum MinigameTargetType
{
    Station,
    Item,
    Player,
    None
}

// The root player component: component references, Awake wiring, input callbacks routing to the
// right component, and orchestration (Update/LateUpdate order, minigame launch/finish/cancel) that
// doesn't cleanly belong to any single sibling. Business logic lives on the sibling components below -
// see PlayerMotor, PlayerLook, PlayerInventory, PlayerIKRig, PlayerInteractor, PlayerTaskBook, and
// PlayerVitals (Assets/Scripts/Player/).
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerController : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("The player THIS machine's user controls. Exactly one PlayerController in the scene should have this checked. UNCHECK it on dummy players and network clones.")]
    [SerializeField] private bool isLocalPlayer = true;

    /// <summary>True for the player this machine's user controls.</summary>
    public bool IsLocal => isLocalPlayer;

    /// <summary>The local player. Set in Awake; null until the local PlayerController has awoken.</summary>
    public static PlayerController Local { get; private set; }

    // Static state must not survive a Play session when Domain Reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Local = null;

    [Header("Component References")]
    [Tooltip("Owns movement, gravity, jump, sprint, crouch, lean-blend and teleport.")]
    [SerializeField] private PlayerMotor motor;
    [Tooltip("Owns look rotation, the minigame free-look yaw clamp, and the lean camera arc.")]
    [SerializeField] private PlayerLook look;
    [Tooltip("Owns held items (both hands), hand sockets, equip/haul/swap-hands logic, and the drop-vs-throw charge system.")]
    [SerializeField] private PlayerInventory inventory;
    [Tooltip("Owns the minigame hand-follows-mouse IK, the two-handed haul pose, and the finger-grip curl.")]
    [SerializeField] private PlayerIKRig ikRig;
    [Tooltip("Owns the interaction raycast, the crosshair/prompt UI fade, and who the player is aiming at.")]
    [SerializeField] private PlayerInteractor interactor;
    [Tooltip("Owns this player's task lists and the task-evaluation loop.")]
    [SerializeField] private PlayerTaskBook taskBook;
    [Tooltip("Owns role, health, ghosting, custody, strangle, punch, and Corrupted power-ups.")]
    [SerializeField] private PlayerVitals vitals;
    // All seven components above must live on this same GameObject (PlayerInput SendMessage relies
    // on the same-GameObject requirement for PlayerMotor/PlayerLook/PlayerInventory/PlayerIKRig; the
    // other three are pure logic components resolved the same way for consistency). Each is resolved
    // automatically via GetComponent in Awake if left unassigned.

    public PlayerInteractor Interactor => interactor;
    public PlayerTaskBook TaskBook => taskBook;
    public PlayerVitals Vitals => vitals;

    [Header("Look Settings")]
    [SerializeField] private Transform playerCamera;

    public Transform RightHandSocket => inventory.RightHandSocket;
    public Transform LeftHandSocket => inventory.LeftHandSocket;
    public Transform RightHandBone => ikRig.RightHandBone;
    /// <summary>Exposed for PlayerInventory.AttachHaulItem's debug log.</summary>
    public Animator PlayerAnimator => animator;

    // Grip tuning/state, haul tuning/state, and the minigame-IK-tracking fields all live on
    // PlayerIKRig. These forwarding accessors keep the same names since MinigameHandRig, PlayerLook,
    // and PlayerInventory read or write them. hangReachWeight has no forwarder - it's internal to
    // ApplyHangReachIK below, which reads it via ikRig directly since nothing outside this class
    // touches it.
    public bool hangReachActive { get => ikRig.hangReachActive; set => ikRig.hangReachActive = value; }
    public Vector3 hangReachPos { get => ikRig.hangReachPos; set => ikRig.hangReachPos = value; }
    public Quaternion hangReachRot { get => ikRig.hangReachRot; set => ikRig.hangReachRot = value; }
    public float hangReachRotWeight { get => ikRig.hangReachRotWeight; set => ikRig.hangReachRotWeight = value; } // 0 = keep held pose, 1 = fully align to hangReachRot
    public bool haulActive { get => ikRig.haulActive; set => ikRig.haulActive = value; }
    public float ikBlendSpeed => ikRig.ikBlendSpeed;

    /// <summary>Exposed for PlayerIKRig.ApplyMinigameIK's Station-target branch.</summary>
    public Transform ActiveMinigameStation => activeMinigameStation;

    public int currentItemIndex = 0;

    // Public getters so items / other components can use the player's camera
    public Transform PlayerCamera => playerCamera;
    public CharacterController CharController => motor.CharController;
    /// <summary>Exposed for PlayerVitals' strangle orbit (UpdateStrangleLock), which shuffles side to
    /// side on the same A/D input Update() reads every frame.</summary>
    public Vector2 MoveInput => moveInput;

    [Header("Minigame State")]
    // Read-through onto MinigameBase's own registry (ARCHITECTURE.md P3) - the ONE source of truth
    // for "is this player in a minigame", covering every launch path (StartMinigame and
    // TaskDepositStation.LaunchDepositMinigame alike) since both now register through SetupMinigame.
    // No longer a field: nothing outside MinigameBase's registry may set this.
    public bool isPlayingMinigame => MinigameBase.IsAnyActive;
    public bool isMinigameLooking = false;
    private GameObject activeMinigameInstance;
    private Transform activeMinigameStation;
    public GameObject activeMinigameTarget;
    public TaskInstance activeMinigameTask; // The task the open minigame belongs to (its waypoint is hidden while playing)
    public MinigameTargetType currentMinigameTargetType = MinigameTargetType.None;

    // Internal State
    private Animator animator; // Controls the 3D model's animations
    private PlayerInput playerInput;
    public bool controlsLocked { get; private set; } // true while a blocking UI (pause menu) is up
    private Vector2 moveInput;
    private Vector2 lookInput;

    [Header("Lean (hold Ctrl to bend forward at the hips - reach low stations)")]
    [Tooltip("The lower spine bone to bend forward while leaning (e.g. Spine_01). Empty = the avatar's Spine bone. The bend is SKIPPED while a minigame is driving the right hand, so the outstretched arm stays put for aiming.")]
    [SerializeField] private Transform leanSpineBone;
    private bool warnedNoSpineBone;

    void Awake()
    {
        if (isLocalPlayer)
        {
            if (Local != null && Local != this)
                Debug.LogWarning($"[PlayerController] Two local players detected ('{Local.name}' and '{name}'). " +
                                 "Uncheck 'Is Local Player' on dummy players and network clones.");
            Local = this;
        }
        RoleManager.Instance?.Register(this);

        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (look == null) look = GetComponent<PlayerLook>();
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (ikRig == null) ikRig = GetComponent<PlayerIKRig>();
        if (interactor == null) interactor = GetComponent<PlayerInteractor>();
        if (taskBook == null) taskBook = GetComponent<PlayerTaskBook>();
        if (vitals == null) vitals = GetComponent<PlayerVitals>();
        playerInput = GetComponent<PlayerInput>();
        // Grab the Animator from the child CharacterVisuals model
        animator = GetComponentInChildren<Animator>();
        ikRig.CollectHandGripBones();

        // Lock cursor for FPS control
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDestroy()
    {
        if (Local == this) Local = null;
        RoleManager.Instance?.Unregister(this);
    }

    void Update()
    {
        // Hip lean (hold Ctrl). Runs BEFORE the controls-locked / minigame gates and reads the key
        // directly, so you can still lean down to reach into a low chest during its minigame.
        motor.UpdateLeanBlend();

        // While a blocking UI (pause / settings menu) is up, the player is fully frozen so they can
        // use the menu with the mouse. No movement, look, interaction or animation updates.
        if (!controlsLocked)
        {
            // 1. ALWAYS update the IK tracking (so the hand follows the mouse)
            ikRig.UpdateMinigameIKTarget();
            ikRig.UpdateHaulIKTarget();

            // 2. ONLY allow movement, camera rotation, and raycasting if NOT in a minigame
            //    and NOT locked into a strangle (the strangle coroutine drives position/rotation itself).
            if (!isPlayingMinigame && !vitals.isStrangling)
            {
                look.HandleRotation(lookInput);
                motor.HandleMovement(moveInput);
                motor.HandleCrouchTransition();
                interactor.CheckForInteractable();
                inventory.TickThrowCharge();
            }
            else
            {
                // Allow camera rotation if holding RMB in a minigame
                if (isPlayingMinigame && isMinigameLooking)
                {
                    look.HandleRotation(lookInput);
                }
                // Force the target UI crosshair/prompts to fade away while the minigame or strangle is active
                interactor.HideUIImmediately();
            }

            // 3. Smoothly fade the UI text in or out every frame
            interactor.UpdateUIFade();

            // 4. SYNC ANIMATIONS WITH MOVEMENT
            if (animator != null)
            {
                if (vitals.isStrangling)
                {
                    // Locked onto a victim: freeze the legs (and the walk cycle that fights the hand IK).
                    animator.SetFloat("Speed", 0f);
                }
                else
                {
                    // Normal locomotion - including while REACHING for a target, so the legs keep walking.
                    Vector3 horizontalVelocity = new Vector3(CharController.velocity.x, 0f, CharController.velocity.z);
                    animator.SetFloat("Speed", horizontalVelocity.magnitude, 0.1f, Time.deltaTime);
                }
            }
        }

        // Apply the lean camera arc HERE (end of Update, not LateUpdate) so the fully-leaned camera
        // pose is already final before any other script's Update() runs its own logic against it -
        // e.g. WaypointManager.Update() projects world -> screen via WorldToScreenPoint. Left
        // unconditional (like motor.UpdateLeanBlend() above) so it keeps decaying the arc smoothly
        // even while controlsLocked.
        look.ApplyLeanCameraArc();
    }

    // Runs after Update, before the Animator evaluates IK (OnAnimatorIK on PlayerIKHelper, which sits
    // on the Animator's child GameObject). These two stay here (not on a sibling) because they pose
    // bones and must run AFTER the Animator has evaluated this frame's pose, or it would immediately
    // overwrite them.
    void LateUpdate()
    {
        ApplyLeanSpineBend();
        ikRig.ApplyHandGripPose();
    }

    #region Input Action Callbacks

    public void OnMove(InputValue value) => moveInput = value.Get<Vector2>();

    public void OnLook(InputValue value) => lookInput = value.Get<Vector2>();

    public void OnInteract(InputValue value)
    {
        if (!value.isPressed || isPlayingMinigame) return;
        interactor.PerformInteraction();
    }

    // Dropping AND throwing are both on [Q] now (see OnDropItem). This is kept only so an old
    // "ThrowItem" binding, if any remains, does nothing.
    public void OnThrowItem(InputValue value) { }

    // OnCrouch, OnLean, OnJump, and OnSprint live on PlayerMotor (it sits on this same GameObject, so
    // PlayerInput's SendMessage calls them there directly).

    public void OnPrevious(InputValue value) { if (value.isPressed) CycleInventory(-1); }
    public void OnNext(InputValue value) { if (value.isPressed) CycleInventory(1); }

    private void CycleInventory(int direction)
    {
        currentItemIndex += direction;
        if (currentItemIndex > 2) currentItemIndex = 0;
        if (currentItemIndex < 0) currentItemIndex = 2;

        Debug.Log($"Switched to item slot: {currentItemIndex}");
    }

    // Triggered by your 'F' key. King-only: appoints whoever the crosshair is on as Kingsguard.
    public void OnNominate(InputValue value)
    {
        if (!value.isPressed || vitals.isGhost) return;

        if (vitals.currentRole == PlayerRole.King && interactor.TargetPlayer != null)
        {
            if (RoleManager.Instance != null)
            {
                RoleManager.Instance.SetKingsguard(interactor.TargetPlayer);
                interactor.ClearTargetPlayer(); // don't spam it
            }
        }
    }

    // [Q]: tap = drop the item in place, hold = charge and throw it.
    public void OnDropItem(InputValue value)
    {
        if (value.isPressed)
        {
            // Royal Pardon (King / Kingsguard, dragging a prisoner) takes priority over drop/throw.
            if (vitals.TryPardon()) return;

            if (isPlayingMinigame) return;
            if (GetHeldItem() == null && GetLeftHeldItem() == null)
            {
                Debug.Log("Nothing to drop.");
                return;
            }

            // Start of press: begin charging a potential throw. If it turns out to be a tap we
            // just drop instead on release.
            inventory.StartCharging();
        }
        else
        {
            if (!inventory.IsChargingThrow) return; // press was consumed (pardon / nothing held / minigame)
            inventory.StopCharging();

            bool wasTap = inventory.WasTap;

            // Ghosts and taps just drop in place; a real hold throws the active-hand item.
            if (vitals.isGhost || wasTap || GetHeldItem() == null)
                inventory.DropHeldItemInPlace();
            else
                inventory.ExecuteThrow();
        }
    }

    // Triggered by the 'R' key. Moves items between the left and right hands.
    public void OnSwapHands(InputValue value)
    {
        if (!value.isPressed) return;
        if (isPlayingMinigame || vitals.isArrested) return;

        // A two-handed haul item occupies both hands - nothing to swap.
        if (GetHeldItem() != null && GetHeldItem().haulWithBothHands) return;

        if (GetHeldItem() == null && GetLeftHeldItem() == null)
        {
            Debug.Log("Nothing to swap between hands.");
            return;
        }

        inventory.SwapHands();
        taskBook.RefreshLocalWaypoints();
    }

    public void OnPunch(InputValue value) => vitals.HandlePunch(value.isPressed, GetHeldItem());

    // --- DEDICATED RIGHT CLICK (Strangle OR Arrest, or minigame free-look) ---
    public void OnStrangle(InputValue value)
    {
        // --- MINIGAME CAMERA LOOK INTERCEPTION ---
        if (isPlayingMinigame)
        {
            isMinigameLooking = value.isPressed;

            if (isMinigameLooking)
            {
                look.BeginMinigameLook();
                // Hide cursor and lock it to the center so we can move the camera
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                // Snap back to the pose we had when the minigame opened.
                look.EndMinigameLook();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return; // Stop here! Do not run the strangle/arrest logic!
        }

        vitals.HandleStrangleOrArrest(value.isPressed, GetHeldItem(), GetLeftHeldItem());
    }

    // --- DEDICATED USE POWER-UP MECHANIC (F Key) ---
    public void OnUseItem(InputValue value) => vitals.HandleUsePowerUp(value.isPressed);

    public void OnUsePowerUp1(InputValue value) { if (value.isPressed) vitals.EquipCorruptedSlot(0); }
    public void OnUsePowerUp2(InputValue value) { if (value.isPressed) vitals.EquipCorruptedSlot(1); }
    public void OnUsePowerUp3(InputValue value) { if (value.isPressed) vitals.EquipCorruptedSlot(2); }

    public void OnScrollWheel(InputValue value) => vitals.HandleScrollWheel(value.Get<Vector2>().y);

    #endregion

    // Called from PlayerIKHelper.OnAnimatorIK - reaches the RIGHT hand toward hangReachPos while it
    // holds an item, for the sword-hang deposit minigame. Stays here (not on PlayerIKRig) since the
    // hangReach* state is written by MinigameHandRig via the forwarding properties above, and this is
    // the one remaining reader that isn't part of the grip/haul IK system PlayerIKRig owns.
    public void ApplyHangReachIK(int layerIndex)
    {
        if (animator == null) return;

        float target = hangReachActive ? 1f : 0f;
        ikRig.hangReachWeight = Mathf.Lerp(ikRig.hangReachWeight, target, Time.deltaTime * ikBlendSpeed);
        if (ikRig.hangReachWeight <= 0.01f) return;

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, ikRig.hangReachWeight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, hangReachPos);

        float rotW = ikRig.hangReachWeight * Mathf.Clamp01(hangReachRotWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rotW);
        animator.SetIKRotation(AvatarIKGoal.RightHand, hangReachRot);
    }

    // Bends the lower spine forward (about the player's right axis) so the model visibly folds at the
    // hips while Ctrl is held. Runs in LateUpdate, after the animator, so it overrides the pose.
    // Skipped while a minigame is driving the right hand (hangReachActive) - the fold would drag the
    // outstretched arm off its aim target; the camera arc alone lowers the view.
    private void ApplyLeanSpineBend()
    {
        if (motor.LeanBlend <= 0.001f || hangReachActive) return;

        Transform sb = leanSpineBone;
        if (sb == null && animator != null && animator.isHuman)
            sb = animator.GetBoneTransform(HumanBodyBones.Spine)
              ?? animator.GetBoneTransform(HumanBodyBones.Chest);

        if (sb == null)
        {
            if (!warnedNoSpineBone)
            {
                warnedNoSpineBone = true;
                Debug.LogWarning("[Lean] No spine bone - assign 'Lean Spine Bone' (e.g. Spine_01) on the Player so the model folds.");
            }
            return;
        }

        sb.rotation = Quaternion.AngleAxis(look.LeanAngle * motor.LeanBlend, transform.right) * sb.rotation;
    }

    // EquipItem, GetHeldItem/ClearHeldItem, GetLeftHeldItem/ClearLeftHeldItem, IsHoldingItem, and
    // IsHoldingHeavyItem now live on PlayerInventory - these forwarders are what TaskDepositStation,
    // PickupItem, ConcreteTaskSteps, and the minigames all still call.
    public void EquipItem(PickupItem newItem) => inventory.EquipItem(newItem);
    public PickupItem GetHeldItem() => inventory.GetHeldItem();
    public void ClearHeldItem() => inventory.ClearHeldItem();
    public PickupItem GetLeftHeldItem() => inventory.GetLeftHeldItem();
    public void ClearLeftHeldItem() => inventory.ClearLeftHeldItem();
    public bool IsHoldingItem(ItemDefinition definition, ItemState requiredState = ItemState.None)
        => inventory.IsHoldingItem(definition, requiredState);
    public bool IsHoldingHeavyItem() => inventory.IsHoldingHeavyItem();

    // Called by UIManager when the pause / settings menu opens or closes. Freezes the player and
    // stops all input so the mouse can be used on the menu.
    public void SetControlsLocked(bool locked)
    {
        controlsLocked = locked;

        // Drop any input already latched so we don't keep moving/looking after the menu opens.
        moveInput = Vector2.zero;
        lookInput = Vector2.zero;
        motor.CancelSprint();
        motor.CancelLeanInput(); // legacy Ctrl read in Update() still drives the lean while locked
        inventory.StopCharging();
        vitals.CancelStrangleButton();

        // Stop every input action callback from firing while the menu is up.
        if (playerInput != null) playerInput.enabled = !locked;
    }

    // Constrained walking for placement minigames (e.g. SwordHangMinigame).
    public void MinigameWalk(Vector2 move, Vector3 anchor, float radius) => motor.WalkConstrained(move, anchor, radius);

    // Horizontal-only look for placement minigames.
    public void MinigameLookYaw(float degrees) => look.MinigameLookYaw(degrees);

    // Vertical look for placement minigames.
    public void MinigameLookPitch(float degrees) => look.MinigameLookPitch(degrees);

    public void TeleportTo(Transform targetTransform) => motor.TeleportTo(targetTransform);
    public void TeleportTo(Vector3 position, Quaternion rotation) => motor.TeleportTo(position, rotation);

    // Aim the body + camera at a world point (used when a placement minigame starts).
    public void PointCameraAt(Vector3 worldPoint) => look.PointCameraAt(worldPoint);

    // Added the Transform parameter with a default null fallback
    // CHANGED: Signature now takes GameObject targetInteractable instead of Transform stationTransform
    public void StartMinigame(GameObject minigamePrefab, TaskInstance task, GameObject targetInteractable = null)
    {
        if (isPlayingMinigame || minigamePrefab == null) return;

        activeMinigameTarget = targetInteractable;
        activeMinigameTask = task; // So the waypoint for this task can be hidden while the minigame is open
        inventory.itemSwappedToLeftHand = false; // Reset flag

        // --- DYNAMIC CAMERA & POSITION SNAPPING ---
        if (targetInteractable != null)
        {
            // SCENARIO 1: We are interacting with another Player
            PlayerController targetPlayer = targetInteractable.GetComponent<PlayerController>();
            if (targetPlayer != null)
            {
                currentMinigameTargetType = MinigameTargetType.Player; // <-- NEW
                activeMinigameStation = targetPlayer.transform;

                // Snap body and camera to look exactly at the other player
                Vector3 lookDir = targetPlayer.transform.position - playerCamera.position;
                Vector3 bodyDir = new Vector3(lookDir.x, 0, lookDir.z);
                if (bodyDir != Vector3.zero) transform.rotation = Quaternion.LookRotation(bodyDir);

                float targetPitch = Quaternion.LookRotation(lookDir).eulerAngles.x;
                if (targetPitch > 180f) targetPitch -= 360f;
                look.SnapPitch(targetPitch);
            }
            // SCENARIO 2: The minigame targets an ITEM (one we hold, or a world prop such as a
            // Cake/Sword). Item minigames are performed in-hand and must NEVER move or turn the player.
            else if (targetInteractable.GetComponent<PickupItem>() != null)
            {
                currentMinigameTargetType = MinigameTargetType.Item;
                activeMinigameStation = LeftHandSocket;

                PickupItem targetItem = targetInteractable.GetComponent<PickupItem>();

                // Pick which item to raise into the left hand for the animation:
                // the targeted instance if we're already holding it, else whatever is in the active hand.
                PickupItem itemToRaise = (targetItem == GetLeftHeldItem() || targetItem == GetHeldItem())
                    ? targetItem
                    : GetHeldItem();

                if (itemToRaise != null && itemToRaise == GetLeftHeldItem())
                {
                    // Already sitting in the left hand: just orient the socket, no swap, no return needed.
                    if (LeftHandSocket != null)
                        LeftHandSocket.localRotation = Quaternion.Euler(itemToRaise.leftHandSocketRotation);
                }
                else if (itemToRaise != null)
                {
                    // The minigame borrows the left hand. If the off-hand holds something else, drop it.
                    if (GetLeftHeldItem() != null)
                    {
                        Debug.Log($"Dropped off-hand {GetLeftHeldItem().DisplayName} to free the left hand for the minigame.");
                        GetLeftHeldItem().DetachFromHand();
                        ClearLeftHeldItem();
                        taskBook.CheckRegressionForAll();
                    }

                    if (LeftHandSocket != null)
                        LeftHandSocket.localRotation = Quaternion.Euler(itemToRaise.leftHandSocketRotation);

                    itemToRaise.AttachToHand(LeftHandSocket);
                    inventory.itemSwappedToLeftHand = true; // ReturnSwappedItem() puts currentlyHeldItem back afterwards
                }

                // Camera PITCH only — look down at the hands. Body rotation and position are left alone.
                if (LeftHandSocket != null)
                {
                    Vector3 lookDir = LeftHandSocket.position - playerCamera.position;
                    float targetPitch = Quaternion.LookRotation(lookDir).eulerAngles.x;
                    if (targetPitch > 180f) targetPitch -= 360f;
                    look.SnapPitch(targetPitch);
                }
            }
            // SCENARIO 3: We are interacting with a Task Station (Default)
            else
            {
                currentMinigameTargetType = MinigameTargetType.Station; // <-- NEW
                activeMinigameStation = targetInteractable.transform;

                Transform standPoint = targetInteractable.transform.Find("StandPoint");
                if (standPoint != null)
                {
                    Vector3 lockedFloorPosition = new Vector3(standPoint.position.x, transform.position.y, standPoint.position.z);
                    if (CharController != null) CharController.enabled = false;
                    transform.position = lockedFloorPosition;
                    transform.rotation = standPoint.rotation;
                    if (CharController != null)
                    {
                        CharController.enabled = true;
                        CharController.Move(Vector3.down * 0.15f); // Prevent physics popping
                    }
                }
                else
                {
                    Vector3 lookDir = targetInteractable.transform.position - playerCamera.position;
                    Vector3 bodyDir = new Vector3(lookDir.x, 0, lookDir.z);
                    if (bodyDir != Vector3.zero) transform.rotation = Quaternion.LookRotation(bodyDir);
                }

                Vector3 finalLookDirection = targetInteractable.transform.position - playerCamera.position;
                float targetPitch = Quaternion.LookRotation(finalLookDirection).eulerAngles.x;
                if (targetPitch > 180f) targetPitch -= 360f;
                look.SnapPitch(targetPitch);
            }
        }

        // Save the rotation so we can snap back to it later when Right Click is released
        look.CaptureMinigameLookHome();

        // Free the mouse cursor for tactile dragging
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Instantiate and set up the minigame, routed through a MinigameContext (ARCHITECTURE.md P3) -
        // the same entry point TaskDepositStation.LaunchDepositMinigame uses.
        activeMinigameInstance = Instantiate(minigamePrefab);
        MinigameBase minigameScript = activeMinigameInstance.GetComponent<MinigameBase>();
        if (minigameScript != null)
        {
            MinigameContext context = new MinigameContext(this, task)
            {
                TargetType = currentMinigameTargetType,
                Target = targetInteractable,
                HeldItem = targetInteractable != null ? targetInteractable.GetComponent<PickupItem>() : null,
            };
            minigameScript.SetupMinigame(context.Resolve());
        }

        // Redraw waypoints now so this task's marker disappears while the minigame is open.
        taskBook.RefreshLocalWaypoints();
    }

    public void FinishMinigame(TaskInstance task)
    {
        // isPlayingMinigame now clears itself: MinigameBase already removed this minigame from its
        // registry before calling here (see MinigameBase.CompleteMinigame).
        isMinigameLooking = false;
        activeMinigameTask = null; // Minigame closed: this task's waypoint may show again
        // ApplyMinigameIK reads these to pose the off-hand for a StartMinigame-launched minigame -
        // clear them so a later deposit minigame (which never sets them) doesn't inherit a stale
        // Player/Item/Station pose left over from this one.
        currentMinigameTargetType = MinigameTargetType.None;
        activeMinigameStation = null;

        inventory.ReturnSwappedItem(); // Snap item back to right hand

        // Re-lock the mouse cursor for FPS gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (task != null)
        {
            // 2. Officially complete the step now that the minigame is won
            task.CompleteActiveStep();

            // 2b. Auto-advance any follow-up steps that are ALREADY satisfied by player state
            //     (e.g. an AcquireItemStep for an item already in hand, or a NavigateStep for the
            //     zone the player is standing in). Passes no target, so steps that need a specific
            //     interaction object are left for the player to do.
            while (!task.IsComplete && !isPlayingMinigame)
            {
                int before = task.CurrentStepIndex;
                task.EvaluateCurrentStep(this, null);
                if (task.CurrentStepIndex == before) break; // no progress this pass
            }

            // 3. Check if that was the final step of the entire task
            if (task.IsComplete && TaskManager.Instance != null)
            {
                TaskManager.Instance.CompleteTask(this, task);
            }
        }
        else
        {
            // Standalone item minigame (launched straight from the held item, no task attached).
            ResolveStandaloneItemMinigame();
        }

        taskBook.RefreshLocalWaypoints();
    }

    // Called after an item's own minigame is won with no task driving it. Advances any matching
    // active step (without re-launching a minigame) - covers a task player whose ProcessItemStep
    // fired via the item's processMinigamePrefab. Does nothing for a faker with no such task.
    private void ResolveStandaloneItemMinigame()
    {
        // The item may already have been consumed by the minigame, so fall back to the target the
        // minigame was launched against.
        GameObject evalTarget = GetHeldItem() != null ? GetHeldItem().gameObject : activeMinigameTarget;

        taskBook.EvaluateActiveTasks(evalTarget, skipMinigame: true);

        if (GetHeldItem() != null && !GetHeldItem().Has(ItemState.Processed))
        {
            GetHeldItem().ProcessItem();
        }
    }

    public void CancelMinigame()
    {
        // isPlayingMinigame now clears itself: MinigameBase already removed this minigame from its
        // registry before calling here (see MinigameBase.CancelMinigame).
        isMinigameLooking = false;
        activeMinigameTask = null; // Minigame closed: this task's waypoint may show again
        // See the matching comment in FinishMinigame - avoid handing a stale Player/Item/Station
        // IK pose to whatever minigame opens next.
        currentMinigameTargetType = MinigameTargetType.None;
        activeMinigameStation = null;

        inventory.ReturnSwappedItem(); // Snap item back to right hand

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("Minigame Cancelled.");

        // Bring the task's waypoint back now that the minigame was abandoned.
        taskBook.RefreshLocalWaypoints();
    }
}
