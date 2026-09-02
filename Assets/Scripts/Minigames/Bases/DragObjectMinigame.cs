using UnityEngine;

/// <summary>
/// Aim the hand at a world object, LEFT-CLICK to grab it, then drag. Success when a tracked value
/// reaches its goal:
///  - <see cref="DragMode.Axis"/>  : slide along a local axis 0 → 1 (unfurl the rolled banner).
///  - <see cref="DragMode.Angle"/> : rotate toward level, |roll| ≤ tolerance (straighten the picture).
///
/// Concrete subclass: point <see cref="grabbable"/> at the object (or leave null to use Context.Target),
/// set the mode + limits, and implement <see cref="ApplyDrag"/> to move/rotate it from the hand delta.
/// </summary>
public abstract class DragObjectMinigame : HandMinigame
{
    public enum DragMode { Axis, Angle }

    [Header("Drag")]
    public DragMode mode = DragMode.Axis;
    [Tooltip("How close the hand must be to the object to grab it on click.")]
    public float grabRadius = 0.35f;

    [Header("Axis mode")]
    [Tooltip("Local axis along which 0 = start, 1 = fully done (e.g. local -Y for a banner unrolling downward).")]
    public Vector3 localAxis = Vector3.down;
    [Tooltip("Travel in metres that maps to 0 → 1.")]
    public float axisTravel = 1.2f;

    [Header("Angle mode")]
    [Tooltip("Degrees of |roll| within which the object counts as level.")]
    public float levelTolerance = 3f;

    protected Transform grabbable;
    protected bool grabbed;
    protected float value01;          // Axis: 0..1 progress. Angle: unused (see IsLevel).
    private Vector3 lastHandPos;

    protected override void OnHandBegin()
    {
        if (grabbable == null && Context != null && Context.Target != null)
            grabbable = Context.Target.transform;
    }

    protected override void OnMinigameUpdate()
    {
        Vector3 handPos = MouseWorld();
        Hand.ReachToward(handPos);

        if (!grabbed)
        {
            if (MinigameInput.PrimaryDown && grabbable != null
                && Vector3.Distance(handPos, grabbable.position) <= grabRadius)
            {
                grabbed = true;
                lastHandPos = handPos;
            }
            return;
        }

        Vector3 handDelta = handPos - lastHandPos;
        lastHandPos = handPos;

        ApplyDrag(handDelta);

        if (MinigameInput.PrimaryUp) grabbed = false;

        if (IsDone()) CompleteMinigame();
    }

    /// <summary>Move / rotate <see cref="grabbable"/> from the per-frame hand movement.</summary>
    protected abstract void ApplyDrag(Vector3 handWorldDelta);

    /// <summary>Default success test - override for custom conditions.</summary>
    protected virtual bool IsDone()
    {
        return mode == DragMode.Axis ? value01 >= 1f : IsLevel();
    }

    protected bool IsLevel()
    {
        if (grabbable == null) return false;
        float roll = Mathf.DeltaAngle(0f, grabbable.localEulerAngles.z);
        return Mathf.Abs(roll) <= levelTolerance;
    }
}
