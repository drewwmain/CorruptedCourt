using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Minigames;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.EditorTools
{
    /// <summary>
    /// Walks every TaskData asset and reports authoring mistakes (Tasks/ARCHITECTURE.md §IV.3) before
    /// they're discovered at runtime. Report-only - never edits assets or the scene.
    ///
    /// Scene-matching checks (station/zone existence, TaskDepositStation presence, reversibility) only
    /// see whatever scene is currently open: TaskLocation/TaskZone/TaskStationState have no
    /// [ExecuteAlways], so their self-registering OnEnable never runs outside Play Mode, and
    /// Object.FindObjectsByType only finds what's actually loaded right now. Run this with the
    /// relevant scene open.
    /// </summary>
    public static class TaskDataValidator
    {
        // Stray formatting characters that have actually broken string-matching in this project before
        // (TaskStepDefinitionMigration.cs found U+2060 word-joiners baked into authored ID strings) -
        // plus the other common zero-width/invisible Unicode characters.
        private static readonly char[] InvisibleChars =
        {
            (char)0x200B, // zero-width space
            (char)0x200C, // zero-width non-joiner
            (char)0x200D, // zero-width joiner
            (char)0x2060, // word joiner - the exact character TaskStepDefinitionMigration.cs found baked in
            (char)0xFEFF, // BOM / zero-width no-break space
        };

        private const int MaxRecipeDepth = 3;

        [MenuItem("Corrupted Court/Validate Task Data")]
        public static void Run()
        {
            int issueCount = 0;

            List<TaskData> allTasks = LoadAllAssets<TaskData>("t:TaskData");
            TaskLocation[] sceneLocations = Object.FindObjectsByType<TaskLocation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            TaskZone[] sceneZones = Object.FindObjectsByType<TaskZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            TaskStationState[] sceneStationStates = Object.FindObjectsByType<TaskStationState>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            List<EmoteDefinition> emotes = LoadAllAssets<EmoteDefinition>("t:EmoteDefinition");

            foreach (TaskData task in allTasks)
            {
                issueCount += ValidateTask(task, sceneLocations, sceneZones, emotes);
            }

            issueCount += ValidateEmoteDefinitions(emotes);

            List<RecipeIndex> recipeIndexes = LoadAllAssets<RecipeIndex>("t:RecipeIndex");
            issueCount += ValidateRecipes(recipeIndexes);

            ReportStationReversibility(sceneStationStates); // informational only, not counted as issues

            Debug.Log($"[TaskDataValidator] Checked {allTasks.Count} TaskData asset(s), {emotes.Count} " +
                      $"EmoteDefinition asset(s), {CountRecipes(recipeIndexes)} recipe(s); {issueCount} " +
                      "issue(s) reported. Scene-matching checks only see the currently open scene.");
        }

        private static int ValidateTask(TaskData task, TaskLocation[] sceneLocations, TaskZone[] sceneZones, List<EmoteDefinition> emotes)
        {
            int issues = 0;

            if (string.IsNullOrEmpty(task.taskID))
            {
                LogIssue(task, "taskID is empty.");
                issues++;
            }
            else if (HasInvisibleChar(task.taskID))
            {
                LogIssue(task, $"taskID '{task.taskID}' contains an invisible/non-printable character.");
                issues++;
            }

            if (task.stepTemplates == null) return issues;

            foreach (TaskStep step in task.stepTemplates)
            {
                if (step == null) continue; // TaskData.OnValidate already flags an empty slot

                // Reuses each step's own GetConfigurationWarning() - already covers "item identity with
                // no ItemDefinition" per step type, so this validator doesn't re-derive that check.
                string warning = step.GetConfigurationWarning();
                if (!string.IsNullOrEmpty(warning))
                {
                    LogIssue(task, $"{step.GetType().Name}: {warning}");
                    issues++;
                }

                issues += ValidateStepLocations(task, step, sceneLocations, sceneZones, emotes);
            }

            return issues;
        }

        // Cross-references every station/zone ID field the live step types actually have against the
        // open scene, the invisible-character check on each ID string, and the DepositItemStep-specific
        // "does it actually have a TaskDepositStation" check.
        private static int ValidateStepLocations(TaskData task, TaskStep step, TaskLocation[] sceneLocations,
            TaskZone[] sceneZones, List<EmoteDefinition> emotes)
        {
            int issues = 0;

            switch (step)
            {
                case DepositItemStep deposit:
                    issues += CheckLocationID(task, step, deposit.targetStationID, sceneLocations, sceneZones, requireDepositStation: true);
                    break;

                case StationInteractStep station:
                    issues += CheckLocationID(task, step, station.targetStationID, sceneLocations, sceneZones, requireDepositStation: false);
                    break;

                case NavigateStep navigate:
                    issues += CheckLocationID(task, step, navigate.targetZoneID, sceneLocations, sceneZones, requireDepositStation: false);
                    break;

                case ProcessItemStep process:
                    if (!string.IsNullOrEmpty(process.targetStationID))
                        issues += CheckLocationID(task, step, process.targetStationID, sceneLocations, sceneZones, requireDepositStation: false);
                    break;

                case EmoteStep emote:
                    if (!string.IsNullOrEmpty(emote.requiredZoneID))
                        issues += CheckLocationID(task, step, emote.requiredZoneID, sceneLocations, sceneZones, requireDepositStation: false);
                    issues += CheckEmoteHasAssets(task, emote, emotes);
                    break;

                case OrderRecallStep order:
                    issues += CheckLocationID(task, step, order.sourceStationID, sceneLocations, sceneZones, requireDepositStation: false);
                    issues += CheckLocationID(task, step, order.inputStationID, sceneLocations, sceneZones, requireDepositStation: false);
                    break;
            }

            return issues;
        }

        private static int CheckLocationID(TaskData task, TaskStep step, string id, TaskLocation[] sceneLocations,
            TaskZone[] sceneZones, bool requireDepositStation)
        {
            if (string.IsNullOrEmpty(id)) return 0; // empty here is either optional or already flagged above

            int issues = 0;

            if (HasInvisibleChar(id))
            {
                LogIssue(task, $"{step.GetType().Name}: location/zone ID '{id}' contains an invisible/non-printable character.");
                issues++;
            }

            TaskLocation matchedLocation = null;
            foreach (TaskLocation loc in sceneLocations)
            {
                if (loc != null && loc.locationID == id) { matchedLocation = loc; break; }
            }

            bool matchedZone = false;
            if (matchedLocation == null)
            {
                foreach (TaskZone zone in sceneZones)
                {
                    if (zone != null && zone.zoneID == id) { matchedZone = true; break; }
                }
            }

            if (matchedLocation == null && !matchedZone)
            {
                LogIssue(task, $"{step.GetType().Name}: no TaskLocation or TaskZone with ID '{id}' found in the open scene.");
                issues++;
            }
            else if (requireDepositStation && matchedLocation != null)
            {
                TaskDepositStation depositStation = matchedLocation.GetComponent<TaskDepositStation>();
                if (depositStation == null) depositStation = matchedLocation.GetComponentInParent<TaskDepositStation>();

                if (depositStation == null)
                {
                    LogIssue(task, $"{step.GetType().Name}: '{id}' has no TaskDepositStation component.");
                    issues++;
                }
            }

            return issues;
        }

        private static int CheckEmoteHasAssets(TaskData task, EmoteStep emote, List<EmoteDefinition> emotes)
        {
            foreach (EmoteDefinition def in emotes)
            {
                if (def == null) continue;

                if (emote.requiredEmote != null)
                {
                    if (def == emote.requiredEmote) return 0;
                }
                else if (def.category == emote.requiredCategory)
                {
                    return 0;
                }
            }

            string what = emote.requiredEmote != null ? emote.requiredEmote.name : emote.requiredCategory.ToString();
            LogIssue(task, $"EmoteStep: no authored EmoteDefinition matches '{what}'.");
            return 1;
        }

        private static int ValidateEmoteDefinitions(List<EmoteDefinition> emotes)
        {
            int issues = 0;

            foreach (EmoteDefinition def in emotes)
            {
                if (def == null) continue;

                if (string.IsNullOrEmpty(def.broadcastVerb))
                {
                    Log.Warn($"[TaskDataValidator] EmoteDefinition '{def.name}' has no broadcastVerb.", def);
                    issues++;
                }
            }

            return issues;
        }

        private static int ValidateRecipes(List<RecipeIndex> indexes)
        {
            int issues = 0;

            foreach (RecipeIndex index in indexes)
            {
                if (index == null || index.recipes == null) continue;

                foreach (ItemRecipe recipe in index.recipes)
                {
                    if (recipe == null || recipe.inputs == null) continue;

                    foreach (ItemIdentity input in recipe.inputs)
                    {
                        if (input.definition == null) continue;

                        if (!IsReachable(input, index, 0, new HashSet<ItemDefinition>()))
                        {
                            Log.Warn($"[TaskDataValidator] ItemRecipe '{recipe.name}': input " +
                                     $"'{input.definition.displayName}' is not reachable (no recipe produces it " +
                                     "and no prefab carries that identity).", recipe);
                            issues++;
                        }
                    }
                }
            }

            return issues;
        }

        // Mirrors ItemSourceResolver.ResolveInternal's own walk, but checking asset EXISTENCE (a
        // project-wide prefab scan) instead of runtime PickupItem.AllItems, since this runs at Edit
        // time with no guarantee any scene is loaded.
        private static bool IsReachable(ItemIdentity id, RecipeIndex index, int depth, HashSet<ItemDefinition> visited)
        {
            if (PrefabExistsFor(id.definition)) return true;
            if (depth >= MaxRecipeDepth || !visited.Add(id.definition)) return false;

            ItemRecipe producer = index.FindByOutput(id);
            if (producer == null || producer.inputs == null) return false;

            foreach (ItemIdentity input in producer.inputs)
            {
                if (input.definition != null && IsReachable(input, index, depth + 1, visited)) return true;
            }

            return false;
        }

        private static bool PrefabExistsFor(ItemDefinition definition)
        {
            if (definition == null) return false;

            foreach (string guid in AssetDatabase.FindAssets("t:GameObject"))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;

                PickupItem item = prefab.GetComponent<PickupItem>();
                if (item != null && item.definition == definition) return true;
            }

            return false;
        }

        // Information, not a warning - nothing in code records which locationIDs are SUPPOSED to be
        // reversible (only the architecture doc's own table does), so this reports for the author to
        // judge rather than guessing which ones are "wrong" (Cake/Turkey/etc. are correctly never
        // reversible by design).
        private static void ReportStationReversibility(TaskStationState[] sceneStationStates)
        {
            Dictionary<string, bool> anyReversibleByLocation = new Dictionary<string, bool>();

            foreach (TaskStationState state in sceneStationStates)
            {
                if (state == null) continue;

                string id = state.LocationID;
                if (string.IsNullOrEmpty(id)) continue;

                bool alreadyTrue = anyReversibleByLocation.TryGetValue(id, out bool existing) && existing;
                anyReversibleByLocation[id] = alreadyTrue || state.PlayerReversible;
            }

            foreach (KeyValuePair<string, bool> entry in anyReversibleByLocation)
            {
                Debug.Log($"[TaskDataValidator] Station '{entry.Key}': " +
                          $"{(entry.Value ? "has" : "has NO")} a reversible instance in the open scene.");
            }
        }

        private static bool HasInvisibleChar(string s)
        {
            foreach (char c in s)
            {
                if (char.IsControl(c)) return true;

                foreach (char bad in InvisibleChars)
                {
                    if (c == bad) return true;
                }
            }

            return false;
        }

        private static List<T> LoadAllAssets<T>(string filter) where T : Object
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            List<T> result = new List<T>(guids.Length);

            foreach (string guid in guids)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) result.Add(asset);
            }

            return result;
        }

        private static int CountRecipes(List<RecipeIndex> indexes)
        {
            int count = 0;
            foreach (RecipeIndex index in indexes)
            {
                if (index != null && index.recipes != null) count += index.recipes.Count;
            }
            return count;
        }

        private static void LogIssue(TaskData task, string message)
        {
            Log.Warn($"[TaskDataValidator] '{task.name}': {message}", task);
        }
    }
}
