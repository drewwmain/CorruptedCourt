using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// The single authored surface for match balance (G3.4). Every manager and player component copies
    /// its tunables from the assigned MatchConfig at match start (see MatchManager.ApplyMatchConfig),
    /// shouting to the console - regardless of the CC_LOGGING symbol - whenever a scene/prefab value it
    /// is about to overwrite disagreed with the asset, so a stale playtest override can never contradict
    /// it silently. Tick MatchManager.ignoreMatchConfig to fall back to the raw inspector values.
    /// </summary>
    [CreateAssetMenu(fileName = "MatchConfig", menuName = "Corrupted Court/Match Config")]
    public class MatchConfig : ScriptableObject
    {
        [Header("Roles")]
        [Tooltip("Fraction of the lobby made Corrupted at match start (0.3 = 30%). Rounded to a whole count, then clamped by the caps below.")]
        [Range(0f, 1f)] public float corruptedPercentage = 0.3f;
        [Tooltip("Minimum Corrupted players, applied after rounding the percentage to a count. Keeps small lobbies playable.")]
        public int minCorrupted = 1;
        [Tooltip("Maximum Corrupted players, applied after the minimum. 0 = no upper cap.")]
        public int maxCorrupted = 0;
        [Tooltip("Seconds a reigning King has to execute a Corrupted before the curse strikes. Also the value the countdown resets to on succession / a successful execution.")]
        public float kingCurseDuration = 300f;

        [Header("Custody")]
        [Tooltip("Arrests a Royal may make in a match before running out of shackles (the current code " +
                 "never refills this - it is a whole-match budget).")]
        public int arrestQuota = 2;
        [Tooltip("Seconds an unattended prisoner takes to violently break their restraints (freeing themselves and stunning the captor).")]
        public float breakoutDuration = 30f;

        [Header("Strangle (Corrupted)")]
        [Tooltip("Seconds the strangle button must be held, once locked onto a victim, for the kill to land.")]
        public float strangleHoldTime = 1.5f;
        [Tooltip("Seconds before a strangler can attempt another strangle after a successful one.")]
        public float strangleCooldown = 4f;

        [Header("Abilities")]
        [Tooltip("Dagger power-up wind-up: seconds between committing the strike and the lethal ray firing (the counter-play window).")]
        public float daggerWindup = 1.5f;
        [Tooltip("Seconds between Haunt uses for a ghost.")]
        public float hauntCooldown = 12f;

        [Header("Match Flow")]
        [Tooltip("Assumed jog speed in world units/second used to size the pre-meeting scramble timer (map span / this + a flat buffer).")]
        public float transitionTravelSpeed = 4f;
        [Tooltip("Seconds the meeting phase lasts.")]
        public float meetingDuration = 120f;

        [Header("Tasks")]
        [Tooltip("Court-meter points a completed task adds, indexed by (taskTier - 1): element 0 = tier 1, element 1 = tier 2, element 2 = tier 3. A tier with no weight falls back to 1.")]
        public float[] tierWeights = { 1f, 2f, 4f };
        [Tooltip("Fresh task assignments handed to each eligible player at the start of every action stage.")]
        public int tasksPerStage = 3;
        [Tooltip("Full stages of assignments the Court is expected to finish to fill the meter. Sizes the target once at match start.")]
        public int targetStages = 3;
        [Tooltip("Most incomplete assignments a player carries into a new stage before the oldest is dropped.")]
        public int maxCarriedTasks = 2;

        [Header("Sabotage")]
        [Tooltip("Seconds an active global sabotage lasts before it auto-resolves if the Court never fixes it.")]
        public float sabotageDuration = 45f;
        [Tooltip("Shared Corrupted cooldown after a global sabotage ends before another can start.")]
        public float sabotageTeamCooldown = 60f;
        [Tooltip("Court-meter points drained per second while Poison the Feast is active.")]
        public float poisonDrainPerSecond = 0.5f;

        // --- Apply helpers ---------------------------------------------------------------------------
        // Copy a config value into a runtime field and, if the field being overwritten disagreed,
        // warn LOUDLY. Deliberately UnityEngine.Debug (not Log.Warn) so the message survives a build
        // with no CC_LOGGING symbol - a stale playtest override must always be visible, exactly like
        // an OnValidate misconfiguration warning.

        public static void ApplyFloat(string owner, string label, ref float field, float configValue)
        {
            if (!Mathf.Approximately(field, configValue))
                Debug.LogWarning($"[MatchConfig] {owner}.{label}: scene/inspector value {field} IGNORED - the MatchConfig asset sets {configValue}. Clear the override or fix the asset.");
            field = configValue;
        }

        public static void ApplyInt(string owner, string label, ref int field, int configValue)
        {
            if (field != configValue)
                Debug.LogWarning($"[MatchConfig] {owner}.{label}: scene/inspector value {field} IGNORED - the MatchConfig asset sets {configValue}. Clear the override or fix the asset.");
            field = configValue;
        }
    }
}
