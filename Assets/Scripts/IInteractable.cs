using UnityEngine;

public interface IInteractable
{
    // Called when the player presses the interact button
    void OnInteract(GameObject interactor);

    // Useful for updating UI crosshairs (e.g., "Press E to Fix Wiring")
    string GetInteractionPrompt(); 
}