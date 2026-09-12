using System.Collections.Generic;
using UnityEngine;

namespace CorruptedCourt.Items
{
    /// <summary>
    /// A named group of interchangeable item identities (e.g. Instrument = {Guitar, Trumpet,
    /// Flute}). Authoring asset - holds no runtime state. Consumed by
    /// AcquireItemStep.requiredItemSet and, later, EmoteDefinition.requiresHeldItem.
    /// </summary>
    [CreateAssetMenu(fileName = "New Item Definition Set", menuName = "Corrupted Court/Item Definition Set")]
    public class ItemDefinitionSet : ScriptableObject
    {
        public List<ItemDefinition> members = new List<ItemDefinition>();

        public bool Contains(ItemDefinition def)
        {
            if (def == null || members == null) return false;

            foreach (ItemDefinition member in members)
            {
                if (member != null && member == def) return true;
            }

            return false;
        }
    }
}
