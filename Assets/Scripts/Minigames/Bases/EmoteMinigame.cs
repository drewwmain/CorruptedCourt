using UnityEngine;

/// <summary>
/// A minigame completed by committing an emote from a required <see cref="EmoteCategory"/> on the
/// wheel. No partner, no hand rig.
///
/// Fits: Speech at the podium (category <see cref="EmoteCategory.Speech"/>). The partnered
/// emote games (Dance, Conversation) extend <see cref="PartnerMinigame"/> and compose the wheel
/// instead, because they also need proximity / alternation checks.
/// </summary>
public abstract class EmoteMinigame : MinigameBase
{
    [Header("Emote")]
    [Tooltip("The wheel is filtered to this category; picking any option from it completes the minigame.")]
    public EmoteCategory requiredCategory = EmoteCategory.Speech;

    protected override void OnMinigameBegin()
    {
        if (EmoteWheelController.Instance == null)
        {
            Debug.LogWarning($"[{GetType().Name}] no EmoteWheelController in the scene - cancelling.");
            CancelMinigame();
            return;
        }

        EmoteWheelController.Instance.Open(player, requiredCategory, OnEmoteCommitted);
    }

    private void OnEmoteCommitted(EmoteDefinition choice)
    {
        if (choice != null && choice.category == requiredCategory)
        {
            OnEmoteAccepted(choice);
            CompleteMinigame();
        }
        else
        {
            CancelMinigame();
        }
    }

    /// <summary>Hook for subclasses (play a crowd reaction, log the speech line, ...).</summary>
    protected virtual void OnEmoteAccepted(EmoteDefinition choice) { }
}
