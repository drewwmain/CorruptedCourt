using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Items;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Gameplay
{
    // Owns what the player is holding: the two hand slots (currentlyHeldItem / leftHeldItem), the hand
    // sockets those items attach to, equip/haul/swap-hands logic, and the drop-vs-throw charge system.
    // Extracted from PlayerController - see PlayerController.cs for the thin forwarders TaskDepositStation,
    // PickupItem, the task step types (Tasks/Steps/), and the minigames all still call (EquipItem, GetHeldItem,
    // ClearHeldItem, GetLeftHeldItem, ClearLeftHeldItem, IsHoldingItem, RightHandSocket, LeftHandSocket).
    //
    // Must live on the same GameObject as PlayerController (back-reference below resolves via
    // GetComponent and assumes that).
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInventory : MonoBehaviour
    {
        [Header("Equipment Settings")]
        [SerializeField] private Transform rightHandSocket;
        [SerializeField] private Transform leftHandSocket; // The future rig bone attachment point
        public Transform RightHandSocket => rightHandSocket;
        public Transform LeftHandSocket => leftHandSocket;

        [Header("Throwing Mechanics")]
        public float maxThrowChargeTime = 2.0f; // Maximum seconds the button can be held
        public float baseThrowForce = 25f;      // The baseline force before weight is applied
        [Tooltip("Hold [Q] up to this long = a tap: drop the item in place. Longer = charge and throw.")]
        public float dropTapMaxDuration = 0.2f;

        private float currentThrowCharge = 0f;
        private bool isChargingThrow = false;
        private float dropPressTime = 0f;

        /// <summary>True while [Q] is held and a throw is charging. PlayerController.OnDropItem reads this on release.</summary>
        public bool IsChargingThrow => isChargingThrow;
        /// <summary>True if press-to-release was short enough to count as a tap (drop) rather than a hold (throw).</summary>
        public bool WasTap => (Time.time - dropPressTime) <= dropTapMaxDuration;

        private PickupItem currentlyHeldItem; // RIGHT hand: the "active" hand used for throwing, processing, minigames, and partner tasks
        private PickupItem leftHeldItem;      // LEFT hand: the off-hand. Carries a second item; press SwapHands to move it into the active hand

        // Set by PlayerController.StartMinigame when an Item-target minigame borrows the left hand for an
        // item normally held in the right hand; ReturnSwappedItem puts it back once the minigame ends.
        public bool itemSwappedToLeftHand = false;

        // The sibling PlayerController on this same GameObject.
        private PlayerController player;

        void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        // --- Drop / throw charge ---

        /// <summary>Ticks the throw-charge timer. Called every frame from PlayerController.Update() - a
        /// no-op unless a throw is currently charging.</summary>
        public void TickThrowCharge()
        {
            if (isChargingThrow)
            {
                currentThrowCharge += Time.deltaTime;
                currentThrowCharge = Mathf.Clamp(currentThrowCharge, 0f, maxThrowChargeTime);
            }
        }

        /// <summary>Begins charging a potential throw. Called from PlayerController.OnDropItem on press.</summary>
        public void StartCharging()
        {
            dropPressTime = Time.time;
            isChargingThrow = true;
            currentThrowCharge = 0f;
        }

        /// <summary>Stops charging without dropping or throwing - the caller still decides tap-vs-throw
        /// itself using WasTap. Called from PlayerController.OnDropItem on release.</summary>
        public void StopCharging() => isChargingThrow = false;

        // Drops the active-hand item where the player stands; if the active hand is empty, drops the off-hand item.
        public void DropHeldItemInPlace()
        {
            PickupItem toDrop = currentlyHeldItem != null ? currentlyHeldItem : leftHeldItem;
            if (toDrop == null)
            {
                Log.Game("Nothing to drop.");
                return;
            }

            ItemLifecycle.Drop(player, toDrop, DropCause.Manual);
            if (toDrop == currentlyHeldItem) currentlyHeldItem = null;
            else leftHeldItem = null;

            Log.Game($"Dropped {toDrop.DisplayName}.");
            player.TaskBook.CheckRegressionForAll();
            player.TaskBook.EvaluateActiveTasks(new TaskEvalContext
            {
                Player = player,
                Reason = TaskEvalReason.InventoryChanged,
                Target = toDrop.gameObject,
                Item = toDrop
            });
            player.TaskBook.RefreshLocalWaypoints();
        }

        /// <summary>Drops any heavy or two-handed-haul item from BOTH hands where the player stands, then
        /// regresses any task that needed it. Called by MatchManager (via PlayerController.SetScrambleGrace)
        /// when the pre-meeting scramble begins so a player caught mid-haul isn't movement-locked out of
        /// reaching the meeting room in time (G3.1). No-op if neither hand holds a heavy/haul item.</summary>
        public void DropHeavyItems()
        {
            // Captured (rather than a bool flag) so each dropped item - up to one per hand - can raise
            // its own InventoryChanged evaluation below.
            PickupItem droppedRightItem = null;
            PickupItem droppedLeftItem = null;

            if (currentlyHeldItem != null && (currentlyHeldItem.isHeavy || currentlyHeldItem.haulWithBothHands))
            {
                droppedRightItem = currentlyHeldItem;
                currentlyHeldItem = null;
                ItemLifecycle.Drop(player, droppedRightItem, DropCause.Forced);
                player.haulActive = false;
                Log.Game($"Dropped {droppedRightItem.DisplayName} for the meeting scramble.");
            }

            if (leftHeldItem != null && (leftHeldItem.isHeavy || leftHeldItem.haulWithBothHands))
            {
                droppedLeftItem = leftHeldItem;
                leftHeldItem = null;
                ItemLifecycle.Drop(player, droppedLeftItem, DropCause.Forced);
                Log.Game($"Dropped {droppedLeftItem.DisplayName} for the meeting scramble.");
            }

            if (droppedRightItem != null || droppedLeftItem != null)
            {
                player.TaskBook.CheckRegressionForAll();

                if (droppedRightItem != null)
                {
                    player.TaskBook.EvaluateActiveTasks(new TaskEvalContext
                    {
                        Player = player,
                        Reason = TaskEvalReason.InventoryChanged,
                        Target = droppedRightItem.gameObject,
                        Item = droppedRightItem
                    });
                }

                if (droppedLeftItem != null)
                {
                    player.TaskBook.EvaluateActiveTasks(new TaskEvalContext
                    {
                        Player = player,
                        Reason = TaskEvalReason.InventoryChanged,
                        Target = droppedLeftItem.gameObject,
                        Item = droppedLeftItem
                    });
                }

                player.TaskBook.RefreshLocalWaypoints();
            }
        }

        public void ExecuteThrow()
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
            ItemLifecycle.Drop(player, itemToThrow, DropCause.Thrown);
            ClearHeldItem();

            player.TaskBook.CheckRegressionForAll();
            player.TaskBook.EvaluateActiveTasks(new TaskEvalContext
            {
                Player = player,
                Reason = TaskEvalReason.InventoryChanged,
                Target = itemToThrow.gameObject,
                Item = itemToThrow
            });

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
                Vector3 throwDirection = player.PlayerCamera.forward + (Vector3.up * 0.1f);
                rb.AddForce(throwDirection.normalized * finalForce, ForceMode.Impulse);

                // Optional Polish: Add some random tumbling spin to the item while it flies
                rb.AddTorque(UnityEngine.Random.insideUnitSphere * (finalForce * 0.5f), ForceMode.Impulse);
            }

            Log.Game($"Threw {itemToThrow.DisplayName} with force {finalForce}. (Charge: {chargePercentage * 100}%, Weight: {itemToThrow.itemWeight})");

            // --- NEW: PROJECTILE IMPACT SETUP ---
            // Dynamically add the impact script to the item in the air
            ThrownProjectile projectile = itemToThrow.gameObject.AddComponent<ThrownProjectile>();

            // Pass the player, the starting coordinates, the charge %, and the max punch force
            projectile.Initialize(player, transform.position, chargePercentage, player.Vitals.pushbackForce);

            player.TaskBook.RefreshLocalWaypoints();
        }

        // --- Swap hands ---

        /// <summary>Swaps the active-hand and off-hand items. Called from PlayerController.OnSwapHands
        /// after its preconditions (not mid-minigame, not arrested, no two-handed haul item, not both
        /// empty) pass.</summary>
        public void SwapHands()
        {
            PickupItem temp = currentlyHeldItem;
            currentlyHeldItem = leftHeldItem;
            leftHeldItem = temp;

            // Re-seat whatever ended up in each hand on the matching socket.
            if (currentlyHeldItem != null) ItemLifecycle.GiveToHand(player, currentlyHeldItem, Hand.Right);
            if (leftHeldItem != null) ItemLifecycle.GiveToHand(player, leftHeldItem, Hand.Left);

            Log.Game($"Swapped hands. Active: {(currentlyHeldItem != null ? currentlyHeldItem.DisplayName : "empty")} | Off-hand: {(leftHeldItem != null ? leftHeldItem.DisplayName : "empty")}");
        }

        // --- Equip / haul ---

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
                Log.Game($"Hauling {newItem.DisplayName}");
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
                ItemLifecycle.GiveToHand(player, leftHeldItem, Hand.Left);
                Log.Game($"Equipped {leftHeldItem.DisplayName} in the off-hand");
                return;
            }

            // Otherwise the new item goes into the active (right) hand.
            // Drop whatever is already in the active hand first.
            if (currentlyHeldItem != null)
            {
                currentlyHeldItem.DetachFromHand();
            }

            currentlyHeldItem = newItem;
            ItemLifecycle.GiveToHand(player, currentlyHeldItem, Hand.Right);

            Log.Game($"Equipped {currentlyHeldItem.DisplayName}");
        }

        // Attaches a haul item; PoseHaulItem() then keeps it in front of the torso each frame and
        // ApplyHaulIK takes the hands.
        private void AttachHaulItem(PickupItem item)
        {
            item.PrepareForHaul();
            item.transform.SetParent(transform, true); // parent so it survives a menu pause; pose drives the rest
            player.haulActive = true;
            PoseHaulItem();

            Log.Game($"[Haul] Carrying {item.DisplayName}. leftGrip={(item.leftGripPoint != null)} " +
                      $"rightGrip={(item.rightGripPoint != null)} ikBlendSpeed={player.ikBlendSpeed} " +
                      $"animatorHuman={(player.PlayerAnimator != null && player.PlayerAnimator.isHuman)}");
        }

        // Places the hauled item in front of the body at a height measured down from the eyes, facing
        // the player's forward. Camera-anchored so it lands at hip/chest height on any rig. Called every
        // frame from PlayerController.UpdateHaulIKTarget while hauling.
        public void PoseHaulItem()
        {
            if (currentlyHeldItem == null || !currentlyHeldItem.haulWithBothHands) return;
            PickupItem hi = currentlyHeldItem;

            Vector3 flatFwd = transform.forward;
            flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            Transform cam = player != null ? player.PlayerCamera : null;
            float eyeY = cam != null ? cam.position.y : transform.position.y + 1.6f;
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

        // True if an item of this identity - carrying every flag in requiredState - is held in EITHER hand.
        public bool IsHoldingItem(ItemDefinition definition, ItemState requiredState = ItemState.None)
        {
            if (definition == null) return false;
            if (currentlyHeldItem != null && currentlyHeldItem.Matches(definition, requiredState)) return true;
            if (leftHeldItem != null && leftHeldItem.Matches(definition, requiredState)) return true;
            return false;
        }

        // True if either hand is holding a heavy item.
        public bool IsHoldingHeavyItem()
        {
            return (currentlyHeldItem != null && currentlyHeldItem.isHeavy)
                || (leftHeldItem != null && leftHeldItem.isHeavy);
        }

        // --- Minigame item-swap restore ---

        // Snaps an item that was temporarily raised into the left hand for an Item-target minigame
        // (see PlayerController.StartMinigame) back to the right hand once the minigame ends.
        public void ReturnSwappedItem()
        {
            if (itemSwappedToLeftHand && currentlyHeldItem != null)
            {
                ItemLifecycle.GiveToHand(player, currentlyHeldItem, Hand.Right);
                itemSwappedToLeftHand = false;
            }
        }
    }
}
