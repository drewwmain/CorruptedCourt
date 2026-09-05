using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 3. TASK STATION INTERACTION (Standard & Group)
    // ---------------------------------------------------
    [System.Serializable]
    public class StationInteractStep : TaskStep
    {
        [Tooltip("The locationID of the station to interact with (exact match against TaskLocation.locationID).")]
        public string targetStationID;

        [Tooltip("Set to > 1 if multiple players must interact simultaneously.")]
        public int requiredSimultaneousPlayers = 1;

        // Shared scratch buffer for the group-proximity check. CheckCompletion runs on the main thread
        // and consumes the hits immediately, so one static buffer is safe. 32 is comfortably above any
        // realistic count of colliders on the character layer inside one interaction radius.
        private static readonly Collider[] proximityBuffer = new Collider[32];

        public override string GetObjectiveText()
        {
            if (requiredSimultaneousPlayers > 1)
                return $"Gather {requiredSimultaneousPlayers} players at the <color=#F4D03F>{targetStationID}</color>";

            return $"Interact with the <color=#F4D03F>{targetStationID}</color>";
        }

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
        {
            if (targetInteractable == null) return false;

            // The object we interacted with must BE (or sit under) the target station.
            TaskLocation loc = ResolveLocation(targetInteractable);
            if (loc == null || loc.locationID != targetStationID) return false;

            // If it's a group task, check proximity of other players.
            if (requiredSimultaneousPlayers > 1)
            {
                int playersNearby = 0;
                int hitCount = Physics.OverlapSphereNonAlloc(
                    targetInteractable.transform.position, player.Interactor.InteractionRange, proximityBuffer, player.Interactor.CharacterLayer);

                // Buffer full: OverlapSphereNonAlloc silently drops the rest, so the count below could be
                // low. It can only ever UNDER-count, so a full buffer that already meets the requirement
                // is still a valid pass; only warn when it might have cost us the completion.
                if (hitCount == proximityBuffer.Length)
                    Log.Warn($"[StationInteractStep] proximity buffer full ({proximityBuffer.Length}) at " +
                             $"'{targetStationID}' - nearby-player count may be truncated.");

                for (int i = 0; i < hitCount; i++)
                {
                    if (proximityBuffer[i] != null && proximityBuffer[i].GetComponent<PlayerController>() != null)
                        playersNearby++;
                }

                return playersNearby >= requiredSimultaneousPlayers;
            }

            return true; // Standard single-player interaction successful
        }
    }
}
