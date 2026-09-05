using UnityEngine;
using System.Collections.Generic;

// Owns everything about this player's state as a character: role, health, ghosting, stun/pushback,
// custody (arrest/captor/prisoner/breakout), the strangle mechanic, punch, Corrupted power-ups, and
// which zone they're standing in. Extracted from PlayerController - see PlayerController.cs for
// player.Vitals, the single access point external code now uses.
[RequireComponent(typeof(PlayerController))]
public class PlayerVitals : MonoBehaviour
{
    [Header("Role Settings")]
    public PlayerRole currentRole = PlayerRole.None;

    [Header("Status")]
    public int currentHealth = 1;
    public int maxHealth = 1;
    public bool isBlocking = false;
    public bool isGhost = false;

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
    /// <summary>Read by PlayerMotor.HandleMovement, which locks movement speed to 0 while a pushback is in progress.</summary>
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

    // --- CHANGED: FIXED-SLOT CORRUPTED INVENTORY ---
    public PowerUpData[] corruptedInventory = new PowerUpData[3];

    [Header("Active Inventory")]
    public Transform powerUpHoldPoint;
    public int activeSlotIndex = -1;
    private GameObject activePowerUpVisual;

    // --- NEW: SCROLL DEBOUNCE ---
    public float scrollCooldown = 0.15f;
    private float lastScrollTime = 0f;

    [Header("Power-Up Prefabs & Status")]
    public GameObject trapPrefab;
    public GameObject illusionPrefab;
    private bool isStunned = false;
    /// <summary>Read by PlayerMotor.HandleMovement / UpdateLeanBlend, which lock movement and lean while stunned.</summary>
    public bool IsStunned => isStunned;
    private Renderer[] playerRenderers;

    // The sibling PlayerController and PlayerMotor on this same GameObject.
    private PlayerController player;
    private PlayerMotor motor;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        motor = GetComponent<PlayerMotor>();
        // Cache all the meshes so the Invisibility Potion can turn them off
        playerRenderers = GetComponentsInChildren<Renderer>();
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
        PickupItem heldItem = player.GetHeldItem();
        if (heldItem != null)
        {
            heldItem.DetachFromHand();
            player.ClearHeldItem();
        }
        PickupItem leftItem = player.GetLeftHeldItem();
        if (leftItem != null)
        {
            leftItem.DetachFromHand();
            player.ClearLeftHeldItem();
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
            player.CharController.Move(direction * currentForce * Time.deltaTime);

            timer += Time.deltaTime;
            yield return null; // Wait for next frame
        }

