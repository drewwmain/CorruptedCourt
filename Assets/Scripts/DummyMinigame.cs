using UnityEngine;

public class DummyMinigame : MinigameBase
{
    // We will link this to a UI Button in the Inspector
    public void OnClickWinButton()
    {
        Debug.Log("Minigame Won! Sending signal back to Player...");
        CompleteMinigame(); // This calls the base method we wrote in Phase 2
    }
}