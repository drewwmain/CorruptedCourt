using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Companion to phase 3b. Rewrites the legacy string identity fields on every <see cref="TaskData"/>
/// step into typed <see cref="ItemDefinition"/> references plus <see cref="ItemState"/> flags, using
/// the ItemDefinition assets that <c>ItemDefinitionMigration</c> created in <c>Assets/Data/Items</c>.
///
/// <list type="bullet">
/// <item>"Processed" / "Deposited" prefixes are decoded to <see cref="ItemState"/> flags (same rules
///       as the 3a item migration).</item>
/// <item>Station / zone / clothing id strings (<c>targetStationID</c>, <c>targetZoneID</c>,
///       <c>sourceStationID</c>, <c>inputStationID</c>, <c>clothingName</c>) and the asset's own
///       <c>taskID</c> are scrubbed of invisible formatting characters (the authored assets have
///       stray U+2060 word-joiners baked in, so e.g. <c>targetStationID</c> "&#x2060;HighTable&#x2060;"
///       never string-matches the scene's clean "HighTable" and the step can't complete).</item>
/// <item><see cref="ProcessItemStep"/>'s dual string resolves to <c>targetItem</c> when an
///       ItemDefinition of that name exists, otherwise to <c>targetStationID</c>.</item>
/// </list>
///
/// Additive and re-runnable. A field it can't resolve (no matching ItemDefinition asset) keeps its
/// legacy string so <see cref="TaskData.OnValidate"/> keeps flagging it. Run from the menu, then
/// run "Corrupted Court/Reserialize TaskData Assets" and commit the changed .asset files.
/// </summary>
public static class TaskStepDefinitionMigration
{
    private const string ItemsFolder = "Assets/Data/ItemDefinitions";
    private const string ProcessedPrefix = "Processed";
    private const string DepositedPrefix = "Deposited";

    [MenuItem("Corrupted Court/Migrate Task Step Definitions")]
    public static void Migrate()
    {
        Dictionary<string, ItemDefinition> defs = LoadDefinitions();

        var sb = new StringBuilder();
        int resolved = 0, unresolved = 0, assetsChanged = 0, idsScrubbed = 0;
        var unresolvedNames = new SortedSet<string>(StringComparer.Ordinal);

        string[] guids = AssetDatabase.FindAssets("t:TaskData");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TaskData data = AssetDatabase.LoadAssetAtPath<TaskData>(path);
            if (data == null || data.stepTemplates == null) continue;

            bool dirty = false;

            // Scrub invisible formatting chars from the asset's own id.
            string cleanTaskID = Sanitize(data.taskID);
            if (cleanTaskID != data.taskID) { data.taskID = cleanTaskID; idsScrubbed++; dirty = true; }

            for (int i = 0; i < data.stepTemplates.Count; i++)
            {
                TaskStep step = data.stepTemplates[i];
                if (step == null) continue;

                // Scrub invisible formatting chars from every station / zone / clothing id string.
                idsScrubbed += ScrubStepIds(step, ref dirty);

                switch (step)
                {
                    case AcquireItemStep s:
                        dirty |= Resolve(defs, ref s.legacyRequiredItemName, ref s.requiredItem, ref s.requiredState,
                                         path, i, ref resolved, ref unresolved, unresolvedNames);
                        break;
                    case DepositItemStep s:
                        dirty |= Resolve(defs, ref s.legacyRequiredItemName, ref s.requiredItem, ref s.requiredState,
                                         path, i, ref resolved, ref unresolved, unresolvedNames);
                        break;
                    case ConsumeItemStep s:
                        dirty |= Resolve(defs, ref s.legacyRequiredItemName, ref s.requiredItem, ref s.requiredState,
                                         path, i, ref resolved, ref unresolved, unresolvedNames);
                        break;
                    case PlayerInteractStep s:
                        dirty |= Resolve(defs, ref s.legacyRequiredHeldItemName, ref s.requiredItem, ref s.requiredState,
                                         path, i, ref resolved, ref unresolved, unresolvedNames);
                        break;
                    case MutualPlayerInteractStep s:
                        dirty |= Resolve(defs, ref s.legacyMyRequiredItemName, ref s.requiredItem, ref s.requiredState,
                                         path, i, ref resolved, ref unresolved, unresolvedNames);
                        dirty |= Resolve(defs, ref s.legacyTargetRequiredItemName, ref s.targetRequiredItem, ref s.targetRequiredState,
                                         path, i, ref resolved, ref unresolved, unresolvedNames);
                        break;
                    case ProcessItemStep s:
                        dirty |= ResolveProcess(defs, s, path, i, ref resolved, ref unresolved, unresolvedNames);
                        break;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(data);
                assetsChanged++;
                sb.AppendLine($"    - {path}");
            }
        }

        AssetDatabase.SaveAssets();

        var head = new StringBuilder();
        head.AppendLine("[TaskStepDefinitionMigration] Complete.");
        head.AppendLine($"  TaskData assets scanned: {guids.Length}");
        head.AppendLine($"  ItemDefinitions available in {ItemsFolder}: {defs.Count}");
        head.AppendLine($"  Step fields resolved to ItemDefinition: {resolved}");
        head.AppendLine($"  Id strings scrubbed of invisible chars (taskID / station / zone / clothing): {idsScrubbed}");
        head.AppendLine($"  Step fields left un-migrated (no matching ItemDefinition): {unresolved}");
        if (unresolvedNames.Count > 0)
            head.AppendLine($"    missing definitions: {string.Join(", ", unresolvedNames)}");
        head.AppendLine($"  Assets changed: {assetsChanged}");
        if (assetsChanged > 0) head.Append(sb);
        head.AppendLine("  Next: run 'Corrupted Court/Reserialize TaskData Assets', then commit.");
        Debug.Log(head.ToString());
    }

