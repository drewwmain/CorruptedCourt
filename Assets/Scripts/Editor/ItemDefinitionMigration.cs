using UnityEditor;
using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.EditorTools
{
    /// <summary>
    /// OBSOLETE (phase 3a). This one-shot tool populated <see cref="PickupItem.definition"/> /
    /// <see cref="PickupItem.state"/> and <c>TaskLocation.acceptedItem</c> from the old
    /// <c>itemName</c> / <c>acceptedItemName</c> strings. Phase 3b deleted those strings and switched
    /// all matching to the typed references, so the tool has nothing left to read.
    ///
    /// Kept as a stub (menu item preserved) so any docs / muscle memory pointing at it still resolve.
    /// Item authoring is now: assign an <see cref="ItemDefinition"/> on each PickupItem prefab and on
    /// each deposit station's TaskLocation. For TaskData steps, use
    /// "Corrupted Court/Migrate Task Step Definitions".
    /// </summary>
    public static class ItemDefinitionMigration
    {
        [MenuItem("Corrupted Court/Migrate Item Definitions")]
        public static void Migrate()
        {
            Debug.Log("[ItemDefinitionMigration] Obsolete since phase 3b - item identity is the typed " +
                      "ItemDefinition + ItemState now, and the legacy strings this tool read are gone. " +
                      "Nothing to do. For TaskData steps, run 'Corrupted Court/Migrate Task Step Definitions'.");
        }
    }
}
