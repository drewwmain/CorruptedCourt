using System.Collections.Generic;
using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// R3/R4's home: a station's condition (unlit candle vs. lit, unfilled vase vs. filled), its
    /// seats, and its revert metadata (ARCHITECTURE.md §8.1). Sits beside TaskLocation.
    ///
    /// For a station with a sibling TaskDepositStation, Current is derived live from
    /// TaskDepositStation.HasReceivedItem() rather than stored here - so MarkSatisfied/TryRevert are
    /// no-ops for those (a deposit station's "revert" already exists as retrieval, per §8.2: "On a
    /// deposit station, reversal IS retrieval... wire reversal to that path rather than building a
    /// second one"). RefreshFromDeposit() is the hook TaskDepositStation calls whenever its slots
    /// change, so the visuals and StationConditionChanged stay in sync despite MarkSatisfied/TryRevert
    /// never firing for these stations.
    /// </summary>
    [RequireComponent(typeof(TaskLocation))]
    public class TaskStationState : MonoBehaviour
    {
        public enum Condition { Pending, Busy, Satisfied }

        [SerializeField] private int seatCapacity = 1;
        [SerializeField] private bool playerReversible = true;
        [Tooltip("Shown on the reversal prompt once StationReversal exists (B7). e.g. \"snuff\" / \"tilt\" / \"re-roll\".")]
        [SerializeField] private string revertVerb = "reset";
        [SerializeField] private float revertHoldSeconds = 1.0f;
        [Tooltip("Active while Current == Pending.")]
        [SerializeField] private GameObject pendingVisual;
        [Tooltip("Active while Current == Satisfied.")]
        [SerializeField] private GameObject satisfiedVisual;

        private TaskLocation location;
        private TaskDepositStation depositStation; // optional; when present, Current derives from it
        private Condition condition = Condition.Pending;
        private readonly List<PlayerController> seatedPlayers = new List<PlayerController>();

        /// <summary>The sibling TaskLocation's locationID, or null if none is present.</summary>
        public string LocationID => location != null ? location.locationID : null;

        public string RevertVerb => revertVerb;
        public bool PlayerReversible => playerReversible;
        public float RevertHoldSeconds => revertHoldSeconds;

        public Condition Current => depositStation != null
            ? (depositStation.HasReceivedItem() ? Condition.Satisfied : Condition.Pending)
            : condition;

        /// <summary>True when this station's Current derives from a sibling TaskDepositStation rather
        /// than the stored condition field - MarkSatisfied/TryRevert/DebugForceRevert are all no-ops for
        /// these (see the class summary). Debug/tooling only, e.g. StationDebugPanel disabling its
        /// force-cheat buttons here rather than offering ones that silently do nothing.</summary>
        public bool IsDepositBacked => depositStation != null;

        /// <summary>Who satisfied this station. Logging/UI only - never gates anything.</summary>
        public PlayerController SatisfiedBy { get; private set; }

        public bool SeatsFull => seatedPlayers.Count >= seatCapacity;

        private void Awake()
        {
            location = GetComponent<TaskLocation>();
            depositStation = GetComponent<TaskDepositStation>();
        }

        private void OnEnable()
        {
            StationRegistry.Register(this);
            UpdateVisual();
        }

        private void OnDisable() => StationRegistry.Unregister(this);
        private void OnDestroy() => StationRegistry.Unregister(this);

        public bool TryClaimSeat(PlayerController p)
        {
            if (p == null || SeatsFull || seatedPlayers.Contains(p)) return false;
            seatedPlayers.Add(p);
            return true;
        }

        public void ReleaseSeat(PlayerController p)
        {
            if (p == null) return;
            seatedPlayers.Remove(p);
        }

        /// <summary>Releases any seated player now farther than <paramref name="maxDistance"/> from this
        /// station. There's no hold-to-interact mechanic anywhere in this codebase to release a seat on
        /// button-up, so callers (StationInteractStep) call this to lazily re-validate occupancy on every
        /// new interaction attempt instead - see IMPLEMENTATION_PROMPTS.md B6b.</summary>
        public void PruneStaleSeats(float maxDistance)
        {
            float maxDistSq = maxDistance * maxDistance;
            for (int i = seatedPlayers.Count - 1; i >= 0; i--)
            {
                PlayerController p = seatedPlayers[i];
                if (p == null || (p.transform.position - transform.position).sqrMagnitude > maxDistSq)
                    seatedPlayers.RemoveAt(i);
            }
        }

        public void MarkSatisfied(PlayerController by)
        {
            if (depositStation != null) return; // derives from HasReceivedItem() instead - see class summary
            if (condition == Condition.Satisfied) return;

            condition = Condition.Satisfied;
            SatisfiedBy = by;
            UpdateVisual();
            GameEvents.RaiseStationConditionChanged(this);
        }

        public bool TryRevert(PlayerController by)
        {
            if (depositStation != null) return false; // reversal is retrieval for these - see class summary
            if (!playerReversible || condition != Condition.Satisfied) return false;

            condition = Condition.Pending;
            SatisfiedBy = null;
            UpdateVisual();
            GameEvents.RaiseStationConditionChanged(this);
            return true;
        }

        /// <summary>Debug-only: forces this station back to Pending, bypassing playerReversible (real
        /// player-facing reversal must still respect it - see TryRevert). No-op for a deposit-backed
        /// station, same as TryRevert. Used by StationDebugPanel; never called by real gameplay.</summary>
        public void DebugForceRevert()
        {
            if (depositStation != null) return;
            if (condition != Condition.Satisfied) return;

            condition = Condition.Pending;
            SatisfiedBy = null;
            UpdateVisual();
            GameEvents.RaiseStationConditionChanged(this);
        }

        /// <summary>Called by TaskDepositStation whenever its deposited-item slots change (deposit,
        /// retrieval, sabotage eject/destroy). No-op when there's no deposit sibling.</summary>
        public void RefreshFromDeposit()
        {
            if (depositStation == null) return;
            UpdateVisual();
            GameEvents.RaiseStationConditionChanged(this);
        }

        private void UpdateVisual()
        {
            bool satisfied = Current == Condition.Satisfied;
            if (pendingVisual != null) pendingVisual.SetActive(!satisfied);
            if (satisfiedVisual != null) satisfiedVisual.SetActive(satisfied);
        }
    }
}
