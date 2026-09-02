using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Radial "hold a key, mouse to a slice, release to commit" emote picker. A persistent service
/// (one in the scene / on the player HUD), used both by roleplay gameplay and by the emote-driven
/// minigames (Speech at the podium, Dance, Conversation).
///
/// Scaffolding: the open/commit API and the catalogue are here; the actual wheel UI (slices,
/// hover-by-angle, categories tab) is built at P4. <see cref="Open"/> currently just exposes the
/// filtered options and waits for <see cref="Commit"/> / <see cref="Cancel"/> to be driven by that UI.
/// </summary>
public class EmoteWheelController : MonoBehaviour
{
    public static EmoteWheelController Instance { get; private set; }

    [Tooltip("Every emote the wheel can offer. Filtered by category when a minigame requests one.")]
    public List<EmoteDefinition> catalogue = new List<EmoteDefinition>();

    [Tooltip("Hold this to open the wheel during normal gameplay.")]
    public KeyCode openKey = KeyCode.B;

    public bool IsOpen { get; private set; }
    public EmoteCategory? ActiveFilter { get; private set; }

    private Action<EmoteDefinition> onCommit;
    private PlayerController performer;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Options currently offered (respects <see cref="ActiveFilter"/>).</summary>
    public IEnumerable<EmoteDefinition> CurrentOptions
    {
        get
        {
            foreach (EmoteDefinition e in catalogue)
                if (e != null && (ActiveFilter == null || e.category == ActiveFilter.Value))
                    yield return e;
        }
    }

    /// <summary>
    /// Open the wheel. <paramref name="filter"/> restricts it to one category (minigames pass their
    /// required one). <paramref name="onCommit"/> fires with the chosen emote, or null if cancelled.
    /// </summary>
    public void Open(PlayerController player, EmoteCategory? filter, Action<EmoteDefinition> onCommit)
    {
        performer = player;
        ActiveFilter = filter;
        this.onCommit = onCommit;
        IsOpen = true;
        // TODO(P4): show the radial UI, populate from CurrentOptions.
    }

    /// <summary>Called by the wheel UI when the player releases on a slice.</summary>
    public void Commit(EmoteDefinition choice)
    {
        if (!IsOpen) return;
        IsOpen = false;
        ActiveFilter = null;

        if (choice != null && performer != null && !string.IsNullOrEmpty(choice.animTrigger))
        {
            Animator a = performer.GetComponentInChildren<Animator>();
            if (a != null) a.SetTrigger(choice.animTrigger);
        }

        Action<EmoteDefinition> cb = onCommit;
        onCommit = null;
        cb?.Invoke(choice);
    }

    /// <summary>Called by the wheel UI when the player releases off any slice.</summary>
    public void Cancel()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ActiveFilter = null;
        Action<EmoteDefinition> cb = onCommit;
        onCommit = null;
        cb?.Invoke(null);
    }
}
