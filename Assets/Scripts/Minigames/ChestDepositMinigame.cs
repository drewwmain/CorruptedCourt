using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Chest deposit minigame (dowry_deposit): open the lid, then drop the held item inside.
    ///
    /// Flow:
    ///  1. The held item shifts to the LEFT hand; the right hand is freed.
    ///  2. The right hand reaches to the chest LID (mouse aims). LEFT-CLICK near it to grab.
    ///  3. Move the mouse UP to swing the lid open. Once it's open past the threshold the item shifts
    ///     back to the RIGHT hand and the shared ItemDepositMinigame aim / release / contact loop runs.
    ///  4. It deposits once its designated base contact point physically reaches the interior-floor
    ///     DepositTarget volume of a free slot; the lid then swings shut and the step completes. A miss
    ///     leaves it loose to pick up and retry (the lid stays open).
    ///
    /// Since P5 the release / settle / retry / abandon / cancel-to-hand / dropHandle lifecycle and the
    /// contact-driven deposit gate live in ItemDepositMinigame. This class keeps only the lid open/close
    /// phase machine wrapped around that loop, the frozen dead-straight drop, the "make the chest
    /// passable" reach helper, and the slim-player-radius tweak. See ARCHITECTURE.md.
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
        [Tooltip("DISABLED - the released item now always drops straight down from where you let go, with " +
                 "no sideways steering toward the chest. Kept only so it can still make the chest's non-mesh " +
                 "colliders passable to the falling item when > 0; leave at 0.")]
        public float dropFunnelSpeed = 0f;

        private enum Phase { ReachLid, OpenLid, AimItem, ClosingLid }

        private Phase phase;
        private float lidOpen01;
        private Quaternion lidClosedLocalRot;
        private float lidCloseFrom;
        private float lidCloseTimer;

        private Collider[] passableChestColliders;
        private float savedPlayerRadius = -1f;

        // --- HandMinigame gates -------------------------------------------------------------------
        // The item stays under the minigame's own reach/look/footwork all the way through seating and
        // the lid auto-close (unlike the sword, which hands control back the instant it's thrown) - so
        // these stay active until resolving, with a couple of AimItem-and-released exceptions below.
        protected override bool LookActive => !resolving;
        protected override bool FootworkActive => !resolving && !(phase == Phase.AimItem && released);
        // Vertical mouse movement is busy swinging the lid open during OpenLid - don't also pitch the camera.
        protected override bool SuppressPitchLook => phase == Phase.OpenLid;
        protected override bool AllowTapCancel() => !released;
        protected override bool WantsFreeCursor => !(phase == Phase.AimItem && released);

        public override void BeginDeposit(PickupItem heldItem, TaskDepositStation station)
        {
            if (!PrepareDeposit(heldItem, station)) return;

            // Prefab assets can't hold references to scene objects, so resolve the lid / grab point by
            // name at runtime. A child named "...Hinge" wins (that's the pivot to rotate); otherwise the
            // first child with "lid" in its name.
            if (lid == null) lid = FindChild(Station.transform, "hinge") ?? FindChild(Station.transform, "lid");
            if (lid == null) { Log.Warn("[ChestDeposit] No lid/hinge assigned or found under the station - cannot run."); CancelMinigame(); return; }
            if (lidGrabPoint == null) lidGrabPoint = FindChild(Station.transform, "grab");
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
            if (passable) passableChestColliders = Station != null ? Station.GetComponentsInChildren<Collider>() : null;
            if (passableChestColliders == null) return;

            Collider playerCC = player != null ? player.CharController : null;
            Collider itemCol = Item != null ? Item.GetComponent<Collider>() : null;

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
                // can't snag on the rim or an inner wall - the deposit still registers off the interior
                // DepositTarget volume. With the funnel off (dropFunnelSpeed 0) leave the chest solid.
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

        private void MoveItemToHand(Transform parent, bool aimPose)
        {
            if (Item == null || parent == null) return;
            Item.transform.SetParent(parent, false);
            Item.transform.localPosition = aimPose ? carryLocalPos : Vector3.zero;
            Item.transform.localRotation = Quaternion.Euler(aimPose ? carryLocalEuler : Vector3.zero);

            Rigidbody rb = Item.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Collider c = Item.GetComponent<Collider>();
            if (c != null) c.enabled = false;
        }

        // Wrap the shared aim/release/contact loop in the lid phase machine: ReachLid / OpenLid gate
        // entry, AimItem hands off to ItemDepositMinigame.OnMinigameUpdate, ClosingLid runs the shut swing.
        protected override void OnMinigameUpdate()
        {
            if (Item == null || Station == null || lid == null) { FinishFail(); return; }

            switch (phase)
            {
                case Phase.ReachLid:   UpdateReachLid();       break;
                case Phase.OpenLid:    UpdateOpenLid();        break;
                case Phase.AimItem:    base.OnMinigameUpdate(); break;
                case Phase.ClosingLid: UpdateClosingLid();     break;
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
                Item = null;
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
                phase = Phase.AimItem;
                EnterAiming(); // resets released/resolving/awaitingRetry/touchedStation, poses in the right hand, Hand.Begin()
            }
        }

        // --- ItemDepositMinigame hooks ---

        protected override void PoseCarriedItem() => MoveItemToHand(Hand.HandBone, true);

        // Dead-straight drop: rotation AND X/Z position frozen, no bounce, gentle depenetration. The item
        // falls from exactly where the player let go - there is NO steering toward the chest.
        protected override GuidedDrop.Settings DropSettings() => new GuidedDrop.Settings
        {
            maxAngularVelocity = 2.5f,
            maxDepenetrationVelocity = 0.5f,
            freezeRotation = true,
            freezeHorizontalPosition = true,
            funnelSpeed = 0f,
            materialName = "ChestDrop"
        };

        protected override void OnReleased()
        {
            // Only when the vestigial funnel flag is on: also let the falling item pass through the
            // chest's solid non-trigger colliders (DropInPlace re-enabled the item collider, so assert
            // the ignore pairs here). With dropFunnelSpeed 0 the chest stays solid to the item.
            if (dropFunnelSpeed <= 0f) return;
            Collider c = Item != null ? Item.GetComponent<Collider>() : null;
            if (c == null) return;
            foreach (Collider cc in Station.GetComponentsInChildren<Collider>())
                if (cc != null && !cc.isTrigger) Physics.IgnoreCollision(c, cc, true);
        }

        // Seated: swing the lid shut, THEN finish (instead of the base's immediate CompleteMinigame).
        // Item stays referenced so Update keeps ticking; it is nulled at the end of UpdateClosingLid.
        protected override void OnDeposited(int slot)
        {
            lidCloseFrom = lidOpen01;
            lidCloseTimer = 0f;
            phase = Phase.ClosingLid;
        }

        protected override void OnCleanup()
        {
            SetChestPassable(false); // re-enable player <-> chest collision (idempotent once cleared)
            if (savedPlayerRadius > 0f && player != null && player.CharController != null)
                player.CharController.radius = savedPlayerRadius;
        }

        public override void CancelMinigame()
        {
            if (lid != null) lid.localRotation = lidClosedLocalRot;
            base.CancelMinigame(); // dropHandle end, OnCleanup, hand the item back if not released, then MinigameBase
        }
    }
}
