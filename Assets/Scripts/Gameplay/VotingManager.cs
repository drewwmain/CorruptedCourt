using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    public class VotingManager : MonoBehaviour
    {
        public static VotingManager Instance { get; private set; }

        /// <summary>How the current meeting got its defendant.</summary>
        public enum MeetingKind
        {
            /// <summary>A Royal condemned someone: the defendant is known the moment the meeting opens.</summary>
            GallowsTrial,
            /// <summary>A body was reported: the court must nominate a defendant before any trial vote.</summary>
            Inquest
        }

        // Where the current meeting is in its own internal flow. GallowsTrial skips straight to Voting;
        // Inquest runs Nominating first. Concluded = resolved (adjourned, or tallied).
        private enum MeetingStep { Idle, Nominating, Voting, Concluded }

        // Vote tokens - the strings the UI sends into CastVote. One place so the UI and the tally agree.
        public const string VoteConfirm = "Confirm";
        public const string VoteDeny = "Deny";
        public const string VoteSkip = "Skip";

        // The player currently on trial: the gallows defendant (set by PlayerVitals.LockPrisonerToGallows
        // before the meeting opens) OR the inquest's top nominee (set here by ResolveNominations).
        // Pure runtime state - never authored in the Inspector.
        public PlayerController condemnedPlayer;

        [Header("Inquest")]
        [Tooltip("Seconds the inquest nomination phase runs before the top nominee is put on trial. " +
                 "Ends early once every living player has nominated. Must be shorter than " +
                 "MatchManager.meetingDuration or the trial vote gets no time.")]
        [SerializeField] private float nominationDuration = 45f;

        // Dictionary tracking [The Voter] -> ["Confirm", "Deny", or "Skip"]
        private readonly Dictionary<PlayerController, string> playerVotes = new Dictionary<PlayerController, string>();

        // [The Nominator] -> [The player they nominated]. One nomination each; re-nominating overwrites.
        private readonly Dictionary<PlayerController, PlayerController> nominations = new Dictionary<PlayerController, PlayerController>();

        // Reused scratch for the nomination tally so ResolveNominations allocates nothing.
        private readonly Dictionary<PlayerController, int> nomineeCounts = new Dictionary<PlayerController, int>();

        private MeetingStep step = MeetingStep.Idle;
        private float nominationTimer;

        /// <summary>Read by the meeting UI: true while an inquest is taking nominations (show the
        /// nominate options), false during a trial vote (show Confirm / Deny / Skip).</summary>
        public bool AwaitingNominations => step == MeetingStep.Nominating;

        /// <summary>How the current meeting reached (or is reaching) its defendant.</summary>
        public MeetingKind CurrentMeetingKind { get; private set; }

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        void Update()
        {
            // Only the inquest nomination window is time-boxed here; the trial-vote window is bounded
            // by MatchManager's meeting timer, which ends the meeting with TallyVotes().
            if (step != MeetingStep.Nominating) return;

            nominationTimer -= Time.deltaTime;
            if (nominationTimer <= 0f) ResolveNominations();
        }

        // Called by MatchManager when the Meeting phase starts, with the kind the trigger set.
        public void StartMeeting(MeetingKind kind)
        {
            CurrentMeetingKind = kind;
            playerVotes.Clear();
            nominations.Clear();
            nomineeCounts.Clear();

            // Unlock cursor and show UI
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (kind == MeetingKind.Inquest)
            {
                // An inquest names its own defendant - never inherit one from a previous meeting.
                condemnedPlayer = null;
                step = MeetingStep.Nominating;
                nominationTimer = nominationDuration;

                Log.Game("--- INQUEST OPENED: nominate a suspect ---");
                GameEvents.RaiseNominationPhaseStarted();
            }
            else // GallowsTrial - the defendant was set before the meeting opened.
            {
                step = MeetingStep.Voting;

                Log.Game(condemnedPlayer != null
                    ? $"--- GALLOWS TRIAL: the court votes on {condemnedPlayer.gameObject.name} ---"
                    : "--- GALLOWS TRIAL: no defendant present ---");
                GameEvents.RaiseVotingPhaseStarted();
            }
        }

        // --- NOMINATION (inquest only) ---

        // Called by the meeting UI. Each living player may nominate one living player.
        public void CastNomination(PlayerController nominator, PlayerController nominee)
        {
            if (step != MeetingStep.Nominating) return;
            if (nominator == null || nominee == null) return;
            if (nominator.Vitals == null || nominator.Vitals.isGhost) return; // ghosts cannot nominate
            if (nominee.Vitals == null || nominee.Vitals.isGhost) return;     // only the living stand trial

            bool changed = !nominations.TryGetValue(nominator, out PlayerController prev) || prev != nominee;
            nominations[nominator] = nominee;
            if (changed) Log.Game($"{nominator.gameObject.name} nominates {nominee.gameObject.name}.");

            // Everyone living has weighed in - no reason to keep waiting on the timer.
            int living = CountLivingPlayers();
            if (living > 0 && nominations.Count >= living) ResolveNominations();
        }

        // Picks the defendant from the nominations, or adjourns the meeting if there is no clear one.
        private void ResolveNominations()
        {
            if (step != MeetingStep.Nominating) return;

            nomineeCounts.Clear();
            foreach (PlayerController nominee in nominations.Values)
            {
                if (nominee == null || nominee.Vitals == null || nominee.Vitals.isGhost) continue;
                nomineeCounts.TryGetValue(nominee, out int c);
                nomineeCounts[nominee] = c + 1;
            }

            PlayerController top = null;
            int topCount = 0;
            bool tie = false;
            foreach (KeyValuePair<PlayerController, int> kv in nomineeCounts)
            {
                if (kv.Value > topCount) { top = kv.Key; topCount = kv.Value; tie = false; }
                else if (kv.Value == topCount) { tie = true; }
            }

            // Tie or zero nominations: the meeting ends with no execution.
            if (top == null || topCount == 0 || tie)
            {
                condemnedPlayer = null;
                step = MeetingStep.Concluded;
                Log.Game("--- INQUEST: no clear suspect (tie or no nominations). No trial will be held. ---");
                GameEvents.RaiseMeetingResult("The inquest reached no clear suspect. No trial will be held.");
                GameEvents.RaiseVotingPanelHidden();
                return;
            }

            condemnedPlayer = top;
            step = MeetingStep.Voting;
            Log.Game($"--- INQUEST: {top.gameObject.name} will stand trial ({topCount} nomination(s)). ---");
            GameEvents.RaiseVotingPhaseStarted();
        }

        // --- TRIAL VOTE ---

        // Called by the UI buttons (options: VoteConfirm / VoteDeny / VoteSkip).
        public void CastVote(PlayerController voter, string voteOption)
        {
            if (step != MeetingStep.Voting) return;        // no votes during nomination or after tally
            if (voter == null || voter.Vitals == null || voter.Vitals.isGhost) return; // ghosts cannot vote

            // A defendant cannot cast a vote in their own trial.
            if (voter == condemnedPlayer)
            {
                Log.Game($"{voter.gameObject.name} is the defendant and cannot vote in their own trial.");
                return;
            }

            if (playerVotes.ContainsKey(voter))
            {
                playerVotes[voter] = voteOption;
                Log.Game($"{voter.gameObject.name} changed their vote to {voteOption}.");
            }
            else
            {
                playerVotes.Add(voter, voteOption);
                Log.Game($"{voter.gameObject.name} voted to {voteOption}.");
            }
        }

        // Called by MatchManager when the meeting timer hits zero.
        public void TallyVotes()
        {
            Log.Game("--- TALLYING VOTES ---");
            step = MeetingStep.Concluded;

            PlayerController defendant = condemnedPlayer;

            // No defendant this meeting (inquest adjourned, or a gallows trial whose prisoner is gone):
            // nothing to resolve, and the adjourn path already told the view.
            if (defendant == null || defendant.Vitals == null)
            {
                Log.Game("RESULT: no defendant - no verdict.");
                condemnedPlayer = null;
                GameEvents.RaiseVotingPanelHidden();
                return;
            }

            int confirm = 0, deny = 0, skip = 0;
            foreach (KeyValuePair<PlayerController, string> kv in playerVotes)
            {
                PlayerController voter = kv.Key;
                if (voter == null || voter.Vitals == null || voter.Vitals.isGhost) continue; // died since voting
                if (voter == defendant) continue;                                             // never counts

                if (kv.Value == VoteConfirm) confirm++;
                else if (kv.Value == VoteDeny) deny++;
                else if (kv.Value == VoteSkip) skip++;
            }

            // Every living non-defendant who never cast a vote is a silent abstention.
            int eligible = CountEligibleVoters(defendant);
            int cast = confirm + deny + skip;
            int noVote = Mathf.Max(0, eligible - cast);
            int abstained = skip + noVote;

            // Skip and no-shows are abstentions: excluded from the verdict entirely.
            // Verdict is Confirm > Deny; a tie acquits.
            bool execute = confirm > deny;

            string breakdown = $"Confirm: {confirm}  |  Deny: {deny}  |  Abstained: {abstained} (Skip {skip}, no vote {noVote})";
            Log.Game($"Results - {breakdown}");

            if (execute && !defendant.Vitals.isGhost)
            {
                Log.Game($"RESULT: the court confirms the execution! {defendant.gameObject.name} is EXECUTED!");
                defendant.Vitals.isArrested = false; // clear their state
                defendant.Vitals.BecomeGhost();
                EvaluateKingsCurse(defendant);
                GameEvents.RaiseMeetingResult($"<b>{defendant.DisplayName} was executed.</b>\n{breakdown}");
            }
            else
            {
                Log.Game($"RESULT: the court did not confirm the execution. {defendant.gameObject.name} is acquitted.");
                defendant.Vitals.isArrested = false;
                GameEvents.RaiseMeetingResult($"<b>{defendant.DisplayName} was acquitted.</b>\n{breakdown}");
            }

            condemnedPlayer = null;
            GameEvents.RaiseVotingPanelHidden();
        }

        // The King's Curse only cares that the court's verdict put a Corrupted player down. It does not
        // care whether that verdict came from a gallows trial or an inquest - this runs for both kinds.
        private void EvaluateKingsCurse(PlayerController executed)
        {
            if (executed == null || executed.Vitals == null) return;
            if (RoleManager.Instance == null || RoleManager.Instance.currentKing == null) return;

            if (executed.Vitals.faction == Faction.Corrupted)
            {
                Log.Game("A Corrupted player was executed - the King's Curse timer resets.");
                RoleManager.Instance.ResetKingTimer();
            }
            else
            {
                Log.Game("<color=#E74C3C>An innocent was led to the slaughter. The curse timer keeps ticking.</color>");
            }
        }

        // --- ROSTER HELPERS (null-guarded loops over the Inspector-populated lobby list) ---

        private int CountLivingPlayers()
        {
            if (RoleManager.Instance == null) return int.MaxValue; // no roster: can't early-resolve, wait out the timer
            int n = 0;
            foreach (PlayerController p in RoleManager.Instance.allPlayers)
                if (p != null && p.Vitals != null && !p.Vitals.isGhost) n++;
            return n;
        }

        private int CountEligibleVoters(PlayerController defendant)
        {
            if (RoleManager.Instance == null) return playerVotes.Count;
            int n = 0;
            foreach (PlayerController p in RoleManager.Instance.allPlayers)
            {
                if (p == null || p.Vitals == null || p.Vitals.isGhost) continue;
                if (p == defendant) continue;
                n++;
            }
            return n;
        }
    }
}
