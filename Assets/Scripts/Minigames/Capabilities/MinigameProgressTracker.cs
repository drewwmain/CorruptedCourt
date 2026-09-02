using System;
using UnityEngine;

/// <summary>
/// Generic 0 → 1 progress with a completion threshold, so minigames don't each hand-roll a
/// <c>cuts</c> / <c>bites</c> / wiped-area / sparks / ink counter.
///
/// Modes:
///  - <see cref="Count"/>       : N discrete successes (cake cuts, bites, sparks landed).
///  - <see cref="Accumulate"/>  : add a per-tick amount (pour fill, wipe coverage, draw charge).
///  - <see cref="Zones"/>       : several sub-targets that must each be individually satisfied
///                                (polish - every dirty patch wiped; multi-notch checks).
///
/// Not a MonoBehaviour - a minigame owns one as a field.
/// </summary>
[Serializable]
public class MinigameProgressTracker
{
    public enum Mode { Count, Accumulate, Zones }

    [SerializeField] private Mode mode = Mode.Count;
    [SerializeField] private int countRequired = 3;
    [SerializeField] private float accumulateTarget = 1f;

    private float value;                 // Count: successes; Accumulate: running total
    private bool[] zoneDone;             // Zones: one flag per sub-target
    private bool completed;

    /// <summary>Fires once when progress first reaches 100%.</summary>
    public event Action Completed;

    public float Progress01
    {
        get
        {
            switch (mode)
            {
                case Mode.Count:      return countRequired <= 0 ? 1f : Mathf.Clamp01(value / countRequired);
                case Mode.Accumulate: return accumulateTarget <= 0f ? 1f : Mathf.Clamp01(value / accumulateTarget);
                case Mode.Zones:      return ZoneProgress();
                default:              return 0f;
            }
        }
    }

    public bool IsComplete => completed || Progress01 >= 1f;

    // --- Count mode ---------------------------------------------------------------------------
    public void ConfigureCount(int required) { mode = Mode.Count; countRequired = Mathf.Max(1, required); Reset(); }
    public void Tick(int amount = 1) { if (mode == Mode.Count) { value += amount; CheckDone(); } }

    // --- Accumulate mode ---------------------------------------------------------------------
    public void ConfigureAccumulate(float target) { mode = Mode.Accumulate; accumulateTarget = Mathf.Max(0.0001f, target); Reset(); }
    public void Add(float amount) { if (mode == Mode.Accumulate) { value += amount; CheckDone(); } }

    // --- Zones mode ------------------------------------------------------------------------------
    public void ConfigureZones(int zoneCount) { mode = Mode.Zones; zoneDone = new bool[Mathf.Max(1, zoneCount)]; completed = false; }
    public void MarkZone(int index)
    {
        if (mode != Mode.Zones || zoneDone == null || index < 0 || index >= zoneDone.Length) return;
        zoneDone[index] = true;
        CheckDone();
    }

    public void Reset()
    {
        value = 0f;
        completed = false;
        if (zoneDone != null) Array.Clear(zoneDone, 0, zoneDone.Length);
    }

    private float ZoneProgress()
    {
        if (zoneDone == null || zoneDone.Length == 0) return 0f;
        int done = 0;
        for (int i = 0; i < zoneDone.Length; i++) if (zoneDone[i]) done++;
        return (float)done / zoneDone.Length;
    }

    private void CheckDone()
    {
        if (completed || Progress01 < 1f) return;
        completed = true;
        Completed?.Invoke();
    }
}
