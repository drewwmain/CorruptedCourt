using System.Collections.Generic;
using UnityEngine;

namespace CorruptedCourt.Items
{
    /// <summary>
    /// A display-name override for one item state combination (e.g. "Polished Sword" when
    /// Processed is set). Authoring data - see ItemDefinition.variantNames / DisplayNameFor.
    /// </summary>
    [System.Serializable]
    public struct VariantName
    {
        [Tooltip("The state flags an item must carry (every one of them) for this variant name to apply.")]
        public ItemState whenState;

        [Tooltip("The display name to use when whenState is satisfied.")]
        public string displayName;
    }

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

        [Tooltip("Per-state display name overrides (e.g. \"Polished Sword\" when Processed is set). Nothing " +
                 "is renamed at runtime (R9) - this only changes what's shown.")]
        public List<VariantName> variantNames = new List<VariantName>();

        /// <summary>
        /// The display name for an item carrying <paramref name="state"/>. Among the variants whose
        /// whenState is fully satisfied by state, the one with the most flags set wins (most specific
        /// match); falls back to <see cref="displayName"/> when none match.
        /// </summary>
        public string DisplayNameFor(ItemState state)
        {
            string best = null;
            int bestFlagCount = -1;

            if (variantNames != null)
            {
                foreach (VariantName variant in variantNames)
                {
                    if (variant.whenState == ItemState.None) continue; // not a real variant condition
                    if ((state & variant.whenState) != variant.whenState) continue; // state must carry every flag this variant requires

                    int flagCount = CountFlags(variant.whenState);
                    if (flagCount > bestFlagCount)
                    {
                        bestFlagCount = flagCount;
                        best = variant.displayName;
                    }
                }
            }

            return best ?? displayName;
        }

        private static int CountFlags(ItemState state)
        {
            int value = (int)state;
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }
    }
}
