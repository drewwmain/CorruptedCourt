using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Tasks
{
    // ---------------------------------------------------
    // 9. EQUIP CLOTHING STEP
    // ---------------------------------------------------
    [System.Serializable]
    public class EquipClothingStep : TaskStep
    {
        // NOTE: clothing has no ItemDefinition today, so this is the one step still matching on a
        // GameObject name. Migrate it when clothing gets typed identities (out of scope for phase 3b).
        public string clothingName;

        public override string GetObjectiveText()
        {
            return $"Put on the <color=#5DADE2>{clothingName}</color>";
        }

        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
        {
            return targetInteractable != null && targetInteractable.name.Contains(clothingName);
        }
    }
}
