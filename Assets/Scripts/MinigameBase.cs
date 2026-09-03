using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root of every minigame. Owns the shared lifecycle and a global "is any minigame running"
/// registry. Subclasses pick a family (PanelMinigame / HandMinigame / PartnerMinigame / ...) rather
/// than extending this directly. See Assets/Scripts/Minigames/ARCHITECTURE.md.
/// </summary>
public abstract class MinigameBase : MonoBehaviour
{
    // --- global registry: the single source of truth for "the player is in a minigame" -----------
    private static readonly HashSet<MinigameBase> active = new HashSet<MinigameBase>();

    private static void Prune() { active.RemoveWhere(m => m == null); }

    /// <summary>Every minigame that has run SetupMinigame and not yet ended. Self-prunes destroyed entries.</summary>
    public static IReadOnlyCollection<MinigameBase> ActiveMinigames { get { Prune(); return active; } }

    /// <summary>True while any minigame is open. Replaces scattered isPlayingMinigame / hangReachActive checks.</summary>
    public static bool IsAnyActive { get { Prune(); return active.Count > 0; } }

    /// <summary>The most recently started still-open minigame, or null.</summary>
    public static MinigameBase Current
    {
        get { Prune(); foreach (MinigameBase m in active) return m; return null; }
    }

    // --- per-instance state ---------------------------------------------------------------------
    protected PlayerController player;
    protected TaskInstance activeTask;

    /// <summary>Richer launch payload. Null when launched via the legacy 2-arg SetupMinigame.</summary>
    public MinigameContext Context { get; protected set; }

    /// <summary>
    /// Legacy entry point: injects the player and task the moment the minigame spawns.
    /// </summary>
    public virtual void SetupMinigame(PlayerController playerRef, TaskInstance task)
    {
        player = playerRef;
        activeTask = task;
        active.Add(this);
        OnMinigameBegin();
    }

    /// <summary>
    /// Preferred entry point once a launcher builds a MinigameContext. Forwards to the legacy
    /// overload after stashing the context, so migration can be done file by file.
    /// </summary>
    public virtual void SetupMinigame(MinigameContext context)
    {
        Context = context;
        SetupMinigame(context != null ? context.Player : null,
                      context != null ? context.Task : null);
    }

    /// <summary>Call when the player successfully finishes the minigame's action.</summary>
    public virtual void CompleteMinigame()
    {
        active.Remove(this);
        OnMinigameEnd(true);

        if (player != null)
        {
            player.FinishMinigame(activeTask);
        }

        Destroy(gameObject);
    }

    /// <summary>Call if the player closes the minigame early or is interrupted (attacked, Esc).</summary>
    public virtual void CancelMinigame()
    {
        active.Remove(this);
        OnMinigameEnd(false);

        if (player != null)
        {
            player.CancelMinigame();
        }

        Destroy(gameObject);
    }

    // --- hooks for subclasses ------------------------------------------------------------------

    /// <summary>Runs right after player / task / context are injected. Override for setup.</summary>
    protected virtual void OnMinigameBegin() { }

    /// <summary>Runs as the minigame closes. <paramref name="won"/> = it reached its success state.</summary>
    protected virtual void OnMinigameEnd(bool won) { }
}
