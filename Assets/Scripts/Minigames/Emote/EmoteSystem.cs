using UnityEngine;
using CorruptedCourt.Items;

namespace CorruptedCourt.Minigames
{
    /// <summary>Broad buckets the emote wheel groups options under. Minigames require a specific one.</summary>
    public enum EmoteCategory
    {
        Gesture,
        Speech,
        Dance,
        Music
    }

    /// <summary>
    /// One selectable emote. Authored as an asset so designers can add options without code.
    /// Create via Assets ▸ Create ▸ Corrupted Court ▸ Emote.
    /// </summary>
    [CreateAssetMenu(fileName = "Emote_", menuName = "Corrupted Court/Emote")]
    public class EmoteDefinition : ScriptableObject
    {
        [Tooltip("Stable identity for this emote, independent of the asset name / Display Name.")]
        public string emoteID;

        [Tooltip("Shown on the wheel slice.")]
        public string displayName = "Emote";

        [Tooltip("Icon for the wheel slice.")]
        public Sprite icon;

        public EmoteCategory category = EmoteCategory.Gesture;

        [Tooltip("Animator trigger (or state name) played on the performing player.")]
        public string animTrigger = "";

        [Tooltip("Third-person verb for the broadcast nameplate, e.g. 'waltzes', 'strums', 'argues'.")]
        public string broadcastVerb = "";

        [Tooltip("Looping emote (dance / conversation idle) vs a one-shot (a bow, a wave).")]
        public bool loops = false;

        [Tooltip("Roughly how long the one-shot lasts, for sequencing. Ignored if it loops.")]
        public float durationSeconds = 2f;

        [Tooltip("Optional: only satisfies a task step while the performer holds a matching item " +
                 "(e.g. a trumpet emote needs an Instrument). Still plays freely either way.")]
        public ItemDefinitionSet requiresHeldItem;
    }
}
