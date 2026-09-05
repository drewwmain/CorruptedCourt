using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 11. GROUP NAVIGATE (Bedding Ceremony)
    // ---------------------------------------------------
    [System.Serializable]
    public class GroupNavigateStep : TaskStep
    {
        [Tooltip("The ID of the room the crowd must gather in.")]
        public string targetZoneID;

        [Tooltip("How many total players must be in the room to complete the step.")]
        public int requiredPlayerCount;

        public override string GetObjectiveText()
        {
            return $"Gather {requiredPlayerCount} members of the court in the <color=#F4D03F>{targetZoneID}</color>";
        }

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
        {
            if (player.Vitals.currentZoneID != targetZoneID) return false;

            int playersInRoom = 0;

            if (RoleManager.Instance != null)
            {
                foreach (PlayerController p in RoleManager.Instance.allPlayers)
                {
                    if (p == null) continue;

                    if (!p.Vitals.isGhost && p.Vitals.currentZoneID == targetZoneID)
                    {
                        playersInRoom++;
                    }
                }
            }

            return playersInRoom >= requiredPlayerCount;
        }
    }
}
