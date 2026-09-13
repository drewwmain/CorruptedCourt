using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// A curated, queryable list of ItemRecipes (ARCHITECTURE.md §6.5) - mirrors ItemDefinitionSet's
    /// shape (B3), the closest existing precedent for "a ScriptableObject that's just a curated list."
    /// </summary>
    [CreateAssetMenu(fileName = "RecipeIndex", menuName = "Corrupted Court/Recipe Index")]
    public class RecipeIndex : ScriptableObject
    {
        public List<ItemRecipe> recipes = new List<ItemRecipe>();

        /// <summary>The recipe whose output matches <paramref name="id"/> (every flag in id.state must
        /// be present in the recipe's output state), or null if none does.</summary>
        public ItemRecipe FindByOutput(ItemIdentity id)
        {
            if (id.definition == null || recipes == null) return null;

            foreach (ItemRecipe recipe in recipes)
            {
                if (recipe == null) continue;
                if (recipe.output.definition == id.definition && (recipe.output.state & id.state) == id.state)
                    return recipe;
            }

            return null;
        }
    }
}
