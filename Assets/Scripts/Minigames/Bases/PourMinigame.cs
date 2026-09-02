using UnityEngine;

/// <summary>
/// Two-vessel pour: grab an empty vessel, then grab a source vessel, then tilt the source over the
/// target and hold the aim while a fill meter rises. Mis-aim = spill (meter stalls / drains).
///
/// Grab order and vessel prefabs are the concrete subclass's job; the shared parts are the phase
/// machine, the tilt read, and the <see cref="Fill"/> meter wired to completion.
///
/// Fits: pour wine (empty glass + pitcher).
/// </summary>
public abstract class PourMinigame : HandMinigame
{
    public enum Phase { GrabEmpty, GrabSource, Pour, Done }

    [Header("Pour")]
    [Tooltip("How close the hand must be to a vessel to pick it up.")]
    public float grabRadius = 0.35f;
    [Tooltip("Source tilt (deg from upright) past which liquid actually flows.")]
    public float pourTiltThreshold = 55f;
    [Tooltip("Max horizontal offset of the stream from the target mouth that still counts as 'aimed'.")]
    public float aimTolerance = 0.06f;
    [Tooltip("Seconds of good pour to fill the glass.")]
    public float fillSeconds = 2.5f;

    protected Phase phase = Phase.GrabEmpty;
    protected readonly MinigameProgressTracker Fill = new MinigameProgressTracker();
    protected PickupItem emptyVessel;
    protected PickupItem sourceVessel;

    protected override void OnHandBegin()
    {
        Fill.ConfigureAccumulate(Mathf.Max(0.01f, fillSeconds));
        Fill.Completed += () => { phase = Phase.Done; CompleteMinigame(); };
    }

    protected override void OnMinigameUpdate()
    {
        Hand.AimFromMouse(reachDistance);

        switch (phase)
        {
            case Phase.GrabEmpty:  if (TryGrab(ref emptyVessel, EmptyVesselCandidate()))  phase = Phase.GrabSource; break;
            case Phase.GrabSource: if (TryGrab(ref sourceVessel, SourceVesselCandidate())) phase = Phase.Pour;       break;
            case Phase.Pour:       UpdatePour(); break;
        }
    }

    private bool TryGrab(ref PickupItem slot, PickupItem candidate)
    {
        if (slot != null || candidate == null || !MinigameInput.PrimaryDown) return false;
        if (Vector3.Distance(MouseWorld(), candidate.transform.position) > grabRadius) return false;
        slot = candidate;
        Hand.AttachItem(slot, Vector3.zero, Vector3.zero);
        return true;
    }

    private void UpdatePour()
    {
        if (sourceVessel == null) return;
        float tilt = Vector3.Angle(sourceVessel.transform.up, Vector3.up);
        bool flowing = tilt >= pourTiltThreshold;
        bool aimed = IsAimedAtGlass();

        if (flowing && aimed) Fill.Add(Time.deltaTime);
        OnPourTick(flowing, aimed, Fill.Progress01);
    }

    // --- hooks --------------------------------------------------------------------------------

    /// <summary>The empty-vessel PickupItem the player should grab first.</summary>
    protected abstract PickupItem EmptyVesselCandidate();

    /// <summary>The source-vessel PickupItem (pitcher) the player should grab second.</summary>
    protected abstract PickupItem SourceVesselCandidate();

    /// <summary>True when the source's spout is over the empty vessel's mouth within <see cref="aimTolerance"/>.</summary>
    protected abstract bool IsAimedAtGlass();

    /// <summary>Per-frame feedback while pouring (stream VFX, liquid level, spill sound).</summary>
    protected virtual void OnPourTick(bool flowing, bool aimed, float fill01) { }
}
