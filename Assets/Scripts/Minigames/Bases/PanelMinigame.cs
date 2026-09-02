using System;
using UnityEngine;

/// <summary>
/// A minigame that is entirely a UI Canvas - buttons, drags, keyboard - with no world or hand
/// interaction. The launcher has already freed the cursor. Subclasses wire Unity UI events to
/// <see cref="Win"/> / <see cref="Lose"/> or use <see cref="RequireClicks"/> for "press N times".
///
/// Fits: DummyMinigame, the Book-of-names memorise-and-order puzzle, and the "just click a button"
/// fallback flavour of any physical minigame (handy for AI / fakers).
/// </summary>
public abstract class PanelMinigame : MinigameBase
{
    private int clicksNeeded;
    private int clicks;
    private Action onClicksDone;
    private bool finished;

    /// <summary>Wire a repeated button to this: fires <paramref name="onDone"/> after <paramref name="count"/> presses.</summary>
    protected void RequireClicks(int count, Action onDone)
    {
        clicksNeeded = Mathf.Max(1, count);
        clicks = 0;
        onClicksDone = onDone;
    }

    /// <summary>Hook a repeated UI button here when using <see cref="RequireClicks"/>.</summary>
    public void RegisterClick()
    {
        if (finished) return;
        clicks++;
        if (clicks >= clicksNeeded)
        {
            onClicksDone?.Invoke();
        }
    }

    /// <summary>Hook a single "done" button here, or call from subclass logic.</summary>
    public void Win()
    {
        if (finished) return;
        finished = true;
        CompleteMinigame();
    }

    /// <summary>Abandon the panel (e.g. a Close button).</summary>
    public void Lose()
    {
        if (finished) return;
        finished = true;
        CancelMinigame();
    }
}
