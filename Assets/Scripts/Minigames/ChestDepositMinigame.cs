using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Minigames
{
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
    ///
    /// The falling-item physics (no-bounce, funnelled drop) and the "has it touched the chest yet" check
    /// are the GuidedDrop / StationContactProbe capabilities in Assets/Scripts/Minigames/Capabilities -
    /// shared with SwordHangMinigame (P1). The freeze / RMB-look / footwork / settings-pause / hand rig
    /// plumbing is HandMinigame, via ItemDepositMinigame (P2) - this class keeps only the lid open/close
    /// phase machine and the funnel-to-slot tuning. See ARCHITECTURE.md.
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

        [Header("Reach / footwork")]
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

        private GuidedDrop.Handle dropHandle;
        private Collider[] passableChestColliders;
        private float savedPlayerRadius = -1f;

        // --- HandMinigame gates -------------------------------------------------------------------
        // The item stays under the minigame's own reach/look/footwork all the way through seating and
        // the lid auto-close (unlike the sword, which hands control back the instant it's thrown) - so
        // these stay active until resolving, with a couple of AimItem-and-released exceptions below.
        protected override bool LookActive => !resolving;
        protected override bool FootworkActive => !resolving && !(phase == Phase.AimItem && itemReleased);
        // Vertical mouse movement is busy swinging the lid open during OpenLid - don't also pitch the camera.
        protected override bool SuppressPitchLook => phase == Phase.OpenLid;
        protected override bool AllowTapCancel() => !itemReleased;
        protected override bool WantsFreeCursor => !(phase == Phase.AimItem && itemReleased);

        public override void BeginDeposit(PickupItem heldItem, TaskDepositStation station)
        {
            item = heldItem;
            chest = station;

            if (item == null || chest == null || cam == null || Hand.HandBone == null) { CancelMinigame(); return; }

            // Prefab assets can't hold references to scene objects, so resolve the lid / grab point by
            // name at runtime. A child named "...Hinge" wins (that's the pivot to rotate); otherwise the
            // first child with "lid" in its name.
            if (lid == null) lid = FindChild(chest.transform, "hinge") ?? FindChild(chest.transform, "lid");
            if (lid == null) { Log.Warn("[ChestDeposit] No lid/hinge assigned or found under the station - cannot run."); CancelMinigame(); return; }
            if (lidGrabPoint == null) lidGrabPoint = FindChild(chest.transform, "grab");
            lidClosedLocalRot = lid.localRotation;

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
                // slot instead, and the deposit still registers off StationContactProbe.Resting() / the slot
                // arrival. With the funnel off (dropFunnelSpeed 0) leave the chest solid to the item.
                if (itemCol != null && (!passable || dropFunnelSpeed > 0f))
                    Physics.IgnoreCollision(itemCol, c, passable);
            }

            if (passable && !anySolidBlocker)
                Log.Warn("[ChestDeposit] The chest has no solid (non-trigger) MeshCollider - the " +
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
            Hand.Begin();                                 // reach the empty right hand toward the mouse
        }

        private void EnterAimItemPhase()
        {
            phase = Phase.AimItem;
            itemReleased = false;
            awaitingRetry = false;
            MoveItemToHand(Hand.HandBone, true);          // back to the right hand to aim it
            Hand.Begin();
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

        protected override void OnMinigameUpdate()
        {
            if (item == null || chest == null || lid == null) { FinishFail(); return; }

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

        private Vector3 LidGrabWorld() => lidGrabPoint != null ? lidGrabPoint.position : lid.position;

        private void UpdateReachLid()
        {
            Hand.ReachToward(MouseWorld());

            if (MinigameInput.PrimaryDown && Vector3.Distance(Hand.HandBone.position, LidGrabWorld()) <= lidGrabDistance)
            {
                phase = Phase.OpenLid;
                if (debugMinigame) Log.Game("[ChestDeposit] grabbed the lid - move the mouse UP to open");
            }
        }

        private void UpdateOpenLid()
        {
            lidOpen01 = Mathf.Clamp01(lidOpen01 + MinigameInput.MouseDelta.y * lidOpenSensitivity);
            // Blend the hinge from its captured closed pose to the authored open pose.
            lid.localRotation = Quaternion.Slerp(lidClosedLocalRot, Quaternion.Euler(lidOpenLocalEuler), lidOpen01);

            // Keep the hand on the lid grab point as it swings.
            Hand.ReachToward(LidGrabWorld());

            if (lidOpen01 >= lidOpenThreshold)
            {
                if (debugMinigame) Log.Game("[ChestDeposit] lid open - item back to the right hand, aim it into the chest");
                EnterAimItemPhase();
            }
        }

        private void UpdateAimItem()
        {
            if (!itemReleased)
            {
                Hand.ReachToward(MouseWorld());
                if (MinigameInput.PrimaryDown) ReleaseItem();
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
            if (!touchedChest && StationContactProbe.Resting(item.transform, chest.transform)) touchedChest = true;

            // Register once it's lined up on a free slot: either after physically touching the chest, or -
            // when the funnel is steering it down a slot column - as soon as it drops to the slot's lip.
            bool linedUp = touchedChest || (dropTargetSlot != null && dropFunnelSpeed > 0f);
            if (linedUp && TrySeatInChest("contact")) return;

            settleTimer -= Time.deltaTime;
            Rigidbody rb = item.GetComponent<Rigidbody>();
            bool moving = rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.04f;
            if (settleTimer <= 0f && !moving) ResolveDrop();
        }

        private void ResolveDrop()
        {
            if (touchedChest && TrySeatInChest("settle")) return;

            if (debugMinigame)
                Log.Game($"[ChestDeposit] MISS - touchedChest={touchedChest}. Leaving the item loose to retry.");
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
            dropHandle?.End();
            dropHandle = null;
            chest.DepositIntoSlot(item, slot, player);
            if (debugMinigame) Log.Game($"[ChestDeposit] seated in slot {slot} via {via} (dist {best:F2}). WIN");

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

            player.ClearHeldItem();
            item.DropInPlace();

            // No tumble, no bounce, gentle depenetration; X/Z stays free so the funnel (below) can steer
            // it onto dropTargetSlot's column as it falls. (GuidedDrop capability.)
            Rigidbody rb = item.GetComponent<Rigidbody>();
            Collider c = item.GetComponent<Collider>();
            dropHandle = GuidedDrop.Begin(rb, c, new GuidedDrop.Settings
            {
                maxAngularVelocity = 2.5f,
                maxDepenetrationVelocity = 0.5f,
                freezeRotation = true,
                freezeHorizontalPosition = false,
                funnelSpeed = dropFunnelSpeed,
                materialName = "ChestDrop"
            });

            // The item's collider was just re-enabled by DropInPlace, which can drop the ignore pairs set
            // while it was disabled - re-assert pass-through with the chest and the player here.
            if (c != null)
            {
                if (player.CharController != null) Physics.IgnoreCollision(c, player.CharController, true);
                if (dropFunnelSpeed > 0f)
                    foreach (Collider cc in chest.GetComponentsInChildren<Collider>())
                        if (cc != null && !cc.isTrigger) Physics.IgnoreCollision(c, cc, true);
            }

            RestorePlayer();
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
        protected override void OnMinigameFixedUpdate()
        {
            if (phase != Phase.AimItem || !itemReleased || resolving || awaitingRetry) return;
            if (item == null || dropTargetSlot == null) return;

            dropHandle?.Funnel(dropTargetSlot.position);
        }

        private void RestartAim()
        {
            dropHandle?.End();
            dropHandle = null;
            itemReleased = false;
            resolving = false;
            awaitingRetry = false;
            touchedChest = false;
            dropTargetSlot = null;
            // Player is "busy" again while re-aiming - ReleaseItem's RestorePlayer left the registry the
            // moment the item was released (see MinigameBase.LeaveActiveRegistry).
            RejoinActiveRegistry();
            player.SetControlsLocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            MoveItemToHand(Hand.HandBone, true);
            Hand.Begin();
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
            dropHandle?.End();
            dropHandle = null;
            if (lid != null) lid.localRotation = lidClosedLocalRot;
            if (!itemReleased && item != null && player != null)
                item.AttachToHand(player.RightHandSocket); // give the item back as a normal held item
            base.CancelMinigame(); // restores the player via HandMinigame.OnMinigameEnd
        }

        void OnDestroy()
        {
            SetChestPassable(false); // re-enable player <-> chest collision
            if (savedPlayerRadius > 0f && player != null && player.CharController != null)
                player.CharController.radius = savedPlayerRadius;
            dropHandle?.End();
            dropHandle = null;
        }
    }
}
