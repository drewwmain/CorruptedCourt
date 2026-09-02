using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TaskZone : MonoBehaviour
{
    // NEW: A global master list of all Task Zones in the scene
    public static List<TaskZone> AllZones = new List<TaskZone>();
    [Tooltip("This must match the targetLocationID in your TaskData ScriptableObjects exactly! (e.g., 'Armory')")]
    public string zoneID;

    private void Awake()
    {
        // Failsafe: Ensure the collider is set to trigger so it doesn't physically block player movement
        Collider coll = GetComponent<Collider>();
        if (coll != null) coll.isTrigger = true;
    }

    // NEW: Add to list when the game starts
    private void OnEnable()
    {
        if (!AllZones.Contains(this)) AllZones.Add(this);
    }

    // NEW: Remove from list if destroyed/disabled
    private void OnDisable()
    {
        if (AllZones.Contains(this)) AllZones.Remove(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"PHYSICS TEST: {other.gameObject.name} entered {zoneID}");
        // When something enters the zone, check if it's a player
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            // Update the player's internal tracking string
            player.currentZoneID = zoneID;
            Debug.Log($"{player.gameObject.name} entered zone: {zoneID}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // When something leaves, check if it's a player
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            // Only clear the zone if they are leaving THIS specific zone.
            // This prevents nasty bugs if you have two task zones placed slightly overlapping!
            if (player.currentZoneID == zoneID)
            {
                player.currentZoneID = "";
                // Debug.Log($"{player.gameObject.name} left zone: {zoneID}");
            }
        }
    }
}