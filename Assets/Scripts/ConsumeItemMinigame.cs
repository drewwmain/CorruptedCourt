using UnityEngine;

/// <summary>
/// Minigame for a ConsumeItemStep (e.g. cake_eat: grab the cake piece off the plate and eat it).
/// On success it destroys the held item, then completes the step.
///
/// Fires for ANY player - via the step's Minigame Prefab if they have the task, or via the held
/// item's "Process Minigame Prefab" if they don't (so Corrupted can fake it). Set both to this prefab.
///
/// PREFAB SETUP: a UI Canvas prefab with this component. Wire your button(s) to OnBite() (fires
/// Bites Required times) or OnWinButton() (one click).
/// </summary>
public class ConsumeItemMinigame : MinigameBase
{
    [Tooltip("Destroy the whole held item (plate + anything parented to it) on success.")]
    public bool destroyWholeHeldItem = true;

    [Tooltip("If not destroying the whole item, destroy child PickupItems (e.g. the CakePiece on the plate) instead.")]
    public bool destroyChildPickups = true;

    [Header("Difficulty")]
    public int bitesRequired = 3;

    private int bites;
    private bool finished;

    public void OnBite()
    {
        if (finished) return;
        bites++;
        if (bites >= bitesRequired) Finish();
    }

    public void OnWinButton()
    {
        if (!finished) Finish();
    }

    private void Finish()
    {
        finished = true;

        if (player != null)
        {
            PickupItem held = player.GetHeldItem();
            if (held != null)
            {
                if (destroyWholeHeldItem)
                {
                    GameObject go = held.gameObject;
                    player.ClearHeldItem();
                    Destroy(go);
                }
                else if (destroyChildPickups)
                {
                    foreach (PickupItem child in held.GetComponentsInChildren<PickupItem>())
                    {
                        if (child != held) Destroy(child.gameObject);
                    }

                    // The container is now empty - pressing [E] on it should do nothing more.
                    held.isSpent = true;
                }
            }
        }

        CompleteMinigame(); // completes the ConsumeItemStep for task players; no-op for fakers
    }
}
