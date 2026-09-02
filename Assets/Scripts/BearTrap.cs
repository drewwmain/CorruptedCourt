using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BearTrap : MonoBehaviour
{
    [Tooltip("How long the player is frozen for.")]
    public float stunDuration = 5f;

    void Awake()
    {
        // Failsafe: Ensure the collider is a trigger so players don't physically bump into it
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponent<PlayerController>();

        if (player != null)
        {
            // Only trigger if the player is alive AND they are NOT a Corrupted!
            if (!player.isGhost && player.currentRole != PlayerRole.Corrupted)
            {
                player.ApplyStun(stunDuration);
                
                // Snap the trap shut, play a sound, and destroy it
                Debug.Log($"Trap sprung on {player.gameObject.name}!");
                Destroy(gameObject);
            }
        }
    }
}