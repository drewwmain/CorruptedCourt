using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

// ---------------------------------------------------
// 2. NAVIGATE TO ZONE
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: null)]
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
}
