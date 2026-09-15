using System.Collections.Generic;
using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Indexes every active <see cref="TaskStationState"/> by its <see cref="TaskLocation.locationID"/>
    /// (ARCHITECTURE.md §8.3). Populated via Register/Unregister from TaskStationState's own
    /// OnEnable/OnDisable/OnDestroy, mirroring TaskLocation.AllLocations's self-registering lifecycle -
    /// just indexed by id instead of flat, since callers query "every station at this location," not
    /// "every station in the map."
    /// </summary>
    public static class StationRegistry
    {
        private static readonly Dictionary<string, List<TaskStationState>> byLocationID = new Dictionary<string, List<TaskStationState>>();
        private static readonly List<TaskStationState> empty = new List<TaskStationState>();

        public static void Register(TaskStationState station)
        {
            if (station == null || string.IsNullOrEmpty(station.LocationID)) return;

            if (!byLocationID.TryGetValue(station.LocationID, out List<TaskStationState> list))
            {
                list = new List<TaskStationState>();
                byLocationID[station.LocationID] = list;
            }
            if (!list.Contains(station)) list.Add(station);
        }

        public static void Unregister(TaskStationState station)
        {
            if (station == null || string.IsNullOrEmpty(station.LocationID)) return;
            if (byLocationID.TryGetValue(station.LocationID, out List<TaskStationState> list)) list.Remove(station);
        }

        /// <summary>Every registered station at <paramref name="locationID"/>. Never null.</summary>
        public static IReadOnlyList<TaskStationState> ByLocation(string locationID)
        {
            if (string.IsNullOrEmpty(locationID)) return empty;
            return byLocationID.TryGetValue(locationID, out List<TaskStationState> list) ? list : empty;
        }

        /// <summary>Every distinct locationID currently registered. Debug/tooling only
        /// (StationDebugPanel) - normal gameplay code already has its locationID in hand and should use
        /// ByLocation/FindNearestPending/PendingCount instead.</summary>
        public static IEnumerable<string> AllLocationIDs => byLocationID.Keys;

        /// <summary>The nearest Pending station at <paramref name="locationID"/> to <paramref name="from"/>,
        /// or null if none are pending - the honest waypoint target under R4 (never points at an
        /// already-satisfied instance when another is available).</summary>
        public static TaskStationState FindNearestPending(string locationID, Vector3 from)
        {
            IReadOnlyList<TaskStationState> list = ByLocation(locationID);
            TaskStationState nearest = null;
            float nearestDistSq = float.MaxValue;

            for (int i = 0; i < list.Count; i++)
            {
                TaskStationState s = list[i];
                if (s == null || s.Current != TaskStationState.Condition.Pending) continue;

                float distSq = (s.transform.position - from).sqrMagnitude;
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = s;
                }
            }

            return nearest;
        }

        /// <summary>How many stations at <paramref name="locationID"/> are currently Pending. Zero
        /// means every task needing that station is stalled until someone reverses one (R4).</summary>
        public static int PendingCount(string locationID)
        {
            IReadOnlyList<TaskStationState> list = ByLocation(locationID);
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].Current == TaskStationState.Condition.Pending) count++;
            }
            return count;
        }
    }
}
