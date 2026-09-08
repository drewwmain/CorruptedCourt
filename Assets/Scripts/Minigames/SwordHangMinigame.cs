using UnityEngine;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Physical "hang the sword on the rack" deposit minigame.
    ///
    /// The item is parented to the RIGHT HAND BONE (so it moves with the arm, including the reach IK)
    /// and posed tip-down. The player is frozen and moves the mouse to reach the hand toward the notches.
    ///
    /// LEFT CLICK releases the sword (physics on). It falls dead-straight, then topples blade-first once
    /// it touches the rack; the moment its designated hilt contact point physically enters a free notch's
    /// DepositTarget volume it snaps cleanly into that slot and the DepositItemStep completes. If it
    /// settles without ever reaching a notch, the sword is a loose pickup on the ground and the minigame
    /// stays open - walking over and picking the sword back up (E) drops the player straight back into the
    /// aiming phase to try again. RIGHT CLICK (before releasing) cancels and returns the sword to a grip.
    ///
    /// Since P5 the release / settle / retry / abandon / cancel-to-hand / dropHandle lifecycle and the
    /// contact-driven deposit gate live in ItemDepositMinigame. This class keeps only the sword flavour:
    /// the tip-down carry pose, the phase-1 frozen drop, the tip-down yaw lock, the blade-down topple
    /// assist, and the two-phase swap (frozen straight drop -> free low-friction settle) that fires when
    /// the sword first touches the rack. See ARCHITECTURE.md.
    /// </summary>
    public class SwordHangMinigame : ItemDepositMinigame
    {
        [Header("Sword grip while aiming")]
        [Tooltip("Local rotation of the sword on the hand bone at the start - set so the tip points at the ground.")]
        public Vector3 carryLocalEuler = new Vector3(180f, 0f, 0f);
        [Tooltip("Local position of the sword on the hand bone while aiming.")]
        public Vector3 carryLocalPos = new Vector3(0f, 0.05f, 0.05f);

        [Header("Rack settle")]
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
        [Tooltip("A downward ray this long from the sword's pivot must hit rack geometry for the sword to " +
                 "count as 'touched the rack' (which starts the topple and the two-phase settle swap). Needs " +
                 "to span the gap from the sword's pivot down to whatever rack surface it first rests on.")]
        public float rackTouchDistance = 0.5f;
        [Tooltip("Let the released sword pass THROUGH the rack's non-mesh (Box) colliders so it can settle " +
                 "onto the actual notch geometry and topple blade-first into a gap, instead of resting on a " +
                 "solid slab above the notches. The rack's Mesh Collider stays solid.")]
        public bool ignoreRackBoxColliders = true;

        // Seconds of blade-down topple assist still to run after the sword touched the rack. Armed by
        // GoPhysical(), counted down in OnMinigameFixedUpdate, and gated so a still-toppling sword is not
        // judged a miss (see ReadyToJudgeMiss).
        private float toppleTimer;

        // --- HandMinigame gates: the sword hands full normal control back to the player the instant it's
        // released (see ItemDepositMinigame.Release), so its own RMB-look / footwork / cursor-refree must
        // stop then too - otherwise they'd run alongside the player's now-active normal controls.
        protected override bool LookActive => !released;
        protected override bool FootworkActive => !released;
        protected override bool WantsFreeCursor => !released;

        // Turn on the base's held-item anti-penetration solve for the sword. The sword's PickupItem must
        // also have Grip Constraint enabled, and the minigame prefab's gripContactMask must be set.
        protected override void OnHandBegin() => useGripConstraint = true;

        // Widen the "touched the rack" probe: the sword's pivot sits well above whatever rack surface it
        // first rests on.
        protected override float StationTouchDistance => rackTouchDistance;

        // Don't call a settled sword a miss while the blade-down topple assist is still running - give it
        // the whole toppleAssistTime to tip toward a notch first.
        protected override bool ReadyToJudgeMiss => toppleTimer <= 0f;

        // The sword is handed back to the hand on cancel whenever it still exists, released or not
        // (a released-but-interrupted sword returns to the grip rather than being left mid-fall).
        protected override bool AbortReattachesItem => true;

        // Glue the sword to the hand BONE, tip-down, kinematic, collider off. Called from BeginDeposit
        // and from RestartAiming.
        protected override void PoseCarriedItem()
        {
            Transform hb = Hand.HandBone;
            if (Item == null || hb == null) return;

            Item.transform.SetParent(hb, false);
            Item.transform.localPosition = carryLocalPos;
            Item.transform.localRotation = Quaternion.Euler(carryLocalEuler);

            Rigidbody rb = Item.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
            Collider col = Item.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        // PHASE 1 - a dead-straight drop: X/Z position and rotation frozen, normal friction, no bounce.
        // The sword falls STRAIGHT DOWN from wherever it was let go, with no forward throw and no tumble,
        // until it physically touches the rack. OnItemTouchedStation() then swaps in the free-rotation,
        // low-friction, topple-assisted settle (PHASE 2, GoPhysical) so it slides toward a notch.
        protected override GuidedDrop.Settings DropSettings() => new GuidedDrop.Settings
        {
            maxAngularVelocity = 2.5f,
            maxDepenetrationVelocity = 0.5f,
            freezeRotation = true,
            freezeHorizontalPosition = true,
            funnelSpeed = 0f,
            materialName = "SwordDrop"
        };

        // Per aim frame: keep the held pose (weight 0 = no rotation target to align to) - routed through
        // MinigameHandRig instead of poking player.hangReachRotWeight directly as a second path.
        protected override void OnAiming() => Hand.SetHandRotation(Quaternion.identity, 0f);

        protected override void OnReleased()
        {
            toppleTimer = 0f;

            Collider col = Item != null ? Item.GetComponent<Collider>() : null;
            // Let the sword fall past the rack's solid Box collider(s) so it can reach the notch geometry
            // (the Mesh collider stays solid). DropInPlace just re-enabled the sword collider, so assert
            // the ignore pairs here, after that.
            PassRackBoxColliders(col);

            // A sword let go while already leaning against the rack is touching it right now - go straight
            // to the physical settle rather than waiting a frame in the frozen drop.
            if (Item != null
                && StationContactProbe.Resting(Item.transform, Station.transform, StationTouchDistance))
            {
                touchedStation = true;
                OnItemTouchedStation();
            }
        }

        // The straight-dropping sword has physically touched the rack: hand it to the physical settle.
        protected override void OnItemTouchedStation() => GoPhysical();

        // Runs after the animator/IK have posed the hand: force the sword's orientation so the tip always
        // points straight down, only yawing with the player so it looks natural as they turn.
        protected override void OnMinigameLateUpdate()
        {
            if (!released && Item != null && player != null)
                Item.transform.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f)
                                          * Quaternion.Euler(carryLocalEuler);
        }

        // AFTER the sword has touched the rack (GoPhysical arms toppleTimer - this never runs while the
        // sword is still dropping through the air), apply a steady, gentle torque that rotates the BLADE
        // toward pointing straight down. A sword resting flat on the rack should slowly topple blade-first
        // and slide off toward a notch or to the ground rather than sit there. It is a torque, not a set
        // angular velocity, so it builds up slowly; it is capped by the rigidbody's maxAngularVelocity,
        // eased to nothing as the blade nears vertical, and it stops when the sword seats, when
        // toppleAssistTime elapses, or when it has reached blade-down.
        protected override void OnMinigameFixedUpdate()
        {
            if (!released || !touchedStation || resolving || awaitingRetry
                || toppleTimer <= 0f || toppleAssistSpin <= 0f) return;
            if (Item == null) return;

            toppleTimer -= Time.fixedDeltaTime;

            Rigidbody rb = Item.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;

            // Hilt->tip axis in world space, from carryLocalEuler (the pose the sword was held in) so
            // there is no second hard-coded "long axis" to keep in sync.
            Vector3 bladeWorld = Item.transform.rotation
                                 * (Quaternion.Inverse(Quaternion.Euler(carryLocalEuler)) * Vector3.down);

            // 0 = blade already pointing straight down, 1 = blade horizontal, 2 = blade straight up.
            float misalign = 1f - Vector3.Dot(bladeWorld, Vector3.down);
            if (misalign < 0.03f) return;

            // Torque about the axis that rotates bladeWorld toward Vector3.down (AddTorque about
            // Cross(a,b) turns a toward b). Cross is ~zero only when the blade is already vertical -
            // fall back to any horizontal axis so a rare tip-straight-up sword still rights itself.
            Vector3 torqueAxis = Vector3.Cross(bladeWorld, Vector3.down);
            if (torqueAxis.sqrMagnitude < 1e-6f) torqueAxis = Item.transform.right;
            torqueAxis.Normalize();

            rb.AddTorque(torqueAxis * (toppleAssistSpin * misalign), ForceMode.Acceleration);
        }

        // Restart the aim phase after the player grabs the sword back: the base re-poses and re-locks,
        // this re-centres the WASD leash where the player picked it up and disarms the topple assist.
        protected override void RestartAiming()
        {
            base.RestartAiming();
            ReanchorFootwork();
            toppleTimer = 0f;
        }

        // PHASE 2: the sword has touched the rack. Swap the frozen straight-drop for a free physics settle
        // - rotation and X/Z unlocked, low friction (releaseSlideFriction, combined by Minimum so it
        // bites), and the brief topple assist above - so it tips toward its low end and slides toward a
        // notch instead of resting wherever it first made contact.
        private void GoPhysical()
        {
            if (Item == null) return;

            dropHandle?.End();
            dropHandle = null;

            Rigidbody rb = Item.GetComponent<Rigidbody>();
            Collider col = Item.GetComponent<Collider>();
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

        // Make the released sword pass through the rack's solid non-mesh (Box) colliders - a slab that
        // typically sits over the notch level so the player can't clip the rack. The concave Mesh
        // collider (the real rack shape, with the peg gaps) stays solid, so the sword can descend past
        // the slab and settle onto / between the pegs where the DropSlots are.
        private void PassRackBoxColliders(Collider swordCol)
        {
            if (!ignoreRackBoxColliders || swordCol == null || Station == null) return;
            foreach (Collider c in Station.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || c is MeshCollider) continue;
                Physics.IgnoreCollision(swordCol, c, true);
            }
        }
    }
}
