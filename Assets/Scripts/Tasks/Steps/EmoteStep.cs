using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;
using CorruptedCourt.Minigames;

namespace CorruptedCourt.Tasks
{
    /// <summary>How a nearby partner's emote must relate to this player's for <see cref="EmoteStep"/> to
    /// count it (Tasks/ARCHITECTURE.md §11.5).</summary>
    public enum PartnerRule
    {
        /// <summary>No partner needed - performing the emote alone satisfies the step.</summary>
        None,
        /// <summary>Any living, in-range partner currently performing an emote of the same category counts.</summary>
        SameCategory,
        /// <summary>The partner must independently satisfy this same step's emote + held-item requirements
        /// (e.g. two musicians each holding an instrument, both mid-Music-emote) - not just any same-category emote.</summary>
        MatchingItemEmote
    }

    // ---------------------------------------------------
    // EMOTE
    // ---------------------------------------------------
    [System.Serializable]
    public class EmoteStep : TaskStep
    {
        [Tooltip("Required emote category, unless Required Emote overrides it.")]
        public EmoteCategory requiredCategory;

        [Tooltip("Optional: require exactly this emote instead of any emote in Required Category.")]
        public EmoteDefinition requiredEmote;

        [Tooltip("Optional: the performer must be holding a matching item (e.g. an Instrument set).")]
        public ItemDefinitionSet requiredHeldItemSet;

        [Tooltip("Whether a nearby partner performing too is required, and how their emote must relate to this one.")]
        public PartnerRule partner = PartnerRule.None;

        [Tooltip("Max distance to a qualifying partner.")]
        public float partnerMaxDistance = 4f;

        [Tooltip("Require the partner to share this player's current zone.")]
        public bool partnerMustShareZone = true;

        [Tooltip("The TaskZone.zoneID the emote must be performed in. Empty = any zone.")]
        public string requiredZoneID;

        public override string GetObjectiveText()
        {
            string what = requiredEmote != null ? requiredEmote.displayName : requiredCategory.ToString();
            string where = string.IsNullOrEmpty(requiredZoneID) ? "" : $" at the <color=#F4D03F>{requiredZoneID}</color>";
            return $"Perform a <color=#5DADE2>{what}</color> emote{where}";
        }

        // EmoteStep is ambient (Tasks/ARCHITECTURE.md §7.1) - it never has an interaction target, so it
        // only ever completes through the context-aware overload below, on TaskEvalReason.EmotePerformed.
        public override bool CheckCompletion(PlayerController player, GameObject targetInteractable = null) => false;

        public override bool CheckCompletion(TaskEvalContext ctx, TaskStepRuntime runtime)
        {
            if (ctx.Reason != TaskEvalReason.EmotePerformed) return false;
            if (ctx.Player == null || ctx.Emote == null) return false;

            if (!EmoteMatches(ctx.Emote, requiredEmote, requiredCategory)) return false;
            if (!HeldItemSatisfied(ctx.Player, requiredHeldItemSet)) return false;
            if (!string.IsNullOrEmpty(requiredZoneID) && ctx.Player.Vitals.currentZoneID != requiredZoneID) return false;
            if (!PartnerSatisfied(ctx.Player, ctx.Emote)) return false;

            return true;
        }

        private static bool EmoteMatches(EmoteDefinition emote, EmoteDefinition required, EmoteCategory category)
            => required != null ? emote == required : emote.category == category;

        private static bool HeldItemSatisfied(PlayerController player, ItemDefinitionSet itemSet)
        {
            if (itemSet == null) return true;
            PickupItem held = player.GetHeldItem();
            return held != null && itemSet.Contains(held.definition);
        }

        // Scans every living player rather than some fixed pre-filter radius - partnerMaxDistance is
        // authored per-task and could be anything, so the real distance/zone check has to happen here,
        // not at the GameEvents.EmotePerformed raise site (see PlayerTaskBook.HandleEmotePerformed).
        private bool PartnerSatisfied(PlayerController player, EmoteDefinition myEmote)
        {
            if (partner == PartnerRule.None) return true;
            if (RoleManager.Instance == null) return false;

            foreach (PlayerController other in RoleManager.Instance.allPlayers)
            {
                if (other == null || other == player) continue;
                if (other.Vitals.isGhost) continue; // "a living player"

                PlayerEmotes otherEmotes = other.GetComponent<PlayerEmotes>();
                if (otherEmotes == null || !otherEmotes.IsPerforming || otherEmotes.Current == null) continue;

                if (Vector3.Distance(player.transform.position, other.transform.position) > partnerMaxDistance) continue;
                if (partnerMustShareZone && player.Vitals.currentZoneID != other.Vitals.currentZoneID) continue;

                if (PartnerEmoteQualifies(other, otherEmotes.Current, myEmote)) return true;
            }
            return false;
        }

        private bool PartnerEmoteQualifies(PlayerController partnerPlayer, EmoteDefinition partnerEmote, EmoteDefinition myEmote)
        {
            switch (partner)
            {
                case PartnerRule.SameCategory:
                    return partnerEmote.category == myEmote.category;
                case PartnerRule.MatchingItemEmote:
                    return EmoteMatches(partnerEmote, requiredEmote, requiredCategory)
                           && HeldItemSatisfied(partnerPlayer, requiredHeldItemSet);
                default:
                    return true;
            }
        }

        public override string GetConfigurationWarning()
        {
            if (requiredEmote != null && requiredEmote.category != requiredCategory)
                return "Required Emote's category doesn't match Required Category - Required Emote overrides " +
                       "it, so Required Category is unused here. Clear one or align them.";
            return null;
        }

        // New capability - WaypointManager's old if-else chain never handled this step at all, so
        // emote tasks had no waypoint before B16.
        public override ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime)
        {
            if (!string.IsNullOrEmpty(requiredZoneID) && player.Vitals.currentZoneID != requiredZoneID)
            {
                Transform zone = FindLocationOrZone(requiredZoneID);
                if (zone != null) return new ObjectiveTarget { Transform = zone, IsDirect = true };
            }

            if (partner != PartnerRule.None)
            {
                return new ObjectiveTarget
                {
                    Transform = null,
                    Hint = "Find another player performing this nearby."
                };
            }

            return default; // in the right place already (or no zone required) - just perform the emote
        }
    }
}
