using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Minigames;

namespace CorruptedCourt.Tasks
{
    /// <summary>
    /// What triggered an evaluation of a player's active tasks. Interact / InventoryChanged /
    /// ZoneChanged are wired starting with B1 (see Tasks/IMPLEMENTATION_PROMPTS.md). The remaining
    /// four are declared now so TaskStep subclasses can be written against a stable enum, but have
    /// no call site yet - EmotePerformed arrives with B12, StationConditionChanged with B6,
    /// MinigameCompleted with B10, PartnerAction alongside it.
    /// </summary>
    public enum TaskEvalReason
    {
        Interact,
        InventoryChanged,
        ZoneChanged,
        EmotePerformed,
        StationConditionChanged,
        MinigameCompleted,
        PartnerAction
    }

    /// <summary>
    /// Everything a <see cref="TaskStep"/> might need to judge one evaluation event. Replaces a bare
    /// (PlayerController, GameObject) pair so events with no interaction target - an emote, a zone
    /// change, a station reversal - don't have to fake one. See Tasks/ARCHITECTURE.md §7.1.
    /// </summary>
    public class TaskEvalContext
    {
        public PlayerController Player;
        public TaskEvalReason Reason;
        public GameObject Target;
        public TaskLocation Location;
        public PickupItem Item;
        public PlayerController Partner;
        public EmoteDefinition Emote;
        public bool SkipMinigame;
    }
}
