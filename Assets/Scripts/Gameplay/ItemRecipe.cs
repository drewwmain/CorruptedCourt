using UnityEngine;
using CorruptedCourt.Items;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Gameplay
{
    /// <summary>How a recipe's output actually gets produced - documentation for whichever minigame
    /// does the producing (a recipe is a lookup table, not an execution engine).</summary>
    public enum ProductionKind { FlagHeldItem, SpawnAtStation, DepositIntoContainer }

    /// <summary>
    /// One entry in R2's production graph (ARCHITECTURE.md §6.5): what identity this produces, from
    /// what input identity/identities, and where. A lookup table for ItemSourceResolver - minigames
    /// still do the actual producing. Lives in Gameplay (not Items) because producingTask is TaskData,
    /// a Gameplay-assembly type.
    /// </summary>
    [CreateAssetMenu(fileName = "New Item Recipe", menuName = "Corrupted Court/Item Recipe")]
    public class ItemRecipe : ScriptableObject
    {
        [Tooltip("The identity this recipe produces.")]
        public ItemIdentity output;

        [Tooltip("The identity/identities consumed to produce the output.")]
        public ItemIdentity[] inputs;

        [Tooltip("locationID of the station where this production happens.")]
        public string producerLocationID;

        public ProductionKind kind;

        [Tooltip("Documentation only - which task this production step belongs to.")]
        public TaskData producingTask;

        [TextArea]
        [Tooltip("Player-facing hint, e.g. \"Polish a sword at the Armory\".")]
        public string objectiveHint;
    }
}
