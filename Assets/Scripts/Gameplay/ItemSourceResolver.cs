using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    /// <summary>Where an objective points, and how directly. IsDirect = true means the identity itself
    /// exists in the world; false means this is a step up the production chain instead (ARCHITECTURE.md
    /// §9.3).</summary>
    public struct ObjectiveTarget
    {
        public Transform Transform;
        public string Hint;
        public bool IsDirect;
    }

    /// <summary>
    /// R2's answer (ARCHITECTURE.md §9.3): "Find and pick up: Polished Sword" is useless when none
    /// exists, so Resolve walks the recipe graph back to something that does exist - typically an
    /// infinite spawner - turning it into "Polish a sword at the Armory" instead.
    ///
    /// Singleton (not static): ExistsInWorld/Resolve need a curated RecipeIndex asset reference, and
    /// this codebase's existing pattern for that (TaskManager/MatchManager's Inspector-assigned
    /// MatchConfig field) is a serialized reference on a MonoBehaviour, not a static Resources.Load
    /// (no precedent for that anywhere in this project). Matches the RoleManager/TaskManager singleton
    /// pattern exactly (Awake sets Instance, else Destroy).
    /// </summary>
    public class ItemSourceResolver : MonoBehaviour
    {
        public static ItemSourceResolver Instance { get; private set; }

        [Tooltip("The production graph this resolver walks. Empty until B19a authors recipe assets.")]
        [SerializeField] private RecipeIndex recipeIndex;

        private const int MaxDepth = 3;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        /// <summary>True when an instance matching <paramref name="id"/> is sitting in the world (not
        /// held by a player). An infinite spawner counts - it's a registered PickupItem like any other.</summary>
        public bool ExistsInWorld(ItemIdentity id, out PickupItem found)
        {
            if (id.definition != null)
            {
                foreach (PickupItem item in PickupItem.AllItems)
                {
                    if (item != null && item.transform.parent == null && item.Matches(id))
                    {
                        found = item;
                        return true;
                    }
                }
            }

            found = null;
            return false;
        }

        /// <summary>Resolves an objective for <paramref name="id"/>: a direct world instance if one
        /// exists, otherwise a step up the production chain (depth-capped at 3, cycle-guarded),
        /// terminating naturally at an infinite spawner once ExistsInWorld finds it.</summary>
        public ObjectiveTarget Resolve(ItemIdentity id, Vector3 from)
        {
            return ResolveInternal(id, from, 0, new HashSet<ItemDefinition>());
        }

        private ObjectiveTarget ResolveInternal(ItemIdentity id, Vector3 from, int depth, HashSet<ItemDefinition> visited)
        {
            if (ExistsInWorld(id, out PickupItem found))
                return new ObjectiveTarget { Transform = found.transform, Hint = null, IsDirect = true };

            if (id.definition == null || depth >= MaxDepth || recipeIndex == null || !visited.Add(id.definition))
                return default;

            ItemRecipe recipe = recipeIndex.FindByOutput(id);
            if (recipe == null) return default;

            if (recipe.inputs != null)
            {
                foreach (ItemIdentity input in recipe.inputs)
                {
                    ObjectiveTarget target = ResolveInternal(input, from, depth + 1, visited);
                    if (target.Transform != null)
                        return new ObjectiveTarget { Transform = target.Transform, Hint = recipe.objectiveHint, IsDirect = false };
                }
            }

            // No input resolves to anything - point at the producer station itself, if it exists.
            TaskLocation producerLoc = FindLocationByID(recipe.producerLocationID);
            if (producerLoc != null)
                return new ObjectiveTarget { Transform = producerLoc.transform, Hint = recipe.objectiveHint, IsDirect = false };

            return default;
        }

        // Mirrors WaypointManager.FindLocationByID's existing lookup.
        private static TaskLocation FindLocationByID(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            foreach (TaskLocation loc in TaskLocation.AllLocations)
            {
                if (loc != null && loc.locationID == id) return loc;
            }

            return null;
        }
    }
}
