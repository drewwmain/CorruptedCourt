using UnityEngine;

/// <summary>
/// "Producing" minigames (cut the cake, cut the turkey leg) spawn a fresh PickupItem into the world
/// and then the player has to carry it somewhere (the plate). This bundles the spawn + naming +
/// follow-up hint so each producing minigame doesn't re-do it.
///
/// Scaffolding: <see cref="Produce"/> spawns and names the item like CakeCuttingMinigame does today.
/// <see cref="PushCarryHint"/> is where a transient waypoint / objective toward the destination gets
/// raised - left as a TODO until the waypoint API is picked (RefreshLocalWaypoints / TaskManager).
/// </summary>
[System.Serializable]
public class SpawnAndCarryObjective
{
    [Tooltip("PickupItem prefab dropped into the world when the producing action completes.")]
    public PickupItem producedPrefab;

    [Tooltip("Name forced onto the spawned item so it matches the follow-up AcquireItemStep. Blank = keep the prefab's name.")]
    public string producedItemName = "";

    [Tooltip("locationID of where the player should carry it next (e.g. the Plate deposit station).")]
    public string carryToLocationID = "";

    [Tooltip("Offset from the source object where the item appears (right / up, in the player's frame).")]
    public Vector2 spawnRightUpOffset = new Vector2(0.45f, 0.35f);

    /// <summary>Spawn the produced item next to <paramref name="source"/> (falls back to in front of the player).</summary>
    public PickupItem Produce(PlayerController player, Transform source)
    {
        if (producedPrefab == null)
        {
            Debug.LogWarning("[SpawnAndCarryObjective] No producedPrefab assigned - nothing produced.");
            return null;
        }

        Vector3 pos = source != null
            ? source.position
                + player.transform.right * spawnRightUpOffset.x
                + Vector3.up * spawnRightUpOffset.y
            : player.transform.position + player.transform.forward * 0.8f + Vector3.up * 1.0f;

        PickupItem piece = Object.Instantiate(producedPrefab, pos, Quaternion.identity);
        if (!string.IsNullOrEmpty(producedItemName))
        {
            piece.itemName = producedItemName;
            piece.gameObject.name = producedItemName;
        }
        piece.isInfiniteSource = false;
        return piece;
    }

    /// <summary>Raise a transient "carry it to X" objective / waypoint for the player.</summary>
    public void PushCarryHint(PlayerController player)
    {
        // TODO(P5): surface a temporary waypoint toward carryToLocationID. The follow-up
        // AcquireItemStep / DepositItemStep on the task already drives the real objective text;
        // this is only the "and now take it over there" nudge.
    }
}
