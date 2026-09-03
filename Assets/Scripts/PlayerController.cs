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

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float jumpHeight = 1.5f;
    
    [Header("Look Settings")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float upperLookLimit = 80f;
    [SerializeField] private float lowerLookLimit = -80f;

    [Header("Smooth Crouch Settings")]
    [SerializeField] private float crouchTransitionSpeed = 10f;
    
    [Header("Equipment Settings")]
    [SerializeField] private Transform rightHandSocket;
    [SerializeField] private Transform leftHandSocket; // The future rig bone attachment point
    [SerializeField] private Transform rightHandBone;  // The actual rig hand bone (Hand_R) - items glued here follow the animated / IK'd arm
    public Transform RightHandSocket => rightHandSocket;
    public Transform LeftHandSocket => leftHandSocket;
    public Transform RightHandBone => rightHandBone != null ? rightHandBone
        : (animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null);

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
    [HideInInspector] public bool hangReachActive;
    [HideInInspector] public Vector3 hangReachPos;
    [HideInInspector] public Quaternion hangReachRot = Quaternion.identity;
    [HideInInspector] public float hangReachRotWeight; // 0 = keep held pose, 1 = fully align to hangReachRot
    private float hangReachWeight;

    // --- Two-handed haul: both hands lock onto grip points on a carried heavy item ---
    [HideInInspector] public bool haulActive;
    [HideInInspector] public Vector3 haulLeftHandPos, haulRightHandPos;
    [HideInInspector] public Vector3 haulLeftElbowPos, haulRightElbowPos;
    [HideInInspector] public Quaternion haulLeftHandRot = Quaternion.identity, haulRightHandRot = Quaternion.identity;
    [HideInInspector] public bool haulUseRotation;
    private float haulIKWeight;
    private PickupItem currentlyHeldItem; // RIGHT hand: the "active" hand used for throwing, processing, minigames, and partner tasks
    private PickupItem leftHeldItem;      // LEFT hand: the off-hand. Carries a second item; press SwapHands to move it into the active hand
    public int currentItemIndex = 0;
    private float targetHeight;
    private float targetCameraY;
    private float standingCameraY = 0.8f;
    private float crouchingCameraY = 0.5f;

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
    public CharacterController CharController => characterController;

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

    [Header("Throwing Mechanics")]
    public float maxThrowChargeTime = 2.0f; // Maximum seconds the button can be held
    public float baseThrowForce = 25f;      // The baseline force before weight is applied
    [Tooltip("Hold [Q] up to this long = a tap: drop the item in place. Longer = charge and throw.")]
    public float dropTapMaxDuration = 0.2f;

    private float currentThrowCharge = 0f;
    private bool isChargingThrow = false;
    private float dropPressTime = 0f;

    [Header("Power-Up Prefabs & Status")]
    public GameObject trapPrefab;
    public GameObject illusionPrefab;
    private bool isStunned = false;
    private Renderer[] playerRenderers;

    [Header("Minigame State")]
    public bool isPlayingMinigame = false;
    public bool isMinigameLooking = false;
    private GameObject activeMinigameInstance;
    private Transform activeMinigameStation;
    public GameObject activeMinigameTarget;
    public TaskInstance activeMinigameTask; // The task the open minigame belongs to (its waypoint is hidden while playing)
    private bool itemSwappedToLeftHand = false;
    // --- NEW: Tracks the current target for IK logic ---
    public MinigameTargetType currentMinigameTargetType = MinigameTargetType.None;

    [Tooltip("How far left or right (in degrees) a player can look while playing a minigame.")]
    public float minigameLookLimit = 90f;
    private float currentMinigameYaw = 0f; // Tracks how far we have turned

    // --- NEW: Camera Snap Anchors ---
    private Quaternion minigameStartBodyRotation;
    private float minigameStartVerticalRotation;

    [Header("Minigame IK Tracking")]
    [Tooltip("How far in front of the camera the hand should hover while playing.")]
    public float minigameIKDepth = 0.6f; 
    [Tooltip("How fast the hand raises and lowers when opening/closing a minigame.")]
    public float ikBlendSpeed = 8f;

    [Tooltip("Tweak this to rotate the hand bone so the palm faces inward.")]
    public Vector3 rightHandRotationOffset = new Vector3(0f, 0f, 90f);
    
    private float currentIKWeight = 0f;
    private Vector3 ikTargetPosition;

    // Internal State
    private CharacterController characterController;
    private Animator animator; // NEW: Controls the 3D model's animations
    private PlayerInput playerInput;
    public bool controlsLocked { get; private set; } // true while a blocking UI (pause menu) is up
    private Vector2 moveInput;
    private Vector2 lookInput;
    private Vector3 velocity;
    private float verticalRotation = 0f;
    
    // Input States
    private bool isSprinting = false;
    private bool isCrouching = false;
    private bool isGrounded;
    
    // Original CharacterController constraints for crouching
    private float originalHeight;
    private float crouchHeight;

    [Header("Lean (hold Ctrl to bend forward at the hips - reach low stations)")]
    [Tooltip("Bend angle at the hips when fully leaned. The camera swings forward+down on this same arc.")]
    [SerializeField] private float leanAngle = 45f;
    [Tooltip("How fast the lean blends in/out.")]
    [SerializeField] private float leanSpeed = 8f;
    [Tooltip("Height of the hip pivot in the player's LOCAL space (the camera arcs around this point). Lower it if the camera doesn't move far enough forward.")]
    [SerializeField] private float leanHipLocalY = -0.1f;
    [Tooltip("How much the camera pitches while fully leaned, in degrees. Positive = look down, NEGATIVE = tilt UP (keeps your hands in frame while aiming in a minigame). Independent of the body-bend angle.")]
    [SerializeField] private float leanViewPitch = -5f;
    [Range(0f, 1f)]
    [Tooltip("While a minigame drives the right hand, the camera's FORWARD lean travel is scaled by this (the drop is kept). Lower = the hand's aim target stays within arm's reach.")]
    [SerializeField] private float leanMinigameForwardFactor = 0.2f;
    [Tooltip("The lower spine bone to bend forward while leaning (e.g. Spine_01). Empty = the avatar's Spine bone. The bend is SKIPPED while a minigame is driving the right hand, so the outstretched arm stays put for aiming.")]
    [SerializeField] private Transform leanSpineBone;
    private bool isLeaning;
    private float leanBlend;
    private Vector3 camBaseLocalXZ;   // the camera's un-leaned local X/Z offset
    private float camBaseY;           // the camera's un-leaned local Y (driven by the crouch lerp)

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

        characterController = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        // Grab the Animator from the child CharacterVisuals model
        animator = GetComponentInChildren<Animator>();
        CollectHandGripBones();
        originalHeight = characterController.height;
        crouchHeight = originalHeight / 2f;

        // Cache all the meshes so the Invisibility Potion can turn them off
        playerRenderers = GetComponentsInChildren<Renderer>();

        targetHeight = originalHeight;
        targetCameraY = standingCameraY;

        // Cache the camera's resting local offset so the lean arc can always be computed from a
        // clean base (never from the already-arced position - that runs away).
        if (playerCamera != null)
        {
            camBaseLocalXZ = new Vector3(playerCamera.localPosition.x, 0f, playerCamera.localPosition.z);
            camBaseY = playerCamera.localPosition.y;
        }

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
        bool ctrlHeld = isLeaning || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool wantLean = ctrlHeld && !isStrangling && !isArrested && !isStunned;
        leanBlend = Mathf.MoveTowards(leanBlend, wantLean ? 1f : 0f, Time.deltaTime * leanSpeed);

        // While a blocking UI (pause / settings menu) is up, the player is fully frozen so they can
        // use the menu with the mouse. No movement, look, interaction or animation updates.
        if (controlsLocked) return;

        // 1. ALWAYS update the IK tracking (so the hand follows the mouse)
        UpdateMinigameIKTarget();
        UpdateHaulIKTarget();

        // 2. ONLY allow movement, camera rotation, and raycasting if NOT in a minigame
        //    and NOT locked into a strangle (the strangle coroutine drives position/rotation itself).
        if (!isPlayingMinigame && !isStrangling)
        {
            HandleRotation();
            HandleMovement();
            HandleCrouchTransition();
            CheckForInteractable();

            // --- NEW: THROW CHARGING TIMER ---
            if (isChargingThrow)
            {
                currentThrowCharge += Time.deltaTime;
                currentThrowCharge = Mathf.Clamp(currentThrowCharge, 0f, maxThrowChargeTime);
            }
        }
        else
        {
            // --- NEW: Allow camera rotation if holding RMB in a minigame ---
            if (isPlayingMinigame && isMinigameLooking)
            {
                HandleRotation();
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
                Vector3 horizontalVelocity = new Vector3(characterController.velocity.x, 0f, characterController.velocity.z);
                animator.SetFloat("Speed", horizontalVelocity.magnitude, 0.1f, Time.deltaTime);
            }
        }
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

    public void OnCrouch(InputValue value)
    {
        Debug.Log("Crouched");
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

    private void HandleCrouchTransition()
    {
        characterController.height = Mathf.Lerp(characterController.height, targetHeight, Time.deltaTime * crouchTransitionSpeed);

        // Drive the un-leaned base Y (crouch lerp), then set the camera to that clean base. The lean
        // arc is layered on top in LateUpdate, computed from this base - so it can't accumulate.
        camBaseY = Mathf.Lerp(camBaseY, targetCameraY, Time.deltaTime * crouchTransitionSpeed);
        playerCamera.localPosition = new Vector3(camBaseLocalXZ.x, camBaseY, camBaseLocalXZ.z);
    }

    public void OnJump(InputValue value)
    {
        // --- NEW: Block jumping entirely if holding a heavy item (either hand) ---
        if (IsHoldingHeavyItem())
        {
            if(value.isPressed) Debug.Log("Cannot jump while carrying a heavy item!");
            return;
        }
        if (value.isPressed && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

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

    public void OnSprint(InputValue value)
    {
        // value.isPressed is true when you press and hold the key down
        // value.isPressed becomes false the exact moment you let go of the key
        isSprinting = value.isPressed;
        if(value.isPressed) Debug.Log("Sprinting");
        else Debug.Log("Walking");
    }
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
            if (currentlyHeldItem == null && leftHeldItem == null)
            {
                Debug.Log("Nothing to drop.");
                return;
            }

            // Start of press: begin charging a potential throw. If it turns out to be a tap we
            // just drop instead on release.
            dropPressTime = Time.time;
            isChargingThrow = true;
            currentThrowCharge = 0f;
        }
        else
        {
            if (!isChargingThrow) return; // press was consumed (pardon / nothing held / minigame)
            isChargingThrow = false;

            bool wasTap = (Time.time - dropPressTime) <= dropTapMaxDuration;

            // Ghosts and taps just drop in place; a real hold throws the active-hand item.
            if (isGhost || wasTap || currentlyHeldItem == null)
                DropHeldItemInPlace();
            else
                ExecuteThrow();
        }
    }

    // Drops the active-hand item where the player stands; if the active hand is empty, drops the off-hand item.
    private void DropHeldItemInPlace()
    {
        PickupItem toDrop = currentlyHeldItem != null ? currentlyHeldItem : leftHeldItem;
        if (toDrop == null)
        {
            Debug.Log("Nothing to drop.");
            return;
        }

        toDrop.DetachFromHand();
        if (toDrop == currentlyHeldItem) currentlyHeldItem = null;
        else leftHeldItem = null;

        Debug.Log($"Dropped {toDrop.itemName}.");
        foreach (TaskInstance task in activeTasks) task.CheckForTaskRegression(this);
        RefreshLocalWaypoints();
    }

    // Triggered by the 'R' key (SwapHands action). Moves items between the left and right hands.
    public void OnSwapHands(InputValue value)
    {
        if (!value.isPressed) return;

        // Can't rearrange your grip mid-minigame or while being escorted in custody.
        if (isPlayingMinigame || isArrested) return;

        // A two-handed haul item occupies both hands - nothing to swap.
        if (currentlyHeldItem != null && currentlyHeldItem.haulWithBothHands) return;

        if (currentlyHeldItem == null && leftHeldItem == null)
        {
            Debug.Log("Nothing to swap between hands.");
            return;
        }

        PickupItem temp = currentlyHeldItem;
        currentlyHeldItem = leftHeldItem;
        leftHeldItem = temp;

        // Re-seat whatever ended up in each hand on the matching socket.
        if (currentlyHeldItem != null) currentlyHeldItem.AttachToHand(rightHandSocket);
        if (leftHeldItem != null) leftHeldItem.AttachToHand(leftHandSocket);

        Debug.Log($"Swapped hands. Active: {(currentlyHeldItem != null ? currentlyHeldItem.itemName : "empty")} | Off-hand: {(leftHeldItem != null ? leftHeldItem.itemName : "empty")}");

        RefreshLocalWaypoints();
    }

    // --- NEW: PUNCH MECHANIC ---
    public void OnPunch(InputValue value)
    {
        if (!value.isPressed || isGhost) return;

        // --- If we are holding an item, don't punch (a Royal can still raise it to block). ---
        if (currentlyHeldItem != null)
        {
            if (currentlyHeldItem is RoyalWeapon royalWeapon)
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
                currentMinigameYaw = 0f;
                // Hide cursor and lock it to the center so we can move the camera
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                // --- NEW: SNAP BACK TO ORIGINAL POSITION ---
                transform.rotation = minigameStartBodyRotation;
                verticalRotation = minigameStartVerticalRotation;
                playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);

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
                if (currentlyHeldItem != null || leftHeldItem != null || activePowerUpVisual != null)
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
            if (currentlyHeldItem != null || leftHeldItem != null || activePowerUpVisual != null) break;

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
        if (characterController != null)
        {
            strangleIgnoredColliders = victim.GetComponentsInChildren<Collider>();
            foreach (Collider col in strangleIgnoredColliders)
                if (col != null && col.enabled) Physics.IgnoreCollision(characterController, col, true);
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
        if (strangleVictim == null || characterController == null) return;

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

        if (characterController.enabled) characterController.Move(delta);

        // Keep facing the victim's neck
        Vector3 faceDir = center - transform.position;
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(faceDir);
    }

    private void EndStrangleLock()
    {
        if (characterController != null && strangleIgnoredColliders != null)
        {
            foreach (Collider col in strangleIgnoredColliders)
                if (col != null) Physics.IgnoreCollision(characterController, col, false);
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
        hangReachWeight = Mathf.Lerp(hangReachWeight, target, Time.deltaTime * ikBlendSpeed);
        if (hangReachWeight <= 0.01f) return;

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, hangReachWeight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, hangReachPos);

        float rotW = hangReachWeight * Mathf.Clamp01(hangReachRotWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rotW);
        animator.SetIKRotation(AvatarIKGoal.RightHand, hangReachRot);
    }

    // Called from PlayerIKHelper.OnAnimatorIK - pins BOTH hands to the grip points of a hauled item
    // so the player looks like they're carrying it. Fed each frame by UpdateHaulIKTarget().
    public void ApplyHaulIK(int layerIndex)
    {
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

    // Bends the lower spine forward (about the player's right axis) so the model visibly folds at the
    // hips while Ctrl is held. Runs in LateUpdate, after the animator, so it overrides the pose.
    // Skipped while a minigame is driving the right hand (hangReachActive) - the fold would drag the
    // outstretched arm off its aim target; the camera arc alone lowers the view.
    private bool warnedNoSpineBone;
    private void ApplyLeanSpineBend()
    {
        if (leanBlend <= 0.001f || hangReachActive) return;

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

        sb.rotation = Quaternion.AngleAxis(leanAngle * leanBlend, transform.right) * sb.rotation;
    }

    // Keeps haulActive in sync with what's held, re-poses the item, and refreshes the grip-point +
    // elbow-hint positions. Hands are auto-assigned by which side of the player each grip is on.
    private void UpdateHaulIKTarget()
    {
        bool hauling = currentlyHeldItem != null && currentlyHeldItem.haulWithBothHands;
        haulActive = hauling;
        if (!hauling) return;

        PickupItem hi = currentlyHeldItem;
        PoseHaulItem();

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

    private void ExecuteThrow()
    {
        isChargingThrow = false;
        
        if (currentlyHeldItem == null) return;

        // 1. Calculate the final force 
        // (Charge % * Base Force) / Item Weight
        float chargePercentage = currentThrowCharge / maxThrowChargeTime;
        
        // Failsafe: Ensure a quick tap still applies a tiny bit of force (10% minimum) so it doesn't just drop at their feet
        chargePercentage = Mathf.Max(chargePercentage, 0.1f); 
        
        float finalForce = (chargePercentage * baseThrowForce) / currentlyHeldItem.itemWeight;

        // 2. Detach and clear the item from the player's inventory
        PickupItem itemToThrow = currentlyHeldItem;
        itemToThrow.DetachFromHand();
        ClearHeldItem(); 

        foreach (TaskInstance task in activeTasks)
        {
            task.CheckForTaskRegression(this);
        }

        // 3. Awaken the Physics components
        Rigidbody rb = itemToThrow.GetComponent<Rigidbody>();
        Collider col = itemToThrow.GetComponent<Collider>();

        if (col != null) col.enabled = true;
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;

            // 4. Apply the impulse force
            // We add a tiny bit to the Y axis (Vector3.up * 0.1f) to give the throw a natural arc
            Vector3 throwDirection = playerCamera.forward + (Vector3.up * 0.1f);
            rb.AddForce(throwDirection.normalized * finalForce, ForceMode.Impulse);
            
            // Optional Polish: Add some random tumbling spin to the item while it flies
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * (finalForce * 0.5f), ForceMode.Impulse);
        }

        Debug.Log($"Threw {itemToThrow.itemName} with force {finalForce}. (Charge: {chargePercentage * 100}%, Weight: {itemToThrow.itemWeight})");

        // --- NEW: PROJECTILE IMPACT SETUP ---
        // Dynamically add the impact script to the item in the air
        ThrownProjectile projectile = itemToThrow.gameObject.AddComponent<ThrownProjectile>();
        
        // Pass the player, the starting coordinates, the charge %, and the max punch force
        projectile.Initialize(this, transform.position, chargePercentage, pushbackForce);
        
        RefreshLocalWaypoints();
    }

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
        if (currentlyHeldItem != null)
        {
            Debug.Log("Dropped standard item to pull out power-up!");
            currentlyHeldItem = null;
        }
        if (leftHeldItem != null)
        {
            Debug.Log("Dropped off-hand item to pull out power-up!");
            leftHeldItem = null;
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
            characterController.Move(direction * currentForce * Time.deltaTime);
            
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

        Debug.Log($"<color=#3498DB>{gameObject.name} raises the {weapon.itemName} to block!</color>");
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
        if (currentlyHeldItem != null)
        {
            Debug.Log("You dropped your task item!");
            currentlyHeldItem = null;
        }
        if (leftHeldItem != null)
        {
            Debug.Log("You dropped your off-hand item!");
            leftHeldItem = null;
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
            characterController.Move(moveDelta);
            
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
    private void HandleMovement()
    {
        isGrounded = characterController.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; 
        }

        float currentSpeed = walkSpeed;

        // --- NEW: OVERRIDE MOVEMENT SPEED FOR HEAVY ITEMS, STUNS, & ARRESTS ---
        bool isHoldingHeavy = IsHoldingHeavyItem();

        // 1. Highest Priority: Stuns, Pushbacks, and Arrests completely lock voluntary movement
        if (isStunned || isBeingPushed || isArrested) 
        {
            currentSpeed = 0f;
        }
        // 2. Second Priority: Heavy items and Dragging Prisoners halve speed and disable sprint/crouch speeds
        else if (isHoldingHeavy || isDraggingPrisoner) 
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

        // --- THE FIX: Combine movement and gravity into ONE vector ---
        Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
        Vector3 finalMovement = moveDirection * currentSpeed;

        // Calculate gravity
        velocity.y += gravity * Time.deltaTime;
        finalMovement.y = velocity.y; // Add the Y velocity to the final movement

        // Execute exactly ONE Move call so Unity calculates the velocity perfectly!
        characterController.Move(finalMovement * Time.deltaTime);
    }

    private void HandleRotation()
    {
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
        verticalRotation -= lookInput.y * mouseSensitivity;
        verticalRotation = Mathf.Clamp(verticalRotation, lowerLookLimit, upperLookLimit);
        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
    }

    // Runs after Update/crouch have set the base camera pose, so the lean arc isn't overwritten.
    void LateUpdate()
    {
        ApplyLeanCameraArc();
        ApplyLeanSpineBend();
        ApplyHandGripPose();
    }

    // Finds the right-hand finger + thumb bones to curl for the minigame grab gesture (inspector
    // overrides if given, else RightHandBone's children by name), then works out a per-bone curl
    // rotation that swings each fingertip TOWARD THE PALM, so the grab direction can't be inverted.
    private void CollectHandGripBones()
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
    // when the hand is fully open so idle animation keeps full control of the fingers. Runs from
    // LateUpdate (not Update), so it still fires while a minigame has controls locked.
    private void ApplyHandGripPose()
    {
        if (!minigameHandGrip) return;
        if (!gripBonesCollected) CollectHandGripBones();

        // Curl toward a fist while the LEFT MOUSE BUTTON is held during ANY minigame, open otherwise.
        // isPlayingMinigame covers the StartMinigame-based ones (cake, consume, ...); hangReachActive
        // covers the deposit minigames (sword rack, dowry chest) which are launched straight from the
        // TaskDepositStation and never set isPlayingMinigame.
        bool inMinigame = isPlayingMinigame || hangReachActive;
        // The deposit minigames act on the mouse-DOWN (drop / grab the lid), so a real hold never
        // happens - latch a short pulse on the click so the grab is always visible.
        if (inMinigame && Input.GetMouseButtonDown(0)) gripHoldUntil = Time.time + handGripPulseTime;
        bool wantGrip = inMinigame && (Input.GetMouseButton(0) || Time.time < gripHoldUntil);
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

    // Pivots the camera forward + down around a hip point, matching a bend at the hips, so it clears
    // the body instead of clipping straight down through it. Computed from the CLEAN base (camBase*)
    // set by HandleCrouchTransition, never from the current position - so it can't run away.
    private void ApplyLeanCameraArc()
    {
        if (leanBlend <= 0.001f || playerCamera == null) return;

        float theta = leanAngle * leanBlend;
        Vector3 baseLocal = new Vector3(camBaseLocalXZ.x, camBaseY, camBaseLocalXZ.z);
        Vector3 pivot = new Vector3(baseLocal.x, leanHipLocalY, baseLocal.z);

        Vector3 arm = Quaternion.AngleAxis(theta, Vector3.right) * (baseLocal - pivot); // swing forward + down
        // During a minigame the camera pushing forward drags the hand's IK target out of arm's reach
        // (it's measured from the camera), so keep the drop but cut most of the forward travel.
        if (hangReachActive) arm.z *= leanMinigameForwardFactor;
        playerCamera.localPosition = pivot + arm;
        // View pitch is its own tunable (can be negative = tilt up) so the hands stay in frame.
        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f)
                                     * Quaternion.AngleAxis(leanViewPitch * leanBlend, Vector3.right);
    }
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
                        if (currentlyHeldItem != null && currentlyHeldItem.requiresPartner)
                        {
                            prompts.Add($"Press <color=#F4D03F>[E]</color> to use <color=#5DADE2>{currentlyHeldItem.itemName}</color> with <color=#58D68D>{otherPlayer.gameObject.name}</color>");
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

            if (currentlyHeldItem != null || leftHeldItem != null)
            {
                if (interactionUI != null)
                {
                    List<string> heldPrompts = new List<string>();

                    if (currentlyHeldItem != null)
                    {
                        // UPDATED: E is now explicitly for using, Q is for dropping
                        heldPrompts.Add($"Press <color=#F4D03F>[E]</color> to use or <color=#F4D03F>[Q]</color> to drop <color=#5DADE2>{currentlyHeldItem.itemName}</color>");
                    }

                    if (leftHeldItem != null)
                    {
                        heldPrompts.Add($"Press <color=#F4D03F>[R]</color> to swap in <color=#5DADE2>{leftHeldItem.itemName}</color> (off-hand)");
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
        if (currentlyHeldItem != null && !(currentTarget is TaskDepositStation) && TryProximityDeposit())
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
        else if (currentlyHeldItem != null)
        {
            // A spent item (e.g. an emptied plate) does nothing on [E] - just carry or drop it.
            if (currentlyHeldItem.isSpent)
            {
                Debug.Log($"Nothing left to do with the {currentlyHeldItem.itemName}.");
                return;
            }

            // A held item that carries its own minigame ALWAYS launches it on [E].
            // Independent of role, of any related task, or of whether that task was already done.
            if (currentlyHeldItem.processMinigamePrefab != null)
            {
                StartMinigame(currentlyHeldItem.processMinigamePrefab, null, currentlyHeldItem.gameObject);
                return;
            }

            bool taskCompleted = false;

            // --- UNIVERSAL TASK EVALUATION ---
            for (int i = activeTasks.Count - 1; i >= 0; i--)
            {
                TaskInstance task = activeTasks[i];
                // Pass the held item as the target
                if (task.EvaluateCurrentStep(this, currentlyHeldItem.gameObject)) 
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
                Debug.Log($"Nothing to do with the {currentlyHeldItem.itemName} here - carry it where it needs to go.");
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
        if (currentlyHeldItem == null || TaskLocation.AllLocations == null) return false;

        TaskDepositStation best = null;
        float bestDist = depositProximityRange;

        foreach (TaskLocation loc in TaskLocation.AllLocations)
        {
            if (loc == null || string.IsNullOrEmpty(loc.acceptedItemName)
                || loc.acceptedItemName != currentlyHeldItem.itemName) continue;
            TaskDepositStation st = loc.GetComponent<TaskDepositStation>();
            if (st == null || !st.HasFreeSlot()) continue;

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

    public void EquipItem(PickupItem newItem)
    {
        if (newItem == null) return;

        // Two-handed haul items: carried in front of the torso, both hands IK-locked to grip points.
        if (newItem.haulWithBothHands)
        {
            if (currentlyHeldItem != null) currentlyHeldItem.DetachFromHand();
            if (leftHeldItem != null) { leftHeldItem.DetachFromHand(); leftHeldItem = null; }
            currentlyHeldItem = newItem;
            AttachHaulItem(newItem);
            Debug.Log($"Hauling {newItem.itemName}");
            return;
        }

        // Heavy items are effectively two-handed: you can't dual-wield with one involved.
        // If either the new item or anything already held is heavy, fall back to single-hand
        // behaviour (drop the active-hand item, take the new one in the active hand).
        bool heavyInvolved = newItem.isHeavy || IsHoldingHeavyItem();

        if (!heavyInvolved && currentlyHeldItem != null && leftHeldItem == null)
        {
            // Active hand is full but the off-hand is free: carry the new item there.
            leftHeldItem = newItem;
            leftHeldItem.AttachToHand(leftHandSocket);
            Debug.Log($"Equipped {leftHeldItem.itemName} in the off-hand");
            return;
        }

        // Otherwise the new item goes into the active (right) hand.
        // Drop whatever is already in the active hand first.
        if (currentlyHeldItem != null)
        {
            currentlyHeldItem.DetachFromHand();
        }

        currentlyHeldItem = newItem;
        currentlyHeldItem.AttachToHand(rightHandSocket);

        Debug.Log($"Equipped {currentlyHeldItem.itemName}");
    }

    // Attaches a haul item; PoseHaulItem() then keeps it in front of the torso each frame and
    // ApplyHaulIK takes the hands.
    private void AttachHaulItem(PickupItem item)
    {
        item.PrepareForHaul();
        item.transform.SetParent(transform, true); // parent so it survives a menu pause; pose drives the rest
        haulActive = true;
        PoseHaulItem();

        Debug.Log($"[Haul] Carrying {item.itemName}. leftGrip={(item.leftGripPoint != null)} " +
                  $"rightGrip={(item.rightGripPoint != null)} ikBlendSpeed={ikBlendSpeed} " +
                  $"animatorHuman={(animator != null && animator.isHuman)}");
    }

    // Places the hauled item in front of the body at a height measured down from the eyes, facing
    // the player's forward. Camera-anchored so it lands at hip/chest height on any rig.
    private void PoseHaulItem()
    {
        if (currentlyHeldItem == null || !currentlyHeldItem.haulWithBothHands) return;
        PickupItem hi = currentlyHeldItem;

        Vector3 flatFwd = transform.forward;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
        flatFwd.Normalize();

        float eyeY = playerCamera != null ? playerCamera.position.y : transform.position.y + 1.6f;
        Vector3 pos = new Vector3(transform.position.x, eyeY, transform.position.z)
                      + flatFwd * hi.haulForward
                      + Vector3.up * hi.haulHeightBelowEye;

        hi.transform.SetPositionAndRotation(
            pos,
            Quaternion.LookRotation(flatFwd, Vector3.up) * Quaternion.Euler(hi.haulLocalEuler));
    }

    // Add these public helpers so external stations can take the item
    public PickupItem GetHeldItem() { return currentlyHeldItem; }

    public void ClearHeldItem() { currentlyHeldItem = null; }

    // --- NEW: DUAL-WIELD HELPERS ---
    public PickupItem GetLeftHeldItem() { return leftHeldItem; }

    public void ClearLeftHeldItem() { leftHeldItem = null; }

    // True if an item with this name is held in EITHER hand.
    public bool IsHoldingItemNamed(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return false;
        if (currentlyHeldItem != null && currentlyHeldItem.itemName == itemName) return true;
        if (leftHeldItem != null && leftHeldItem.itemName == itemName) return true;
        return false;
    }

    // True if either hand is holding a heavy item.
    public bool IsHoldingHeavyItem()
    {
        return (currentlyHeldItem != null && currentlyHeldItem.isHeavy)
            || (leftHeldItem != null && leftHeldItem.isHeavy);
    }


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
        isSprinting = false;
        isChargingThrow = false;
        strangleButtonHeld = false;
        isLeaning = false; // legacy Ctrl read in Update() still drives the lean while locked

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

    // Horizontal-only look for placement minigames. The minigame calls this while the player holds
    // RMB, passing an already-scaled yaw in degrees. Turns the body (and the camera with it) so they
    // can pan across a wide target; the mouse's vertical axis stays free for aiming.
    public void MinigameLookYaw(float degrees)
    {
        transform.Rotate(0f, degrees, 0f, Space.World);
    }

    // Vertical look for placement minigames. Positive `degrees` = look up (matches mouse-up). Pitch is
    // clamped to the same limits as normal FPS look and applied straight to the camera.
    public void MinigameLookPitch(float degrees)
    {
        if (playerCamera == null) return;
        verticalRotation = Mathf.Clamp(verticalRotation - degrees, lowerLookLimit, upperLookLimit);
        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
    }

    // Add this helper method anywhere inside PlayerController
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

    // Aim the body + camera at a world point (used when a placement minigame starts).
    public void PointCameraAt(Vector3 worldPoint)
    {
        if (playerCamera == null) return;

        Vector3 dir = worldPoint - playerCamera.position;
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        if (flat.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(flat);

        float pitch = Quaternion.LookRotation(dir).eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        verticalRotation = Mathf.Clamp(pitch, lowerLookLimit, upperLookLimit);
        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
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
        if (currentlyHeldItem != null)
        {
            currentlyHeldItem.DetachFromHand();
            currentlyHeldItem = null;
        }
        if (leftHeldItem != null)
        {
            leftHeldItem.DetachFromHand();
            leftHeldItem = null;
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
        walkSpeed *= 1.5f; 
    }

    public void AssignTasks(List<TaskInstance> newTasks)
    {
        activeTasks.Clear();
        activeTasks.AddRange(newTasks);

        allAssignedTasks.Clear();
        allAssignedTasks.AddRange(newTasks);

        // No InitializeTask() call - each TaskInstance is constructed fresh (step index 0, steps
        // deep-copied and un-completed), so a new instance IS the initialization.

        if (IsLocal && UIManager.Instance != null)
        {
            UIManager.Instance.UpdatePlayerTaskList(this, allAssignedTasks, activeTasks, currentRole);
            RefreshLocalWaypoints();
        }
    }

    // Called when the player successfully interacts with a task station
    public void RemoveCompletedTask(TaskInstance completedTask)
    {
        if (activeTasks.Contains(completedTask)) 
        {
            // Remove it from active logic, but keep it in allAssignedTasks!
            activeTasks.Remove(completedTask); 
           
            // Inside RemoveCompletedTask(...)
            if (IsLocal && UIManager.Instance != null)
            {
                // Add 'this' as the first parameter
                UIManager.Instance.UpdatePlayerTaskList(this, allAssignedTasks, activeTasks, currentRole);
                RefreshLocalWaypoints();
            }
        }
    }

    // A centralized helper to refresh the UI markers dynamically
    public void RefreshLocalWaypoints()
    {
        if (IsLocal)
        {
            if (UIManager.Instance != null)
            {
                // Force the text to evaluate the new step
                UIManager.Instance.UpdatePlayerTaskList(this, allAssignedTasks, activeTasks, currentRole);
            }

            if (WaypointManager.Instance != null)
            {
                if (showWaypoints)
                {
                    WaypointManager.Instance.playerCamera = this.playerCamera.GetComponent<Camera>();
                    WaypointManager.Instance.UpdateWaypoints(this, allAssignedTasks);
                }
                else
                {
                    // Pass an empty list to clear out any active waypoints on the screen
                    WaypointManager.Instance.UpdateWaypoints(this, new List<TaskInstance>());
                }
            }
        }
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
        if (currentlyHeldItem != null && currentlyHeldItem.requiresPartner)
        {
            Debug.Log($"Used {currentlyHeldItem.itemName} with {target.gameObject.name}!" + (taskCompleted ? " (Task Completed)" : " (Faked Task)"));
            GameObject initiatorItemObj = currentlyHeldItem.gameObject;
            this.ClearHeldItem(); 
            Destroy(initiatorItemObj);
        }
        else if (currentlyHeldItem == null)
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

        isPlayingMinigame = true;
        activeMinigameTarget = targetInteractable;
        activeMinigameTask = task; // So the waypoint for this task can be hidden while the minigame is open
        itemSwappedToLeftHand = false; // Reset flag
        
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
                verticalRotation = Mathf.Clamp(targetPitch, lowerLookLimit, upperLookLimit);
            }
            // SCENARIO 2: The minigame targets an ITEM (one we hold, or a world prop such as a
            // Cake/Sword). Item minigames are performed in-hand and must NEVER move or turn the player.
            else if (targetInteractable.GetComponent<PickupItem>() != null)
            {
                currentMinigameTargetType = MinigameTargetType.Item;
                activeMinigameStation = leftHandSocket;

                PickupItem targetItem = targetInteractable.GetComponent<PickupItem>();

                // Pick which item to raise into the left hand for the animation:
                // the targeted instance if we're already holding it, else whatever is in the active hand.
                PickupItem itemToRaise = (targetItem == leftHeldItem || targetItem == currentlyHeldItem)
                    ? targetItem
                    : currentlyHeldItem;

                if (itemToRaise != null && itemToRaise == leftHeldItem)
                {
                    // Already sitting in the left hand: just orient the socket, no swap, no return needed.
                    if (leftHandSocket != null)
                        leftHandSocket.localRotation = Quaternion.Euler(itemToRaise.leftHandSocketRotation);
                }
                else if (itemToRaise != null)
                {
                    // The minigame borrows the left hand. If the off-hand holds something else, drop it.
                    if (leftHeldItem != null)
                    {
                        Debug.Log($"Dropped off-hand {leftHeldItem.itemName} to free the left hand for the minigame.");
                        leftHeldItem.DetachFromHand();
                        leftHeldItem = null;
                        foreach (TaskInstance regressionTask in activeTasks)
                        {
                            regressionTask.CheckForTaskRegression(this);
                        }
                    }

                    if (leftHandSocket != null)
                        leftHandSocket.localRotation = Quaternion.Euler(itemToRaise.leftHandSocketRotation);

                    itemToRaise.AttachToHand(leftHandSocket);
                    itemSwappedToLeftHand = true; // ReturnSwappedItem() puts currentlyHeldItem back afterwards
                }

                // Camera PITCH only — look down at the hands. Body rotation and position are left alone.
                if (leftHandSocket != null)
                {
                    Vector3 lookDir = leftHandSocket.position - playerCamera.position;
                    float targetPitch = Quaternion.LookRotation(lookDir).eulerAngles.x;
                    if (targetPitch > 180f) targetPitch -= 360f;
                    verticalRotation = Mathf.Clamp(targetPitch, lowerLookLimit, upperLookLimit);
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
                    if (characterController != null) characterController.enabled = false;
                    transform.position = lockedFloorPosition;
                    transform.rotation = standPoint.rotation;
                    if (characterController != null) 
                    {
                        characterController.enabled = true;
                        characterController.Move(Vector3.down * 0.15f); // Prevent physics popping
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
                verticalRotation = Mathf.Clamp(targetPitch, lowerLookLimit, upperLookLimit);
            }
            
            // Apply the calculated vertical camera pitch
            playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        }

        // Save the rotation so we can snap back to it later when Right Click is released
        minigameStartBodyRotation = transform.rotation;
        minigameStartVerticalRotation = verticalRotation;
        
        // Free the mouse cursor for tactile dragging
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Instantiate and setup the Minigame UI
        activeMinigameInstance = Instantiate(minigamePrefab);
        MinigameBase minigameScript = activeMinigameInstance.GetComponent<MinigameBase>();
        if (minigameScript != null)
        {
            minigameScript.SetupMinigame(this, task);
        }

        // Redraw waypoints now so this task's marker disappears while the minigame is open.
        RefreshLocalWaypoints();
    }

    public void FinishMinigame(TaskInstance task)
    {
        isPlayingMinigame = false;
        isMinigameLooking = false;
        activeMinigameTask = null; // Minigame closed: this task's waypoint may show again

        ReturnSwappedItem(); // --- NEW: Snap item back to right hand ---

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
        GameObject evalTarget = currentlyHeldItem != null ? currentlyHeldItem.gameObject : activeMinigameTarget;

        for (int i = activeTasks.Count - 1; i >= 0; i--)
        {
            TaskInstance task = activeTasks[i];
            if (task.EvaluateCurrentStep(this, evalTarget, true)) // skipMinigame: true
            {
                if (TaskManager.Instance != null) TaskManager.Instance.CompleteTask(this, task);
            }
        }

        if (currentlyHeldItem != null && !currentlyHeldItem.isProcessed)
        {
            currentlyHeldItem.ProcessItem();
        }
    }

    public void CancelMinigame()
    {
        isPlayingMinigame = false;
        isMinigameLooking = false;
        activeMinigameTask = null; // Minigame closed: this task's waypoint may show again

        ReturnSwappedItem(); // --- NEW: Snap item back to right hand ---

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("Minigame Cancelled.");

        // Bring the task's waypoint back now that the minigame was abandoned.
        RefreshLocalWaypoints();
    }

    private void UpdateMinigameIKTarget()
    {
            if (isPlayingMinigame && playerCamera != null && !isMinigameLooking)        {
            // 1. Get the raw 2D pixel coordinate of the mouse on the screen
            Vector3 mouseScreenPos = Input.mousePosition;
            
            // 2. Set the Z axis to define how deep into the 3D world we want to project
            mouseScreenPos.z = minigameIKDepth;
            
            // 3. Convert that screen pixel + depth into a physical 3D world coordinate
            ikTargetPosition = playerCamera.GetComponent<Camera>().ScreenToWorldPoint(mouseScreenPos);
        }
    }

    public void ApplyMinigameIK(int layerIndex)
    {
        if (animator == null) return;

        float targetWeight = isPlayingMinigame ? 1f : 0f;
        currentIKWeight = Mathf.Lerp(currentIKWeight, targetWeight, Time.deltaTime * ikBlendSpeed);

        if (currentIKWeight > 0.01f)
        {
            // --- RIGHT HAND (Always tracks the Mouse in all 3 scenarios) ---
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, currentIKWeight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, ikTargetPosition);
            
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, currentIKWeight);
            Quaternion handOffset = Quaternion.Euler(rightHandRotationOffset);
            animator.SetIKRotation(AvatarIKGoal.RightHand, playerCamera.transform.rotation * handOffset);

            // --- LEFT HAND (Dynamically changes based on target) ---
            if (currentMinigameTargetType == MinigameTargetType.Player)
            {
                // SCENARIO 1: PLAYER - Keep the left arm resting naturally by their side
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            }
            else if (currentMinigameTargetType == MinigameTargetType.Item)
            {
                // SCENARIO 2: ITEM - Raise left hand to hold the item in the center of the screen
                if (leftHandSocket != null)
                {
                    animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, currentIKWeight);
                    animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, currentIKWeight); // Turn on rotation!
                    
                    animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandSocket.position);
                    
                    // Match the hand bone rotation to the socket's rotation so you can fix weird wrist twists!
                    animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandSocket.rotation);
                }
            }
            else 
            {
                // SCENARIO 3: STATION - Slam left hand onto the center of the table
                if (activeMinigameStation != null)
                {
                    animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, currentIKWeight);
                    
                    Vector3 leftHandTarget = activeMinigameStation.position;
                    Collider stationCol = activeMinigameStation.GetComponent<Collider>();
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

    private void ReturnSwappedItem()
    {
        if (itemSwappedToLeftHand && currentlyHeldItem != null)
        {
            currentlyHeldItem.AttachToHand(rightHandSocket);
            itemSwappedToLeftHand = false;
        }
    }
    #endregion
}