        isBeingPushed = false;
    }

    // --- NEW: PUNCH MECHANIC --- (routed from PlayerController.OnPunch)
    public void HandlePunch(bool isPressed, PickupItem heldItem)
    {
        if (!isPressed || isGhost) return;

        // --- If we are holding an item, don't punch (a Royal can still raise it to block). ---
        if (heldItem != null)
        {
            if (heldItem is RoyalWeapon royalWeapon)
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

    private void ExecutePunch()
    {
        Debug.Log($"{gameObject.name} throws a punch!");

        // Cast a thick sphere forward. We omit the layer mask so it can hit players OR physics items
        if (Physics.SphereCast(player.PlayerCamera.position, punchRadius, player.PlayerCamera.forward, out RaycastHit hit, punchRange))
        {
            // 1. Did we hit a Player?
            PlayerController victim = hit.collider.GetComponent<PlayerController>();
            if (victim != null && victim != player && !victim.Vitals.isGhost)
            {
                Debug.Log($"Punched {victim.gameObject.name}!");

                // Calculate push direction (from puncher to victim)
                Vector3 pushDirection = (victim.transform.position - transform.position).normalized;
                pushDirection.y = 0; // Prevent launching them into the sky

                victim.Vitals.ApplyPushback(pushDirection, pushbackForce, pushbackDuration);
                return;
            }

            // 2. Did we hit an Item/Physics object?
            Rigidbody rb = hit.collider.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                Debug.Log($"Punched an item!");
                // Shove the item exactly the direction the camera is looking
                rb.AddForce(player.PlayerCamera.forward * (pushbackForce / 2f), ForceMode.Impulse);
            }
        }
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

    // --- Royal Pardon: routed from PlayerController.OnDropItem's press branch. Returns true if a
    // pardon was performed (caller should stop, not start a drop/throw charge). ---
    public bool TryPardon()
    {
        if (isGhost || !(currentRole == PlayerRole.King || currentRole == PlayerRole.Kingsguard)
            || !isDraggingPrisoner || currentPrisoner == null)
            return false;

        currentPrisoner.Vitals.isArrested = false; // Free them!

        if (currentPrisoner.Vitals.breakoutTimerCoroutine != null)
        {
            currentPrisoner.Vitals.StopCoroutine(currentPrisoner.Vitals.breakoutTimerCoroutine);
            currentPrisoner.Vitals.breakoutTimerCoroutine = null;
        }

        isDraggingPrisoner = false;
        Debug.Log($"You pardoned and freed {currentPrisoner.gameObject.name}!");
        currentPrisoner = null;
        return true;
    }

    // --- DEDICATED RIGHT CLICK, non-minigame branch: Strangle OR Arrest. Routed from
    // PlayerController.OnStrangle once it's ruled out the minigame-camera-look interception. ---
    /// <summary>Cancels a latched strangle-button hold, e.g. when a blocking menu opens.</summary>
    public void CancelStrangleButton() => strangleButtonHeld = false;

    public void HandleStrangleOrArrest(bool isPressed, PickupItem heldItem, PickupItem leftHeldItem)
    {
        if (isGhost) return;

        // --- SCENARIO A: CORRUPTED STRANGLE ---
        if (currentRole == PlayerRole.Corrupted)
        {
            if (isPressed)
            {
                if (heldItem != null || leftHeldItem != null || activePowerUpVisual != null)
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
        if ((currentRole == PlayerRole.King || currentRole == PlayerRole.Kingsguard) && isPressed)
        {
            if (isDraggingPrisoner)
            {
                if (sceneGallows == null) sceneGallows = FindAnyObjectByType<Gallows>();

                bool nearGallows = sceneGallows != null && Vector3.Distance(transform.position, sceneGallows.transform.position) <= gallowsRange;

                bool nearRoyal = false;
                PlayerController nearbyRoyal = null;
                Collider[] royalHits = Physics.OverlapSphere(transform.position, player.Interactor.InteractionRange, player.Interactor.CharacterLayer);

                foreach (Collider c in royalHits)
                {
                    PlayerController p = c.GetComponent<PlayerController>();
                    if (p != null && p != player && !p.Vitals.isGhost && (p.Vitals.currentRole == PlayerRole.King || p.Vitals.currentRole == PlayerRole.Kingsguard))
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
                else if (nearRoyal && !nearbyRoyal.Vitals.isDraggingPrisoner)
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
            if (player.GetHeldItem() != null || player.GetLeftHeldItem() != null || activePowerUpVisual != null) break;

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

            if (strangleVictim == null || strangleVictim.Vitals.isGhost)
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
        if (timer >= strangleHoldTime && strangleVictim != null && !strangleVictim.Vitals.isGhost)
        {
            // Court members can grab and hold someone, but their strangle never kills.
            if (currentRole == PlayerRole.Court)
            {
                Debug.Log($"{gameObject.name} strangled {strangleVictim.gameObject.name} - but Court members deal no damage.");
            }
            else
            {
                Debug.Log($"Successfully strangled {strangleVictim.gameObject.name}!");
                strangleVictim.Vitals.TakeDamage(1);
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

    // The strangle only connects with the player the crosshair is actually on (Interactor.TargetPlayer
    // - the same thing that turns the crosshair green) and only once close enough.
    private PlayerController FindStrangleVictim()
    {
        PlayerController v = player.Interactor.TargetPlayer;
        if (v == null || v == player || v.Vitals.isGhost || v.Vitals.currentRole == PlayerRole.Corrupted) return null;

        Vector3 flat = v.transform.position - transform.position;
        flat.y = 0f;
        if (flat.magnitude > strangleConnectDistance) return null; // still too far to grab

        return v;
    }

    // World point the strangler's hands reach for: the victim's actual neck/head bone when available.
    private Vector3 StrangleNeckPoint()
    {
        if (strangleVictim != null) return strangleVictim.Vitals.GetNeckWorldPosition();
        // Post-strangle IK fade-out with no victim: keep the reach close to the body, not the sky.
        return transform.position + transform.forward * 0.35f + Vector3.up * strangleNeckHeight;
    }

    // Resolves (and caches) this player's neck/head bone so a strangler can lock its hands there.
    // Works for humanoid rigs and for the generic Synty rig via the bones FirstPersonHeadHider already points at.
    public Vector3 GetNeckWorldPosition()
    {
        if (cachedNeckAnchor == null)
        {
            Animator animator = player.PlayerAnimator;
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
        if (player.CharController != null)
        {
            strangleIgnoredColliders = victim.GetComponentsInChildren<Collider>();
            foreach (Collider col in strangleIgnoredColliders)
                if (col != null && col.enabled) Physics.IgnoreCollision(player.CharController, col, true);
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
        if (strangleVictim == null || player.CharController == null) return;

        Vector3 neck = StrangleNeckPoint();
        Vector3 center = new Vector3(neck.x, transform.position.y, neck.z);

        // Current angle of the strangler around the neck
        Vector3 offset = transform.position - center;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.0001f) offset = -transform.forward;
        float angle = Mathf.Atan2(offset.z, offset.x);

        // A/D shuffles slowly around the circle
        angle -= player.MoveInput.x * strangleOrbitSpeed * Mathf.Deg2Rad * Time.deltaTime;

        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 targetPos = center + dir * strangleGrabDistance;

        // Reposition slowly, so a sprinting victim outruns the grip
        Vector3 newPos = Vector3.MoveTowards(transform.position, targetPos, strangleFollowSpeed * Time.deltaTime);
        Vector3 delta = newPos - transform.position;
        delta.y = -2f * Time.deltaTime; // small downward bias to stay grounded

        if (player.CharController.enabled) player.CharController.Move(delta);

        // Keep facing the victim's neck
        Vector3 faceDir = center - transform.position;
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(faceDir);
    }

    private void EndStrangleLock()
    {
        if (player.CharController != null && strangleIgnoredColliders != null)
        {
            foreach (Collider col in strangleIgnoredColliders)
                if (col != null) Physics.IgnoreCollision(player.CharController, col, false);
        }
        strangleIgnoredColliders = null;

        isStrangling = false;
        strangleVictim = null;
    }

    // Called from PlayerIKHelper.OnAnimatorIK - drives both hands out for a strangle:
    // to the victim's throat when locked, or straight ahead where the player is aiming while hunting.
    public void ApplyStrangleIK(int layerIndex)
    {
        Animator animator = player.PlayerAnimator;
        if (animator == null) return;

        bool armsOut = isStrangling || isReachingToStrangle;
        float target = armsOut ? 1f : 0f;
        strangleHandIKWeight = Mathf.Lerp(strangleHandIKWeight, target, Time.deltaTime * player.ikBlendSpeed);

        if (strangleHandIKWeight <= 0.01f)
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            return;
        }

        Vector3 grip;
        if (strangleVictim != null)
        {
            grip = strangleVictim.Vitals.GetNeckWorldPosition();
            // Pull toward the strangler so the hands close on the FRONT of the throat, not the bone pivot.
            Vector3 toMe = transform.position - grip;
            toMe.y = 0f;
            if (toMe.sqrMagnitude > 0.0001f) grip += toMe.normalized * strangleHandInset;
        }
        else
        {
            // Hunting for a target: reach straight out along the aim.
            grip = player.PlayerCamera.position + player.PlayerCamera.forward * strangleReachDistance;
        }

        // Drop (or raise) the grip so the hands sit on the throat rather than up at the jaw.
        grip += Vector3.up * strangleHandHeightOffset;

        // Stable reach direction (flattened) so the hands hold one orientation instead of following the walk cycle.
        Vector3 reachDir = grip - player.PlayerCamera.position;
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

        ApplyOneStrangleHand(animator, AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, grip + apart, rightRot, elbowBase + transform.right * 0.3f);
        ApplyOneStrangleHand(animator, AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, grip - apart, leftRot, elbowBase - transform.right * 0.3f);
    }

    private void ApplyOneStrangleHand(Animator animator, AvatarIKGoal goal, AvatarIKHint elbow, Vector3 handPos, Quaternion handRot, Vector3 elbowPos)
    {
        animator.SetIKPositionWeight(goal, strangleHandIKWeight);
        animator.SetIKPosition(goal, handPos);
        animator.SetIKRotationWeight(goal, strangleHandIKWeight);
        animator.SetIKRotation(goal, handRot);
        animator.SetIKHintPositionWeight(elbow, strangleHandIKWeight * 0.5f);
        animator.SetIKHintPosition(elbow, elbowPos);
    }

    // --- UPDATED: CUSTODY CORE MECHANICS ---
    private void ExecuteArrest()
    {
        Ray ray = new Ray(player.PlayerCamera.position, player.PlayerCamera.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 2.5f, player.Interactor.CharacterLayer))
        {
            PlayerController victim = hit.collider.GetComponent<PlayerController>();
            if (victim != null && !victim.Vitals.isGhost && victim != player)
            {
                // If they are already arrested, just pick up the leash (costs no quota)
                if (victim.Vitals.isArrested)
                {
                    isDraggingPrisoner = true;
                    currentPrisoner = victim;
                    victim.Vitals.BecomeArrested(player);
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
                victim.Vitals.BecomeArrested(player);
            }
        }
    }

    private void TransferPrisoner(PlayerController targetRoyal)
    {
        Debug.Log($"Handed off {currentPrisoner.gameObject.name} to {targetRoyal.gameObject.name}!");

        // Transfer the custody variables
        targetRoyal.Vitals.isDraggingPrisoner = true;
        targetRoyal.Vitals.currentPrisoner = this.currentPrisoner;

        // Update the prisoner's target (The Breakout Timer WILL NOT reset)
        this.currentPrisoner.Vitals.BecomeArrested(targetRoyal);

        // Clear our own hands
        this.isDraggingPrisoner = false;
        this.currentPrisoner = null;
    }

    private void DropLeash()
    {
        isDraggingPrisoner = false;
        if (currentPrisoner != null)
        {
            currentPrisoner.Vitals.currentCaptor = null; // Setting this to null instantly breaks their follow loop
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
            if (currentPrisoner.Vitals.breakoutTimerCoroutine != null)
            {
                currentPrisoner.Vitals.StopCoroutine(currentPrisoner.Vitals.breakoutTimerCoroutine);
                currentPrisoner.Vitals.breakoutTimerCoroutine = null;
            }

            currentPrisoner.Vitals.currentCaptor = null;
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
        if (player.GetHeldItem() != null)
        {
            Debug.Log("You dropped your task item!");
            player.ClearHeldItem();
        }
        if (player.GetLeftHeldItem() != null)
        {
            Debug.Log("You dropped your off-hand item!");
            player.ClearLeftHeldItem();
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
            player.CharController.Move(moveDelta);

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
                currentCaptor.Vitals.isDraggingPrisoner = false;
                currentCaptor.Vitals.currentPrisoner = null;

                // Stun the Royal for 3 seconds
                currentCaptor.Vitals.ApplyStun(3f);
            }

            // Completely break the follow loop
            currentCaptor = null;
        }
    }

    // --- DEDICATED USE POWER-UP MECHANIC (F Key) --- (routed from PlayerController.OnUseItem)
    public void HandleUsePowerUp(bool isPressed)
    {
        if (!isPressed || isGhost || currentRole != PlayerRole.Corrupted) return;

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

    // Routed from PlayerController.OnScrollWheel.
    public void HandleScrollWheel(float scrollY)
    {
        if (currentRole != PlayerRole.Corrupted || isGhost) return;

        if (Mathf.Abs(scrollY) < 0.1f) return;

        // Hardware spam prevention
        if (Time.time < lastScrollTime + scrollCooldown) return;
        lastScrollTime = Time.time;

        // 1. Build a dynamic list of valid stops: Always include -1 (empty hands),
        //    plus any slot index that actually contains an item.
        List<int> validStops = new List<int>();
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

    public void EquipCorruptedSlot(int index)
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
        if (player.GetHeldItem() != null)
        {
            Debug.Log("Dropped standard item to pull out power-up!");
            player.ClearHeldItem();
        }
        if (player.GetLeftHeldItem() != null)
        {
            Debug.Log("Dropped off-hand item to pull out power-up!");
            player.ClearLeftHeldItem();
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
                Ray rayTrap = new Ray(player.PlayerCamera.position, player.PlayerCamera.forward);
                int environmentMask = ~player.Interactor.CharacterLayer; // Ignore players

                if (Physics.Raycast(rayTrap, out RaycastHit hitTrap, player.Interactor.InteractionRange * 1.5f, environmentMask))
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
                if (player.Interactor.CurrentTarget != null && player.Interactor.CurrentTarget is TaskDepositStation station)
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
                    if (RoleManager.Instance.currentKing != null && !RoleManager.Instance.currentKing.Vitals.isGhost)
                        vipTargets.Add(RoleManager.Instance.currentKing.transform);

                    if (RoleManager.Instance.currentKingsguard != null && !RoleManager.Instance.currentKingsguard.Vitals.isGhost)
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
                Collider[] hitColliders = Physics.OverlapSphere(transform.position, 10f, player.Interactor.CharacterLayer);
                int blindedCount = 0;

                foreach (Collider hitC in hitColliders)
                {
                    PlayerController victim = hitC.GetComponent<PlayerController>();
                    if (victim != null && victim != player && !victim.Vitals.isGhost && victim.Vitals.currentRole != PlayerRole.Corrupted)
                    {
                        victim.Vitals.ApplyBlindness(5f);
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

    // --- NEW: DAGGER DELAY & COUNTER-PLAY ---
    private System.Collections.IEnumerator DaggerStrikeRoutine(PowerUpData daggerData)
    {
        Debug.Log($"<color=#E74C3C>{gameObject.name} readies a dagger...</color>");

        // 1.5 second wind-up delay (Player movement is NOT restricted here!)
        yield return new WaitForSeconds(1.5f);

        Debug.Log($"<color=#C0392B>{gameObject.name} strikes!</color>");

        // 1. Fire the lethal raycast
        RaycastHit[] hits = Physics.SphereCastAll(player.PlayerCamera.position, 0.5f, player.PlayerCamera.forward, 2.5f, player.Interactor.CharacterLayer);
        bool hitConnected = false;

        // 2. Loop through every object we hit
        foreach (RaycastHit hit in hits)
        {
            PlayerController victim = hit.collider.GetComponent<PlayerController>();

            if (victim != null && victim != player && !victim.Vitals.isGhost && victim.Vitals.currentRole != PlayerRole.Corrupted)
            {
                // 3. CHECK FOR THE ROYAL BLOCK
                if ((victim.Vitals.currentRole == PlayerRole.King || victim.Vitals.currentRole == PlayerRole.Kingsguard) && victim.Vitals.isBlocking)
                {
                    Debug.Log($"<color=#F1C40F>Blocked! {victim.gameObject.name} deflected the assassination attempt!</color>");
                    hitConnected = true;
                    break; // Attack is blocked, item is fully consumed
                }
                else
                {
                    Debug.Log($"Stabbed {victim.gameObject.name} with a dagger!");
                    victim.Vitals.TakeDamage(1);
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

        Camera cam = player.PlayerCamera.GetComponent<Camera>();
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
                if (!p.Vitals.isGhost && p.Vitals.currentRole != PlayerRole.Corrupted && p != player)
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
}
