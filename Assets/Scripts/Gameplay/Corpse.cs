using UnityEngine;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    // A body left behind when a player dies. Spawned by MatchManager.SpawnCorpse at the victim's
    // position/rotation, carrying who they were, the zone they died in, and the match-clock time of
    // death. A living player can walk up and report it with [E] (it is an IInteractable on the
    // Interactable layer); ghosts cannot. Reporting despawns the body and opens a meeting via
    // MatchManager.TriggerReportedBodyMeeting. The body persists until reported unless corpseLifetime
    // is set above 0.
    [RequireComponent(typeof(Collider))]
    public class Corpse : MonoBehaviour, IInteractable
    {
        [Header("Tuning")]
        [Tooltip("Seconds before an unreported body despawns on its own. 0 = never expires (the body " +
                 "stays until a living player reports it). Reserved for later balancing.")]
        public float corpseLifetime = 0f;

        // --- Runtime identity, stamped once by Initialize() at spawn. Read by the meeting flow / UI. ---

        /// <summary>The dead player this body belongs to. Can go null later if that GameObject is destroyed.</summary>
        public PlayerController Victim { get; private set; }
        /// <summary>Victim's display name, captured at spawn so it survives the victim being destroyed.</summary>
        public string VictimName { get; private set; } = "Unknown";
        /// <summary>Zone ID the victim was standing in when they died. Empty string = unknown.</summary>
        public string DeathZoneID { get; private set; } = "";
        /// <summary>MatchManager.MatchTime at the moment of death.</summary>
        public float DeathMatchTime { get; private set; }

        private bool initialized;
        private bool reported;

        // Called by MatchManager.SpawnCorpse immediately after Instantiate.
        public void Initialize(PlayerController victim, string deathZoneID, float deathMatchTime)
        {
            if (initialized) return;
            initialized = true;

            Victim = victim;
            if (victim != null) VictimName = victim.DisplayName;
            DeathZoneID = string.IsNullOrEmpty(deathZoneID) ? "" : deathZoneID;
            DeathMatchTime = deathMatchTime;

            // Delayed Destroy keeps unreported bodies off the per-frame update path entirely.
            if (corpseLifetime > 0f) Destroy(gameObject, corpseLifetime);
        }

        public string GetInteractionPrompt()
        {
            return $"Press <color=#F4D03F>[E]</color> to report <color=#E74C3C>{VictimName}</color>'s body";
        }

        public void OnInteract(GameObject interactor)
        {
            if (reported || interactor == null) return;

            PlayerController reporter = interactor.GetComponent<PlayerController>();
            if (reporter == null) return;

            // Ghosts may not report.
            if (reporter.Vitals != null && reporter.Vitals.isGhost)
            {
                Log.Game("[Corpse] A ghost cannot report a body.");
                return;
            }

            if (MatchManager.Instance == null) return;

            // The entry point refuses the report if a meeting is already under way; leave the body so
            // it can still be reported in the next action stage.
            if (!MatchManager.Instance.TriggerReportedBodyMeeting(reporter, Victim, DeathZoneID))
            {
                Log.Game("[Corpse] The body cannot be reported right now.");
                return;
            }

            reported = true;
            Log.Game($"[Corpse] {reporter.gameObject.name} reported {VictimName}'s body.");
            Destroy(gameObject);
        }
    }
}
