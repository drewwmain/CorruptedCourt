using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PowerUpPickup : MonoBehaviour, IInteractable
{
    public PowerUpData powerUpData;

    public string GetInteractionPrompt()
    {
        // This is a fallback. The PlayerController will actually override this 
        // dynamically so Corrupted players see the real name!
        return "Press <color=#F4D03F>[E]</color> to examine strange object";
    }

    public void OnInteract(GameObject interactor)
    {
        // We leave this blank because we are handling the strict role-check 
        // directly inside the PlayerController's interaction flow.
    }
}