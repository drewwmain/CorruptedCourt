using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Physical "hang the sword on the rack" deposit minigame.
    ///
    /// The item is parented to the RIGHT HAND BONE (so it moves with the arm, including the reach IK)
    /// and posed tip-down. The player is frozen and moves the mouse to reach the hand toward the notches.
    ///
    /// LEFT CLICK releases the sword (physics on). After it settles, if it came to rest ON / between the
    /// notches it snaps cleanly into that slot and the DepositItemStep completes. If it missed, the sword
    /// is a loose pickup on the ground and the minigame stays open - walking over and picking the sword
    /// back up (E) drops the player straight back into the aiming phase to try again.
    /// RIGHT CLICK (before releasing) cancels and returns the sword to a normal grip.
    ///
    /// Launched by TaskDepositStation when its "Deposit Minigame Prefab" is set. Prefab = an empty
    /// GameObject with this component. Extends MinigameBase so success advances the DepositItemStep.
    ///
    /// The falling-item physics (no-bounce, straight-drop) and the "has it touched the rack yet" check
    /// are the GuidedDrop / StationContactProbe capabilities in Assets/Scripts/Minigames/Capabilities -
    /// shared with ChestDepositMinigame (P1). The freeze / RMB-look / footwork / settings-pause / hand rig
    /// plumbing is HandMinigame, via ItemDepositMinigame (P2) - this class keeps only the notch-slot pick
    /// and the tip-down pose. See ARCHITECTURE.md.
    /// </summary>
    public class SwordHangMinigame : ItemDepositMinigame
    {
        public override void BeginDeposit(PickupItem heldItem, TaskDepositStation station) => BeginHang(heldItem, station);

        [Header("Sword grip while aiming")]
        [Tooltip("Local rotation of the sword on the hand bone at the start - set so the tip points at the ground.")]
        public Vector3 carryLocalEuler = new Vector3(180f, 0f, 0f);
        [Tooltip("Local position of the sword on the hand bone while aiming.")]
        public Vector3 carryLocalPos = new Vector3(0f, 0.05f, 0.05f);

        [Header("Landing check")]
        [Tooltip("Seconds to wait for a released sword to settle before judging the outcome.")]
        public float settleTime = 1.2f;
        [Tooltip("Friction of the released sword while it falls and settles. Low = it slides down the rack " +
                 "toward the notches instead of sticking at the angle it was let go; too low and it skates " +
                 "straight off onto the floor. Combined with the surface by Minimum so this value bites.")]
        public float releaseSlideFriction = 0.3f;
        [Tooltip("Once a released sword has landed on the rack, a steady gentle torque (rad/s^2) rotates it " +
                 "so the BLADE tips toward pointing straight down - a sword resting flat on the rack should " +
                 "slowly topple blade-first and slide off toward a notch or the ground, not just sit there. " +
                 "Scaled by how far from blade-down it still is, and capped by the rigidbody's max angular " +
                 "velocity so it stays slow. 0 = off (pure physics).")]
        public float toppleAssistSpin = 3f;
        [Tooltip("Maximum seconds the blade-down topple torque is applied after the sword lands on the rack. " +
                 "It also stops early once the blade is pointing down.")]
        public float toppleAssistTime = 2f;
        [Tooltip("Once the sword has PHYSICALLY touched the rack, it hangs on the nearest free DropSlot within this distance. Place each DropSlot where the sword's origin should rest when hung.")]
        public float catchRadius = 0.45f;
        [Tooltip("A downward ray this long from the sword's pivot must hit rack geometry for the sword to " +
                 "count as 'touched the rack' (which starts the topple and lets it hang). Needs to span " +
                 "the gap from the sword's pivot down to whatever rack surface it first rests on.")]
        public float rackTouchDistance = 0.5f;
        [Tooltip("Let the released sword pass THROUGH the rack's non-mesh (Box) colliders so it can settle " +
                 "onto the actual notch geometry and topple blade-first into a gap, instead of resting on a " +
                 "solid slab above the notches. The rack's Mesh Collider stays solid.")]
        public bool ignoreRackBoxColliders = true;
        [Tooltip("Log the landing numbers to the Console so you can tune catchRadius / DropSlot placement.")]
        public bool debugLanding = true;
        [Tooltip("After a miss the minigame waits for the player to pick the sword back up. If they walk this far from the dropped sword instead, the minigame gives up (they can still retry via E on the rack).")]
        public float abandonDistance = 8f;

        private PickupItem item;
        private TaskDepositStation rack;
        private bool released;
        private bool resolving;
        private bool awaitingRetry;
        private bool touchedRack;
        private float settleTimer;
        private float toppleTimer;
        private GuidedDrop.Handle dropHandle;

        // --- HandMinigame gates: the sword hands full normal control back to the player the instant it's
        // released (see ReleaseSword), so its own RMB-look / footwork / cursor-refree must stop then too -
        // otherwise they'd run alongside the player's now-active normal controls.
        protected override bool LookActive => !released;
        protected override bool FootworkActive => !released;
        protected override bool WantsFreeCursor => !released;

        // Turn on the base's held-item anti-penetration solve for the sword. The sword's PickupItem must
        // also have Grip Constraint enabled, and the minigame prefab's gripContactMask must be set.
        protected override void OnHandBegin() => useGripConstraint = true;

        public void BeginHang(PickupItem heldItem, TaskDepositStation targetRack)
        {
            item = heldItem;
            rack = targetRack;

            Transform handBone = Hand != null ? Hand.HandBone : null;
            if (item == null || rack == null || cam == null || handBone == null) { CancelMinigame(); return; }

            // Re-enter the busy registry: on the very first call this is a harmless re-add (SetupMinigame
            // already added it); on a retry restart (after ReleaseSword's RestorePlayer left it - see
            // MinigameBase.LeaveActiveRegistry) this is what makes the player "busy" again while re-aiming.
            RejoinActiveRegistry();

            player.SetControlsLocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // The player walked up to the rack themselves - leave them exactly where they are. On a retry
            // restart (picked the sword back up somewhere else) this re-centres the WASD leash there too.
            ReanchorFootwork();

            // Glue the sword to the hand BONE, tip-down.
            item.transform.SetParent(handBone, false);
            item.transform.localPosition = carryLocalPos;
            item.transform.localRotation = Quaternion.Euler(carryLocalEuler);
            SetItemPhysics(false);

            Hand.Begin();
        }

        protected override void OnMinigameUpdate()
        {
            if (item == null || rack == null) { FinishFail(); return; }

            if (!released)
            {
                UpdateAiming();
                if (MinigameInput.PrimaryDown) ReleaseSword();
                return;
            }

            if (resolving) return;

            // Player picked the sword back up while the minigame is still open - straight back to aiming.
            if (player.GetHeldItem() == item)
            {
                RestartAiming();
                return;
            }

            if (awaitingRetry)
            {
                // Missed. The sword is loose; wait for the pickup above. If the player abandons it and
                // walks off, close the minigame (they can still retry with E on the rack).
                if (Vector3.Distance(player.transform.position, item.transform.position) > abandonDistance)
                    FinishFail();
                return;
            }

            // The moment the straight-dropping sword physically touches the rack, hand it to the physical
            // settle (free rotation + slide + topple assist). The generous probe distance covers the
            // sword's origin sitting well above whatever rack surface it first rests on.
            if (!touchedRack && StationContactProbe.Resting(item.transform, rack.transform, rackTouchDistance))
            {
                touchedRack = true;
                GoPhysical();
            }

            // It can only hang once it has actually touched the rack AND is lined up with a free slot.
            // That may be true the instant it lands, or after it has settled against the notches.
            if (touchedRack && TryHangOnSlot("contact")) return;

            settleTimer -= Time.deltaTime;
            Rigidbody rb = item.GetComponent<Rigidbody>();
            bool stillMoving = rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.04f;
            // Don't call it a miss while the blade-down topple assist is still running - give it the
            // whole toppleAssistTime to tip the sword toward a notch first.
            if (settleTimer <= 0f && !stillMoving && toppleTimer <= 0f) ResolveLanding();
        }

        // Nearest FREE DropSlot on the rack to the sword's pivot right now. Returns -1 (distance +inf)
        // when the rack has no free slot, or the sword / rack reference has gone away.
        private int NearestFreeSlot(out float distance)
        {
            distance = float.MaxValue;
            if (item == null || rack == null) return -1;

            int slot = -1;
            for (int i = 0; i < rack.SlotCount; i++)
            {
                if (!rack.IsSlotFree(i)) continue;
                Transform st = rack.GetDropSlot(i);
                if (st == null) continue;
                float d = Vector3.Distance(item.transform.position, st.position);
                if (d < distance) { distance = d; slot = i; }
            }
            return slot;
        }

        // If the sword's pivot is within catchRadius of a free DropSlot, hang it there. Returns true.
        private bool TryHangOnSlot(string via)
        {
            int slot = NearestFreeSlot(out float best);
            if (slot < 0 || best > catchRadius) return false;

            resolving = true;
            dropHandle?.End();
            dropHandle = null;
            rack.DepositIntoSlot(item, slot, player); // parents + poses it in the notch
            if (debugLanding)
                Log.Game($"[SwordHang] hung on slot {slot} via {via} (dist {best:F2} <= {catchRadius}).");
            item = null;
            CompleteMinigame();               // advances the DepositItemStep
            return true;
        }

        // The player grabbed the sword again mid-minigame - restart the aiming phase with the same
        // rack and task rather than ending.
        private void RestartAiming()
        {
            dropHandle?.End();
            dropHandle = null;
            released = false;
            resolving = false;
            awaitingRetry = false;
            touchedRack = false;
            toppleTimer = 0f;
            BeginHang(item, rack);
        }

        private void UpdateAiming()
        {
            // The hand reaches to exactly where the mouse points - no assist. The player has to
            // physically line the sword up over a notch gap and be close enough to reach it.
            Hand.ReachToward(MouseWorld());
            // weight 0 = keep the held pose (no rotation target to align to) - routed through
            // MinigameHandRig like Hand.ReachToward above, instead of poking player.hangReachRotWeight
            // directly as a second path.
            Hand.SetHandRotation(Quaternion.identity, 0f);
        }

        // Runs after the animator/IK have posed the hand: force the sword's orientation so the tip
        // always points straight down, only yawing with the player so it looks natural as they turn.
        protected override void OnMinigameLateUpdate()
        {
            if (!released && item != null && player != null)
                item.transform.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f)
                                          * Quaternion.Euler(carryLocalEuler);
        }

        // AFTER the sword has touched the rack (GoPhysical arms toppleTimer - this never runs while the
        // sword is still dropping through the air), apply a steady, gentle torque that rotates the BLADE
        // toward pointing straight down. A sword resting flat on the rack should slowly topple blade-first
        // and slide off toward a notch or to the ground rather than sit there. It is a torque, not a set
        // angular velocity, so it builds up slowly (reads as "slowly toppling"); it is capped by the
        // rigidbody's maxAngularVelocity, eased to nothing as the blade nears vertical, and it stops when
        // the sword seats, when toppleAssistTime elapses, or when it has reached blade-down.
        protected override void OnMinigameFixedUpdate()
        {
            if (!released || !touchedRack || resolving || awaitingRetry
                || toppleTimer <= 0f || toppleAssistSpin <= 0f) return;
            if (item == null) return;

            toppleTimer -= Time.fixedDeltaTime;

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;

            // Hilt->tip axis in world space, from carryLocalEuler (the pose the sword was held in) so
            // there is no second hard-coded "long axis" to keep in sync.
            Vector3 bladeWorld = item.transform.rotation
                                 * (Quaternion.Inverse(Quaternion.Euler(carryLocalEuler)) * Vector3.down);

            // 0 = blade already pointing straight down, 1 = blade horizontal, 2 = blade straight up.
            float misalign = 1f - Vector3.Dot(bladeWorld, Vector3.down);
            if (misalign < 0.03f) return;

            // Torque about the axis that rotates bladeWorld toward Vector3.down (AddTorque about
            // Cross(a,b) turns a toward b). Cross is ~zero only when the blade is already vertical -
            // fall back to any horizontal axis so a rare tip-straight-up sword still rights itself.
            Vector3 torqueAxis = Vector3.Cross(bladeWorld, Vector3.down);
            if (torqueAxis.sqrMagnitude < 1e-6f) torqueAxis = item.transform.right;
            torqueAxis.Normalize();

            rb.AddTorque(torqueAxis * (toppleAssistSpin * misalign), ForceMode.Acceleration);
        }

        private void ReleaseSword()
        {
            released = true;
            awaitingRetry = false;
            touchedRack = false;
            toppleTimer = 0f;
            settleTimer = settleTime;

            player.ClearHeldItem();

            // Let go of the sword right where the hand is - it falls under physics and, crucially,
            // becomes pick-up-able again (DropInPlace resets isHeld).
            item.DropInPlace();

            // PHASE 1 - a dead-straight drop: X/Z position and rotation frozen, normal friction, no bounce.
            // The sword falls STRAIGHT DOWN from wherever it was let go, with no forward throw and no
            // tumble, until it physically touches the rack. GoPhysical() then swaps in the free-rotation,
            // low-friction, topple-assisted settle (PHASE 2) so it slides toward a notch instead of just
            // freezing wherever it landed. (GuidedDrop capability.)
            Rigidbody rb = item.GetComponent<Rigidbody>();
            Collider col = item.GetComponent<Collider>();
            dropHandle = GuidedDrop.Begin(rb, col, new GuidedDrop.Settings
            {
                maxAngularVelocity = 2.5f,
                maxDepenetrationVelocity = 0.5f,
                freezeRotation = true,
                freezeHorizontalPosition = true,
                funnelSpeed = 0f,
                materialName = "SwordDrop"
            });

            // Don't let it bounce off the player standing right there.
            if (col != null && player.CharController != null)
                Physics.IgnoreCollision(col, player.CharController, true);

            // Let the sword fall past the rack's solid Box collider(s) so it can reach the notch geometry
            // (the Mesh collider stays solid). DropInPlace just re-enabled the sword collider, so assert
            // the ignore pairs here, after that.
            PassRackBoxColliders(col);

            // A sword let go while already leaning against the rack is touching it right now - go
            // straight to the physical settle rather than waiting a frame in the frozen drop.
            if (StationContactProbe.Resting(item.transform, rack.transform, rackTouchDistance))
            {
                touchedRack = true;
                GoPhysical();
            }

            // The player can move again while the sword falls - hand normal control straight back
            // (RestorePlayer is safe to call again when the minigame later actually ends).
            RestorePlayer();
        }

        // Make the released sword pass through the rack's solid non-mesh (Box) colliders - a slab that
        // typically sits over the notch level so the player can't clip the rack. The concave Mesh
        // collider (the real rack shape, with the peg gaps) stays solid, so the sword can descend past
        // the slab and settle onto / between the pegs where the DropSlots are.
        private void PassRackBoxColliders(Collider swordCol)
        {
            if (!ignoreRackBoxColliders || swordCol == null || rack == null) return;
            foreach (Collider c in rack.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || c is MeshCollider) continue;
                Physics.IgnoreCollision(swordCol, c, true);
            }
        }

        // PHASE 2: the sword has touched the rack. Swap the frozen straight-drop for a free physics settle
        // - rotation and X/Z unlocked, low friction (releaseSlideFriction, combined by Minimum so it
        // bites), and the brief topple assist below - so it tips toward its low end and slides toward a
        // notch instead of resting wherever it first made contact.
        private void GoPhysical()
        {
            if (item == null) return;

            dropHandle?.End();
            dropHandle = null;

            Rigidbody rb = item.GetComponent<Rigidbody>();
            Collider col = item.GetComponent<Collider>();
            dropHandle = GuidedDrop.Begin(rb, col, new GuidedDrop.Settings
            {
                maxAngularVelocity = 2.5f,
                maxDepenetrationVelocity = 0.5f,
                freezeRotation = false,
                freezeHorizontalPosition = false,
                dynamicFriction = releaseSlideFriction,
                staticFriction = releaseSlideFriction,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                funnelSpeed = 0f,
                materialName = "SwordDrop"
            });

            if (col != null && player != null && player.CharController != null)
                Physics.IgnoreCollision(col, player.CharController, true);

            toppleTimer = toppleAssistTime;
        }

        private void ResolveLanding()
        {
            // Settled. It only counts if it PHYSICALLY touched the rack and rests near a free slot.
            if (touchedRack && TryHangOnSlot("settle")) return;

            if (debugLanding)
            {
                NearestFreeSlot(out float d);
                Log.Game($"[SwordHang] MISS - touchedRack={touchedRack}, nearest free slot {d:F2}m " +
                         $"vs catchRadius {catchRadius}. Leaving the sword loose to retry.");
            }
            awaitingRetry = true;
        }

        // --- helpers ---

        private void SetItemPhysics(bool loose)
        {
            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = !loose;
                rb.useGravity = loose;
                if (loose) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            Collider col = item.GetComponent<Collider>();
            if (col != null) col.enabled = loose;
        }

        private void AbortToHand()
        {
            dropHandle?.End();
            dropHandle = null;
            if (item != null && player != null) item.AttachToHand(player.RightHandSocket);
            base.CancelMinigame(); // restores the player via HandMinigame.OnMinigameEnd
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
            AbortToHand();
        }

        void OnDestroy()
        {
            dropHandle?.End();
            dropHandle = null;
        }
    }
}
