using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration: turns the string-encoded item identity (<c>PickupItem.itemName</c> and
/// <c>TaskLocation.acceptedItemName</c>, with <c>"Processed"</c> / <c>"Deposited"</c> prefixes) into
/// typed <see cref="ItemDefinition"/> references plus <see cref="ItemState"/> flags.
///
/// Additive only - it does NOT touch matching logic. Run it from the menu, then commit the
/// generated <c>Assets/Data/Items</c> assets and the modified prefabs. Safe to run repeatedly.
/// </summary>
public static class ItemDefinitionMigration
{
    private const string PrefabRoot = "Assets/Prefabs";
    private const string ItemsFolder = "Assets/Data/Items";
    private const string ProcessedPrefix = "Processed";
    private const string DepositedPrefix = "Deposited";

    [MenuItem("Corrupted Court/Migrate Item Definitions")]
    public static void Migrate()
    {
        EnsureFolder(ItemsFolder);

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });

        // --- Pass 1: collect every distinct base item name, stripping the state prefixes, from both
        //     PickupItem.itemName and TaskLocation.acceptedItemName across every prefab. ---
        var baseNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string guid in prefabGuids)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (root == null) continue;

            foreach (PickupItem pi in root.GetComponentsInChildren<PickupItem>(true))
            {
                string b = StripPrefixes(pi.itemName, out _);
                if (!string.IsNullOrEmpty(b)) baseNames.Add(b);
            }
            foreach (TaskLocation tl in root.GetComponentsInChildren<TaskLocation>(true))
            {
                string b = StripPrefixes(tl.acceptedItemName, out _);
                if (!string.IsNullOrEmpty(b)) baseNames.Add(b);
            }
        }

        // --- Ensure one ItemDefinition asset per base name (create only what's missing). ---
        var defs = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        int createdCount = 0;
        var createdNames = new List<string>();
        foreach (string baseName in baseNames)
        {
            string assetPath = $"{ItemsFolder}/{baseName}.asset";
            ItemDefinition def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<ItemDefinition>();
                def.itemID = baseName;
                def.displayName = baseName;
                AssetDatabase.CreateAsset(def, assetPath);
                createdCount++;
                createdNames.Add(baseName);
            }
            defs[baseName] = def;
        }
        AssetDatabase.SaveAssets();

        // --- Pass 2: wire definitions + state onto prefab components; save only prefabs that changed. ---
        int itemsAssigned = 0, itemFlagsSet = 0, locationsAssigned = 0, prefabsSaved = 0;
        var changedPrefabs = new List<string>();

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) continue;

            bool dirty = false;

            foreach (PickupItem pi in root.GetComponentsInChildren<PickupItem>(true))
            {
                string b = StripPrefixes(pi.itemName, out ItemState flags);
                if (string.IsNullOrEmpty(b) || !defs.TryGetValue(b, out ItemDefinition def)) continue;

                if (pi.definition != def) { pi.definition = def; itemsAssigned++; dirty = true; }
                if (pi.state != flags)    { pi.state = flags;    itemFlagsSet++;  dirty = true; }
            }

            foreach (TaskLocation tl in root.GetComponentsInChildren<TaskLocation>(true))
            {
                string b = StripPrefixes(tl.acceptedItemName, out _);
                if (string.IsNullOrEmpty(b) || !defs.TryGetValue(b, out ItemDefinition def)) continue;

                if (tl.acceptedItem != def) { tl.acceptedItem = def; locationsAssigned++; dirty = true; }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(root);
                PrefabUtility.SavePrefabAsset(root);
                prefabsSaved++;
                changedPrefabs.Add(path);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // --- Summary ---
        var sb = new StringBuilder();
        sb.AppendLine("[ItemDefinitionMigration] Complete.");
        sb.AppendLine($"  Prefabs scanned under {PrefabRoot}: {prefabGuids.Length}");
        sb.AppendLine($"  Distinct base item names: {baseNames.Count}");
        sb.AppendLine($"  ItemDefinitions created in {ItemsFolder}: {createdCount}"
                      + (createdNames.Count > 0 ? $"  [{string.Join(", ", createdNames)}]" : ""));
        sb.AppendLine($"  PickupItem.definition assigned/updated: {itemsAssigned}");
        sb.AppendLine($"  PickupItem.state updated: {itemFlagsSet}");
        sb.AppendLine($"  TaskLocation.acceptedItem assigned/updated: {locationsAssigned}");
        sb.AppendLine($"  Prefabs saved: {prefabsSaved}");
        foreach (string p in changedPrefabs) sb.AppendLine($"    - {p}");
        if (createdCount == 0 && prefabsSaved == 0)
            sb.AppendLine("  (nothing to do - already migrated)");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Strips leading <c>"Processed"</c> / <c>"Deposited"</c> prefixes (repeatably, in any order) and
    /// reports the <see cref="ItemState"/> flags they imply. Never strips down to an empty string.
    /// </summary>
    private static string StripPrefixes(string raw, out ItemState flags)
    {
        flags = ItemState.None;
        if (string.IsNullOrEmpty(raw)) return raw;

        string n = raw.Trim();
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

    // Creates the folder chain (e.g. Assets/Data then Assets/Data/Items) if any part is missing.
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
