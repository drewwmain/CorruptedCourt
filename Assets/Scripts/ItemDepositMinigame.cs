using UnityEngine;

/// <summary>
/// Base for the "carry a held item to a deposit station and physically put it away" minigames
/// (SwordHangMinigame, ChestDepositMinigame, ...). TaskDepositStation.LaunchDepositMinigame spawns
/// the prefab, calls SetupMinigame, then BeginDeposit with the held item and the station.
/// </summary>
public abstract class ItemDepositMinigame : MinigameBase
{
    /// <summary>
    /// Hand the minigame the item the player is carrying and the station they interacted with.
    /// Called once, right after SetupMinigame.
    /// </summary>
    public abstract void BeginDeposit(PickupItem heldItem, TaskDepositStation station);
}
