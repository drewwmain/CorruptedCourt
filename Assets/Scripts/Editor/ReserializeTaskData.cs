using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot utility: rewrites every TaskData .asset to the current serialization form so a
/// [MovedFrom] managed-reference type remap gets baked to disk. Safe to delete after use.
/// </summary>
public static class ReserializeTaskData
{
    [MenuItem("Corrupted Court/Reserialize TaskData Assets")]
    public static void Run()
    {
        string[] guids = AssetDatabase.FindAssets("t:TaskData");
        var paths = new string[guids.Length];
        for (int i = 0; i < guids.Length; i++)
            paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);

        AssetDatabase.ForceReserializeAssets(paths);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ReserializeTaskData] Reserialized {paths.Length} TaskData assets.");
    }
}
