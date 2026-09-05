using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Base for the "carry a held item to a deposit station and physically put it away" minigames
    /// (SwordHangMinigame, ChestDepositMinigame, ...). TaskDepositStation.LaunchDepositMinigame spawns
    /// the prefab, calls SetupMinigame, then BeginDeposit with the held item and the station.
    ///
    /// P2: re-parented onto HandMinigame, which now owns the freeze / RMB-look / footwork / settings-pause
    /// / MouseWorld / RestorePlayer plumbing the two deposit minigames used to duplicate. See ARCHITECTURE.md.
    /// </summary>
    public abstract class ItemDepositMinigame : HandMinigame
    {
        /// <summary>
        /// Hand the minigame the item the player is carrying and the station they interacted with.
        /// Called once, right after SetupMinigame.
        /// </summary>
        public abstract void BeginDeposit(PickupItem heldItem, TaskDepositStation station);
    }
}
