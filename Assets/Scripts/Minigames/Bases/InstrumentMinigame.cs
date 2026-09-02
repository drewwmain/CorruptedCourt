using UnityEngine;

/// <summary>
/// Play an instrument by an ACTIVATION GESTURE plus NOTE INPUT (A/S/D/F/G).
///  - <see cref="WindInstrumentMinigame"/> : raise the hand/instrument to the mouth anchor, then the
///    note keys play. (flute, trumpet)
///  - <see cref="StringInstrumentMinigame"/> : a mouse strum must accompany each note key. (guitar)
///
/// Completion = play the target number of valid notes (optionally following a prompted sequence).
/// </summary>
public abstract class InstrumentMinigame : HandMinigame
{
    [Header("Instrument")]
    [Tooltip("Local pose of the instrument gripped in the hand.")]
    public Vector3 instrumentLocalPos = Vector3.zero;
    public Vector3 instrumentLocalEuler = Vector3.zero;
    [Tooltip("Notes to play to finish.")]
    public int notesToComplete = 8;

    protected readonly MinigameProgressTracker Progress = new MinigameProgressTracker();
    protected PickupItem instrument;
    protected bool active; // activation gesture currently satisfied

    protected override void OnHandBegin()
    {
        instrument = Context != null ? Context.HeldItem : (player != null ? player.GetHeldItem() : null);
        if (instrument != null) Hand.AttachItem(instrument, instrumentLocalPos, instrumentLocalEuler);

        Progress.ConfigureCount(Mathf.Max(1, notesToComplete));
        Progress.Completed += CompleteMinigame;
    }

    protected override void OnMinigameUpdate()
    {
        active = UpdateActivationGesture();

        int notes = MinigameInput.NoteKeysDown();
        if (notes != 0 && active && NoteAllowedThisFrame())
        {
            Progress.Tick(CountBits(notes));
            OnNotePlayed(notes);
        }
    }

    private static int CountBits(int mask)
    {
        int n = 0;
        while (mask != 0) { n += mask & 1; mask >>= 1; }
        return n;
    }

    // --- hooks -------------------------------------------------------------------------------

    /// <summary>Drive the hand (to mouth / strum-ready) and return true when notes may sound.</summary>
    protected abstract bool UpdateActivationGesture();

    /// <summary>Extra per-frame gate (string instruments require a strum here).</summary>
    protected virtual bool NoteAllowedThisFrame() => true;

    protected virtual void OnNotePlayed(int noteMask) { }
}

/// <summary>Flute / trumpet: bring the instrument to the mouth, then the note keys sound.</summary>
public abstract class WindInstrumentMinigame : InstrumentMinigame
{
    [Tooltip("How close the hand must be to the mouth anchor for notes to sound.")]
    public float mouthRadius = 0.14f;

    protected Transform mouthAnchor;

    protected override void OnHandBegin()
    {
        base.OnHandBegin();
        Animator a = player != null ? player.GetComponentInChildren<Animator>() : null;
        mouthAnchor = (a != null && a.isHuman) ? a.GetBoneTransform(HumanBodyBones.Head) : player.PlayerCamera;
    }

    protected override bool UpdateActivationGesture()
    {
        Hand.AimFromMouse(reachDistance);
        if (Hand.HandBone == null || mouthAnchor == null) return false;
        return Vector3.Distance(Hand.HandBone.position, mouthAnchor.position) <= mouthRadius;
    }
}

/// <summary>Guitar: hold it in a play position and flick the mouse (strum) as you press a note key.</summary>
public abstract class StringInstrumentMinigame : InstrumentMinigame
{
    [Tooltip("Mouse-Y flick speed (axis units / frame) that counts as a strum.")]
    public float strumThreshold = 3f;

    protected override bool UpdateActivationGesture()
    {
        Hand.AimFromMouse(reachDistance);
        return true; // the guitar is always in position; the strum is the real gate
    }

    protected override bool NoteAllowedThisFrame() => MinigameInput.MouseStrum(strumThreshold);
}
