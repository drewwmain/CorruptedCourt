using UnityEngine;

/// <summary>
/// The player holds a TOOL and works it over a TARGET object until a progress meter fills:
/// cut the cake / turkey (N cuts), polish the sword (every dirty zone wiped), light a candle /
/// firework (enough sparks near the wick), write on a contract (ink budget spent).
///
/// Concrete subclass responsibilities:
///  - configure <see cref="Progress"/> in <see cref="OnHandBegin"/> (Count / Accumulate / Zones),
///  - in <see cref="OnToolUpdate"/>, read the hand position vs the target and feed the tracker,
///  - optionally set <see cref="produce"/> for "spawn an item and carry it to the plate".
/// </summary>
public abstract class ToolOnTargetMinigame : HandMinigame
{
    [Header("Tool on target")]
    [Tooltip("Local pose of the tool while gripped in the hand.")]
    public Vector3 toolLocalPos = Vector3.zero;
    public Vector3 toolLocalEuler = Vector3.zero;
    [Tooltip("How close the tool has to be to a working point on the target to count as 'in contact'.")]
    public float contactRadius = 0.12f;

    [Tooltip("Optional: spawn a produced item and nudge the player to carry it somewhere.")]
    public SpawnAndCarryObjective produce;

    protected readonly MinigameProgressTracker Progress = new MinigameProgressTracker();
    protected PickupItem tool;
    protected Transform target;

    protected override void OnHandBegin()
    {
        tool = Context != null ? Context.HeldItem : (player != null ? player.GetHeldItem() : null);
        target = Context != null && Context.Target != null ? Context.Target.transform : null;

        if (tool != null) Hand.AttachItem(tool, toolLocalPos, toolLocalEuler);

        Progress.Completed += OnProgressComplete;
        ConfigureProgress();
    }

    protected override void OnMinigameUpdate()
    {
        Hand.AimFromMouse(reachDistance);
        OnToolUpdate();
    }

    private void OnProgressComplete()
    {
        if (produce != null && produce.producedPrefab != null)
        {
            produce.Produce(player, target);
            produce.PushCarryHint(player);
            if (tool != null && ConsumeToolOnComplete)
            {
                GameObject go = tool.gameObject;
                player.ClearHeldItem();
                Destroy(go);
            }
        }
        OnProcessed();
        CompleteMinigame();
    }

    // --- hooks --------------------------------------------------------------------------------

    /// <summary>Set up <see cref="Progress"/> - e.g. <c>Progress.ConfigureCount(3)</c>.</summary>
    protected abstract void ConfigureProgress();

    /// <summary>Per-frame: measure hand-vs-target and call <c>Progress.Tick()/Add()/MarkZone()</c>.</summary>
    protected abstract void OnToolUpdate();

    /// <summary>Runs just before completion (fade the dirt mask fully out, enable the flame, ...).</summary>
    protected virtual void OnProcessed() { }

    /// <summary>Destroy the held tool's *source item* when producing a piece (cake -> gone). Default off.</summary>
    protected virtual bool ConsumeToolOnComplete => false;
}
