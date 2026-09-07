using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// "Has the released item physically touched the station yet?" - the gate every deposit minigame
    /// uses before it will register a drop. A short ray straight down from the item's pivot, filtered to
    /// the station's own collider hierarchy. Works with non-convex mesh colliders (unlike
    /// Physics.ComputePenetration / OverlapSphere).
    ///
    /// The ray starts just above the item's origin, so it would otherwise hit the item's OWN mesh
    /// before ever reaching the station - hits on the item's collider hierarchy are skipped.
    ///
    /// Consolidates SwordHangMinigame.RaycastTouchingRack() and ChestDepositMinigame.RaycastTouchingChest().
    /// </summary>
    public static class StationContactProbe
    {
        // Shared scratch for the non-alloc ray - StationContactProbe.Resting is only ever called from the
        // main thread, once or twice per frame per open deposit minigame.
        private static readonly RaycastHit[] hitBuf = new RaycastHit[16];

        /// <summary>
        /// True when <paramref name="item"/>'s pivot is resting within <paramref name="distance"/> metres
        /// of a collider that belongs to <paramref name="stationRoot"/>'s hierarchy. The item's own
        /// colliders are ignored.
        /// </summary>
        public static bool Resting(Transform item, Transform stationRoot, float distance = 0.12f)
        {
            return Resting(item, stationRoot, distance, out _);
        }

        /// <summary>As <see cref="Resting"/>, but also reports the nearest qualifying hit (point / normal).</summary>
        public static bool Resting(Transform item, Transform stationRoot, float distance, out RaycastHit hit)
        {
            hit = default;
            if (item == null || stationRoot == null) return false;

            int n = Physics.RaycastNonAlloc(item.position + Vector3.up * 0.03f, Vector3.down,
                                            hitBuf, distance, ~0, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n && i < hitBuf.Length; i++)
            {
                RaycastHit h = hitBuf[i];
                if (h.collider == null) continue;
                // The ray starts just above the item's own origin - don't let its own mesh count.
                Transform ht = h.collider.transform;
                if (ht == item || ht.IsChildOf(item)) continue;
                if (!BelongsTo(h.collider, stationRoot)) continue;
                if (h.distance < best) { best = h.distance; hit = h; found = true; }
            }
            return found;
        }

        private static bool BelongsTo(Collider col, Transform stationRoot)
        {
            if (col == null) return false;
            TaskDepositStation station = stationRoot.GetComponent<TaskDepositStation>();
            if (station != null && col.GetComponentInParent<TaskDepositStation>() == station) return true;
            return col.transform == stationRoot || col.transform.IsChildOf(stationRoot);
        }
    }
}
