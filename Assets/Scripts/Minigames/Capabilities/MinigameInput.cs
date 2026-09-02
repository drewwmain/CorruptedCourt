using UnityEngine;

/// <summary>
/// One input surface for every minigame, so they don't each poke <c>Input.GetMouseButton…</c>
/// directly and so there's a single place to gate input while the settings menu is up or to swap in
/// the new Input System later.
///
/// Scaffolding: the common reads are implemented; the specialised gesture reads
/// (<see cref="DrawPull"/>, <see cref="MouseStrum"/>, <see cref="MouseSwing"/>) return sensible
/// values now and can be tuned per minigame when those are built.
/// </summary>
public static class MinigameInput
{
    /// <summary>True while the settings / pause menu is open - callers should ignore input this frame.</summary>
    public static bool Suppressed =>
        UIManager.Instance != null && UIManager.Instance.IsSettingsOpen;

    // --- primary (left mouse) --------------------------------------------------------------------
    public static bool PrimaryDown => !Suppressed && Input.GetMouseButtonDown(0);
    public static bool Primary     => !Suppressed && Input.GetMouseButton(0);
    public static bool PrimaryUp   => !Suppressed && Input.GetMouseButtonUp(0);

    // --- secondary (right mouse) - hold to look around ------------------------------------------
    public static bool SecondaryDown => !Suppressed && Input.GetMouseButtonDown(1);
    public static bool SecondaryHeld => !Suppressed && Input.GetMouseButton(1);
    public static bool SecondaryUp   => !Suppressed && Input.GetMouseButtonUp(1);

    // --- pointer -------------------------------------------------------------------------------
    public static Vector3 MouseScreenPosition => Input.mousePosition;

    /// <summary>Per-frame mouse delta from the legacy "Mouse X/Y" axes.</summary>
    public static Vector2 MouseDelta =>
        Suppressed ? Vector2.zero : new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

    // --- WASD shuffle -----------------------------------------------------------------------------
    /// <summary>x = strafe (A/D), y = forward (S/W). Raw, unnormalised.</summary>
    public static Vector2 MoveAxis
    {
        get
        {
            if (Suppressed) return Vector2.zero;
            return new Vector2(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
        }
    }

    // --- specialised gesture reads (instruments / bow / duel) ----------------------------------

    /// <summary>The A/S/D/F/G note keys pressed this frame, as a 5-bit mask (bit0 = A … bit4 = G).</summary>
    public static int NoteKeysDown()
    {
        if (Suppressed) return 0;
        int mask = 0;
        if (Input.GetKeyDown(KeyCode.A)) mask |= 1 << 0;
        if (Input.GetKeyDown(KeyCode.S)) mask |= 1 << 1;
        if (Input.GetKeyDown(KeyCode.D)) mask |= 1 << 2;
        if (Input.GetKeyDown(KeyCode.F)) mask |= 1 << 3;
        if (Input.GetKeyDown(KeyCode.G)) mask |= 1 << 4;
        return mask;
    }

    /// <summary>Backward mouse-Y travel this frame (bow draw). 0 when pushing forward.</summary>
    public static float DrawPull() => Mathf.Max(0f, -MouseDelta.y);

    /// <summary>True on a fast vertical mouse flick (guitar strum). Threshold in axis units / frame.</summary>
    public static bool MouseStrum(float threshold = 3f) => Mathf.Abs(MouseDelta.y) >= threshold;

    /// <summary>Screen-space mouse velocity in px/sec (duel swing). Magnitude scales with swing power.</summary>
    public static Vector2 MouseSwing()
    {
        if (Suppressed || Time.deltaTime <= 0f) return Vector2.zero;
        return MouseDelta / Time.deltaTime;
    }
}
