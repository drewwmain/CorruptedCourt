using UnityEngine;

public enum PowerUpType
{
    // Core Arsenal
    TargetedSabotage,
    Traps,
    Daggers,
    InvisibilityPotion,
    
    // Advanced Intrigue
    SpymastersLedger,
    StolenHeraldry,
    AlchemistsBlindingAsh,
    FoolsIllusion
}

[CreateAssetMenu(fileName = "New Power Up", menuName = "Social Deduction/Power Up Data")]
public class PowerUpData : ScriptableObject
{
    [Header("Basic Info")]
    public string powerUpName;
    [TextArea] public string description;
    
    [Tooltip("The 3D Synty Prefab used for the UI Icon")]
    public GameObject iconPrefab;

    [Header("Mechanics")]
    public PowerUpType powerUpType;
}