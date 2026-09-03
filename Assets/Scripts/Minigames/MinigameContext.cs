using UnityEngine;

/// <summary>
/// Everything a minigame needs to know about why it was launched, in one payload. Built by the
/// launcher (PlayerController.StartMinigame or TaskDepositStation.LaunchDepositMinigame) and handed
/// to MinigameBase.SetupMinigame(context). Replaces the scattered activeMinigame* fields on
/// PlayerController. See Assets/Scripts/Minigames/ARCHITECTURE.md.
/// </summary>
public class MinigameContext
{
    /// <summary>The player running the minigame.</summary>
    public PlayerController Player;

    /// <summary>The task whose current step this minigame satisfies. Null = faked / standalone.</summary>
    public TaskInstance Task;

    /// <summary>Station / Item / Player / None - resolved by the launcher.</summary>
    public MinigameTargetType TargetType = MinigameTargetType.None;

    /// <summary>The station, world prop, held item, or other player this minigame acts on.</summary>
    public GameObject Target;

    /// <summary>The item the player carried in (deposits, tool-use, consume). May be null.</summary>
    public PickupItem HeldItem;

    /// <summary>Convenience: <see cref="Target"/> as a deposit station, if it is one.</summary>
    public TaskDepositStation Station;

    /// <summary>Convenience: <see cref="Target"/> as another player, if it is one.</summary>
    public PlayerController PartnerPlayer;

    public MinigameContext() { }

    public MinigameContext(PlayerController player, TaskInstance task)
    {
        Player = player;
        Task = task;
    }

    /// <summary>Fills the convenience casts from <see cref="Target"/>. Call after setting Target.</summary>
    public MinigameContext Resolve()
    {
        if (Target != null)
        {
            if (Station == null) Station = Target.GetComponent<TaskDepositStation>();
            if (PartnerPlayer == null) PartnerPlayer = Target.GetComponent<PlayerController>();
        }
        return this;
    }
}
