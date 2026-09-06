using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    public class RoleManager : MonoBehaviour
    {
        // Singleton pattern so other scripts can easily access the RoleManager
        public static RoleManager Instance { get; private set; }

        [Header("Lobby Tracking")]
        public List<PlayerController> allPlayers = new List<PlayerController>();

        // Track the unique roles for easy reference later (e.g., when the King is murdered)
        public PlayerController currentKing { get; private set; }
        public PlayerController currentKingsguard { get; private set; }

        [Header("King's Curse")]
        [Tooltip("Seconds a reigning King has to execute a Corrupted before the curse strikes. Also the " +
                 "value the countdown is reset to on succession and after a successful Corrupted execution.")]
        [FormerlySerializedAs("kingCurseTimer")]
        public float kingCurseDuration = 300f;

        [Tooltip("How many times the crown may pass by curse-succession in one match. 0 = unlimited. " +
                 "Once the limit is hit, a curse strips the King with no heir (the realm collapses).")]
        public int maxSuccessions = 0;

        public bool isKingCursed = false;

        // Runtime countdown to the next curse. Reset to kingCurseDuration at match start, on succession,
        // and when the King executes a Corrupted. Pure runtime state - never serialised.
        [System.NonSerialized] public float curseTimeRemaining;

        // Times the crown has passed by curse-succession this match (see maxSuccessions).
        private int successionCount;

        [Header("Balance Settings")]
        [Tooltip("Percentage of players that will be Corrupted (Default is 30% or 0.3f).")]
        public float corruptedPercentage = 0.3f;
        [Tooltip("Minimum Corrupted players after the percentage is rounded to a count. Keeps small " +
                 "lobbies playable. Copied from MatchConfig at match start.")]
        public int minCorrupted = 1;
        [Tooltip("Maximum Corrupted players (0 = no cap), applied after the minimum. Copied from " +
                 "MatchConfig at match start.")]
        public int maxCorrupted = 0;

        [Header("Testing")]
        [Tooltip("Force the local player's starting role. 'None' = normal random distribution. This is the " +
                 "legacy combined knob: each value is mapped onto a Faction + CourtTitle pair at match start " +
                 "(King/Kingsguard/Court are all Court-aligned; only Corrupted sets the Corrupted faction).")]
        public PlayerRole forceTestRole = PlayerRole.None;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                GatherExistingPlayers(); // pick up any PlayerController that awoke before this manager
            }
            else Destroy(gameObject);
        }

        // Players self-register from PlayerController.Awake. This catches the ones that awoke first.
        private void GatherExistingPlayers()
        {
            foreach (PlayerController pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                Register(pc);
        }

        /// <summary>Adds a player to the lobby list. Idempotent - safe to call more than once.</summary>
        public void Register(PlayerController player)
        {
            if (player == null || allPlayers.Contains(player)) return;
            allPlayers.Add(player);
        }

        /// <summary>Removes a player from the lobby list. Called from PlayerController.OnDestroy.</summary>
        public void Unregister(PlayerController player)
        {
            if (player == null) return;
            allPlayers.Remove(player);
        }

        // Copies role-balance tunables from the MatchConfig asset. Called by MatchManager at match
        // start, immediately before AssignAllRoles. No-op if cfg is null.
        public void ApplyConfig(MatchConfig cfg)
        {
            if (cfg == null) return;
            MatchConfig.ApplyFloat(nameof(RoleManager), nameof(corruptedPercentage), ref corruptedPercentage, cfg.corruptedPercentage);
            MatchConfig.ApplyInt(nameof(RoleManager), nameof(minCorrupted), ref minCorrupted, cfg.minCorrupted);
            MatchConfig.ApplyInt(nameof(RoleManager), nameof(maxCorrupted), ref maxCorrupted, cfg.maxCorrupted);
            MatchConfig.ApplyFloat(nameof(RoleManager), nameof(kingCurseDuration), ref kingCurseDuration, cfg.kingCurseDuration);
        }

        // Called by the MatchManager at the very start of the game
        public void AssignAllRoles()
        {
            // Strip out empty or destroyed entries before anything touches them
            allPlayers.RemoveAll(p => p == null);
            isKingCursed = false;
            curseTimeRemaining = kingCurseDuration;
            successionCount = 0;
            if (allPlayers.Count == 0)
            {
                Log.Warn("No players found in the RoleManager list!");
                return;
            }

            List<PlayerController> remainingPlayers = new List<PlayerController>(allPlayers);

            // The local player is flagged on its PlayerController (PlayerController.Local), not by name.
            PlayerController localPlayer = PlayerController.Local;

            // Calculate how many Corrupted the match SHOULD have based on the 30% distribution table
            // Adding 0.5f ensures standard rounding (e.g., 15 players * 0.3 = 4.5, which rounds up to 5)
            int actualCorruptedCount = Mathf.FloorToInt((allPlayers.Count * corruptedPercentage) + 0.5f);

            // Clamp to the configured caps: min keeps small lobbies playable, max (0 = no cap) stops a
            // small lobby tipping straight into parity.
            if (actualCorruptedCount < minCorrupted) actualCorruptedCount = minCorrupted;
            if (maxCorrupted > 0 && actualCorruptedCount > maxCorrupted) actualCorruptedCount = maxCorrupted;

            int corruptedSlotsToFill = actualCorruptedCount;
            bool kingAssigned = false;

            // --- INSPECTOR TESTING OVERRIDE LOGIC ---
            if (localPlayer != null && forceTestRole != PlayerRole.None)
            {
                // Map the legacy combined value onto the two orthogonal axes. Only "Corrupted" is a
                // non-Court faction; "King"/"Kingsguard" are Court-aligned officers.
                Faction forcedFaction = forceTestRole == PlayerRole.Corrupted ? Faction.Corrupted : Faction.Court;
                CourtTitle forcedTitle =
                    forceTestRole == PlayerRole.King ? CourtTitle.King :
                    forceTestRole == PlayerRole.Kingsguard ? CourtTitle.Kingsguard :
                    CourtTitle.None;

                localPlayer.Vitals.AssignFaction(forcedFaction);
                localPlayer.Vitals.AssignTitle(forcedTitle);
                remainingPlayers.Remove(localPlayer);
                Log.Game($"[TESTING] Forced {localPlayer.gameObject.name} to be {forceTestRole} (Faction={forcedFaction}, Title={forcedTitle}).");

                // Adjust the remaining pools so we don't accidentally double-assign unique roles
                if (forceTestRole == PlayerRole.King)
                {
                    currentKing = localPlayer;
                    kingAssigned = true;
                }
                else if (forceTestRole == PlayerRole.Corrupted)
                {
                    corruptedSlotsToFill--; // We just filled one slot, so the script spawns one less random Impostor
                }
            }
            // ---------------------------------------------

            ShuffleList(remainingPlayers);

            // 1. Assign King (if they weren't forced in the test block). Court faction + King title.
            if (!kingAssigned)
            {
                currentKing = remainingPlayers[0];
                currentKing.Vitals.AssignFaction(Faction.Court);
                currentKing.Vitals.AssignTitle(CourtTitle.King);
                remainingPlayers.RemoveAt(0);
            }

            // 2. Assign Corrupted. Corrupted faction, no court title.
            int playerIndex = 0;
            for (int i = 0; i < corruptedSlotsToFill; i++)
            {
                if (playerIndex < remainingPlayers.Count)
                {
                    remainingPlayers[playerIndex].Vitals.AssignFaction(Faction.Corrupted);
                    remainingPlayers[playerIndex].Vitals.AssignTitle(CourtTitle.None);
                    playerIndex++;
                }
            }

            // 3. Assign Court (everyone leftover gets this). Court faction, no court title.
            while (playerIndex < remainingPlayers.Count)
            {
                remainingPlayers[playerIndex].Vitals.AssignFaction(Faction.Court);
                remainingPlayers[playerIndex].Vitals.AssignTitle(CourtTitle.None);
                playerIndex++;
            }

            currentKingsguard = null;

            Log.Game($"--- ROLE SETUP COMPLETE: 1 King, {actualCorruptedCount} Corrupted, {allPlayers.Count - actualCorruptedCount - 1} Court ---");
        }

        // A standard Fisher-Yates shuffle algorithm to randomize the list
        private void ShuffleList(List<PlayerController> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                PlayerController temp = list[i];
                int randomIndex = Random.Range(i, list.Count);
                list[i] = list[randomIndex];
                list[randomIndex] = temp;
            }
        }

        // Called by the King's PlayerController. Appointment writes ONLY the court title - a player's
        // Faction is fixed for the match, so appointing a Corrupted player as Kingsguard leaves them
        // Corrupted (they keep their kill ability, still don't credit the meter, still count as
        // Corrupted for parity).
        public void SetKingsguard(PlayerController newGuard)
        {
            // 1. If someone is already the Kingsguard, strip their title (NOT their faction).
            if (currentKingsguard != null && currentKingsguard != newGuard)
            {
                currentKingsguard.Vitals.AssignTitle(CourtTitle.None);
                Log.Game($"[RoleManager] {currentKingsguard.gameObject.name} was demoted from Kingsguard.");
            }

            // 2. Give the new Kingsguard the title, leaving their faction untouched.
            currentKingsguard = newGuard;
            currentKingsguard.Vitals.AssignTitle(CourtTitle.Kingsguard);

            Log.Game($"--- THE KING HAS APPOINTED {currentKingsguard.gameObject.name} AS THE NEW KINGSGUARD ---");
        }
        void Update()
        {
            // Only run the timer if we have a living King who isn't already cursed
            if (currentKing != null && !currentKing.Vitals.isGhost && !isKingCursed)
            {
                curseTimeRemaining -= Time.deltaTime;

                if (curseTimeRemaining <= 0f)
                {
                    Log.Game("<color=#8E44AD>The King failed to act in time!</color>");
                    CurseTheKing();
                }
            }
        }

        // Curse timer expired. Instead of dead-ending the match (no King => nobody can arrest => no
        // meeting can ever be called => the timerless action stage runs forever), the crown passes:
        //   - to a living Court-faction Kingsguard if one exists,
        //   - otherwise to a random living Court-faction player,
        //   - and if no living Court-faction player remains, to nobody (parity is already met, so the
        //     population win conditions should be resolving the match).
        public void CurseTheKing()
        {
            if (currentKing == null || isKingCursed) return;

            PlayerController cursedKing = currentKing;
            Log.Game($"<color=#8E44AD>THE KING'S CURSE HAS STRUCK! {cursedKing.gameObject.name} has been stripped of their crown!</color>");

            // The cursed King loses only the title (AssignTitle(None) also drops the royal bonus HP);
            // their Faction is fixed for the match.
            cursedKing.Vitals.AssignTitle(CourtTitle.None);
            currentKing = null;

            // Opt-in "the realm collapses after N cursed kings" variant. 0 = unlimited (never collapses).
            if (maxSuccessions > 0 && successionCount >= maxSuccessions)
            {
                isKingCursed = true;
                Log.Game($"<color=#8E44AD>The realm collapses - {successionCount} succession(s) spent, the throne stays empty.</color>");
                return;
            }

            PlayerController heir = ChooseHeir(cursedKing);
            if (heir == null)
            {
                isKingCursed = true;
                Log.Game("<color=#8E44AD>No living Court remains to take the throne. The monarchy has fallen.</color>");
                return;
            }

            CrownHeir(heir);
        }

        // Succession pick. Never returns a ghost, the just-cursed King, or a Corrupted-faction player.
        private PlayerController ChooseHeir(PlayerController cursedKing)
        {
            // 1. A living Kingsguard whose Faction is Court.
            if (currentKingsguard != null
                && currentKingsguard != cursedKing
                && !currentKingsguard.Vitals.isGhost
                && currentKingsguard.Vitals.faction == Faction.Court)
            {
                return currentKingsguard;
            }

            // 2. A random living Court-faction player.
            List<PlayerController> candidates = new List<PlayerController>();
            foreach (PlayerController p in allPlayers)
            {
                if (p == null || p == cursedKing || p.Vitals.isGhost) continue;
                if (p.Vitals.faction != Faction.Court) continue; // never crown a Corrupted-faction player
                candidates.Add(p);
            }

            return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
        }

        // Crowns the heir: King title, curse timer reset, Kingsguard slot vacated. Faction is left
        // untouched. The crowning is hard-guarded so a Corrupted-faction player can never take the throne.
        private void CrownHeir(PlayerController heir)
        {
            if (heir == null || heir.Vitals.isGhost || heir.Vitals.faction != Faction.Court)
            {
                isKingCursed = true;
                Log.Warn("[RoleManager] CrownHeir rejected an ineligible heir - the throne stays empty.");
                return;
            }

            // Clear the previous Kingsguard's title (they may be the very player being crowned).
            if (currentKingsguard != null)
            {
                if (currentKingsguard != heir) currentKingsguard.Vitals.AssignTitle(CourtTitle.None);
                currentKingsguard = null;
            }

            currentKing = heir;
            heir.Vitals.AssignTitle(CourtTitle.King);

            successionCount++;
            curseTimeRemaining = kingCurseDuration;

            Log.Game($"<color=#F1C40F>--- SUCCESSION: {heir.gameObject.name} is crowned King (succession {successionCount}); the curse timer resets to {kingCurseDuration:0}s. ---</color>");
        }

        public void ResetKingTimer()
        {
            curseTimeRemaining = kingCurseDuration;
            Log.Game("<color=#F1C40F>The King's Curse timer has been reset!</color>");
        }
    }
}
