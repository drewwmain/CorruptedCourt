using UnityEngine;

/// <summary>
/// "Has the released item physically touched the station yet?" - the gate every deposit minigame
/// uses before it will register a drop. A short ray straight down from the item's pivot, filtered to
/// the station's own collider hierarchy. Works with non-convex mesh colliders (unlike
/// Physics.ComputePenetration / OverlapSphere).
///
/// Consolidates SwordHangMinigame.RaycastTouchingRack() and ChestDepositMinigame.RaycastTouchingChest().
/// </summary>
public static class StationContactProbe
{
    /// <summary>
    /// True when <paramref name="item"/>'s pivot is resting within <paramref name="distance"/> metres
    /// of a collider that belongs to <paramref name="stationRoot"/>'s hierarchy.
    /// </summary>
    public static bool Resting(Transform item, Transform stationRoot, float distance = 0.12f)
    {
        if (item == null || stationRoot == null) return false;

        if (!Physics.Raycast(item.position + Vector3.up * 0.03f, Vector3.down,
                             out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            return false;

        return BelongsTo(hit.collider, stationRoot);
    }

    /// <summary>As <see cref="Resting"/>, but also reports the hit for callers that want the point / normal.</summary>
    public static bool Resting(Transform item, Transform stationRoot, float distance, out RaycastHit hit)
    {
        hit = default;
        if (item == null || stationRoot == null) return false;

        if (!Physics.Raycast(item.position + Vector3.up * 0.03f, Vector3.down,
                             out hit, distance, ~0, QueryTriggerInteraction.Ignore))
            return false;

        return BelongsTo(hit.collider, stationRoot);
    }

    private static bool BelongsTo(Collider col, Transform stationRoot)
    {
        if (col == null) return false;
        TaskDepositStation station = stationRoot.GetComponent<TaskDepositStation>();
        if (station != null && col.GetComponentInParent<TaskDepositStation>() == station) return true;
        return col.transform == stationRoot || col.transform.IsChildOf(stationRoot);
    }
}
