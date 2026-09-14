using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 2. NAVIGATE TO ZONE
    // ---------------------------------------------------
    [System.Serializable]
    public class NavigateStep : TaskStep
    {
        [Tooltip("The ID of the room the player must enter.")]
        public string targetZoneID;

        public override string GetObjectiveText()
        {
            return $"Travel to the <color=#F4D03F>{targetZoneID}</color>";
        }

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
        {
            return player.Vitals.currentZoneID == targetZoneID;
        }

        public override ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime)
        {
            Transform t = FindLocationOrZone(targetZoneID);
            return t != null ? new ObjectiveTarget { Transform = t, IsDirect = true } : default;
        }
    }
}
