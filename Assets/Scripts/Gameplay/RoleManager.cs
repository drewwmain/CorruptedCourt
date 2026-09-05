using System.Collections.Generic;
using UnityEngine;
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
        public float kingCurseTimer = 300f; // 5 minutes to execute a Corrupted
        public bool isKingCursed = false;

        [Header("Balance Settings")]
        [Tooltip("Percentage of players that will be Corrupted (Default is 30% or 0.3f).")]
        public float corruptedPercentage = 0.3f;

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

        // Called by the MatchManager at the very start of the game
        public void AssignAllRoles()
        {
            // Strip out empty or destroyed entries before anything touches them
            allPlayers.RemoveAll(p => p == null);
            isKingCursed = false;
            kingCurseTimer = 300f;
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

            // Failsafe to ensure there is always at least 1 Corrupted player in small lobbies
            if (actualCorruptedCount < 1) actualCorruptedCount = 1;

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
                kingCurseTimer -= Time.deltaTime;

                if (kingCurseTimer <= 0f)
                {
                    Log.Game("<color=#8E44AD>The King failed to act in time!</color>");
                    CurseTheKing();
                }
            }
        }

        public void CurseTheKing()
        {
            if (currentKing == null || isKingCursed) return;

            Log.Game($"<color=#8E44AD>THE KING'S CURSE HAS STRUCK! {currentKing.gameObject.name} has been stripped of their crown!</color>");
            isKingCursed = true;

            // Strip the King's title (NOT their faction). AssignTitle(None) also drops the royal bonus HP.
            currentKing.Vitals.AssignTitle(CourtTitle.None);

            currentKing = null;
        }

        public void ResetKingTimer()
        {
            kingCurseTimer = 300f; // Reset back to 5 minutes
            Log.Game("<color=#F1C40F>The King's Curse timer has been reset!</color>");
        }
    }
}
