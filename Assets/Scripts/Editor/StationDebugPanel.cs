using UnityEditor;
using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.EditorTools
{
    /// <summary>
    /// R4 (stations reversed by players, not timers) is untestable without this
    /// (Tasks/ARCHITECTURE.md §IV.3): lists every locationID's TaskStationState instances and their
    /// live condition, sourced from StationRegistry, with force-satisfy/force-revert cheat buttons so a
    /// station type starved to zero Pending instances can be found and unstuck without manually walking
    /// the whole map.
    ///
    /// Pure IMGUI EditorWindow - no Canvas/prefab authoring needed to use it. Meaningful only in Play
    /// Mode: like TaskDataValidator, this relies on TaskLocation/TaskStationState's self-registration,
    /// which never runs in Edit Mode (no [ExecuteAlways]), so StationRegistry is empty outside Play Mode.
    /// </summary>
    public class StationDebugPanel : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Corrupted Court/Station Debug Panel")]
        public static void Open()
        {
            GetWindow<StationDebugPanel>("Stations");
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint; // keeps Current live-updating while the window is open
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Play Mode only - TaskStationState registers with StationRegistry at runtime " +
                    "(Edit Mode never runs OnEnable for a plain MonoBehaviour).", MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            bool any = false;
            foreach (string locationID in StationRegistry.AllLocationIDs)
            {
                any = true;
                DrawLocationGroup(locationID);
            }

            if (!any)
            {
                EditorGUILayout.HelpBox("No TaskStationState instances registered in the loaded scene(s).", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawLocationGroup(string locationID)
        {
            var stations = StationRegistry.ByLocation(locationID);
            int pendingCount = StationRegistry.PendingCount(locationID);

            EditorGUILayout.LabelField($"{locationID}   ({stations.Count} instance(s), {pendingCount} Pending)", EditorStyles.boldLabel);

            if (pendingCount == 0)
            {
                EditorGUILayout.HelpBox($"Zero Pending - every task needing '{locationID}' is stalled until one is reversed (R4).", MessageType.Warning);
            }

            for (int i = 0; i < stations.Count; i++)
            {
                TaskStationState station = stations[i];
                if (station == null) continue;

                DrawStationRow(station);
            }

            EditorGUILayout.Space();
        }

        private static void DrawStationRow(TaskStationState station)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.ObjectField(station.gameObject, typeof(GameObject), true, GUILayout.Width(200));
            EditorGUILayout.LabelField(station.Current.ToString(), GUILayout.Width(70));

            if (station.IsDepositBacked)
            {
                EditorGUILayout.LabelField("Deposit-derived - use retrieval, not a force cheat.");
            }
            else
            {
                GUI.enabled = station.Current != TaskStationState.Condition.Satisfied;
                if (GUILayout.Button("Force Satisfy", GUILayout.Width(100))) station.MarkSatisfied(null);

                GUI.enabled = station.Current == TaskStationState.Condition.Satisfied;
                if (GUILayout.Button("Force Revert", GUILayout.Width(100))) station.DebugForceRevert();

                GUI.enabled = true;
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
