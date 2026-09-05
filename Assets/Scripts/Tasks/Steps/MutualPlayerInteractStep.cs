using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

// ---------------------------------------------------
// 10. MUTUAL PLAYER INTERACT (Duels, Jousts)
// ---------------------------------------------------
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp", sourceClassName: null)]
[System.Serializable]
public class MutualPlayerInteractStep : TaskStep
{
    [Tooltip("What the player initiating the interaction must be holding.")]
    public ItemDefinition requiredItem;
    [Tooltip("State flags the initiator's item must carry. None = any state.")]
    public ItemState requiredState = ItemState.None;

    [Tooltip("What the TARGET player must be holding to allow the interaction.")]
    public ItemDefinition targetRequiredItem;
    [Tooltip("State flags the target's item must carry. None = any state.")]
    public ItemState targetRequiredState = ItemState.None;

    // [Obsolete] identity moved to the typed fields above.
    [FormerlySerializedAs("myRequiredItemName")]
    [Tooltip("[DEPRECATED] Old string identity. Assign 'Required Item' instead.")]
    public string legacyMyRequiredItemName;
    [FormerlySerializedAs("targetRequiredItemName")]
    [Tooltip("[DEPRECATED] Old string identity. Assign 'Target Required Item' instead.")]
    public string legacyTargetRequiredItemName;

    public override string GetObjectiveText()
    {
        string what = requiredItem != null ? requiredItem.displayName : legacyMyRequiredItemName;
        return $"Cross <color=#5DADE2>{what}</color>s with another armed court member!";
    }

    public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null)
    {
        if (targetInteractable == null) return false;

        PlayerController targetPlayer = targetInteractable.GetComponent<PlayerController>();
        if (targetPlayer == null) return false;

        if (requiredItem == null || targetRequiredItem == null) return false;

        // 1. Check my hands
        var myItem = player.GetHeldItem();
        if (myItem == null || !myItem.Matches(requiredItem, requiredState)) return false;

        // 2. Check their hands
        var theirItem = targetPlayer.GetHeldItem();
        if (theirItem == null || !theirItem.Matches(targetRequiredItem, targetRequiredState)) return false;

        return true;
    }

    public override string GetConfigurationWarning()
    {
        if (requiredItem == null && !string.IsNullOrEmpty(legacyMyRequiredItemName))
            return $"legacyMyRequiredItemName '{legacyMyRequiredItemName}' is set but Required Item is not - assign the ItemDefinition.";
        if (targetRequiredItem == null && !string.IsNullOrEmpty(legacyTargetRequiredItemName))
            return $"legacyTargetRequiredItemName '{legacyTargetRequiredItemName}' is set but Target Required Item is not - assign the ItemDefinition.";
        return null;
    }
}