    private static bool Resolve(
        Dictionary<string, ItemDefinition> defs,
        ref string legacy, ref ItemDefinition target, ref ItemState state,
        string path, int stepIndex, ref int resolved, ref int unresolved, SortedSet<string> missing)
    {
        if (string.IsNullOrEmpty(legacy)) return false;

        if (target != null) { legacy = null; return true; } // already migrated - just drop the stale string

        string baseName = StripPrefixes(Sanitize(legacy), out ItemState flags);
        ItemDefinition def = FindDefinition(defs, baseName);
        if (def == null)
        {
            unresolved++;
            missing.Add(baseName);
            Debug.LogWarning($"[TaskStepDefinitionMigration] {path} step {stepIndex}: no ItemDefinition '{baseName}' " +
                             $"for legacy value '{legacy}' - left for hand-wiring.");
            return false;
        }

        target = def;
        state = flags;
        legacy = null;
        resolved++;
        return true;
    }

    private static bool ResolveProcess(
        Dictionary<string, ItemDefinition> defs, ProcessItemStep s,
        string path, int stepIndex, ref int resolved, ref int unresolved, SortedSet<string> missing)
    {
        if (string.IsNullOrEmpty(s.legacyTargetStationOrItemName)) return false;

        if (s.targetItem != null || !string.IsNullOrEmpty(s.targetStationID))
        {
            s.legacyTargetStationOrItemName = null;
            return true;
        }

        string sanitized = Sanitize(s.legacyTargetStationOrItemName);
        string baseName = StripPrefixes(sanitized, out ItemState flags);
        ItemDefinition def = FindDefinition(defs, baseName);

        if (def != null)
        {
            s.targetItem = def;
            s.targetItemState = flags;
            s.legacyTargetStationOrItemName = null;
            resolved++;
            return true;
        }

        // No ItemDefinition of that name -> treat the value as a station locationID.
        s.targetStationID = sanitized;
        s.legacyTargetStationOrItemName = null;
        resolved++;
        Debug.Log($"[TaskStepDefinitionMigration] {path} step {stepIndex}: ProcessItemStep '{sanitized}' " +
                  $"has no ItemDefinition - assigned it as targetStationID (a locationID). Re-point to a " +
                  $"targetItem by hand if it was meant to be a held item.");
        return true;
    }

    // Cleans invisible formatting chars out of every locationID / zoneID / clothing-name string a
    // step carries. These are string-matched exactly against the scene's clean ids, so a stray
    // U+2060 silently breaks the step (and its waypoint) with nothing visible in the Inspector.
    // Item-identity strings are handled by Resolve/ResolveProcess and are skipped here.
    private static int ScrubStepIds(TaskStep step, ref bool dirty)
    {
        int n = 0;
        switch (step)
        {
            case NavigateStep s:
                n += Scrub(ref s.targetZoneID, ref dirty);
                break;
            case GroupNavigateStep s:
                n += Scrub(ref s.targetZoneID, ref dirty);
                break;
            case StationInteractStep s:
                n += Scrub(ref s.targetStationID, ref dirty);
                break;
            case DepositItemStep s:
                n += Scrub(ref s.targetStationID, ref dirty);
                break;
            case ProcessItemStep s:
                n += Scrub(ref s.targetStationID, ref dirty);
                break;
            case DataRetrievalStep s:
                n += Scrub(ref s.sourceStationID, ref dirty);
                n += Scrub(ref s.inputStationID, ref dirty);
                break;
            case EquipClothingStep s:
                n += Scrub(ref s.clothingName, ref dirty);
                break;
        }
        return n;
    }

    private static int Scrub(ref string value, ref bool dirty)
    {
        string clean = Sanitize(value);
        if (clean == value) return 0;
        value = clean;
        dirty = true;
        return 1;
    }

    private static Dictionary<string, ItemDefinition> LoadDefinitions()
    {
        var map = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
        {
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (def == null) continue;
            if (!string.IsNullOrEmpty(def.itemID)) map[def.itemID] = def;
            if (!string.IsNullOrEmpty(def.name)) map[def.name] = def;
        }
        return map;
    }

    private static ItemDefinition FindDefinition(Dictionary<string, ItemDefinition> defs, string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return null;
        if (defs.TryGetValue(baseName, out ItemDefinition def)) return def;
        return AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/{baseName}.asset");
    }

    // Strips invisible formatting characters (authored ids carry stray U+2060 word-joiners) and
    // whitespace so prefix detection and name lookup work.
    private static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            System.Globalization.UnicodeCategory cat = char.GetUnicodeCategory(c);
            if (cat == System.Globalization.UnicodeCategory.Format ||
                cat == System.Globalization.UnicodeCategory.Control) continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Strips leading "Processed" / "Deposited" prefixes (repeatably, in any order) and reports the
    /// <see cref="ItemState"/> flags they imply. Never strips down to an empty string.
    /// </summary>
    private static string StripPrefixes(string sanitized, out ItemState flags)
    {
        flags = ItemState.None;
        if (string.IsNullOrEmpty(sanitized)) return sanitized;

        string n = sanitized;
        bool stripped = true;
        while (stripped)
        {
            stripped = false;
            if (n.Length > ProcessedPrefix.Length && n.StartsWith(ProcessedPrefix, StringComparison.Ordinal))
            {
                flags |= ItemState.Processed;
                n = n.Substring(ProcessedPrefix.Length);
                stripped = true;
            }
            else if (n.Length > DepositedPrefix.Length && n.StartsWith(DepositedPrefix, StringComparison.Ordinal))
            {
                flags |= ItemState.DepositedContainer;
                n = n.Substring(DepositedPrefix.Length);
                stripped = true;
            }
        }
        return n;
    }
}
