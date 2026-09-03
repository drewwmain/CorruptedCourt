using System.Collections.Generic;
using UnityEngine;

public class TaskLocation : MonoBehaviour
{
    [Tooltip("This MUST match the targetLocationID in your TaskData ScriptableObjects")]
    public string locationID;

    [Tooltip("For deposit stations: the typed identity this location accepts. Assigned by the " +
             "'Corrupted Court/Migrate Item Definitions' tool.")]
    public ItemDefinition acceptedItem;

    // [Obsolete] superseded by 'acceptedItem'. Kept until deposit matching is migrated off strings.
    // Note: any "Processed" / "Deposited" prefix that used to be baked into this string (e.g.
    // "ProcessedSword", "DepositedVase") is NOT represented on 'acceptedItem' yet - a required-state
    // field comes with the matching migration.
    [Tooltip("For deposit stations: the itemName this location accepts. Only PickupItems with this exact name may be deposited here.")]
    public string acceptedItemName = "";

    // A static master list of all locations in the map, so the WaypointManager can instantly find them
    public static List<TaskLocation> AllLocations = new List<TaskLocation>();

    // Registered while the object is active so a deactivated station (e.g. a role-switched Vase)
    // stops being treated as a live deposit location.
    void OnEnable()
    {
        if (!AllLocations.Contains(this)) AllLocations.Add(this);
    }

    void OnDisable()
    {
        AllLocations.Remove(this);
    }

    void OnDestroy()
    {
        AllLocations.Remove(this);
    }
}