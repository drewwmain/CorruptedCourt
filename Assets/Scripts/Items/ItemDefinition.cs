using UnityEngine;

/// <summary>
/// Stable identity for one type of pickup item. Replaces the string <c>PickupItem.itemName</c> as
/// the thing matching is done against. Authoring asset - holds no runtime state.
/// </summary>
[CreateAssetMenu(fileName = "New Item Definition", menuName = "Corrupted Court/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    [Tooltip("Stable string id for this item type (e.g. \"Sword\", \"Flowers\"). Matching is by asset " +
             "reference; this string is a fallback / debug aid and for tooling.")]
    public string itemID;

    [Tooltip("Human-readable name shown in the UI.")]
    public string displayName;
}
