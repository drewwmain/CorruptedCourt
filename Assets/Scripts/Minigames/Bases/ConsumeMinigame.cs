using UnityEngine;

/// <summary>
/// Bring the held item (or a child of it - the cake piece on the plate) up to the player's mouth
/// anchor and "eat" / "drink" it in a few beats, then destroy it.
///
/// <see cref="simpleMode"/> collapses it to a single button/click, preserving the behaviour of the
/// current <c>ConsumeItemMinigame</c> for AI / fakers.
///
/// Fits: eat cake piece, drink wine (also auto-started right after the Cheers PartnerMinigame).
/// </summary>
public abstract class ConsumeMinigame : HandMinigame
{
    [Header("Consume")]
    [Tooltip("One click and it's done - matches the old ConsumeItemMinigame. Off = the physical to-the-mouth version.")]
    public bool simpleMode = false;
    [Tooltip("Bites / sips before it's gone (physical mode).")]
    public int servings = 3;
    [Tooltip("How close the item has to get to the mouth anchor to register a bite/sip.")]
    public float mouthRadius = 0.12f;
    [Tooltip("Destroy the whole held object; off = destroy child PickupItems and mark the container spent.")]
    public bool destroyWholeItem = true;

    protected readonly MinigameProgressTracker Progress = new MinigameProgressTracker();
    protected PickupItem item;
    protected Transform mouthAnchor;
    private bool armed = true; // re-arm between servings so one approach = one serving

    protected override void OnHandBegin()
    {
        item = Context != null ? Context.HeldItem : (player != null ? player.GetHeldItem() : null);
        mouthAnchor = ResolveMouthAnchor();

        Progress.ConfigureCount(simpleMode ? 1 : Mathf.Max(1, servings));
        Progress.Completed += Finish;

        if (item != null && !simpleMode)
            Hand.AttachItem(item, Vector3.zero, Vector3.zero);
    }

    protected override void OnMinigameUpdate()
    {
        if (simpleMode)
        {
            if (MinigameInput.PrimaryDown) Progress.Tick();
            return;
        }

        Hand.AimFromMouse(reachDistance);
        if (item == null || mouthAnchor == null) return;

        float d = Vector3.Distance(item.transform.position, mouthAnchor.position);
        if (armed && d <= mouthRadius) { armed = false; Progress.Tick(); OnServing(Progress.Progress01); }
        else if (!armed && d > mouthRadius * 2f) { armed = true; }
    }

    private void Finish()
    {
        if (item != null)
        {
            if (destroyWholeItem)
            {
                GameObject go = item.gameObject;
                player.ClearHeldItem();
                Destroy(go);
            }
            else
            {
                foreach (PickupItem child in item.GetComponentsInChildren<PickupItem>())
                    if (child != item) Destroy(child.gameObject);
                item.isSpent = true;
            }
        }
        OnConsumed();
        CompleteMinigame();
    }

    /// <summary>Player head/mouth transform - override if the rig exposes one directly.</summary>
    protected virtual Transform ResolveMouthAnchor()
    {
        Animator a = player != null ? player.GetComponentInChildren<Animator>() : null;
        if (a != null && a.isHuman)
        {
            Transform head = a.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head;
        }
        return player != null ? player.PlayerCamera : null;
    }

    protected virtual void OnServing(float progress01) { }
    protected virtual void OnConsumed() { }
}
