using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    // Owns this player's core character state: role, health, ghosting, status effects (stun / pushback /
    // blindness), custody (arrest / captor / prisoner / breakout), punch, and which zone they're standing
    // in. The strangle mechanic and the Corrupted power-up loadout were split out to the sibling
    // PlayerStrangle / PlayerPowerUps components; this class keeps thin forwarders (isStrangling,
    // GetNeckWorldPosition, corruptedInventory) for the external callers that still reach through Vitals.
    // Extracted from PlayerController - see PlayerController.cs for player.Vitals, the access point
    // external code uses.
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerStrangle))]
    [RequireComponent(typeof(PlayerPowerUps))]
    public class PlayerVitals : MonoBehaviour
    {
        [Header("Role Settings")]
        public PlayerRole currentRole = PlayerRole.None;

        [Header("Status")]
        public int currentHealth = 1;
        public int maxHealth = 1;
        public bool isBlocking = false;
        public bool isGhost = false;

        [Header("Status Effects")]
        private bool isStunned = false;
        /// <summary>Read by PlayerMotor.HandleMovement / UpdateLeanBlend, which lock movement and lean while stunned.</summary>
        public bool IsStunned => isStunned;

        private bool isBeingPushed = false;
        /// <summary>Read by PlayerMotor.HandleMovement, which locks movement speed to 0 while a pushback is in progress.</summary>
        public bool IsBeingPushed => isBeingPushed;

        [Header("Custody Mechanics")]
        public PlayerController currentPrisoner;
        public Coroutine breakoutTimerCoroutine;
        public int arrestQuota = 2;
        public bool isDraggingPrisoner = false;
        public bool isArrested = false;
        public PlayerController currentCaptor;
        public float gallowsRange = 4.0f;
        private Gallows sceneGallows;

        // Scratch buffer for the character-layer overlap sweep in the custody handoff check
        // (HandleArrestInput). It runs on the main thread and consumes the hits immediately, so one
        // static buffer is safe. 16 is comfortably above any realistic number of characters inside one
        // sweep radius.
        private static readonly Collider[] characterOverlapBuffer = new Collider[16];
        private static bool characterOverlapBufferFullWarned;

        [Header("Punch Mechanic")]
        public float punchCooldown = 2f;
        private float lastPunchTime = -2f; // Starts at -2 so they can punch immediately
        public float punchRange = 2f;
        public float punchRadius = 0.5f;
        public float pushbackForce = 15f;
        public float pushbackDuration = 0.2f;

        // NEW: Tracks which room the player is currently standing in
        public string currentZoneID = "";

        // The sibling components on this same GameObject.
        private PlayerController player;
        private PlayerMotor motor;
        private PlayerStrangle strangle;
        private PlayerPowerUps powerUps;

        /// <summary>The strangle state machine + reach IK, split out to its own sibling component.</summary>
        public PlayerStrangle Strangle => strangle;
        /// <summary>The Corrupted power-up loadout + effects, split out to its own sibling component.</summary>
        public PlayerPowerUps PowerUps => powerUps;

        // --- Thin forwarders kept so external callers that still reach through Vitals don't change:
        //     PlayerMotor / PlayerController read isStrangling; PlayerStrangle resolves a victim's neck
        //     through that victim's Vitals; PlayerInteractor fills corruptedInventory on pickup. ---
        public bool isStrangling => strangle.isStrangling;
        public bool isReachingToStrangle => strangle.isReachingToStrangle;
        public Vector3 GetNeckWorldPosition() => strangle.GetNeckWorldPosition();
        public PowerUpData[] corruptedInventory => powerUps.corruptedInventory;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            motor = GetComponent<PlayerMotor>();
            strangle = GetComponent<PlayerStrangle>();
            powerUps = GetComponent<PlayerPowerUps>();
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

            Log.Game($"[Role Assignment] {gameObject.name} is now: {currentRole} with {currentHealth} HP");
        }

        // --- NEW: COMBAT DAMAGE SYSTEM ---
        public void TakeDamage(int amount)
        {
            if (isGhost) return; // Ghosts cannot take damage

            currentHealth -= amount;
            Log.Game($"<color=#E74C3C>{gameObject.name} took {amount} damage! Current HP: {currentHealth}</color>");

            if (currentHealth <= 0)
            {
                Log.Game($"<color=#922B21>{gameObject.name} HAS BEEN KILLED!</color>");
                BecomeGhost();
            }
        }

        public void BecomeGhost()
        {
            if (isGhost) return; // Already a ghost

            Log.Game($"--- {gameObject.name} HAS BECOME A GHOST ---");
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

            // Let managers re-evaluate off this one event (MatchManager checks win conditions and drops
            // the player from the pre-meeting absent list) instead of polling isGhost every frame.
            GameEvents.RaisePlayerGhosted(player);
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
            Log.Game($"<color=#E74C3C>{gameObject.name} is STUNNED for {duration} seconds!</color>");

            yield return new WaitForSeconds(duration);

            isStunned = false;
            Log.Game($"{gameObject.name} is no longer stunned.");
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
                Log.Game("Punch is on cooldown!");
                return;
            }

            ExecutePunch();
        }

        private void ExecutePunch()
        {
            Log.Game($"{gameObject.name} throws a punch!");

            // Cast a thick sphere forward. We omit the layer mask so it can hit players OR physics items
            if (Physics.SphereCast(player.PlayerCamera.position, punchRadius, player.PlayerCamera.forward, out RaycastHit hit, punchRange))
            {
                // 1. Did we hit a Player?
                PlayerController victim = hit.collider.GetComponent<PlayerController>();
                if (victim != null && victim != player && !victim.Vitals.isGhost)
                {
                    Log.Game($"Punched {victim.gameObject.name}!");

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
                    Log.Game($"Punched an item!");
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
                Log.Game("You cannot block while your hands are full dragging a prisoner!");
                yield break;
            }

            Log.Game($"<color=#3498DB>{gameObject.name} raises the {weapon.DisplayName} to block!</color>");
            isBlocking = true;

            // Wait for the duration of the block
            yield return new WaitForSeconds(weapon.blockDuration);

            isBlocking = false;
            Log.Game($"{gameObject.name} lowers their guard.</color>");
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
            Log.Game($"You pardoned and freed {currentPrisoner.gameObject.name}!");
            currentPrisoner = null;
            return true;
        }

        // --- DEDICATED RIGHT CLICK, non-minigame branch (King / Kingsguard only): arrest, leash
        // control, and custody handoff. Routed from PlayerController.OnStrangle once it has ruled out
        // the minigame-camera-look interception. The Corrupted strangle branch on the same button
        // lives in PlayerStrangle.HandleStrangleInput. ---
        public void HandleArrestInput(bool isPressed)
        {
            if (isGhost || !isPressed) return;
            if (currentRole != PlayerRole.King && currentRole != PlayerRole.Kingsguard) return;

            if (isDraggingPrisoner)
            {
                if (sceneGallows == null) sceneGallows = FindAnyObjectByType<Gallows>();

                bool nearGallows = sceneGallows != null && Vector3.Distance(transform.position, sceneGallows.transform.position) <= gallowsRange;

                bool nearRoyal = false;
                PlayerController nearbyRoyal = null;
                int royalHitCount = Physics.OverlapSphereNonAlloc(
                    transform.position, player.Interactor.InteractionRange, characterOverlapBuffer, player.Interactor.CharacterLayer);

                // Buffer full: OverlapSphereNonAlloc silently drops the rest, so this sweep can only
                // ever UNDER-count nearby royals - a truncated result may hide a valid handoff target.
                if (royalHitCount == characterOverlapBuffer.Length && !characterOverlapBufferFullWarned)
                {
                    characterOverlapBufferFullWarned = true;
                    Log.Warn($"[PlayerVitals] character-overlap buffer full ({characterOverlapBuffer.Length}) - custody handoff target search may be truncated.");
                }

                for (int i = 0; i < royalHitCount; i++)
                {
                    Collider c = characterOverlapBuffer[i];
                    if (c == null) continue;

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
                        Log.Game($"Grabbed {victim.gameObject.name}'s leash!");
                        return;
                    }

                    // Otherwise, brand new arrest
                    if (arrestQuota <= 0)
                    {
                        Log.Game("You are out of shackles!");
                        return;
                    }

                    arrestQuota--;
                    isDraggingPrisoner = true;
                    currentPrisoner = victim;
                    Log.Game($"<color=#3498DB>Arrested {victim.gameObject.name}! {arrestQuota} shackles left.</color>");
                    victim.Vitals.BecomeArrested(player);
                }
            }
        }

        private void TransferPrisoner(PlayerController targetRoyal)
        {
            Log.Game($"Handed off {currentPrisoner.gameObject.name} to {targetRoyal.gameObject.name}!");

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
                Log.Game($"Dropped {currentPrisoner.gameObject.name}'s leash. They are still frozen!");
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

                Log.Game($"<color=#9B59B6>{currentPrisoner.gameObject.name} has been locked to the Gallows!</color>");
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
            Log.Game($"<color=#E74C3C>You are under arrest by {captor.gameObject.name}!</color>");

            // 1. Force drop whatever is in their hands (both hands)
            if (player.GetHeldItem() != null)
            {
                Log.Game("You dropped your task item!");
                player.ClearHeldItem();
            }
            if (player.GetLeftHeldItem() != null)
            {
                Log.Game("You dropped your off-hand item!");
                player.ClearLeftHeldItem();
            }

            // 2. Force Corrupted to unequip any active power-ups
            if (powerUps.activeSlotIndex != -1)
            {
                powerUps.EquipCorruptedSlot(-1);
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
                Log.Game("<color=#E74C3C>The prisoner broke free from their restraints!</color>");
                isArrested = false;

                if (currentCaptor != null)
                {
                    Log.Game($"<color=#F39C12>{currentCaptor.gameObject.name} was stunned by the escaping prisoner!</color>");

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

        // --- Blinding Ash status effect (applied to a victim by PlayerPowerUps.ExecutePowerUp's
        // AlchemistsBlindingAsh case, via victim.Vitals.ApplyBlindness). ---
        public void ApplyBlindness(float duration)
        {
            if (isGhost) return; // Ghosts don't get blinded
            StartCoroutine(BlindnessRoutine(duration));
        }

        private System.Collections.IEnumerator BlindnessRoutine(float duration)
        {
            Log.Game($"<color=#8E44AD>{gameObject.name} was hit by Blinding Ash!</color>");

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

            Log.Game($"{gameObject.name}'s vision has cleared.");
        }
    }
}
