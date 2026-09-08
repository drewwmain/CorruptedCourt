using UnityEngine;
using UnityEngine.Serialization;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Base for the "carry a held item to a deposit station and physically put it away" minigames
    /// (SwordHangMinigame, ChestDepositMinigame, VaseDepositMinigame, ...). TaskDepositStation.
    /// LaunchDepositMinigame spawns the prefab, calls SetupMinigame, then BeginDeposit with the held
    /// item and the station.
    ///
    /// P2: re-parented onto HandMinigame (freeze / RMB-look / footwork / settings-pause / MouseWorld /
    /// RestorePlayer). P5: this class now also owns the release / settle / retry / abandon / cancel-to-
    /// hand / dropHandle lifecycle the three deposit minigames used to each copy by hand. A concrete
    /// minigame keeps only its own flavour:
    ///  - <see cref="PoseCarriedItem"/>   how the item sits in the hand while aiming (abstract)
    ///  - <see cref="DropSettings"/>      the GuidedDrop tuning for the release (abstract)
    ///  - optional hooks: <see cref="OnAiming"/>, <see cref="OnReleased"/>,
    ///    <see cref="OnItemTouchedStation"/>, <see cref="OnDeposited"/>, <see cref="OnMissed"/>,
    ///    <see cref="OnCleanup"/>, plus the HandMinigame OnMinigameLateUpdate / OnMinigameFixedUpdate.
    ///
    /// The contact-driven deposit gate (an item's DepositContactPoint must physically reach a free
    /// slot's DepositTarget volume, or - with no DepositTarget - within Station.fallbackTargetRadius of
    /// the slot position) lives in <see cref="TryDepositOnContact"/>. See ARCHITECTURE.md.
    /// </summary>
    public abstract class ItemDepositMinigame : HandMinigame
    {
        [Header("Deposit")]
        [Tooltip("Seconds to wait for a released item to settle before judging the outcome (a miss).")]
        public float settleTime = 1.2f;
        [Tooltip("After a miss the minigame waits for the player to pick the item back up. If they walk " +
                 "this far from the dropped item instead, the minigame gives up (they can still retry " +
                 "via E on the station).")]
        public float abandonDistance = 8f;
        [Tooltip("Log the deposit's landing numbers to the Console so DropSlot / DepositTarget placement " +
                 "can be tuned. (Console output only shows with the CC_LOGGING scripting define.)")]
        [FormerlySerializedAs("debugLanding")] // SwordHangMinigame's pre-P5 name for this field
        public bool debugMinigame = true;

        // -- shared runtime state (was duplicated as item/rack, item/chest, item/vase + released flags) --
        protected PickupItem Item;
        protected TaskDepositStation Station;
        // The point on the item (a child marker) that must physically reach a free slot's DepositTarget
        // volume for the deposit to register. Null = no marker; the item's origin is used instead.
        protected DepositContactPoint contactPoint;

        protected bool released;
        protected bool resolving;
        protected bool awaitingRetry;
        // "Has the released item physically touched the station yet?" - consolidates touchedRack /
        // touchedChest / touchedVase. Drives OnItemTouchedStation and the miss-log context.
        protected bool touchedStation;
        protected float settleTimer;
        protected GuidedDrop.Handle dropHandle;

        /// <summary>
        /// Hand the minigame the item the player is carrying and the station they interacted with.
        /// Called once, right after SetupMinigame. The default runs the shared setup and goes straight
        /// into the aim phase; ChestDepositMinigame overrides it to run its lid phase first.
        /// </summary>
        public virtual void BeginDeposit(PickupItem heldItem, TaskDepositStation station)
        {
            if (!PrepareDeposit(heldItem, station)) return;
            EnterAiming();
        }

        // Shared front-half of BeginDeposit: store refs, resolve the contact marker, take control.
        // Returns false (after cancelling) if a required reference is missing. A subclass with its own
        // BeginDeposit (chest) calls this for the common setup, then runs its own phase before EnterAiming.
        protected bool PrepareDeposit(PickupItem heldItem, TaskDepositStation station)
        {
            Item = heldItem;
            Station = station;

            if (Item == null || Station == null || cam == null || Hand == null || Hand.HandBone == null)
            {
                CancelMinigame();
                return false;
            }

            contactPoint = Item.GetComponentInChildren<DepositContactPoint>(true);

            // First call: a harmless re-add (SetupMinigame already registered us). On a retry restart
            // (RestartAiming, after Release's RestorePlayer left the registry) this is what makes the
            // player "busy" again while re-aiming.
            RejoinActiveRegistry();
            player.SetControlsLocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ReanchorFootwork();
            return true;
        }

        // Pose the item ready to aim and start the hand rig. Also the shared re-entry point for
        // RestartAiming and for ChestDepositMinigame's OpenLid -> AimItem transition.
        protected void EnterAiming()
        {
            released = false;
            resolving = false;
            awaitingRetry = false;
            touchedStation = false;
            PoseCarriedItem();
            Hand.Begin();
        }

        // -- the template frame loop (chest wraps this inside its phase switch) --
        protected override void OnMinigameUpdate()
        {
            if (Item == null || Station == null) { FinishFail(); return; }

            if (!released)
            {
                Hand.ReachToward(MouseWorld());
                OnAiming();
                if (MinigameInput.PrimaryDown) Release();
                return;
            }

            if (resolving) return;

            // Player picked the item back up while the minigame is still open - straight back to aiming.
            if (player.GetHeldItem() == Item) { RestartAiming(); return; }

            if (awaitingRetry)
            {
                // Missed. The item is loose; wait for the pickup above. If the player abandons it and
                // walks off, close the minigame (they can still retry with E on the station).
                if (Vector3.Distance(player.transform.position, Item.transform.position) > abandonDistance)
                    FinishFail();
                return;
            }

            if (!touchedStation
                && StationContactProbe.Resting(Item.transform, Station.transform, StationTouchDistance))
            {
                touchedStation = true;
                OnItemTouchedStation();
            }

            if (TryDepositOnContact()) return;

            settleTimer -= Time.deltaTime;
            Rigidbody rb = Item.GetComponent<Rigidbody>();
            bool moving = rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.04f;
            if (settleTimer <= 0f && !moving && ReadyToJudgeMiss) Miss();
        }

        // Let go of the item: it falls under DropSettings(), the player gets normal control back.
        protected void Release()
        {
            released = true;
            awaitingRetry = false;
            touchedStation = false;
            settleTimer = settleTime;

            player.ClearHeldItem();
            Item.DropInPlace();

            Rigidbody rb = Item.GetComponent<Rigidbody>();
            Collider col = Item.GetComponent<Collider>();
            dropHandle = GuidedDrop.Begin(rb, col, DropSettings());

            // Don't let it bounce off the player standing right there.
            if (col != null && player.CharController != null)
                Physics.IgnoreCollision(col, player.CharController, true);

            OnReleased();

            // The player can move again while the item falls - hand normal control straight back
            // (RestorePlayer is safe to call again when the minigame later actually ends).
            RestorePlayer();
        }

        /// <summary>
        /// The shared contact-driven deposit gate. For each FREE slot: the item's contact point must be
        /// within its <c>contactRadius</c> of that slot's <see cref="DepositTarget"/> volume
        /// (<see cref="Collider.ClosestPoint"/>), or - when the slot has no DepositTarget - within
        /// <see cref="TaskDepositStation.fallbackTargetRadius"/> of its DropSlot position. First
        /// qualifying slot wins: end the guided drop, seat the item, hand off to <see cref="OnDeposited"/>.
        /// Safe to call every frame; a no-op once resolving or after the refs have gone.
        /// </summary>
        protected bool TryDepositOnContact()
        {
            if (resolving || Item == null || Station == null) return false;

            Vector3 point = contactPoint != null ? contactPoint.WorldPosition : Item.transform.position;
            float pointRadius = contactPoint != null ? contactPoint.contactRadius : 0f;

            for (int i = 0; i < Station.SlotCount; i++)
            {
                if (!Station.IsSlotFree(i)) continue;

                DepositTarget target = Station.GetDepositTarget(i);
                Collider volume = target != null ? target.Volume : null;

                bool reached;
                if (volume != null)
                {
                    // ClosestPoint returns point unchanged when it is inside the volume -> distance 0.
                    reached = Vector3.Distance(volume.ClosestPoint(point), point) <= pointRadius;
                }
                else
                {
                    Transform slot = Station.GetDropSlot(i);
                    if (slot == null) continue;
                    reached = Vector3.Distance(slot.position, point) <= Station.fallbackTargetRadius;
                }
                if (!reached) continue;

                resolving = true;
                dropHandle?.End();
                dropHandle = null;
                Station.DepositIntoSlot(Item, i, player); // parents + poses it in the slot
                if (debugMinigame) Log.Game($"[{GetType().Name}] contact point reached slot {i}.");
                OnDeposited(i);
                return true;
            }
            return false;
        }

        // Distance from the item's contact point to the nearest FREE slot target - the DepositTarget
        // volume surface when the slot has one, else its DropSlot position. +inf when the station has no
        // free slot or a reference has gone away. Tuning aid for the miss log only.
        private float NearestFreeTargetDistance()
        {
            if (Item == null || Station == null) return float.MaxValue;

            Vector3 point = contactPoint != null ? contactPoint.WorldPosition : Item.transform.position;
            float best = float.MaxValue;
            for (int i = 0; i < Station.SlotCount; i++)
            {
                if (!Station.IsSlotFree(i)) continue;

                DepositTarget target = Station.GetDepositTarget(i);
                Collider volume = target != null ? target.Volume : null;

                float d;
                if (volume != null)
                {
                    d = Vector3.Distance(volume.ClosestPoint(point), point);
                }
                else
                {
                    Transform slot = Station.GetDropSlot(i);
                    if (slot == null) continue;
                    d = Vector3.Distance(slot.position, point);
                }
                if (d < best) best = d;
            }
            return best;
        }

        // Settled without seating. One last contact check (in case it came to rest exactly on a volume
        // edge), then leave the item loose for the player to pick up and retry.
        private void Miss()
        {
            if (TryDepositOnContact()) return;

            if (debugMinigame)
                Log.Game($"[{GetType().Name}] MISS - touchedStation={touchedStation}, contact point " +
                         $"{NearestFreeTargetDistance():F2}m from the nearest free target. " +
                         "Leaving the item loose to retry.");

            OnMissed();
            awaitingRetry = true;
        }

        // The player grabbed the item again mid-minigame - restart the aiming phase with the same
        // station and task rather than ending.
        protected virtual void RestartAiming()
        {
            dropHandle?.End();
            dropHandle = null;
            RejoinActiveRegistry();
            player.SetControlsLocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            EnterAiming();
        }

        protected void FinishFail()
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
            OnCleanup();
            if (AbortReattachesItem && Item != null && player != null)
                Item.AttachToHand(player.RightHandSocket); // give the item back as a normal held item
            base.CancelMinigame(); // restores the player via HandMinigame.OnMinigameEnd
        }

        private void OnDestroy()
        {
            OnCleanup();
            dropHandle?.End();
            dropHandle = null;
        }

        // -- hooks ---------------------------------------------------------------------------------

        /// <summary>Pose the item in the hand ready to aim. Called from BeginDeposit and RestartAiming.</summary>
        protected abstract void PoseCarriedItem();

        /// <summary>GuidedDrop tuning for the release (frozen dead-straight, phase-1 frozen, ...).</summary>
        protected abstract GuidedDrop.Settings DropSettings();

        /// <summary>Per-frame, while still aiming, after the hand reach target is set (e.g. re-assert a pose lock).</summary>
        protected virtual void OnAiming() { }

        /// <summary>After GuidedDrop.Begin + the player-ignore, before RestorePlayer (e.g. make station colliders passable).</summary>
        protected virtual void OnReleased() { }

        /// <summary>The released item's pivot has first come within <see cref="StationTouchDistance"/> of station geometry.</summary>
        protected virtual void OnItemTouchedStation() { }

        /// <summary>The item has been seated in <paramref name="slot"/>. Default: finish. Chest overrides to close the lid first.</summary>
        protected virtual void OnDeposited(int slot)
        {
            Item = null;
            CompleteMinigame(); // advances the DepositItemStep
        }

        /// <summary>The drop settled without seating and is about to become an awaiting-retry loose item.</summary>
        protected virtual void OnMissed() { }

        /// <summary>Undo any physics ignore-pairs / player tweaks. Called from CancelMinigame and OnDestroy (idempotent).</summary>
        protected virtual void OnCleanup() { }

        /// <summary>Downward-ray distance for the "item touched the station" probe. Sword widens this.</summary>
        protected virtual float StationTouchDistance => 0.5f;

        /// <summary>False to hold off calling a settled drop a miss (sword: while the blade-down topple is still running).</summary>
        protected virtual bool ReadyToJudgeMiss => true;

        /// <summary>Whether CancelMinigame hands the item back to the hand. Default: only if not yet released.</summary>
        protected virtual bool AbortReattachesItem => !released;
    }
}
