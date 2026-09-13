using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Minigames
{
    public class DummyMinigame : MinigameBase
    {
        // We will link this to a UI Button in the Inspector
        public void OnClickWinButton()
        {
            // Placeholder stand-in for a real minigame: if this was launched for a ProcessItemStep,
            // flag the held item Processed on win, same as a real minigame would - so a
            // ProcessItemStep-driven task (and whatever follows it, e.g. a DepositItemStep checking
            // for Processed) can be tested end to end before the real minigame exists. This prefab is
            // shared with other placeholder uses (e.g. flower_vase's deposit minigame) - the check
            // below is false for those, so they're unaffected.
            if (activeTask != null && activeTask.GetCurrentStep() is ProcessItemStep && player != null)
            {
                PickupItem held = player.GetHeldItem();
                if (held != null) held.ProcessItem();
            }

            Log.Game("Minigame Won! Sending signal back to Player...");
            CompleteMinigame(); // This calls the base method we wrote in Phase 2
        }
    }
}
