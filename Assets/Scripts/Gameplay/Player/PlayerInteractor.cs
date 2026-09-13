using UnityEngine;
using System.Collections.Generic;
using TMPro;
using CorruptedCourt.Core;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    // Owns aiming and interacting: the interaction raycast, the crosshair/prompt UI fade, who the player
    // is currently aiming at (a station/item, via IInteractable, or another player), and dispatching [E]
    // to the right target. Extracted from PlayerController - see PlayerController.cs for player.Interactor,
    // the single access point external code now uses.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("Social Deduction Settings")]
        [SerializeField] private float interactionRange = 3f;
        [Tooltip("Holding a matching item, [E] deposits it at a deposit station within this range even without aiming at it (for bulky/hauled items you can't see past).")]
        [SerializeField] private float depositProximityRange = 2.5f;
        [SerializeField] private LayerMask interactableLayer;
        [SerializeField] private LayerMask characterLayer; // NEW: Identifies other players
        public float InteractionRange => interactionRange;
        public LayerMask CharacterLayer => characterLayer;

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
        /// <summary>The other player the crosshair is currently on, or null. Read by PlayerController's
        /// OnNominate and by PlayerStrangle's strangle-hunt (FindStrangleVictim).</summary>
        public PlayerController TargetPlayer => targetPlayer;
        public void ClearTargetPlayer() => targetPlayer = null;
        /// <summary>The station/item currently aimed at, or null. Read by PlayerPowerUps' TargetedSabotage power-up.</summary>
        public IInteractable CurrentTarget => currentTarget;

        private Gallows sceneGallows;

        // Shared scratch buffer for the character-layer overlap sweep in CheckForInteractable's custody
        // handoff check. That runs on the main thread and consumes the hits immediately, so one static
        // buffer is safe. 16 is comfortably above any realistic number of characters on the character
        // layer inside one interaction radius.
        private static readonly Collider[] royalOverlapBuffer = new Collider[16];
        private static bool royalOverlapBufferFullWarned;

        // The sibling PlayerController on this same GameObject.
        private PlayerController player;

        void Awake()
        {
            player = GetComponent<PlayerController>();

            // Ensure the UI is hidden when the game starts.
            // 1. Turn it on EXACTLY ONCE when the game boots up
            if (interactionUI != null) interactionUI.gameObject.SetActive(true);
            // 2. Make it instantly invisible so it's ready to fade in later
            if (interactionCanvasGroup != null) interactionCanvasGroup.alpha = 0f;
        }

        // Called every frame from PlayerController.Update() (while !controlsLocked) to fade the
        // interaction prompt / crosshair in and out toward whatever CheckForInteractable last set.
        public void UpdateUIFade()
        {
            if (interactionCanvasGroup != null)
            {
                interactionCanvasGroup.alpha = Mathf.Lerp(interactionCanvasGroup.alpha, targetUIAlpha, Time.deltaTime * uiFadeSpeed);
            }
        }

        // Called from PlayerController.Update() to force the prompt to hide while a minigame or strangle
        // is active (those states suspend normal aiming).
        public void HideUIImmediately()
        {
            targetUIAlpha = 0f;
        }

        // Resolves what a hit collider means for interaction:
        //  1. a grabbable PickupItem ON the exact collider (a loose item wins over a station behind it)
        //  2. any IInteractable ON the exact collider - an active StationReversal wins over its
        //     co-located station's own IInteractable (see PreferActiveReversal)
        //  3. a TaskDepositStation ANCESTOR - so aiming at a chest's lid/sub-mesh deposits, and it beats
        //     a round-switch PickupItem that shares the same chest hierarchy. StationReversal is never
        //     active on a deposit station (reversal is retrieval there instead), so this branch never
        //     needs to consider it.
        //  4. any IInteractable ancestor
        private IInteractable ResolveInteractable(Collider col)
        {
            if (col == null) return null;

            PickupItem exactPickup = col.GetComponent<PickupItem>();
            if (exactPickup != null) return exactPickup;

            IInteractable exact = PreferActiveReversal(col.GetComponents<IInteractable>());
            if (exact != null) return exact;

            TaskDepositStation station = col.GetComponentInParent<TaskDepositStation>();
            if (station != null) return station;

            return col.GetComponentInParent<IInteractable>();
        }

        // A station and its StationReversal both implement IInteractable on the same GameObject, so
        // GetComponent<IInteractable>() alone can't reliably pick between them. Prefer an active
        // StationReversal (a satisfied, reversible station showing its reset prompt); otherwise fall
        // back to the first other interactable found, exactly matching the single-IInteractable
        // behavior every other object still has.
        private static IInteractable PreferActiveReversal(IInteractable[] candidates)
        {
            if (candidates == null || candidates.Length == 0) return null;

            IInteractable fallback = null;
            foreach (IInteractable c in candidates)
            {
                if (c is StationReversal reversal)
                {
                    if (reversal.IsActive) return reversal;
                    continue;
                }
                if (fallback == null) fallback = c;
            }
            return fallback;
        }

        public void CheckForInteractable()
        {
            // --- NEW: CUSTODY UI PROMPTS & PROXIMITY CHECKS ---
            if (player.Vitals.isDraggingPrisoner)
            {
                if (sceneGallows == null) sceneGallows = FindAnyObjectByType<Gallows>();

                bool nearGallows = sceneGallows != null && Vector3.Distance(transform.position, sceneGallows.transform.position) <= player.Vitals.gallowsRange;

                bool nearRoyal = false;
                PlayerController nearbyRoyal = null;
                int royalHitCount = Physics.OverlapSphereNonAlloc(transform.position, InteractionRange, royalOverlapBuffer, characterLayer);

                // Buffer full: OverlapSphereNonAlloc silently drops the rest, so this sweep can only ever
                // UNDER-count nearby royals - a truncated result may hide a valid handoff target. This
                // runs every frame while dragging a prisoner, so warn once rather than per-frame.
                if (royalHitCount == royalOverlapBuffer.Length && !royalOverlapBufferFullWarned)
                {
                    royalOverlapBufferFullWarned = true;
                    Log.Warn($"[PlayerInteractor] royal-proximity buffer full ({royalOverlapBuffer.Length}) - custody handoff target search may be truncated.");
                }

                for (int i = 0; i < royalHitCount; i++)
                {
                    Collider c = royalOverlapBuffer[i];
                    if (c == null) continue;

                    PlayerController p = c.GetComponent<PlayerController>();
                    if (p != null && p != player && !p.Vitals.isGhost && (p.Vitals.courtTitle == CourtTitle.King || p.Vitals.courtTitle == CourtTitle.Kingsguard))
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
                else if (nearRoyal && !nearbyRoyal.Vitals.isDraggingPrisoner)
                {
                    if (interactionUI != null) interactionUI.text = $"Press <color=#F4D03F>[Right Click]</color> to Handoff to {nearbyRoyal.DisplayName}\nPress <color=#F4D03F>[Q]</color> to Pardon";
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

            Ray ray = new Ray(player.PlayerCamera.position, player.PlayerCamera.forward);
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

                    if (player.Vitals.isGhost && (powerUp != null || royalWeapon != null))
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
                            if (player.Vitals.faction == Faction.Corrupted)
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
                            if (player.GetHeldItem() != null && player.GetHeldItem().requiresPartner)
                            {
                                prompts.Add($"Press <color=#F4D03F>[E]</color> to use <color=#5DADE2>{player.GetHeldItem().DisplayName}</color> with <color=#58D68D>{otherPlayer.DisplayName}</color>");
                            }
                            else
                            {
                                prompts.Add($"Press <color=#F4D03F>[E]</color> to interact with <color=#58D68D>{otherPlayer.DisplayName}</color>");
                            }

                            // 2. KING SPECIFIC
                            if (player.Vitals.courtTitle == CourtTitle.King && !player.Vitals.isGhost)
                            {
                                prompts.Add($"Press <color=#F4D03F>[F]</color> to appoint <color=#58D68D>{otherPlayer.DisplayName}</color> as Kingsguard");
                            }

                            // 3. ARREST MECHANICS (King & Kingsguard)
                            if ((player.Vitals.courtTitle == CourtTitle.King || player.Vitals.courtTitle == CourtTitle.Kingsguard) && !player.Vitals.isGhost)
                            {
                                if (otherPlayer.Vitals.isArrested)
                                    prompts.Add($"Press <color=#F4D03F>[Right Click]</color> to grab <color=#58D68D>{otherPlayer.DisplayName}</color>'s leash");
                                else
                                    prompts.Add($"Press <color=#F4D03F>[Right Click]</color> to arrest <color=#58D68D>{otherPlayer.DisplayName}</color>");
                            }

                            // 4. CORRUPTED MECHANICS (Corrupted)
                            // We check to ensure the target is alive and NOT a fellow Corrupted player
                            if (player.Vitals.faction == Faction.Corrupted && !player.Vitals.isGhost && !otherPlayer.Vitals.isGhost && otherPlayer.Vitals.faction != Faction.Corrupted)
                            {
                                prompts.Add($"Hold <color=#F4D03F>[Right Click]</color> to strangle <color=#E74C3C>{otherPlayer.DisplayName}</color>");
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

                if (player.GetHeldItem() != null || player.GetLeftHeldItem() != null)
                {
                    if (interactionUI != null)
                    {
                        List<string> heldPrompts = new List<string>();

                        if (player.GetHeldItem() != null)
                        {
                            // UPDATED: E is now explicitly for using, Q is for dropping
                            heldPrompts.Add($"Press <color=#F4D03F>[E]</color> to use or <color=#F4D03F>[Q]</color> to drop <color=#5DADE2>{player.GetHeldItem().DisplayName}</color>");
                        }

                        if (player.GetLeftHeldItem() != null)
                        {
                            heldPrompts.Add($"Press <color=#F4D03F>[R]</color> to swap in <color=#5DADE2>{player.GetLeftHeldItem().DisplayName}</color> (off-hand)");
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

        public void PerformInteraction()
        {
            // 0. Holding a depositable item near its deposit station: drop it off without needing to aim
            //    at the station (you often can't see past a hauled chest). Skipped when you're already
            //    aiming right at a deposit station - that one takes priority.
            if (player.GetHeldItem() != null && !(currentTarget is TaskDepositStation) && TryProximityDeposit())
                return;

            // 1. If we are looking at something interactable (Stations, items on the floor), interact with it
            if (currentTarget != null)
            {
                // --- CORRUPTED POWER-UP PICKUP LOGIC ---
                PowerUpPickup powerUp = currentTarget as PowerUpPickup;
                if (powerUp != null)
                {
                    if (player.Vitals.isGhost) return; // Ghosts cannot pick up power-ups
                    if (player.Vitals.faction == Faction.Corrupted)
                    {
                        // Find the first empty slot in the array
                        int emptySlotIndex = -1;
                        for (int i = 0; i < player.Vitals.corruptedInventory.Length; i++)
                        {
                            if (player.Vitals.corruptedInventory[i] == null)
                            {
                                emptySlotIndex = i;
                                break;
                            }
                        }

                        if (emptySlotIndex != -1) // We found an empty slot!
                        {
                            Log.Game($"[Corrupted] Picked up power-up: {powerUp.powerUpData.powerUpName} in Slot {emptySlotIndex + 1}");

                            // Assign it to that exact slot
                            player.Vitals.corruptedInventory[emptySlotIndex] = powerUp.powerUpData;

                            GameEvents.RaiseCorruptedInventoryChanged(player.Vitals.corruptedInventory);

                            Destroy(powerUp.gameObject); // Remove from floor

                            // Clear UI
                            currentTarget = null;
                            targetUIAlpha = 0f;
                        }
                        else
                        {
                            Log.Game("Inventory Full! You do not have any empty slots.");
                        }
                    }
                    else
                    {
                        Log.Game("[Innocent] You poke the mysterious object, but have no idea what it is or how to use it.");
                    }
                    return; // Stop here so it doesn't run standard interaction logic
                }

                // --- ROYAL WEAPON RESTRICTION ---
                RoyalWeapon royalWeapon = currentTarget as RoyalWeapon;
                if (royalWeapon != null)
                {
                    if (player.Vitals.isGhost) return; // Ghosts cannot pick up weapons
                    if (player.Vitals.courtTitle != royalWeapon.restrictedRole)
                    {
                        Log.Game($"[Denied] Only the {royalWeapon.restrictedRole} may wield this weapon!");
                        return; // Stop here so they cannot pick it up
                    }
                }

                currentTarget.OnInteract(this.gameObject);

                // --- UNIVERSAL TASK EVALUATION ---
                GameObject targetObj = (currentTarget as MonoBehaviour)?.gameObject;
                player.TaskBook.EvaluateActiveTasks(targetObj, stopOnMinigame: true);

                // If we interacted with a fixed STATION that carries its own process minigame and no
                // task launched one, fire it anyway - so ANY role can run it (fake the task).
                // A grabbable item is explicitly excluded: you pick it up first, then press [E] again
                // with it in hand to run its minigame (see path 3 below).
                bool targetIsGrabbable = targetObj != null && targetObj.GetComponent<PickupItem>() != null;
                if (!player.isPlayingMinigame && !targetIsGrabbable
                    && currentTarget is TaskStation sourceStation
                    && sourceStation.processMinigamePrefab != null)
                {
                    player.StartMinigame(sourceStation.processMinigamePrefab, null, targetObj);
                    return;
                }

                player.TaskBook.RefreshLocalWaypoints();

                // Only update the prompt if a minigame didn't just steal mouse focus
                if (currentTarget != null && interactionUI != null && !player.isPlayingMinigame)
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
            else if (player.GetHeldItem() != null)
            {
                // A spent item (e.g. an emptied plate) does nothing on [E] - just carry or drop it.
                if (player.GetHeldItem().Has(ItemState.Spent))
                {
                    Log.Game($"Nothing left to do with the {player.GetHeldItem().DisplayName}.");
                    return;
                }

                // A held item that carries its own minigame ALWAYS launches it on [E].
                // Independent of role, of any related task, or of whether that task was already done.
                if (player.GetHeldItem().processMinigamePrefab != null)
                {
                    player.StartMinigame(player.GetHeldItem().processMinigamePrefab, null, player.GetHeldItem().gameObject);
                    return;
                }

                // --- UNIVERSAL TASK EVALUATION ---
                bool taskCompleted = player.TaskBook.EvaluateActiveTasks(player.GetHeldItem().gameObject, stopOnMinigame: true);

                // NEW: If a minigame UI is now open, abort below!
                if (player.isPlayingMinigame) return;

                // No process minigame on this item, so [E] does nothing here - it's just carried until
                // it reaches wherever it's needed (a deposit station, another player, etc). Items that
                // ARE meant to be worked on with [E] carry a Process Minigame Prefab, which was launched
                // above.
                if (!taskCompleted)
                    Log.Game($"Nothing to do with the {player.GetHeldItem().DisplayName} here - carry it where it needs to go.");
            }
            else
            {
                Log.Game("No interactable target in range and hands are empty.");
            }
        }

        // Deposits the held item at the nearest matching TaskDepositStation within depositProximityRange,
        // no aiming required. Returns true if a deposit was attempted (and advances any task step it
        // satisfies, mirroring the aimed-interaction path).
        private bool TryProximityDeposit()
        {
            if (player.GetHeldItem() == null || TaskLocation.AllLocations == null) return false;

            TaskDepositStation best = null;
            float bestDist = depositProximityRange;

            foreach (TaskLocation loc in TaskLocation.AllLocations)
            {
                if (loc == null) continue;
                TaskDepositStation st = loc.GetComponent<TaskDepositStation>();
                if (st == null || !st.HasFreeSlot() || !st.AcceptsItem(player.GetHeldItem())) continue;

                float d = Vector3.Distance(transform.position, loc.transform.position);
                if (d <= bestDist) { bestDist = d; best = st; }
            }

            if (best == null) return false;

            best.OnInteract(this.gameObject);

            GameObject stationObj = best.gameObject;
            player.TaskBook.EvaluateActiveTasks(stationObj, stopOnMinigame: true);
            return true;
        }

        private void HandlePlayerInteraction(PlayerController target)
        {
            if (target == null) return;

            if (target.GetHeldItem() != null)
            {
                Log.Game("Multiplayer interaction failed: Your helper must be empty-handed.");
                return;
            }

            // --- NEW: UNIVERSAL TASK EVALUATION ---
            bool taskCompleted = player.TaskBook.EvaluateActiveTasks(target.gameObject);

            // 2. Perform the physical action regardless of tasks! (Allows faking)
            if (player.GetHeldItem() != null && player.GetHeldItem().requiresPartner)
            {
                Log.Game($"Used {player.GetHeldItem().DisplayName} with {target.gameObject.name}!" + (taskCompleted ? " (Task Completed)" : " (Faked Task)"));
                GameObject initiatorItemObj = player.GetHeldItem().gameObject;
                player.ClearHeldItem();
                Destroy(initiatorItemObj);
            }
            else if (player.GetHeldItem() == null)
            {
                Log.Game($"Interacted with {target.gameObject.name}!" + (taskCompleted ? " (Task Completed)" : " (Faked Task)"));
            }

            player.TaskBook.RefreshLocalWaypoints();
        }
    }
}
