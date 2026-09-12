using UnityEngine;
using CorruptedCourt.Core;
using UnityEngine.Serialization;
using CorruptedCourt.Items;
using CorruptedCourt.Minigames;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Swaps an object between a "deposit station" role and a "pickup item" role depending on the match
    /// stage. Example: the Vase is a TaskDepositStation in round 1 (drop flowers into it) and a PickupItem
    /// from round 2 (carry it to the HighTable).
    ///
    /// SETUP: build the two roles as SEPARATE child GameObjects and drag them into the fields below:
    ///   Vase                (empty parent - put THIS component here, keep it active)
    ///   |- Vase_Station      TaskDepositStation + TaskLocation + collider + station visual
    ///   |- Vase_Item         PickupItem + Rigidbody + collider + item visual
    /// This component must NOT sit on either role object (it toggles them and needs to keep running).
    ///
    /// The switch is one-way per play session: once it becomes a pickup it stays a pickup.
    /// </summary>
    public class RoundRoleSwitch : MonoBehaviour
    {
        public enum TestMode
        {
            Off,                // follow the real match stage only
            PickupAfterDeposit, // station first; switch to pickup the moment it has received its item
            PickupNow           // switch to pickup immediately (skips the deposit step)
        }

        [Header("Role Objects")]
        [Tooltip("Active while the object is a deposit station (early rounds).")]
        public GameObject depositStationObject;

        [Tooltip("Active once the object becomes a carryable pickup item.")]
        public GameObject pickupItemObject;

        [Tooltip("With Test Mode = Off, the pickup role turns on when the match reaches this stage.")]
        public int pickupFromStage = 2;

        [Header("Testing")]
        [Tooltip("Off = real stage only. PickupAfterDeposit = stay a station until the flowers (etc.) are " +
                 "deposited, then switch. PickupNow = be a pickup from the start.")]
        public TestMode testMode = TestMode.Off;

        [Tooltip("When the station received a deposit, set the DepositedContainer state flag on the pickup " +
                 "item so only stations that demand it (TaskDepositStation.requiredState) accept it.")]
        [FormerlySerializedAs("prefixNameWhenDeposited")]
        public bool flagAsDepositedContainer = true;

        [Header("Deposited Item Placement")]
        [Tooltip("Snap transferred items to a fixed local spot on the pickup object (so a deposited " +
                 "CakePiece sits ON the plate) instead of keeping their world position.")]
        public bool snapDepositsToRestPoint = true;

        [Tooltip("Local position on the pickup object where a transferred item rests.")]
        public Vector3 depositRestLocalPosition = Vector3.zero;

        [Tooltip("Extra local offset added per additional item, so multiple deposits don't fully overlap.")]
        public Vector3 depositRestPerItemOffset = new Vector3(0f, 0.05f, 0f);

        private bool switchedToPickup;
        private bool applyScheduled; // a deferred (post-deposit) Apply() is queued

        private void OnValidate()
        {
            if (depositStationObject == gameObject || pickupItemObject == gameObject)
                Debug.LogWarning($"[RoundRoleSwitch] {name}: this component must sit on a PARENT object, " +
                                 "not on a role object it toggles.", this);

            if (pickupFromStage < 1)
                Debug.LogWarning($"[RoundRoleSwitch] {name}: Pickup From Stage should be >= 1.", this);
        }

        private void Awake()
        {
            if (depositStationObject == gameObject || pickupItemObject == gameObject)
                Log.Warn($"[RoundRoleSwitch] {name}: this component should live on a parent, not on a role object it toggles.");
        }

        private void OnEnable()
        {
            GameEvents.MatchStateChanged += OnMatchStateChanged;
            GameEvents.StationReceivedDeposit += OnStationReceivedDeposit;
        }

        private void OnDisable()
        {
            GameEvents.MatchStateChanged -= OnMatchStateChanged;
            GameEvents.StationReceivedDeposit -= OnStationReceivedDeposit;
        }

        private void Start()
        {
            switchedToPickup = false;
            Apply();
        }

        // The station -> pickup switch is one-way and keys off the match stage (Test Mode = Off) or a
        // deposit into our own station (PickupAfterDeposit). The real stage only advances on a phase
        // transition, so MatchStateChanged covers that path; OnStationReceivedDeposit covers the
        // deposit path. Together they replace the old every-frame Apply() poll.
        private void OnMatchStateChanged(MatchManager.MatchState _)
        {
            Apply();
        }

        // Fired when any TaskDepositStation receives an item. If it's ours and we haven't switched yet,
        // re-run Apply - but on a later frame, so the deposit's own task-step completion and any deposit
        // minigame finish while our station is still active and registered in TaskLocation.AllLocations.
        private void OnStationReceivedDeposit(TaskDepositStation station)
        {
            if (switchedToPickup || applyScheduled || depositStationObject == null || station == null) return;
            if (!station.transform.IsChildOf(depositStationObject.transform)) return;

            applyScheduled = true;
            StartCoroutine(ApplyAfterDeposit());
        }

        private System.Collections.IEnumerator ApplyAfterDeposit()
        {
            yield return null;                                  // let this frame's task-step evaluation run
            while (MinigameBase.IsAnyActive) yield return null; // and let a deposit minigame wind down
            applyScheduled = false;
            Apply();
        }

        private void Apply()
        {
            bool wasPickup = switchedToPickup;
            if (!switchedToPickup && ShouldBecomePickup())
                switchedToPickup = true;

            if (switchedToPickup && !wasPickup)
            {
                // The switch just happened this frame. Note whether anything was actually deposited
                // (checked while the station still owns its slots), bring up the pickup object, move
                // the deposited props onto it, tag it if it received a deposit, then hide the station.
                bool receivedDeposit = DepositStationSatisfied();

                SetActiveIfNeeded(pickupItemObject, true);
                TransferDepositedItems();

                if (receivedDeposit && flagAsDepositedContainer && pickupItemObject != null)
                {
                    PickupItem pickup = pickupItemObject.GetComponent<PickupItem>();
                    if (pickup != null) pickup.MarkAsDepositedContainer(); // sets ItemState.DepositedContainer
                }

                SetActiveIfNeeded(depositStationObject, false);
                return;
            }

            SetActiveIfNeeded(depositStationObject, !switchedToPickup);
            SetActiveIfNeeded(pickupItemObject, switchedToPickup);
        }

        // Re-parents every item currently in the deposit station onto the pickup object, keeping its
        // world position, so deposited props (e.g. the flowers) stay put and travel with it. Also
        // records what was deposited on the pickup's ItemPayload, so a step can read it back (e.g. the
        // Plate now carries CakePiece x1) without the pickup itself needing to BE that identity.
        private void TransferDepositedItems()
        {
            if (pickupItemObject == null || depositStationObject == null) return;

            TaskDepositStation station = depositStationObject.GetComponent<TaskDepositStation>();
            if (station == null || station.depositedItemSlots == null) return;

            ItemPayload payload = null;
            int placed = 0;
            for (int i = 0; i < station.depositedItemSlots.Length; i++)
            {
                PickupItem item = station.depositedItemSlots[i];
                if (item == null) continue;

                item.transform.SetParent(pickupItemObject.transform, true); // worldPositionStays

                if (snapDepositsToRestPoint)
                {
                    item.transform.localPosition = depositRestLocalPosition + depositRestPerItemOffset * placed;
                    item.transform.localRotation = Quaternion.identity;
                }

                if (item.definition != null)
                {
                    if (payload == null)
                    {
                        payload = pickupItemObject.GetComponent<ItemPayload>();
                        if (payload == null) payload = pickupItemObject.AddComponent<ItemPayload>();
                    }
                    payload.contents = item.definition;
                    payload.count++;
                }

                station.depositedItemSlots[i] = null; // station no longer owns it
                placed++;
            }
        }

        private bool ShouldBecomePickup()
        {
            // Real progression always wins.
            int stage = MatchManager.Instance != null ? MatchManager.Instance.currentStage : 1;
            if (stage >= pickupFromStage) return true;

            switch (testMode)
            {
                case TestMode.PickupNow:
                    return true;
                case TestMode.PickupAfterDeposit:
                    return DepositStationSatisfied();
                default:
                    return false;
            }
        }

        private bool DepositStationSatisfied()
        {
            if (depositStationObject == null) return false;
            TaskDepositStation s = depositStationObject.GetComponent<TaskDepositStation>();
            return s != null && s.HasReceivedItem();
        }

        private static void SetActiveIfNeeded(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }
    }
}
