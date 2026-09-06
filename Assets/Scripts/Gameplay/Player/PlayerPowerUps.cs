using UnityEngine;
using System.Collections.Generic;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    // Owns the Corrupted power-up loadout: the fixed 3-slot inventory, equipping / scroll-cycling /
    // using a slot, the in-hand 3D model, and every PowerUpType effect (Daggers, Invisibility, Traps,
    // Targeted Sabotage, Spymaster's Ledger, Stolen Heraldry, Blinding Ash, Fool's Illusion) plus their
    // coroutines. Extracted from PlayerVitals - see PlayerVitals.cs for player.Vitals.corruptedInventory
    // / .EquipCorruptedSlot(), the forwarders external code still uses, and PlayerController for the
    // OnUseItem / OnUsePowerUp1..3 / OnScrollWheel input routing.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerPowerUps : MonoBehaviour
    {
        // --- FIXED-SLOT CORRUPTED INVENTORY ---
        public PowerUpData[] corruptedInventory = new PowerUpData[3];

        [Header("Active Inventory")]
        public Transform powerUpHoldPoint;
        public int activeSlotIndex = -1;
        private GameObject activePowerUpVisual;

        // --- SCROLL DEBOUNCE ---
        public float scrollCooldown = 0.15f;
        private float lastScrollTime = 0f;

        [Header("Power-Up Prefabs")]
        public GameObject trapPrefab;
        public GameObject illusionPrefab;

        private Renderer[] playerRenderers;

        // Scratch buffer for the Blinding Ash overlap sweep in ExecutePowerUp. It runs on the main
        // thread and consumes the hits immediately, so one static buffer is safe. 16 is comfortably
        // above any realistic number of characters inside the 10-unit blast radius.
        private static readonly Collider[] characterOverlapBuffer = new Collider[16];
        private static bool characterOverlapBufferFullWarned;

        // The sibling components on this same GameObject.
        private PlayerController player;
        private PlayerVitals vitals;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            vitals = GetComponent<PlayerVitals>();
            // Cache all the meshes so the Invisibility Potion can turn them off
            playerRenderers = GetComponentsInChildren<Renderer>();
        }

        /// <summary>True while a power-up 3D model is physically in the Corrupted player's hand. Read by
        /// PlayerStrangle to block a strangle while a power-up is equipped.</summary>
        public bool HasActivePowerUp => activePowerUpVisual != null;

        // --- DEDICATED USE POWER-UP MECHANIC (F Key) --- (routed from PlayerController.OnUseItem)
        public void HandleUsePowerUp(bool isPressed)
        {
            if (!isPressed || vitals.isGhost || vitals.faction != Faction.Corrupted) return;

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

                    GameEvents.RaiseCorruptedInventoryChanged(corruptedInventory);
                    GameEvents.RaiseCorruptedSlotHighlighted(-1);
                }
            }
            else
            {
                Log.Game("You don't have a power-up equipped to use!");
            }
        }

        // Routed from PlayerController.OnScrollWheel.
        public void HandleScrollWheel(float scrollY)
        {
            if (vitals.faction != Faction.Corrupted || vitals.isGhost) return;

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
            if (vitals.faction != Faction.Corrupted || vitals.isGhost) return;

            // 1. Clean up the currently held visual
            if (activePowerUpVisual != null)
            {
                Destroy(activePowerUpVisual);
            }

            // 2. Are we unequipping everything intentionally? (Index -1)
            if (index == -1)
            {
                activeSlotIndex = -1;
                GameEvents.RaiseCorruptedSlotHighlighted(-1);
                return;
            }

            // 3. Update slot tracking
            activeSlotIndex = index;
            PowerUpData data = corruptedInventory[index];

            // 4. If the slot is EMPTY, highlight it but don't spawn anything in-hand
            if (data == null)
            {
                GameEvents.RaiseCorruptedSlotHighlighted(activeSlotIndex);
                Log.Game($"Equipped empty slot {index + 1}");
                return; // Stop here!
            }

            // 5. We are equipping a VALID power-up!
            if (player.GetHeldItem() != null)
            {
                Log.Game("Dropped standard item to pull out power-up!");
                player.ClearHeldItem();
            }
            if (player.GetLeftHeldItem() != null)
            {
                Log.Game("Dropped off-hand item to pull out power-up!");
                player.ClearLeftHeldItem();
            }

            // Spawn the physical 3D model into their hand
            if (data.iconPrefab != null && powerUpHoldPoint != null)
            {
                activePowerUpVisual = Instantiate(data.iconPrefab, powerUpHoldPoint.position, powerUpHoldPoint.rotation, powerUpHoldPoint);
            }

            // Update the UI
            GameEvents.RaiseCorruptedSlotHighlighted(activeSlotIndex);
            Log.Game($"Equipped {data.powerUpName} in slot {index + 1}");
        }

        private bool ExecutePowerUp(PowerUpData powerUp)
        {
            Log.Game($"--- EXECUTING POWER-UP: {powerUp.powerUpName} ---");

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
                                Log.Game("Trap deployed!");
                            }
                            return true; // SUCCESS
                        }
                        else
                        {
                            Log.Game("Surface is too steep to place a trap.");
                            return false; // FAIL
                        }
                    }
                    Log.Game("You must look at the ground to place a trap.");
                    return false; // FAIL

                case PowerUpType.TargetedSabotage:
                    if (player.Interactor.CurrentTarget is TaskDepositStation station)
                    {
                        if (station.SabotageMostRecentDeposit())
                        {
                            Log.Game($"Sabotaged the {station.gameObject.name}! Its most recent deposit is gone - the Court has to bring another.");
                            return true;
                        }

                        Log.Game($"The {station.gameObject.name} has nothing deposited to sabotage.");
                        return false;
                    }
                    else
                    {
                        Log.Game("You must be looking at a Task Deposit Station to sabotage it!");
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

                    if (vipTargets.Count > 0)
                    {
                        GameEvents.RaiseSpymasterWaypointsShown(vipTargets, 10f);
                        Log.Game("Spymaster's Ledger used! High-value targets revealed for 10 seconds.");
                        return true;
                    }
                    Log.Game("Spymaster's Ledger failed. High-value targets are dead or unavailable.");
                    return false;

                case PowerUpType.StolenHeraldry:
                    StartCoroutine(StolenHeraldryRoutine(15f));
                    return true;

                case PowerUpType.AlchemistsBlindingAsh:
                    int ashHitCount = Physics.OverlapSphereNonAlloc(transform.position, 10f, characterOverlapBuffer, player.Interactor.CharacterLayer);
                    int blindedCount = 0;

                    // Buffer full: OverlapSphereNonAlloc silently drops the rest, so some in-range
                    // innocents may be missed by the blast. Warn once so a truncated hit is visible.
                    if (ashHitCount == characterOverlapBuffer.Length && !characterOverlapBufferFullWarned)
                    {
                        characterOverlapBufferFullWarned = true;
                        Log.Warn($"[PlayerPowerUps] character-overlap buffer full ({characterOverlapBuffer.Length}) - Blinding Ash may not have hit every player in range.");
                    }

                    for (int i = 0; i < ashHitCount; i++)
                    {
                        Collider hitC = characterOverlapBuffer[i];
                        if (hitC == null) continue;

                        PlayerController victim = hitC.GetComponent<PlayerController>();
                        if (victim != null && victim != player && !victim.Vitals.isGhost && victim.Vitals.faction != Faction.Corrupted)
                        {
                            victim.Vitals.ApplyBlindness(5f);
                            blindedCount++;
                        }
                    }
                    Log.Game($"Blinding Ash shattered! Blinded {blindedCount} innocent players.");
                    return true;

                case PowerUpType.FoolsIllusion:
                    if (illusionPrefab != null)
                    {
                        Instantiate(illusionPrefab, transform.position, transform.rotation);
                        Log.Game("Fool's Illusion deployed! It will vanish in 10 seconds.");
                    }
                    return true;
            }

            return false; // Fallback
        }

        // --- DAGGER DELAY & COUNTER-PLAY ---
        private System.Collections.IEnumerator DaggerStrikeRoutine(PowerUpData daggerData)
        {
            Log.Game($"<color=#E74C3C>{gameObject.name} readies a dagger...</color>");

            // 1.5 second wind-up delay (Player movement is NOT restricted here!)
            yield return new WaitForSeconds(1.5f);

            Log.Game($"<color=#C0392B>{gameObject.name} strikes!</color>");

            // 1. Fire the lethal raycast
            RaycastHit[] hits = Physics.SphereCastAll(player.PlayerCamera.position, 0.5f, player.PlayerCamera.forward, 2.5f, player.Interactor.CharacterLayer);
            bool hitConnected = false;

            // 2. Loop through every object we hit
            foreach (RaycastHit hit in hits)
            {
                PlayerController victim = hit.collider.GetComponent<PlayerController>();

                if (victim != null && victim != player && !victim.Vitals.isGhost && victim.Vitals.faction != Faction.Corrupted)
                {
                    // 3. CHECK FOR THE ROYAL BLOCK (a royal weapon is title-gated, so gate the deflect on title)
                    if ((victim.Vitals.courtTitle == CourtTitle.King || victim.Vitals.courtTitle == CourtTitle.Kingsguard) && victim.Vitals.isBlocking)
                    {
                        Log.Game($"<color=#F1C40F>Blocked! {victim.gameObject.name} deflected the assassination attempt!</color>");
                        hitConnected = true;
                        break; // Attack is blocked, item is fully consumed
                    }
                    else
                    {
                        Log.Game($"Stabbed {victim.gameObject.name} with a dagger!");
                        victim.Vitals.TakeDamage(1);
                        hitConnected = true;
                        break; // Attack succeeds, item is fully consumed
                    }
                }
            }

            if (!hitConnected)
            {
                Log.Game("The dagger swing missed entirely! (Item consumed)");
            }
        }

        private System.Collections.IEnumerator HandleInvisibility(float duration)
        {
            Log.Game("Invisibility Activated!");

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
            Log.Game("Invisibility Faded.");
        }

        private System.Collections.IEnumerator StolenHeraldryRoutine(float duration)
        {
            // 1. Gather all living innocent players to steal an identity from
            List<PlayerController> innocents = new List<PlayerController>();
            if (RoleManager.Instance != null)
            {
                foreach (PlayerController p in RoleManager.Instance.allPlayers)
                {
                    if (p == null) continue;

                    if (!p.Vitals.isGhost && p.Vitals.faction != Faction.Corrupted && p != player)
                        innocents.Add(p);
                }
            }

            if (innocents.Count == 0)
            {
                Log.Game("No living innocents left to disguise as!");
                yield break;
            }

            // 2. Pick a random innocent
            PlayerController stolenIdentity = innocents[Random.Range(0, innocents.Count)];

            // 3. Save the Corrupted player's real presentation (display name + body material).
            string originalDisplayName = player.DisplayName;
            Material originalMat = null;
            if (playerRenderers != null && playerRenderers.Length > 0 && playerRenderers[0] != null)
            {
                originalMat = playerRenderers[0].material;
            }

            // 4. APPLY THE DISGUISE - display name + material ONLY. gameObject.name and every lookup
            //    identity (the PlayerController reference, PlayerController.Local) are left untouched,
            //    so a disguise can never make the game address the wrong player.
            player.SetDisplayName(stolenIdentity.DisplayName);
            if (originalMat != null)
            {
                // Copy the innocent's material color/texture
                Renderer targetRenderer = stolenIdentity.GetComponentInChildren<Renderer>();
                if (targetRenderer != null) playerRenderers[0].material = targetRenderer.material;
            }

            Log.Game($"<color=#F1C40F>Stolen Heraldry active! You look exactly like {stolenIdentity.DisplayName}.</color>");

            yield return new WaitForSeconds(duration);

            // 5. REMOVE THE DISGUISE
            player.SetDisplayName(originalDisplayName);
            if (originalMat != null && playerRenderers.Length > 0 && playerRenderers[0] != null)
            {
                playerRenderers[0].material = originalMat;
            }

            Log.Game("<color=#F1C40F>Your disguise has worn off!</color>");
        }
    }
}
