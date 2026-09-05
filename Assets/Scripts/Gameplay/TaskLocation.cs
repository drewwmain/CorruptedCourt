using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Items;
using CorruptedCourt.Tasks;
using CorruptedCourt.UI;

namespace CorruptedCourt.Gameplay
{
    public class TaskLocation : MonoBehaviour
    {
        [Tooltip("This MUST match the targetLocationID in your TaskData ScriptableObjects")]
        public string locationID;

        [Tooltip("For deposit stations: the typed identity this location accepts. Assigned by the " +
                 "'Corrupted Court/Migrate Item Definitions' tool. Pair it with TaskDepositStation." +
                 "requiredState when the station demands a processed / deposited-container item.")]
        public ItemDefinition acceptedItem;

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
}
