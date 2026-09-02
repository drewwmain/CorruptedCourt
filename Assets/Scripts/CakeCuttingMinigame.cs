using UnityEngine;

/// <summary>
/// Minigame for cake_cut's ProcessItemStep. On success it drops a freshly cut CakePiece PickupItem
/// into the world next to the cake (the player then has to pick it up and deposit it), and consumes
/// the cake being cut.
///
/// Fires for ANY player - via the cake_cut step's Minigame Prefab if they have the task, or via the
/// Cake item's "Process Minigame Prefab" if they don't (so Corrupted can fake it). Set BOTH fields
/// to this prefab.
///
/// PREFAB SETUP: a UI Canvas prefab with this component. Wire your button(s) to OnCut() (fires
/// Cuts Required times) or OnWinButton() (one click). Assign cakePiecePrefab.
/// </summary>
public class CakeCuttingMinigame : MinigameBase
{
    [Header("Output")]
    [Tooltip("PickupItem prefab dropped into the world when the minigame is won.")]
    public PickupItem cakePiecePrefab;

    [Tooltip("Name forced onto the spawned piece so it matches the AcquireItemStep. Leave blank to keep the prefab's own name.")]
    public string producedItemName = "CakePiece";

    [Tooltip("Destroy whatever cake the player is holding when the piece is produced.")]
    public bool consumeHeldItem = true;

    [Tooltip("Offset from the cake where the piece is dropped (right / up, in the player's frame).")]
    public Vector2 spawnRightUpOffset = new Vector2(0.45f, 0.35f);

    [Header("Difficulty")]
    [Tooltip("Number of successful cuts before the minigame completes.")]
    public int cutsRequired = 3;

    private int cuts;
    private bool finished;

    // Hook a "Cut" button here.
    public void OnCut()
    {
        if (finished) return;
        cuts++;
        if (cuts >= cutsRequired) Finish();
    }

    // Shortcut for a single "Done" button.
    public void OnWinButton()
    {
        if (!finished) Finish();
    }

    private void Finish()
    {
        finished = true;

        if (player != null)
        {
            if (consumeHeldItem)
            {
                PickupItem held = player.GetHeldItem();
                if (held != null)
                {
                    GameObject heldGO = held.gameObject;
                    player.ClearHeldItem();
                    Destroy(heldGO);
                }
            }

            if (cakePiecePrefab != null)
            {
                Vector3 spawnPos = ResolveSpawnPosition();
                PickupItem piece = Instantiate(cakePiecePrefab, spawnPos, Quaternion.identity);

                if (!string.IsNullOrEmpty(producedItemName))
                {
                    piece.itemName = producedItemName;
                    piece.gameObject.name = producedItemName;
                }
                piece.isInfiniteSource = false; // it's a one-off, not a spawner
                // left free-standing in the world - the player must walk over and pick it up
            }
            else
            {
                Debug.LogWarning("[CakeCuttingMinigame] No cakePiecePrefab assigned - nothing produced.");
            }
        }

        CompleteMinigame(); // completes the ProcessItemStep (task players); no-op for fakers
    }

    private Vector3 ResolveSpawnPosition()
    {
        // Drop next to the cake we were cutting; fall back to just in front of the player.
        if (player.activeMinigameTarget != null)
        {
            Vector3 basePos = player.activeMinigameTarget.transform.position;
            return basePos
                 + player.transform.right * spawnRightUpOffset.x
                 + Vector3.up * spawnRightUpOffset.y;
        }

        return player.transform.position
             + player.transform.forward * 0.8f
             + Vector3.up * 1.0f;
    }
}
