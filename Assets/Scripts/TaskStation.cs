using UnityEngine;

// This ensures you can't accidentally forget to add the TaskLocation script to the object!
[RequireComponent(typeof(TaskLocation))]
public class TaskStation : MonoBehaviour, IInteractable
{
    [Tooltip("If set, interacting with this station ALWAYS launches this minigame - any role, with " +
             "or without a related task (so Corrupted can fake it). Set it to the same prefab as the " +
             "task step's Minigame Prefab.")]
    public GameObject processMinigamePrefab;

    private TaskLocation taskLocation;

    void Awake()
    {
        taskLocation = GetComponent<TaskLocation>();
    }

    public string GetInteractionPrompt()
    {
        return "Press <color=#F4D03F>[E]</color> to Interact";
    }

    public void OnInteract(GameObject interactor)
    {
        // 1. WE NO LONGER CHECK TASKS HERE!
        // The PlayerController evaluates its own tasks immediately after calling this method.

        // 2. You can use this space purely for visual/audio effects that should happen 
        // every single time the station is used, regardless of who clicks it!
        Debug.Log($"{interactor.name} interacted with the {taskLocation.locationID} station.");
        
        // Example:
        // GetComponent<AudioSource>().Play();
        // particleSystem.Play();
    }
}