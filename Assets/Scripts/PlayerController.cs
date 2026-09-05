using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections.Generic; // Required for TextMeshPro UI

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

    [Header("Locomotion")]
    [Tooltip("Owns movement, gravity, jump, sprint, crouch, lean-blend and teleport. Must live on " +
             "this same GameObject (PlayerInput SendMessage relies on that). Resolved automatically " +
             "via GetComponent if left unassigned.")]
    [SerializeField] private PlayerMotor motor;

    [Header("Look Settings")]
    [SerializeField] private Transform playerCamera;
    [Tooltip("Owns look rotation (mouse sensitivity, pitch clamp), the minigame free-look yaw clamp, " +
             "and the lean camera arc. Must live on this same GameObject. Resolved automatically via " +
             "GetComponent if left unassigned.")]
    [SerializeField] private PlayerLook look;

    public Transform RightHandSocket => inventory.RightHandSocket;
    public Transform LeftHandSocket => inventory.LeftHandSocket;
    public Transform RightHandBone => ikRig.RightHandBone;
    /// <summary>Exposed for PlayerInventory.AttachHaulItem's debug log.</summary>
    public Animator PlayerAnimator => animator;

    [Header("Inventory")]
    [Tooltip("Owns held items (both hands), hand sockets, equip/haul/swap-hands logic, and the " +
             "drop-vs-throw charge system. Must live on this same GameObject. Resolved automatically " +
             "via GetComponent if left unassigned.")]
    [SerializeField] private PlayerInventory inventory;

    [Header("IK Rig")]
    [Tooltip("Owns the minigame hand-follows-mouse IK, the two-handed haul pose, and the finger-grip " +
             "curl. Must live on this same GameObject. Resolved automatically via GetComponent if " +
             "left unassigned.")]
    [SerializeField] private PlayerIKRig ikRig;

    // Grip tuning/state, haul tuning/state, and the minigame-IK-tracking fields all now live on
    // PlayerIKRig. These forwarding accessors keep the same names since MinigameHandRig (the sword-hang
    // / consume-style minigames' hand-IK wrapper), PlayerLook, and PlayerInventory read or write them.
    // hangReachWeight has no forwarder - it's internal to ApplyHangReachIK below, which reads it via
    // ikRig directly since nothing outside this class touches it.
    public bool hangReachActive { get => ikRig.hangReachActive; set => ikRig.hangReachActive = value; }
    public Vector3 hangReachPos { get => ikRig.hangReachPos; set => ikRig.hangReachPos = value; }
    public Quaternion hangReachRot { get => ikRig.hangReachRot; set => ikRig.hangReachRot = value; }
    public float hangReachRotWeight { get => ikRig.hangReachRotWeight; set => ikRig.hangReachRotWeight = value; } // 0 = keep held pose, 1 = fully align to hangReachRot
    public bool haulActive { get => ikRig.haulActive; set => ikRig.haulActive = value; }
    public float ikBlendSpeed => ikRig.ikBlendSpeed;

    /// <summary>Exposed for PlayerIKRig.ApplyMinigameIK's Station-target branch.</summary>
    public Transform ActiveMinigameStation => activeMinigameStation;

    public int currentItemIndex = 0;

    [Header("Social Deduction Settings")]
    [SerializeField] private float interactionRange = 3f;
    [Tooltip("Holding a matching item, [E] deposits it at a deposit station within this range even without aiming at it (for bulky/hauled items you can't see past).")]
    [SerializeField] private float depositProximityRange = 2.5f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private LayerMask characterLayer; // NEW: Identifies other players
    // Public getters so the item can use the player's camera and settings
    public Transform PlayerCamera => playerCamera;
    public float InteractionRange => interactionRange;
    public LayerMask CharacterLayer => characterLayer;
    public CharacterController CharController => motor.CharController;

    [Header("UI Settings")]
    [SerializeField] private TextMeshProUGUI interactionUI; 
    [SerializeField] private CanvasGroup interactionCanvasGroup; // NEW: Controls fading
    [SerializeField] private UnityEngine.UI.Image crosshair;     // NEW: The center dot
    [SerializeField] private Color normalCrosshairColor = new Color(1f, 1f, 1f, 0.5f); // Semi-transparent white
    [SerializeField] private Color activeCrosshairColor = Color.green;
    [SerializeField] private float uiFadeSpeed = 10f;
    
    private float targetUIAlpha = 0f; // Target opacity for the text
    private IInteractable currentTarget; // Stores what the player is currently looking at
    private PlayerController targetPlayer; // Tracks the player you are looking at
    
    [Header("Role Settings")]
    public PlayerRole currentRole = PlayerRole.None;

    [Header("Status")]
    public int currentHealth = 1;
    public int maxHealth = 1;
    public bool isBlocking = false;
    public bool isGhost = false; // We will fully implement this in Step 4

    [Header("Custody Mechanics")]
    public PlayerController currentPrisoner;
    public Coroutine breakoutTimerCoroutine;
    public int arrestQuota = 2;
    public bool isDraggingPrisoner = false;
    public bool isArrested = false;
    public PlayerController currentCaptor;
    public float gallowsRange = 4.0f;
    private Gallows sceneGallows;

    [Header("Punch Mechanic")]
    public float punchCooldown = 2f;
    private float lastPunchTime = -2f; // Starts at -2 so they can punch immediately
    public float punchRange = 2f;
    public float punchRadius = 0.5f;
    public float pushbackForce = 15f;
    public float pushbackDuration = 0.2f;

    private bool isBeingPushed = false;
    /// <summary>Exposed for PlayerMotor.HandleMovement, which locks movement speed to 0 while a pushback is in progress.</summary>
    public bool IsBeingPushed => isBeingPushed;

    [Header("Corrupted Combat")]
    public float strangleCooldown = 4f;
    private float lastStrangleTime = -4f;
    public float strangleRange = 2f;
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

    // NEW: Tracks which room the player is currently standing in
    public string currentZoneID = "";
    
    [Header("Tasks")]
    [SerializeField] public bool showWaypoints = false;
    // Per-player runtime task state. TaskInstance is a plain class (not a ScriptableObject), so these
    // are runtime-only - Unity does not serialize them and they never appear in the Inspector.
    public List<TaskInstance> activeTasks = new List<TaskInstance>();
    // The visual state list that never shrinks, keeping UI numbers synced
    public List<TaskInstance> allAssignedTasks = new List<TaskInstance>();
    // --- CHANGED: FIXED-SLOT CORRUPTED INVENTORY ---
    public PowerUpData[] corruptedInventory = new PowerUpData[3];

    [Header("Active Inventory")]
    public Transform powerUpHoldPoint; 
    public int activeSlotIndex = -1;   
    private GameObject activePowerUpVisual; 
    
    // --- NEW: SCROLL DEBOUNCE ---
    public float scrollCooldown = 0.15f; 
    private float lastScrollTime = 0f;

    // The drop/throw charge system (maxThrowChargeTime, baseThrowForce, dropTapMaxDuration,
    // isChargingThrow, currentThrowCharge, dropPressTime) now lives on PlayerInventory.

    [Header("Power-Up Prefabs & Status")]
    public GameObject trapPrefab;
    public GameObject illusionPrefab;
    private bool isStunned = false;
    /// <summary>Exposed for PlayerMotor.HandleMovement / UpdateLeanBlend, which lock movement and lean while stunned.</summary>
    public bool IsStunned => isStunned;
    private Renderer[] playerRenderers;

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
    // itemSwappedToLeftHand now lives on PlayerInventory (see ReturnSwappedItem).
    // --- NEW: Tracks the current target for IK logic ---
    public MinigameTargetType currentMinigameTargetType = MinigameTargetType.None;
    // minigameLookLimit, currentMinigameYaw, and the camera snap anchors (minigameStartBodyRotation /
    // minigameStartVerticalRotation) now live on PlayerLook.

    // minigameIKDepth, ikBlendSpeed (forwarding property above), rightHandRotationOffset,
    // currentIKWeight, and ikTargetPosition now live on PlayerIKRig.

    // Internal State
    private Animator animator; // NEW: Controls the 3D model's animations
    private PlayerInput playerInput;
    public bool controlsLocked { get; private set; } // true while a blocking UI (pause menu) is up
    private Vector2 moveInput;
    private Vector2 lookInput;

    [Header("Lean (hold Ctrl to bend forward at the hips - reach low stations)")]
    [Tooltip("The lower spine bone to bend forward while leaning (e.g. Spine_01). Empty = the avatar's Spine bone. The bend is SKIPPED while a minigame is driving the right hand, so the outstretched arm stays put for aiming.")]
    [SerializeField] private Transform leanSpineBone;
    // isLeaning / leanBlend live on PlayerMotor (motor.LeanBlend). verticalRotation, leanAngle,
    // leanHipLocalY, leanViewPitch, leanMinigameForwardFactor, and the lean camera arc itself now live
    // on PlayerLook (look.LeanAngle exposes the angle back here) - only the spine-bend bone stays on
    // PlayerController, since ApplyLeanSpineBend must run after the Animator (LateUpdate).

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
        playerInput = GetComponent<PlayerInput>();
        // Grab the Animator from the child CharacterVisuals model
        animator = GetComponentInChildren<Animator>();
        ikRig.CollectHandGripBones();

        // Cache all the meshes so the Invisibility Potion can turn them off
        playerRenderers = GetComponentsInChildren<Renderer>();

        // Lock cursor for FPS control
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Ensure the UI is hidden when the game starts
        // 1. Turn it on EXACTLY ONCE when the game boots up
        if (interactionUI != null) 
        {
            interactionUI.gameObject.SetActive(true);
        }

        // 2. Make it instantly invisible so it's ready to fade in later
        if (interactionCanvasGroup != null)
        {
            interactionCanvasGroup.alpha = 0f;
        }    }

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
            if (!isPlayingMinigame && !isStrangling)
            {
                look.HandleRotation(lookInput);
                motor.HandleMovement(moveInput);
                motor.HandleCrouchTransition();
                CheckForInteractable();

                // --- NEW: THROW CHARGING TIMER ---
                inventory.TickThrowCharge();
            }
            else
            {
                // --- NEW: Allow camera rotation if holding RMB in a minigame ---
                if (isPlayingMinigame && isMinigameLooking)
                {
                    look.HandleRotation(lookInput);
                }
                // Force the target UI crosshair/prompts to fade away while the minigame or strangle is active
                targetUIAlpha = 0f;
            }

            // 3. Smoothly fade the UI text in or out every frame
            if (interactionCanvasGroup != null)
            {
                interactionCanvasGroup.alpha = Mathf.Lerp(interactionCanvasGroup.alpha, targetUIAlpha, Time.deltaTime * uiFadeSpeed);
            }

            // 4. SYNC ANIMATIONS WITH MOVEMENT
            if (animator != null)
            {
                if (isStrangling)
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
        // e.g. WaypointManager.Update() projects world -> screen via WorldToScreenPoint. It used to
        // run in LateUpdate, one whole phase after WaypointManager reads the camera, so every marker
        // was drawn from the PREVIOUS frame's lean pose and visibly detached from the world while
        // leaning (snapping back once the lean fully released). Left unconditional (like
        // motor.UpdateLeanBlend() above) so it keeps decaying the arc smoothly even while
        // controlsLocked, matching its old always-runs-in-LateUpdate behaviour.
        look.ApplyLeanCameraArc();
    }

    #region Input Action Callbacks 
    
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnLook(InputValue value)
    {
        lookInput = value.Get<Vector2>();
    }

    public void OnInteract(InputValue value)
    {
        // --- NEW: Do not process standard interactions if a minigame is open! ---
        if (!value.isPressed || isPlayingMinigame) return; 
        
        PerformInteraction();
    }

    // Dropping AND throwing are both on [Q] now (see OnDropItem). This is kept only so an old
    // "ThrowItem" binding, if any remains, does nothing.
    public void OnThrowItem(InputValue value) { }

    // OnCrouch, OnLean, HandleCrouchTransition, and OnJump now live on PlayerMotor (it sits on this
    // same GameObject, so PlayerInput's SendMessage calls them there directly).

    public void OnPrevious(InputValue value)
    {
        if (!value.isPressed) return;
        CycleInventory(-1);
    }

    public void OnNext(InputValue value)
    {
        if (!value.isPressed) return;
        CycleInventory(1);
    }

    // OnSprint now lives on PlayerMotor (it sits on this same GameObject, so PlayerInput's
    // SendMessage calls it there directly).

    // Triggered by your 'F' key (or whatever you map it to in the Input System)
    public void OnNominate(InputValue value)
    {
        // Ignore if the key was released, or if the player is a ghost
        if (!value.isPressed || isGhost) return;

        // Ensure this player is the King, and they are actively looking at a valid target player
        if (currentRole == PlayerRole.King && targetPlayer != null)
        {
            // Failsafe: You cannot nominate yourself, and you cannot nominate the Corrupted if you somehow know who they are, 
            // but in most social deduction games, the King CAN accidentally make a traitor the guard! 
            // We just let the RoleManager handle the logic.
            
            if (RoleManager.Instance != null)
            {
                RoleManager.Instance.SetKingsguard(targetPlayer);
                
                // Clear the target so they don't spam it
                targetPlayer = null; 
            }
        }
    }

    // Triggered strictly by your 'Q' key in the Input System
    // [Q]: tap = drop the item in place, hold = charge and throw it (same throw as before).
    public void OnDropItem(InputValue value)
    {
        if (value.isPressed)
        {
            // --- ROYAL PARDON (King / Kingsguard, dragging a prisoner): a quick press, no throw ---
            if (!isGhost && (currentRole == PlayerRole.King || currentRole == PlayerRole.Kingsguard)
                && isDraggingPrisoner && currentPrisoner != null)
            {
                currentPrisoner.isArrested = false; // Free them!

                if (currentPrisoner.breakoutTimerCoroutine != null)
                {
                    currentPrisoner.StopCoroutine(currentPrisoner.breakoutTimerCoroutine);
                    currentPrisoner.breakoutTimerCoroutine = null;
                }

                isDraggingPrisoner = false;
                Debug.Log($"You pardoned and freed {currentPrisoner.gameObject.name}!");
                currentPrisoner = null;
                return; // don't start a drop/throw
            }

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
            if (isGhost || wasTap || GetHeldItem() == null)
                inventory.DropHeldItemInPlace();
            else
                inventory.ExecuteThrow();
        }
    }

    // DropHeldItemInPlace now lives on PlayerInventory (inventory.DropHeldItemInPlace).

    // Triggered by the 'R' key (SwapHands action). Moves items between the left and right hands.
    public void OnSwapHands(InputValue value)
    {
        if (!value.isPressed) return;

        // Can't rearrange your grip mid-minigame or while being escorted in custody.
        if (isPlayingMinigame || isArrested) return;

        // A two-handed haul item occupies both hands - nothing to swap.
        if (GetHeldItem() != null && GetHeldItem().haulWithBothHands) return;

        if (GetHeldItem() == null && GetLeftHeldItem() == null)
        {
            Debug.Log("Nothing to swap between hands.");
            return;
        }

        inventory.SwapHands();

        RefreshLocalWaypoints();
    }

    // --- NEW: PUNCH MECHANIC ---
    public void OnPunch(InputValue value)
    {
        if (!value.isPressed || isGhost) return;

        // --- If we are holding an item, don't punch (a Royal can still raise it to block). ---
        if (GetHeldItem() != null)
        {
            if (GetHeldItem() is RoyalWeapon royalWeapon)
            {
                StartCoroutine(RoyalWeaponBlockRoutine(royalWeapon));
            }

            return; // dropping / throwing that item is on [Q]
        }

        // Check Cooldown
        if (Time.time < lastPunchTime + punchCooldown)
        {
            Debug.Log("Punch is on cooldown!");
            return;
        }

        ExecutePunch();
    }

    // --- DEDICATED RIGHT CLICK (Strangle OR Arrest) ---
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
                // --- NEW: SNAP BACK TO ORIGINAL POSITION ---
                look.EndMinigameLook();

                // Show cursor and unlock it so we can play the minigame again
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return; // Stop here! Do not run the strangle/arrest logic!
        }

        if (isGhost) return;

        // --- SCENARIO A: CORRUPTED STRANGLE ---
        if (currentRole == PlayerRole.Corrupted)
        {
            if (value.isPressed)
            {
                if (GetHeldItem() != null || GetLeftHeldItem() != null || activePowerUpVisual != null)
                {
                    Debug.Log("You cannot strangle someone while holding an item or power-up!");
                    return;
                }
                if (Time.time < lastStrangleTime + strangleCooldown)
                {
                    Debug.Log("Strangulation is on cooldown!");
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
            return;
        }

        // --- SCENARIO B: ROYAL ARREST & LEASH CONTROL ---
        if ((currentRole == PlayerRole.King || currentRole == PlayerRole.Kingsguard) && value.isPressed)
        {
            if (isDraggingPrisoner)
            {
                if (sceneGallows == null) sceneGallows = FindAnyObjectByType<Gallows>();
                
                bool nearGallows = sceneGallows != null && Vector3.Distance(transform.position, sceneGallows.transform.position) <= gallowsRange;
                
                bool nearRoyal = false;
                PlayerController nearbyRoyal = null;
                Collider[] royalHits = Physics.OverlapSphere(transform.position, InteractionRange, characterLayer);
                
                foreach (Collider c in royalHits)
                {
                    PlayerController p = c.GetComponent<PlayerController>();
                    if (p != null && p != this && !p.isGhost && (p.currentRole == PlayerRole.King || p.currentRole == PlayerRole.Kingsguard))
                    {
                        nearRoyal = true;
                        nearbyRoyal = p;
                        break;
                    }
                }

                // Execute based on proximity priority
                if (nearGallows)
                {
                    LockPrisonerToGallows(sceneGallows);
                }
                else if (nearRoyal && !nearbyRoyal.isDraggingPrisoner)
                {
                    TransferPrisoner(nearbyRoyal);
                }
                else
                {
                    DropLeash(); // Nothing nearby, just drop the prisoner
                }
            }
            else
            {
                ExecuteArrest(); // Empty-handed, try to grab someone
            }
        }
    }

   // --- DEDICATED USE POWER-UP MECHANIC (F Key) ---
    public void OnUseItem(InputValue value)
    {
        if (!value.isPressed || isGhost || currentRole != PlayerRole.Corrupted) return;

        // Are they visibly holding a Power-Up?
        if (activeSlotIndex != -1 && corruptedInventory[activeSlotIndex] != null)
        {
            PowerUpData powerUpToUse = corruptedInventory[activeSlotIndex];
            
            bool success = ExecutePowerUp(powerUpToUse);
            
            if (success)
            {
                // Consume the item
                corruptedInventory[activeSlotIndex] = null;
                if (activePowerUpVisual != null) Destroy(activePowerUpVisual);
                
                activeSlotIndex = -1; // Return to empty-handed
                
                if (UIManager.Instance != null) 
                {
                    UIManager.Instance.UpdateCorruptedInventory(corruptedInventory);
                    UIManager.Instance.HighlightSlot(-1);
                }
            }
        }
        else
        {
            Debug.Log("You don't have a power-up equipped to use!");
        }
    }

    private System.Collections.IEnumerator StrangleRoutine()
    {
        Debug.Log($"{gameObject.name} reaches out to strangle...");

        // --- PHASE 1: REACH & HUNT ---
        // Arms extend forward (IK) while the player moves and aims normally. Each frame we look for a
        // valid victim in front, but the strangle only STARTS once we've closed to arm's length -
        // i.e. the reaching hands are actually at the victim's neck.
        isReachingToStrangle = true;
        PlayerController targetVictim = null;

        while (strangleButtonHeld && targetVictim == null)
        {
            if (isGhost || isStunned || isArrested || isBeingPushed) break;
            if (GetHeldItem() != null || GetLeftHeldItem() != null || activePowerUpVisual != null) break;

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

        Debug.Log($"Grabbed {targetVictim.gameObject.name}! Hold the button for {strangleHoldTime}s...");

        // --- PHASE 2: LOCKED STRUGGLE ---
        BeginStrangleLock(targetVictim);

        float timer = 0f;
        while (strangleButtonHeld && timer < strangleHoldTime)
        {
            if (isGhost || isStunned || isArrested || isBeingPushed)
                break;

            if (strangleVictim == null || strangleVictim.isGhost)
            {
                Debug.Log("Target is already dead!");
                break;
            }

            // Slow orbit + follow, and re-face the victim
            UpdateStrangleLock();

            // Cancel if the victim opens up more distance than the grip allows (e.g. sprints off)
            if (Vector3.Distance(transform.position, strangleVictim.transform.position) > strangleGrabDistance + strangleBreakSlack)
            {
                Debug.Log($"{strangleVictim.gameObject.name} broke free from your grasp!");
                break;
            }

            timer += Time.deltaTime;
            yield return null; // Wait for the next frame
        }

        // --- EXECUTION ---
        if (timer >= strangleHoldTime && strangleVictim != null && !strangleVictim.isGhost)
        {
            // Court members can grab and hold someone, but their strangle never kills.
            if (currentRole == PlayerRole.Court)
            {
                Debug.Log($"{gameObject.name} strangled {strangleVictim.gameObject.name} - but Court members deal no damage.");
            }
            else
            {
                Debug.Log($"Successfully strangled {strangleVictim.gameObject.name}!");
                strangleVictim.TakeDamage(1);
            }
            lastStrangleTime = Time.time; // Apply the full cooldown
        }
        else if (!strangleButtonHeld)
        {
            Debug.Log("Strangulation cancelled! You let go too early.");
        }

        // Release the lock and hand control back to normal movement
        EndStrangleLock();
        isReachingToStrangle = false;
        strangleCoroutine = null;
    }

    // The strangle only connects with the player the crosshair is actually on (targetPlayer, set by
    // CheckForInteractable - the same thing that turns the crosshair green) and only once close enough.
    private PlayerController FindStrangleVictim()
    {
        PlayerController v = targetPlayer;
        if (v == null || v == this || v.isGhost || v.currentRole == PlayerRole.Corrupted) return null;

        Vector3 flat = v.transform.position - transform.position;
        flat.y = 0f;
        if (flat.magnitude > strangleConnectDistance) return null; // still too far to grab

        return v;
    }

    // World point the strangler's hands reach for: the victim's actual neck/head bone when available.
    private Vector3 StrangleNeckPoint()
    {
        if (strangleVictim != null) return strangleVictim.GetNeckWorldPosition();
        // Post-strangle IK fade-out with no victim: keep the reach close to the body, not the sky.
        return transform.position + transform.forward * 0.35f + Vector3.up * strangleNeckHeight;
    }

    // Resolves (and caches) this player's neck/head bone so a strangler can lock its hands there.
    // Works for humanoid rigs and for the generic Synty rig via the bones FirstPersonHeadHider already points at.
    public Vector3 GetNeckWorldPosition()
    {
        if (cachedNeckAnchor == null)
        {
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
        if (CharController != null)
        {
            strangleIgnoredColliders = victim.GetComponentsInChildren<Collider>();
            foreach (Collider col in strangleIgnoredColliders)
                if (col != null && col.enabled) Physics.IgnoreCollision(CharController, col, true);
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
        if (strangleVictim == null || CharController == null) return;

        Vector3 neck = StrangleNeckPoint();
        Vector3 center = new Vector3(neck.x, transform.position.y, neck.z);

        // Current angle of the strangler around the neck
        Vector3 offset = transform.position - center;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.0001f) offset = -transform.forward;
        float angle = Mathf.Atan2(offset.z, offset.x);

        // A/D shuffles slowly around the circle
        angle -= moveInput.x * strangleOrbitSpeed * Mathf.Deg2Rad * Time.deltaTime;

        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 targetPos = center + dir * strangleGrabDistance;

        // Reposition slowly, so a sprinting victim outruns the grip
        Vector3 newPos = Vector3.MoveTowards(transform.position, targetPos, strangleFollowSpeed * Time.deltaTime);
        Vector3 delta = newPos - transform.position;
        delta.y = -2f * Time.deltaTime; // small downward bias to stay grounded

        if (CharController.enabled) CharController.Move(delta);

        // Keep facing the victim's neck
        Vector3 faceDir = center - transform.position;
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(faceDir);
    }

    private void EndStrangleLock()
    {
        if (CharController != null && strangleIgnoredColliders != null)
        {
            foreach (Collider col in strangleIgnoredColliders)
                if (col != null) Physics.IgnoreCollision(CharController, col, false);
        }
        strangleIgnoredColliders = null;

        isStrangling = false;
        strangleVictim = null;
    }

    // Called from PlayerIKHelper.OnAnimatorIK - reaches the RIGHT hand toward hangReachPos while it
    // holds an item, for the sword-hang deposit minigame.
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

    // ApplyHaulIK now lives on PlayerIKRig; PlayerIKHelper.OnAnimatorIK calls it there directly.

    // Bends the lower spine forward (about the player's right axis) so the model visibly folds at the
    // hips while Ctrl is held. Runs in LateUpdate, after the animator, so it overrides the pose.
    // Skipped while a minigame is driving the right hand (hangReachActive) - the fold would drag the
    // outstretched arm off its aim target; the camera arc alone lowers the view.
    private bool warnedNoSpineBone;
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

    // UpdateHaulIKTarget now lives on PlayerIKRig (ikRig.UpdateHaulIKTarget, called from Update()).

    // Called from PlayerIKHelper.OnAnimatorIK - drives both hands out for a strangle:
    // to the victim's throat when locked, or straight ahead where the player is aiming while hunting.
    public void ApplyStrangleIK(int layerIndex)
    {
        if (animator == null) return;

        bool armsOut = isStrangling || isReachingToStrangle;
        float target = armsOut ? 1f : 0f;
        strangleHandIKWeight = Mathf.Lerp(strangleHandIKWeight, target, Time.deltaTime * ikBlendSpeed);

        if (strangleHandIKWeight <= 0.01f)
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            return;
        }

        Vector3 grip;
        if (strangleVictim != null)
        {
            grip = strangleVictim.GetNeckWorldPosition();
            // Pull toward the strangler so the hands close on the FRONT of the throat, not the bone pivot.
            Vector3 toMe = transform.position - grip;
            toMe.y = 0f;
            if (toMe.sqrMagnitude > 0.0001f) grip += toMe.normalized * strangleHandInset;
        }
        else
        {
            // Hunting for a target: reach straight out along the aim.
            grip = playerCamera.position + playerCamera.forward * strangleReachDistance;
        }

        // Drop (or raise) the grip so the hands sit on the throat rather than up at the jaw.
        grip += Vector3.up * strangleHandHeightOffset;

        // Stable reach direction (flattened) so the hands hold one orientation instead of following the walk cycle.
        Vector3 reachDir = grip - playerCamera.position;
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

        ApplyOneStrangleHand(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, grip + apart, rightRot, elbowBase + transform.right * 0.3f);
        ApplyOneStrangleHand(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, grip - apart, leftRot, elbowBase - transform.right * 0.3f);
    }

    private void ApplyOneStrangleHand(AvatarIKGoal goal, AvatarIKHint elbow, Vector3 handPos, Quaternion handRot, Vector3 elbowPos)
    {
        animator.SetIKPositionWeight(goal, strangleHandIKWeight);
        animator.SetIKPosition(goal, handPos);
        animator.SetIKRotationWeight(goal, strangleHandIKWeight);
        animator.SetIKRotation(goal, handRot);
        animator.SetIKHintPositionWeight(elbow, strangleHandIKWeight * 0.5f);
        animator.SetIKHintPosition(elbow, elbowPos);
    }

    private void ExecutePunch()
    {
        Debug.Log($"{gameObject.name} throws a punch!");

        // Cast a thick sphere forward. We omit the layer mask so it can hit players OR physics items
        if (Physics.SphereCast(playerCamera.position, punchRadius, playerCamera.forward, out RaycastHit hit, punchRange))
        {
            // 1. Did we hit a Player?
            PlayerController victim = hit.collider.GetComponent<PlayerController>();
            if (victim != null && victim != this && !victim.isGhost)
            {
                Debug.Log($"Punched {victim.gameObject.name}!");
                
                // Calculate push direction (from puncher to victim)
                Vector3 pushDirection = (victim.transform.position - transform.position).normalized;
                pushDirection.y = 0; // Prevent launching them into the sky
                
                victim.ApplyPushback(pushDirection, pushbackForce, pushbackDuration);
                return;
            }

            // 2. Did we hit an Item/Physics object?
            Rigidbody rb = hit.collider.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                Debug.Log($"Punched an item!");
                // Shove the item exactly the direction the camera is looking
                rb.AddForce(playerCamera.forward * (pushbackForce / 2f), ForceMode.Impulse);
            }
        }
    }

    // ExecuteThrow now lives on PlayerInventory (inventory.ExecuteThrow).

    public void OnUsePowerUp1(InputValue value) { if (value.isPressed) EquipCorruptedSlot(0); }
    public void OnUsePowerUp2(InputValue value) { if (value.isPressed) EquipCorruptedSlot(1); }
    public void OnUsePowerUp3(InputValue value) { if (value.isPressed) EquipCorruptedSlot(2); }

    public void OnScrollWheel(InputValue value)
    {
        if (currentRole != PlayerRole.Corrupted || isGhost) return;
        
        float scrollY = value.Get<Vector2>().y;
        if (Mathf.Abs(scrollY) < 0.1f) return; 

        // Hardware spam prevention
        if (Time.time < lastScrollTime + scrollCooldown) return;
        lastScrollTime = Time.time;

        // 1. Build a dynamic list of valid stops: Always include -1 (empty hands), 
        //    plus any slot index that actually contains an item.
        System.Collections.Generic.List<int> validStops = new System.Collections.Generic.List<int>();
        validStops.Add(-1); // Empty hands is always a valid stop
        
        for (int i = 0; i < corruptedInventory.Length; i++)
        {
            if (corruptedInventory[i] != null)
            {
                validStops.Add(i);
            }
        }

        // If inventory is completely empty, stay unequipped
        if (validStops.Count <= 1)
        {
            EquipCorruptedSlot(-1);
            return;
        }

        // 2. Find where we currently are in our list of valid stops
        int currentIndex = validStops.IndexOf(activeSlotIndex);
        if (currentIndex == -1) currentIndex = 0; 

        // 3. REVERSED DIRECTION: Scroll Up increases index (+1), Scroll Down decreases index (-1)
        int direction = scrollY > 0 ? 1 : -1; 

        // 4. Calculate new index with smooth looping
        int newIndex = currentIndex + direction;
        if (newIndex >= validStops.Count) newIndex = 0;          // Loop forward back to start
        if (newIndex < 0) newIndex = validStops.Count - 1;       // Loop backward to end

        int newSlot = validStops[newIndex];
        
        if (newSlot != activeSlotIndex)
        {
            EquipCorruptedSlot(newSlot);
        }
    }

    private void EquipCorruptedSlot(int index)
    {
        if (currentRole != PlayerRole.Corrupted || isGhost) return;

        // 1. Clean up the currently held visual
        if (activePowerUpVisual != null)
        {
            Destroy(activePowerUpVisual);
        }

        // 2. Are we unequipping everything intentionally? (Index -1)
        if (index == -1)
        {
            activeSlotIndex = -1;
            if (UIManager.Instance != null) UIManager.Instance.HighlightSlot(-1);
            return;
        }

        // 3. Update slot tracking
        activeSlotIndex = index;
        PowerUpData data = corruptedInventory[index];

        // 4. If the slot is EMPTY, highlight it but don't spawn anything in-hand
        if (data == null)
        {
            if (UIManager.Instance != null) UIManager.Instance.HighlightSlot(activeSlotIndex);
            Debug.Log($"Equipped empty slot {index + 1}");
            return; // Stop here!
        }

        // 5. We are equipping a VALID power-up!
        if (GetHeldItem() != null)
        {
            Debug.Log("Dropped standard item to pull out power-up!");
            ClearHeldItem();
        }
        if (GetLeftHeldItem() != null)
        {
            Debug.Log("Dropped off-hand item to pull out power-up!");
            ClearLeftHeldItem();
        }

        // Spawn the physical 3D model into their hand
        if (data.iconPrefab != null && powerUpHoldPoint != null)
        {
            activePowerUpVisual = Instantiate(data.iconPrefab, powerUpHoldPoint.position, powerUpHoldPoint.rotation, powerUpHoldPoint);
        }

        // Update the UI
        if (UIManager.Instance != null) UIManager.Instance.HighlightSlot(activeSlotIndex);
        Debug.Log($"Equipped {data.powerUpName} in slot {index + 1}");
    }

    private bool ExecutePowerUp(PowerUpData powerUp)
    {
        Debug.Log($"--- EXECUTING POWER-UP: {powerUp.powerUpName} ---");

        switch (powerUp.powerUpType)
        {
            case PowerUpType.Daggers:
                // Consume the item instantly, but pass the power-up data to the coroutine 
                // in case we want to refund it later!
                StartCoroutine(DaggerStrikeRoutine(powerUp));
                return true;

            case PowerUpType.InvisibilityPotion:
                StartCoroutine(HandleInvisibility(5f));
                return true;

            case PowerUpType.Traps:
                // Cast a ray slightly further to check for a valid floor
                Ray rayTrap = new Ray(playerCamera.position, playerCamera.forward);
                int environmentMask = ~characterLayer; // Ignore players
                
                if (Physics.Raycast(rayTrap, out RaycastHit hitTrap, InteractionRange * 1.5f, environmentMask))
                {
                    if (hitTrap.normal.y > 0.8f) // Ensures it's a flat floor
                    {
                        if (trapPrefab != null)
                        {
                            Instantiate(trapPrefab, hitTrap.point, Quaternion.identity);
                            Debug.Log("Trap deployed!");
                        }
                        return true; // SUCCESS
                    }
                    else
                    {
                        Debug.Log("Surface is too steep to place a trap.");
                        return false; // FAIL
                    }
                }
                Debug.Log("You must look at the ground to place a trap.");
                return false; // FAIL

            case PowerUpType.TargetedSabotage:
                if (currentTarget != null && currentTarget is TaskDepositStation station)
                {
                    station.isSabotaged = true;
                    Debug.Log($"Sabotaged the {station.gameObject.name}! The next Innocent will be stunned.");
                    return true;
                }
                else
                {
                    Debug.Log("You must be looking at a Task Deposit Station to sabotage it!");
                    return false;
                }

            case PowerUpType.SpymastersLedger:
                List<Transform> vipTargets = new List<Transform>();
                if (RoleManager.Instance != null)
                {
                    if (RoleManager.Instance.currentKing != null && !RoleManager.Instance.currentKing.isGhost)
                        vipTargets.Add(RoleManager.Instance.currentKing.transform);
                    
                    if (RoleManager.Instance.currentKingsguard != null && !RoleManager.Instance.currentKingsguard.isGhost)
                        vipTargets.Add(RoleManager.Instance.currentKingsguard.transform);
                }
                
                if (WaypointManager.Instance != null && vipTargets.Count > 0)
                {
                    WaypointManager.Instance.ShowSpymasterWaypoints(vipTargets, 10f);
                    Debug.Log("Spymaster's Ledger used! High-value targets revealed for 10 seconds.");
                    return true;
                }
                Debug.Log("Spymaster's Ledger failed. High-value targets are dead or unavailable.");
                return false;

            case PowerUpType.StolenHeraldry:
                StartCoroutine(StolenHeraldryRoutine(15f));
                return true;

            case PowerUpType.AlchemistsBlindingAsh:
                Collider[] hitColliders = Physics.OverlapSphere(transform.position, 10f, characterLayer);
                int blindedCount = 0;
                
                foreach (Collider hitC in hitColliders)
                {
                    PlayerController victim = hitC.GetComponent<PlayerController>();
                    if (victim != null && victim != this && !victim.isGhost && victim.currentRole != PlayerRole.Corrupted)
                    {
                        victim.ApplyBlindness(5f); 
                        blindedCount++;
                    }
                }
                Debug.Log($"Blinding Ash shattered! Blinded {blindedCount} innocent players.");
                return true;

            case PowerUpType.FoolsIllusion:
                if (illusionPrefab != null)
                {
                    Instantiate(illusionPrefab, transform.position, transform.rotation);
                    Debug.Log("Fool's Illusion deployed! It will vanish in 10 seconds.");
                }
                return true;
        }
        
        return false; // Fallback
    }

    // --- NEW: STATUS EFFECTS & COROUTINES ---
    public void ApplyStun(float duration)
    {
        if (isStunned || isGhost) return; // Don't stack stuns or stun ghosts
        StartCoroutine(StunRoutine(duration));
    }

    private System.Collections.IEnumerator StunRoutine(float duration)
    {
        isStunned = true;
        Debug.Log($"<color=#E74C3C>{gameObject.name} is STUNNED for {duration} seconds!</color>");
        
        yield return new WaitForSeconds(duration);
        
        isStunned = false;
        Debug.Log($"{gameObject.name} is no longer stunned.");
    }

    // --- NEW: DAGGER DELAY & COUNTER-PLAY ---
    private System.Collections.IEnumerator DaggerStrikeRoutine(PowerUpData daggerData)
    {
        Debug.Log($"<color=#E74C3C>{gameObject.name} readies a dagger...</color>");
        
        // 1.5 second wind-up delay (Player movement is NOT restricted here!)
        yield return new WaitForSeconds(1.5f);
        
        Debug.Log($"<color=#C0392B>{gameObject.name} strikes!</color>");

        // 1. Fire the lethal raycast
        RaycastHit[] hits = Physics.SphereCastAll(playerCamera.position, 0.5f, playerCamera.forward, 2.5f, characterLayer);
        bool hitConnected = false;

        // 2. Loop through every object we hit
        foreach (RaycastHit hit in hits)
        {
            PlayerController victim = hit.collider.GetComponent<PlayerController>();
            
            if (victim != null && victim != this && !victim.isGhost && victim.currentRole != PlayerRole.Corrupted)
            {
                // 3. CHECK FOR THE ROYAL BLOCK
                if ((victim.currentRole == PlayerRole.King || victim.currentRole == PlayerRole.Kingsguard) && victim.isBlocking)
                {
                    Debug.Log($"<color=#F1C40F>Blocked! {victim.gameObject.name} deflected the assassination attempt!</color>");
                    hitConnected = true; 
                    break; // Attack is blocked, item is fully consumed
                }
                else
                {
                    Debug.Log($"Stabbed {victim.gameObject.name} with a dagger!");
                    victim.TakeDamage(1); 
                    hitConnected = true;
                    break; // Attack succeeds, item is fully consumed
                }
            }
        }
        
        if (!hitConnected)
        {
            Debug.Log("The dagger swing missed entirely! (Item consumed)");
        }
    }

    private System.Collections.IEnumerator HandleInvisibility(float duration)
    {
        Debug.Log("Invisibility Activated!");
        
        // Turn off all meshes on the player
        foreach (Renderer r in playerRenderers) 
        { 
            if (r != null) r.enabled = false; 
        }
        
        yield return new WaitForSeconds(duration);
        
        // Turn them back on
        foreach (Renderer r in playerRenderers) 
        { 
            if (r != null) r.enabled = true; 
        }
        Debug.Log("Invisibility Faded.");
    }
    
    public void ApplyBlindness(float duration)
    {
        if (isGhost) return; // Ghosts don't get blinded
        StartCoroutine(BlindnessRoutine(duration));
    }

    private System.Collections.IEnumerator BlindnessRoutine(float duration)
    {
        Debug.Log($"<color=#8E44AD>{gameObject.name} was hit by Blinding Ash!</color>");
        
        Camera cam = playerCamera.GetComponent<Camera>();
        if (cam != null)
        {
            // Save their normal vision distance (usually 1000)
            float originalFarClip = cam.farClipPlane;
            
            // Drop it to 5 units so they can only see right in front of their face
            cam.farClipPlane = 5f;
            
            yield return new WaitForSeconds(duration);
            
            // Restore normal vision
            cam.farClipPlane = originalFarClip;
        }
        else
        {
            yield return new WaitForSeconds(duration);
        }
        
        Debug.Log($"{gameObject.name}'s vision has cleared.");
    }

    private System.Collections.IEnumerator StolenHeraldryRoutine(float duration)
    {
        // 1. Gather all living innocent players to steal an identity from
        List<PlayerController> innocents = new List<PlayerController>();
        if (RoleManager.Instance != null)
        {
            foreach (PlayerController p in RoleManager.Instance.allPlayers)
            {
                if (!p.isGhost && p.currentRole != PlayerRole.Corrupted && p != this) 
                    innocents.Add(p);
            }
        }

        if (innocents.Count == 0)
        {
            Debug.Log("No living innocents left to disguise as!");
            yield break;
        }

        // 2. Pick a random innocent
        PlayerController stolenIdentity = innocents[Random.Range(0, innocents.Count)];
        
        // 3. Save the Corrupted player's real data
        string originalName = gameObject.name;
        Material originalMat = null;
        if (playerRenderers != null && playerRenderers.Length > 0 && playerRenderers[0] != null)
        {
            originalMat = playerRenderers[0].material;
        }

        // 4. APPLY THE DISGUISE
        gameObject.name = stolenIdentity.gameObject.name;
        if (originalMat != null)
        {
            // Copy the innocent's material color/texture
            Renderer targetRenderer = stolenIdentity.GetComponentInChildren<Renderer>();
            if (targetRenderer != null) playerRenderers[0].material = targetRenderer.material;
        }

        Debug.Log($"<color=#F1C40F>Stolen Heraldry active! You look exactly like {stolenIdentity.gameObject.name}.</color>");
        
        yield return new WaitForSeconds(duration);

        // 5. REMOVE THE DISGUISE
        gameObject.name = originalName;
        if (originalMat != null && playerRenderers.Length > 0 && playerRenderers[0] != null)
        {
            playerRenderers[0].material = originalMat;
        }
        
        Debug.Log("<color=#F1C40F>Your disguise has worn off!</color>");
    }

    // --- NEW: PUSHBACK PHYSICS ---
    public void ApplyPushback(Vector3 direction, float force, float duration)
    {
        if (isGhost) return; // Can't punch ghosts
        StartCoroutine(PushbackRoutine(direction, force, duration));
    }

    private System.Collections.IEnumerator PushbackRoutine(Vector3 direction, float force, float duration)
    {
        isBeingPushed = true;
        float timer = 0f;

        while (timer < duration)
        {
            // Smoothly decay the force to 0 over the duration of the slide
            float currentForce = Mathf.Lerp(force, 0f, timer / duration);
            
            // Move the CharacterController along the X/Z axis
            CharController.Move(direction * currentForce * Time.deltaTime);
            
            timer += Time.deltaTime;
            yield return null; // Wait for next frame
        }

        isBeingPushed = false;
    }

    // --- NEW: ROYAL WEAPON BLOCKING ---
    private System.Collections.IEnumerator RoyalWeaponBlockRoutine(RoyalWeapon weapon)
    {
        if (isBlocking) yield break; // Prevent them from spamming the block button

        if (isDraggingPrisoner)
        {
            Debug.Log("You cannot block while your hands are full dragging a prisoner!");
            yield break;
        }

        Debug.Log($"<color=#3498DB>{gameObject.name} raises the {weapon.DisplayName} to block!</color>");
        isBlocking = true;
        
        // Wait for the duration of the block
        yield return new WaitForSeconds(weapon.blockDuration);
        
        isBlocking = false;
        Debug.Log($"{gameObject.name} lowers their guard.</color>");
    }

    // --- UPDATED: CUSTODY CORE MECHANICS ---
    private void ExecuteArrest()
    {
        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 2.5f, characterLayer))
        {
            PlayerController victim = hit.collider.GetComponent<PlayerController>();
            if (victim != null && !victim.isGhost && victim != this)
            {
                // If they are already arrested, just pick up the leash (costs no quota)
                if (victim.isArrested)
                {
                    isDraggingPrisoner = true;
                    currentPrisoner = victim;
                    victim.BecomeArrested(this); 
                    Debug.Log($"Grabbed {victim.gameObject.name}'s leash!");
                    return;
                }

                // Otherwise, brand new arrest
                if (arrestQuota <= 0)
                {
                    Debug.Log("You are out of shackles!");
                    return;
                }
                
                arrestQuota--;
                isDraggingPrisoner = true;
                currentPrisoner = victim;
                Debug.Log($"<color=#3498DB>Arrested {victim.gameObject.name}! {arrestQuota} shackles left.</color>");
                victim.BecomeArrested(this);
            }
        }
    }

    private void TransferPrisoner(PlayerController targetRoyal)
    {
        Debug.Log($"Handed off {currentPrisoner.gameObject.name} to {targetRoyal.gameObject.name}!");
                    
        // Transfer the custody variables
        targetRoyal.isDraggingPrisoner = true;
        targetRoyal.currentPrisoner = this.currentPrisoner;
                    
        // Update the prisoner's target (The Breakout Timer WILL NOT reset)
        this.currentPrisoner.BecomeArrested(targetRoyal); 
                    
        // Clear our own hands
        this.isDraggingPrisoner = false;
        this.currentPrisoner = null;
    }

    private void DropLeash()
    {
        isDraggingPrisoner = false;
        if (currentPrisoner != null)
        {
            currentPrisoner.currentCaptor = null; // Setting this to null instantly breaks their follow loop
            Debug.Log($"Dropped {currentPrisoner.gameObject.name}'s leash. They are still frozen!");
            currentPrisoner = null;
        }
    }

    private void LockPrisonerToGallows(Gallows gallows)
    {
        if (currentPrisoner != null)
        {
            // Snap them to the exact spot behind the cube
            currentPrisoner.transform.position = gallows.executionSpot.position;
            currentPrisoner.transform.rotation = gallows.executionSpot.rotation;
            
            // Stop the ticking breakout timer!
            if (currentPrisoner.breakoutTimerCoroutine != null)
            {
                currentPrisoner.StopCoroutine(currentPrisoner.breakoutTimerCoroutine);
                currentPrisoner.breakoutTimerCoroutine = null;
            }

            currentPrisoner.currentCaptor = null; 
            isDraggingPrisoner = false;
            
            Debug.Log($"<color=#9B59B6>{currentPrisoner.gameObject.name} has been locked to the Gallows!</color>");
            // --- NEW: TRIGGER GALLOWS MEETING & PASS CONDEMNED PLAYER ---
            if (VotingManager.Instance != null) 
            {
                VotingManager.Instance.condemnedPlayer = currentPrisoner;
            }
            if (MatchManager.Instance != null) 
            {
                MatchManager.Instance.TriggerGallowsMeeting();
            }
            currentPrisoner = null;
        }
    }

    public void BecomeArrested(PlayerController captor)
    {
        bool wasAlreadyArrested = isArrested;
        
        isArrested = true;
        currentCaptor = captor;
        Debug.Log($"<color=#E74C3C>You are under arrest by {captor.gameObject.name}!</color>");

        // 1. Force drop whatever is in their hands (both hands)
        if (GetHeldItem() != null)
        {
            Debug.Log("You dropped your task item!");
            ClearHeldItem();
        }
        if (GetLeftHeldItem() != null)
        {
            Debug.Log("You dropped your off-hand item!");
            ClearLeftHeldItem();
        }

        // 2. Force Corrupted to unequip any active power-ups
        if (activeSlotIndex != -1)
        {
            EquipCorruptedSlot(-1);
        }

        // 3. Start the forced physical escort routine
        StartCoroutine(CustodyFollowRoutine());

        // 4. START THE BREAKOUT TIMER (Only if this is a fresh arrest!)
        if (!wasAlreadyArrested)
        {
            if (breakoutTimerCoroutine != null) StopCoroutine(breakoutTimerCoroutine);
            breakoutTimerCoroutine = StartCoroutine(BreakoutTimerRoutine());
        }
    }

    private System.Collections.IEnumerator CustodyFollowRoutine()
    {
        // This runs constantly on the PRISONER, dragging them along
        while (isArrested && currentCaptor != null)
        {
            // Calculate a spot exactly 1.5 units in front of the Royal
            Vector3 targetPosition = currentCaptor.transform.position + (currentCaptor.transform.forward * 1.5f);
            
            // Move the CharacterController smoothly to that exact spot
            Vector3 moveDelta = targetPosition - transform.position;
            CharController.Move(moveDelta);
            
            // Force the prisoner to face the same way as the Royal
            transform.rotation = currentCaptor.transform.rotation;

            yield return null; // Update every single frame
        }
    }

    // --- NEW: THE BREAKOUT TIMER ---
    private System.Collections.IEnumerator BreakoutTimerRoutine()
    {
        // The prisoner has exactly 30 seconds before they violently break free
        yield return new WaitForSeconds(30f);

        // If they are still arrested after 30 seconds, execute the breakout!
        if (isArrested)
        {
            Debug.Log("<color=#E74C3C>The prisoner broke free from their restraints!</color>");
            isArrested = false;

            if (currentCaptor != null)
            {
                Debug.Log($"<color=#F39C12>{currentCaptor.gameObject.name} was stunned by the escaping prisoner!</color>");
                
                // Clear the Royal's hands
                currentCaptor.isDraggingPrisoner = false;
                currentCaptor.currentPrisoner = null;
                
                // Stun the Royal for 3 seconds
                currentCaptor.ApplyStun(3f); 
            }

            // Completely break the follow loop
            currentCaptor = null;
        }
    }
    #endregion

    #region Core FPS Logic
    // HandleMovement now lives on PlayerMotor (motor.HandleMovement, called from Update()).
    // HandleRotation now lives on PlayerLook (look.HandleRotation, called from Update()).

    // ApplyLeanCameraArc moved to PlayerLook, called at the end of Update() - see the comment there.
    // These two stay in LateUpdate because they pose bones (spine / fingers) and must run AFTER the
    // Animator has evaluated this frame's pose (which happens between Update and LateUpdate), or the
    // Animator would immediately overwrite them.
    void LateUpdate()
    {
        ApplyLeanSpineBend();
        ikRig.ApplyHandGripPose();
    }

    // CollectHandGripBones, ComputeCurlDelta, and ApplyHandGripPose now live on PlayerIKRig
    // (ikRig.ApplyHandGripPose is called from LateUpdate() above).

    // ApplyLeanCameraArc now lives on PlayerLook (look.ApplyLeanCameraArc, called from Update()).
    #endregion

    #region Social Deduction Mechanics

    // Resolves what a hit collider means for interaction:
    //  1. a grabbable PickupItem ON the exact collider (a loose item wins over a station behind it)
    //  2. any IInteractable ON the exact collider
    //  3. a TaskDepositStation ANCESTOR - so aiming at a chest's lid/sub-mesh deposits, and it beats
    //     a round-switch PickupItem that shares the same chest hierarchy
    //  4. any IInteractable ancestor
    private IInteractable ResolveInteractable(Collider col)
    {
        if (col == null) return null;

        PickupItem exactPickup = col.GetComponent<PickupItem>();
        if (exactPickup != null) return exactPickup;

        IInteractable exact = col.GetComponent<IInteractable>();
        if (exact != null) return exact;

        TaskDepositStation station = col.GetComponentInParent<TaskDepositStation>();
        if (station != null) return station;

        return col.GetComponentInParent<IInteractable>();
    }

    private void CheckForInteractable()
    {
        // --- NEW: CUSTODY UI PROMPTS & PROXIMITY CHECKS ---
        if (isDraggingPrisoner)
        {
            if (sceneGallows == null) sceneGallows = FindAnyObjectByType<Gallows>();
            
            bool nearGallows = sceneGallows != null && Vector3.Distance(transform.position, sceneGallows.transform.position) <= gallowsRange;
            
            bool nearRoyal = false;
            PlayerController nearbyRoyal = null;
            Collider[] royalHits = Physics.OverlapSphere(transform.position, InteractionRange, characterLayer);
            
            foreach (Collider c in royalHits)
            {
                PlayerController p = c.GetComponent<PlayerController>();
                if (p != null && p != this && !p.isGhost && (p.currentRole == PlayerRole.King || p.currentRole == PlayerRole.Kingsguard))
                {
                    nearRoyal = true;
                    nearbyRoyal = p;
                    break;
                }
            }

            if (nearGallows)
            {
                if (interactionUI != null) interactionUI.text = "Press <color=#F4D03F>[Right Click]</color> to Lock to Gallows\nPress <color=#F4D03F>[Q]</color> to Pardon";
                if (crosshair != null) crosshair.color = activeCrosshairColor;
                targetUIAlpha = 1f;
                return; // Stop normal raycast UI completely
            }
            else if (nearRoyal && !nearbyRoyal.isDraggingPrisoner)
            {
                if (interactionUI != null) interactionUI.text = $"Press <color=#F4D03F>[Right Click]</color> to Handoff to {nearbyRoyal.gameObject.name}\nPress <color=#F4D03F>[Q]</color> to Pardon";
                if (crosshair != null) crosshair.color = activeCrosshairColor;
                targetUIAlpha = 1f;
                return; 
            }
            else
            {
                if (interactionUI != null) interactionUI.text = "Press <color=#F4D03F>[Right Click]</color> to Drop Leash\nPress <color=#F4D03F>[Q]</color> to Pardon";
                if (crosshair != null) crosshair.color = normalCrosshairColor;
                targetUIAlpha = 1f;
                return; 
            }
        }

        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        bool foundValidTarget = false; 

        // 1. Get an array of EVERYTHING the ray hits within range
        RaycastHit[] hits = Physics.RaycastAll(ray, InteractionRange, interactableLayer | characterLayer);
        
        float closestDistance = float.MaxValue;
        bool hasValidHit = false;
        RaycastHit closestHit = new RaycastHit(); // Initialize an empty hit

        float closestPickupDistance = float.MaxValue;
        bool hasPickupHit = false;
        RaycastHit closestPickupHit = new RaycastHit();
        const float pickupPreferenceSlack = 1.0f; // how far behind the closest hit a grabbable item may sit and still win

        // 2. Loop through all the hits to find the closest object that ISN'T our own body
        foreach (RaycastHit hit in hits)
        {
            // If the ray hit our own player capsule, completely ignore it and move to the next hit
            if (hit.collider.gameObject == this.gameObject) continue;

            // Track the closest valid object in front of us
            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                hasValidHit = true;
            }

            // Also track the closest grabbable item, so a sword leaning against a deposit station
            // (whose big collider is hit first) can still be picked up.
            if (hit.distance < closestPickupDistance && hit.collider.GetComponent<PickupItem>() != null)
            {
                closestPickupDistance = hit.distance;
                closestPickupHit = hit;
                hasPickupHit = true;
            }
        }

        // Prefer a grabbable item when it's at roughly the same spot as the closest hit.
        if (hasPickupHit && closestPickupDistance <= closestDistance + pickupPreferenceSlack)
        {
            closestHit = closestPickupHit;
            hasValidHit = true;
        }

        // Fallback: the layer-masked cast landed on nothing interactable (common when the aim is on a
        // sub-part like a chest LID whose collider is above the body's own collider). Do one plain
        // raycast and route the hit up to its parent interactable.
        if (ResolveInteractable(hasValidHit ? closestHit.collider : null) == null
            && Physics.Raycast(ray, out RaycastHit plainHit, InteractionRange, Physics.DefaultRaycastLayers)
            && plainHit.collider.gameObject != this.gameObject
            && ResolveInteractable(plainHit.collider) != null)
        {
            closestHit = plainHit;
            hasValidHit = true;
        }

        // 3. If we successfully found a target (that isn't us), process it
        if (hasValidHit)
        {
            IInteractable interactable = ResolveInteractable(closestHit.collider);
            
            if (interactable != null) 
            {
                if (currentTarget != interactable)
                {
                    currentTarget = interactable;
                    targetPlayer = null;
                    if (crosshair != null) crosshair.color = activeCrosshairColor;
                }
               
                // --- NEW: DYNAMIC POWER-UP UI PROMPTS ---
                PowerUpPickup powerUp = currentTarget as PowerUpPickup;
                RoyalWeapon royalWeapon = currentTarget as RoyalWeapon;

                if (isGhost && (powerUp != null || royalWeapon != null))
                {
                    // Ghosts cannot see or interact with weapons/powerups
                    currentTarget = null;
                    if (crosshair != null) crosshair.color = normalCrosshairColor;
                    targetUIAlpha = 0f;
                }
                else
                {
                    if (powerUp != null && interactionUI != null)
                    {
                        if (currentRole == PlayerRole.Corrupted)
                            interactionUI.text = $"Press <color=#F4D03F>[E]</color> to pick up <color=#E74C3C>{powerUp.powerUpData.powerUpName}</color>";
                        else
                            interactionUI.text = $"Press <color=#F4D03F>[E]</color> to examine strange object";
                    }
                    else if (interactionUI != null)
                    {
                        interactionUI.text = currentTarget.GetInteractionPrompt();
                    }
                    
                    targetUIAlpha = 1f;
                    foundValidTarget = true;
                }
                // ----------------------------------------
                
                targetUIAlpha = 1f;
                foundValidTarget = true;
            }
            // SCENARIO 2: We hit another player (Character Layer)
            else 
            {
                PlayerController otherPlayer = closestHit.collider.GetComponent<PlayerController>();
                
                if (otherPlayer != null)
                {
                    currentTarget = null; 
                    targetPlayer = otherPlayer; 
                    
                    if (interactionUI != null) 
                    {
                        List<string> prompts = new List<string>();

                        // 1. MULTIPLAYER TASK (Available to Court, Corrupted, King, & Kingsguard)
                        if (GetHeldItem() != null && GetHeldItem().requiresPartner)
                        {
                            prompts.Add($"Press <color=#F4D03F>[E]</color> to use <color=#5DADE2>{GetHeldItem().DisplayName}</color> with <color=#58D68D>{otherPlayer.gameObject.name}</color>");
                        }
                        else
                        {
                            prompts.Add($"Press <color=#F4D03F>[E]</color> to interact with <color=#58D68D>{otherPlayer.gameObject.name}</color>");
                        }

                        // 2. KING SPECIFIC
                        if (currentRole == PlayerRole.King && !isGhost)
                        {
                            prompts.Add($"Press <color=#F4D03F>[F]</color> to appoint <color=#58D68D>{otherPlayer.gameObject.name}</color> as Kingsguard");
                        }

                        // 3. ARREST MECHANICS (King & Kingsguard)
                        if ((currentRole == PlayerRole.King || currentRole == PlayerRole.Kingsguard) && !isGhost)
                        {
                            if (otherPlayer.isArrested)
                                prompts.Add($"Press <color=#F4D03F>[Right Click]</color> to grab <color=#58D68D>{otherPlayer.gameObject.name}</color>'s leash");
                            else
                                prompts.Add($"Press <color=#F4D03F>[Right Click]</color> to arrest <color=#58D68D>{otherPlayer.gameObject.name}</color>");
                        }

                        // 4. CORRUPTED MECHANICS (Corrupted)
                        // We check to ensure the target is alive and NOT a fellow Corrupted player
                        if (currentRole == PlayerRole.Corrupted && !isGhost && !otherPlayer.isGhost && otherPlayer.currentRole != PlayerRole.Corrupted)
                        {
                            prompts.Add($"Hold <color=#F4D03F>[Right Click]</color> to strangle <color=#E74C3C>{otherPlayer.gameObject.name}</color>");
                        }

                        // Combine all valid prompts into a clean, multi-line display
                        interactionUI.text = string.Join("\n", prompts);
                    }
                    
                    if (crosshair != null) crosshair.color = activeCrosshairColor;
                    targetUIAlpha = 1f;
                    foundValidTarget = true;
                }
            }
        }

        // 4. FALLBACK LOGIC
        if (!foundValidTarget)
        {
            currentTarget = null;
            targetPlayer = null;
           
            if (crosshair != null) crosshair.color = normalCrosshairColor;

            if (GetHeldItem() != null || GetLeftHeldItem() != null)
            {
                if (interactionUI != null)
                {
                    List<string> heldPrompts = new List<string>();

                    if (GetHeldItem() != null)
                    {
                        // UPDATED: E is now explicitly for using, Q is for dropping
                        heldPrompts.Add($"Press <color=#F4D03F>[E]</color> to use or <color=#F4D03F>[Q]</color> to drop <color=#5DADE2>{GetHeldItem().DisplayName}</color>");
                    }

                    if (GetLeftHeldItem() != null)
                    {
                        heldPrompts.Add($"Press <color=#F4D03F>[R]</color> to swap in <color=#5DADE2>{GetLeftHeldItem().DisplayName}</color> (off-hand)");
                    }

                    interactionUI.text = string.Join("\n", heldPrompts);
                }
                targetUIAlpha = 1f;
            }
            else
            {
                targetUIAlpha = 0f;
            }
        }
    }

    private void PerformInteraction()
    {
        // 0. Holding a depositable item near its deposit station: drop it off without needing to aim
        //    at the station (you often can't see past a hauled chest). Skipped when you're already
        //    aiming right at a deposit station - that one takes priority.
        if (GetHeldItem() != null && !(currentTarget is TaskDepositStation) && TryProximityDeposit())
            return;

        // 1. If we are looking at something interactable (Stations, items on the floor), interact with it
        if (currentTarget != null)
        {
            // --- CORRUPTED POWER-UP PICKUP LOGIC ---
            PowerUpPickup powerUp = currentTarget as PowerUpPickup;
            if (powerUp != null)
            {
                if (isGhost) return; // Ghosts cannot pick up power-ups
                if (currentRole == PlayerRole.Corrupted)
                {
                    // Find the first empty slot in the array
                    int emptySlotIndex = -1;
                    for (int i = 0; i < corruptedInventory.Length; i++)
                    {
                        if (corruptedInventory[i] == null)
                        {
                            emptySlotIndex = i;
                            break;
                        }
                    }

                    if (emptySlotIndex != -1) // We found an empty slot!
                    {
                        Debug.Log($"[Corrupted] Picked up power-up: {powerUp.powerUpData.powerUpName} in Slot {emptySlotIndex + 1}");
                        
                        // Assign it to that exact slot
                        corruptedInventory[emptySlotIndex] = powerUp.powerUpData;
                        
                        if (UIManager.Instance != null) UIManager.Instance.UpdateCorruptedInventory(corruptedInventory);
                        
                        Destroy(powerUp.gameObject); // Remove from floor
                        
                        // Clear UI
                        currentTarget = null;
                        targetUIAlpha = 0f; 
                    }
                    else
                    {
                        Debug.Log("Inventory Full! You do not have any empty slots.");
                    }
                }
                else
                {
                    Debug.Log("[Innocent] You poke the mysterious object, but have no idea what it is or how to use it.");
                }
                return; // Stop here so it doesn't run standard interaction logic
            }
            
            // --- ROYAL WEAPON RESTRICTION ---
            RoyalWeapon royalWeapon = currentTarget as RoyalWeapon;
            if (royalWeapon != null)
            {
                if (isGhost) return; // Ghosts cannot pick up weapons
                if (currentRole != royalWeapon.restrictedRole)
                {
                    Debug.Log($"[Denied] Only the {royalWeapon.restrictedRole} may wield this weapon!");
                    return; // Stop here so they cannot pick it up
                }
            }

            currentTarget.OnInteract(this.gameObject);

            // --- UNIVERSAL TASK EVALUATION ---
            GameObject targetObj = (currentTarget as MonoBehaviour)?.gameObject;
            for (int i = activeTasks.Count - 1; i >= 0; i--)
            {
                TaskInstance task = activeTasks[i];
                if (task.EvaluateCurrentStep(this, targetObj))
                {
                    if (TaskManager.Instance != null) TaskManager.Instance.CompleteTask(this, task);
                }

                // NEW: Stop checking other tasks if this click just launched a minigame!
                if (isPlayingMinigame) break;
            }

            // If we interacted with a fixed STATION that carries its own process minigame and no
            // task launched one, fire it anyway - so ANY role can run it (fake the task).
            // A grabbable item is explicitly excluded: you pick it up first, then press [E] again
            // with it in hand to run its minigame (see path 3 below).
            bool targetIsGrabbable = targetObj != null && targetObj.GetComponent<PickupItem>() != null;
            if (!isPlayingMinigame && !targetIsGrabbable
                && currentTarget is TaskStation sourceStation
                && sourceStation.processMinigamePrefab != null)
            {
                StartMinigame(sourceStation.processMinigamePrefab, null, targetObj);
                return;
            }

            RefreshLocalWaypoints();

            // Only update the prompt if a minigame didn't just steal mouse focus
            if (currentTarget != null && interactionUI != null && !isPlayingMinigame)
            {
                interactionUI.text = currentTarget.GetInteractionPrompt();
            }
        }
        // 2. MULTIPLAYER TASK: We are looking at another player
        else if (targetPlayer != null)
        {
            HandlePlayerInteraction(targetPlayer);
        }
        // 3. If we are looking at empty space, but holding an item, try to USE it
        else if (GetHeldItem() != null)
        {
            // A spent item (e.g. an emptied plate) does nothing on [E] - just carry or drop it.
            if (GetHeldItem().Has(ItemState.Spent))
            {
                Debug.Log($"Nothing left to do with the {GetHeldItem().DisplayName}.");
                return;
            }

            // A held item that carries its own minigame ALWAYS launches it on [E].
            // Independent of role, of any related task, or of whether that task was already done.
            if (GetHeldItem().processMinigamePrefab != null)
            {
                StartMinigame(GetHeldItem().processMinigamePrefab, null, GetHeldItem().gameObject);
                return;
            }

            bool taskCompleted = false;

            // --- UNIVERSAL TASK EVALUATION ---
            for (int i = activeTasks.Count - 1; i >= 0; i--)
            {
                TaskInstance task = activeTasks[i];
                // Pass the held item as the target
                if (task.EvaluateCurrentStep(this, GetHeldItem().gameObject))
                {
                    if (TaskManager.Instance != null) TaskManager.Instance.CompleteTask(this, task);
                    taskCompleted = true;
                }

                // NEW: Stop checking other tasks if a minigame was launched!
                if (isPlayingMinigame) break;
            }

            // NEW: If a minigame UI is now open, abort below!
            if (isPlayingMinigame) return;

            // No process minigame on this item, so [E] does nothing here - it's just carried until
            // it reaches wherever it's needed (a deposit station, another player, etc). Items that
            // ARE meant to be worked on with [E] carry a Process Minigame Prefab, which was launched
            // above.
            if (!taskCompleted)
                Debug.Log($"Nothing to do with the {GetHeldItem().DisplayName} here - carry it where it needs to go.");
        }
        else
        {
            Debug.Log("No interactable target in range and hands are empty.");
        }
    }

    // Deposits the held item at the nearest matching TaskDepositStation within depositProximityRange,
    // no aiming required. Returns true if a deposit was attempted (and advances any task step it
    // satisfies, mirroring the aimed-interaction path).
    private bool TryProximityDeposit()
    {
        if (GetHeldItem() == null || TaskLocation.AllLocations == null) return false;

        TaskDepositStation best = null;
        float bestDist = depositProximityRange;

        foreach (TaskLocation loc in TaskLocation.AllLocations)
        {
            if (loc == null) continue;
            TaskDepositStation st = loc.GetComponent<TaskDepositStation>();
            if (st == null || !st.HasFreeSlot() || !st.AcceptsItem(GetHeldItem())) continue;

            float d = Vector3.Distance(transform.position, loc.transform.position);
            if (d <= bestDist) { bestDist = d; best = st; }
        }

        if (best == null) return false;

        best.OnInteract(this.gameObject);

        GameObject stationObj = best.gameObject;
        for (int i = activeTasks.Count - 1; i >= 0; i--)
        {
            TaskInstance task = activeTasks[i];
            if (task.EvaluateCurrentStep(this, stationObj))
            {
                if (TaskManager.Instance != null) TaskManager.Instance.CompleteTask(this, task);
            }
            if (isPlayingMinigame) break;
        }
        return true;
    }

    private void CycleInventory(int direction)
    {
        currentItemIndex += direction;
        if (currentItemIndex > 2) currentItemIndex = 0;
        if (currentItemIndex < 0) currentItemIndex = 2;
        
        Debug.Log($"Switched to item slot: {currentItemIndex}");
    }

    // EquipItem, AttachHaulItem, and PoseHaulItem now live on PlayerInventory - this forwarder is the
    // one TaskDepositStation, PickupItem, ConcreteTaskSteps, and the minigames all still call.
    public void EquipItem(PickupItem newItem) => inventory.EquipItem(newItem);

    // Add these public helpers so external stations can take the item
    public PickupItem GetHeldItem() => inventory.GetHeldItem();

    public void ClearHeldItem() => inventory.ClearHeldItem();

    // --- NEW: DUAL-WIELD HELPERS ---
    public PickupItem GetLeftHeldItem() => inventory.GetLeftHeldItem();

    public void ClearLeftHeldItem() => inventory.ClearLeftHeldItem();

    // True if an item of this identity - carrying every flag in requiredState - is held in EITHER hand.
    public bool IsHoldingItem(ItemDefinition definition, ItemState requiredState = ItemState.None)
        => inventory.IsHoldingItem(definition, requiredState);

    // True if either hand is holding a heavy item.
    public bool IsHoldingHeavyItem() => inventory.IsHoldingHeavyItem();

    // 2. Add this public method anywhere inside the class
    public void AssignRole(PlayerRole newRole)
    {
        currentRole = newRole;
        
        // --- NEW: ROYAL RESILIENCE (HEALTH SYSTEM) ---
        // The King gets extra HP to represent broadsword/shield mitigation
        if (currentRole == PlayerRole.King)
        {
            maxHealth = 3;
            currentHealth = 3;
        }
        else
        {
            maxHealth = 1;
            currentHealth = 1;
        }

        Debug.Log($"[Role Assignment] {gameObject.name} is now: {currentRole} with {currentHealth} HP");
    }

    // Called by UIManager when the pause / settings menu opens or closes. Freezes the player and
    // stops all input so the mouse can be used on the menu.
    public void SetControlsLocked(bool locked)
    {
        controlsLocked = locked;

        // Drop any input already latched so we don't keep moving/looking after the menu opens.
        moveInput = Vector2.zero;
        lookInput = Vector2.zero;
        motor.CancelSprint();
        inventory.StopCharging();
        strangleButtonHeld = false;
        motor.CancelLeanInput(); // legacy Ctrl read in Update() still drives the lean while locked

        // Stop every input action callback from firing while the menu is up.
        if (playerInput != null) playerInput.enabled = !locked;
    }

    // Constrained walking for placement minigames (e.g. SwordHangMinigame). The minigame calls this
    // each frame with a raw move vector (x = strafe, y = forward) it read itself, so the player can
    // shuffle into line with a target while the mouse stays busy aiming. Runs at half walk speed and
    // leashes the player within `radius` metres of `anchor`. Works even while controlsLocked is true
    // because the minigame is driving it directly rather than the normal Update() path.
    public void MinigameWalk(Vector2 move, Vector3 anchor, float radius)
    {
        motor.WalkConstrained(move, anchor, radius);
    }

    // Horizontal-only look for placement minigames. The minigame calls this while the player holds
    // RMB, passing an already-scaled yaw in degrees. Turns the body (and the camera with it) so they
    // can pan across a wide target; the mouse's vertical axis stays free for aiming.
    public void MinigameLookYaw(float degrees)
    {
        look.MinigameLookYaw(degrees);
    }

    // Vertical look for placement minigames. Positive `degrees` = look up (matches mouse-up). Pitch is
    // clamped to the same limits as normal FPS look and applied straight to the camera.
    public void MinigameLookPitch(float degrees)
    {
        look.MinigameLookPitch(degrees);
    }

    // Add this helper method anywhere inside PlayerController
    public void TeleportTo(Transform targetTransform)
    {
        motor.TeleportTo(targetTransform);
    }

    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        motor.TeleportTo(position, rotation);
    }

    // Aim the body + camera at a world point (used when a placement minigame starts).
    public void PointCameraAt(Vector3 worldPoint)
    {
        look.PointCameraAt(worldPoint);
    }

    // --- NEW: COMBAT DAMAGE SYSTEM ---
    public void TakeDamage(int amount)
    {
        if (isGhost) return; // Ghosts cannot take damage

        currentHealth -= amount;
        Debug.Log($"<color=#E74C3C>{gameObject.name} took {amount} damage! Current HP: {currentHealth}</color>");

        if (currentHealth <= 0)
        {
            Debug.Log($"<color=#922B21>{gameObject.name} HAS BEEN KILLED!</color>");
            BecomeGhost();
        }
    }

    public void BecomeGhost()
    {
        if (isGhost) return; // Already a ghost

        Debug.Log($"--- {gameObject.name} HAS BECOME A GHOST ---");
        isGhost = true;

        // 1. Force drop any item they are currently holding (both hands)
        PickupItem heldItem = GetHeldItem();
        if (heldItem != null)
        {
            heldItem.DetachFromHand();
            ClearHeldItem();
        }
        PickupItem leftItem = GetLeftHeldItem();
        if (leftItem != null)
        {
            leftItem.DetachFromHand();
            ClearLeftHeldItem();
        }

        // 2. Change the player's layer to "Ghost" (we will set this up in Unity)
        int ghostLayer = LayerMask.NameToLayer("Ghost");
        if (ghostLayer != -1)
        {
            gameObject.layer = ghostLayer;
            // Optionally change children layers if your player model has multiple parts
            foreach (Transform child in transform)
            {
                child.gameObject.layer = ghostLayer;
            }
        }

        // 3. Hide the physical body so living players can't see it
        // This finds the capsule mesh (and any other visuals) and turns them off
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            r.enabled = false;
        }

        // 4. (Optional) You can increase their movement speed here so ghosts can float around faster
        motor.ScaleWalkSpeed(1.5f);
    }

    public void AssignTasks(List<TaskInstance> newTasks)
    {
        activeTasks.Clear();
        activeTasks.AddRange(newTasks);

        allAssignedTasks.Clear();
        allAssignedTasks.AddRange(newTasks);

        // No InitializeTask() call - each TaskInstance is constructed fresh (step index 0, steps
        // deep-copied and un-completed), so a new instance IS the initialization.

        RefreshLocalWaypoints(); // raises LocalTasksChanged; the HUD + waypoints react. No-op if not local.
    }

    // Called when the player successfully interacts with a task station
    public void RemoveCompletedTask(TaskInstance completedTask)
    {
        if (activeTasks.Contains(completedTask))
        {
            // Remove it from active logic, but keep it in allAssignedTasks!
            activeTasks.Remove(completedTask);

            RefreshLocalWaypoints();
        }
    }

    // Signals that the local player's task list / current step changed. The HUD text and the
    // on-screen waypoints are views that subscribe to GameEvents.LocalTasksChanged - this method
    // must NOT reference UIManager or WaypointManager.
    public void RefreshLocalWaypoints()
    {
        if (!IsLocal) return;
        GameEvents.RaiseLocalTasksChanged(this);
    }
    
    private void HandlePlayerInteraction(PlayerController target)
    {
        if (target == null) return;

        if (target.GetHeldItem() != null)
        {
            Debug.Log("Multiplayer interaction failed: Your helper must be empty-handed.");
            return;
        }

        bool taskCompleted = false;

        // --- NEW: UNIVERSAL TASK EVALUATION ---
        for (int i = activeTasks.Count - 1; i >= 0; i--)
        {
            TaskInstance task = activeTasks[i];
            if (task.EvaluateCurrentStep(this, target.gameObject))
            {
                if (TaskManager.Instance != null) TaskManager.Instance.CompleteTask(this, task);
                taskCompleted = true;
            }
        }

        // 2. Perform the physical action regardless of tasks! (Allows faking)
        if (GetHeldItem() != null && GetHeldItem().requiresPartner)
        {
            Debug.Log($"Used {GetHeldItem().DisplayName} with {target.gameObject.name}!" + (taskCompleted ? " (Task Completed)" : " (Faked Task)"));
            GameObject initiatorItemObj = GetHeldItem().gameObject;
            this.ClearHeldItem();
            Destroy(initiatorItemObj);
        }
        else if (GetHeldItem() == null)
        {
            Debug.Log($"Interacted with {target.gameObject.name}!" + (taskCompleted ? " (Task Completed)" : " (Faked Task)"));
        }

        this.RefreshLocalWaypoints();
    }

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
                        foreach (TaskInstance regressionTask in activeTasks)
                        {
                            regressionTask.CheckForTaskRegression(this);
                        }
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
        RefreshLocalWaypoints();
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

        inventory.ReturnSwappedItem(); // --- NEW: Snap item back to right hand ---

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

        RefreshLocalWaypoints();
    }

    // Called after an item's own minigame is won with no task driving it. Advances any matching
    // active step (without re-launching a minigame) - covers a task player whose ProcessItemStep
    // fired via the item's processMinigamePrefab. Does nothing for a faker with no such task.
    private void ResolveStandaloneItemMinigame()
    {
        // The item may already have been consumed by the minigame, so fall back to the target the
        // minigame was launched against.
        GameObject evalTarget = GetHeldItem() != null ? GetHeldItem().gameObject : activeMinigameTarget;

        for (int i = activeTasks.Count - 1; i >= 0; i--)
        {
            TaskInstance task = activeTasks[i];
            if (task.EvaluateCurrentStep(this, evalTarget, true)) // skipMinigame: true
            {
                if (TaskManager.Instance != null) TaskManager.Instance.CompleteTask(this, task);
            }
        }

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

        inventory.ReturnSwappedItem(); // --- NEW: Snap item back to right hand ---

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("Minigame Cancelled.");

        // Bring the task's waypoint back now that the minigame was abandoned.
        RefreshLocalWaypoints();
    }

    // UpdateMinigameIKTarget and ApplyMinigameIK now live on PlayerIKRig - PlayerController.Update()
    // calls the former each frame, and PlayerIKHelper.OnAnimatorIK calls the latter directly.

    // ReturnSwappedItem now lives on PlayerInventory (inventory.ReturnSwappedItem).
    #endregion
}