using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("Force the local 'Player' to spawn as this role. Set to 'None' for normal random distribution.")]
    public PlayerRole forceTestRole = PlayerRole.None;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
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
            Debug.LogWarning("No players found in the RoleManager list!");
            return;
        }

        List<PlayerController> remainingPlayers = new List<PlayerController>(allPlayers);
        
        // Find the local player using their GameObject name
        PlayerController localPlayer = remainingPlayers.Find(p => p.gameObject.name == "Player");

        // Calculate how many Corrupted the match SHOULD have based on the 30% distribution table
        // Adding 0.5f ensures standard rounding (e.g., 15 players * 0.3 = 4.5, which rounds up to 5)
        int actualCorruptedCount = Mathf.FloorToInt((allPlayers.Count * corruptedPercentage) + 0.5f);
        
        // Failsafe to ensure there is always at least 1 Corrupted player in small lobbies
        if (actualCorruptedCount < 1) actualCorruptedCount = 1;

        int corruptedSlotsToFill = actualCorruptedCount;
        bool kingAssigned = false;

        // --- NEW: INSPECTOR TESTING OVERRIDE LOGIC ---
        if (localPlayer != null && forceTestRole != PlayerRole.None)
        {
            localPlayer.AssignRole(forceTestRole);
            remainingPlayers.Remove(localPlayer);
            Debug.Log($"[TESTING] Forced {localPlayer.gameObject.name} to be {forceTestRole}.");

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

        // 1. Assign King (if they weren't forced in the test block)
        if (!kingAssigned)
        {
            currentKing = remainingPlayers[0];
            currentKing.AssignRole(PlayerRole.King);
            remainingPlayers.RemoveAt(0);
        }

        // 2. Assign Corrupted
        int playerIndex = 0;
        for (int i = 0; i < corruptedSlotsToFill; i++)
        {
            if (playerIndex < remainingPlayers.Count)
            {
                remainingPlayers[playerIndex].AssignRole(PlayerRole.Corrupted);
                playerIndex++;
            }
        }

        // 3. Assign Court (everyone leftover gets this)
        while (playerIndex < remainingPlayers.Count)
        {
            remainingPlayers[playerIndex].AssignRole(PlayerRole.Court);
            playerIndex++;
        }

        currentKingsguard = null;

        Debug.Log($"--- ROLE SETUP COMPLETE: 1 King, {actualCorruptedCount} Corrupted, {allPlayers.Count - actualCorruptedCount - 1} Court ---");
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

    // Called by the King's PlayerController
    public void SetKingsguard(PlayerController newGuard)
    {
        // 1. If someone is already the Kingsguard, demote them back to Court
        if (currentKingsguard != null && currentKingsguard != newGuard)
        {
            currentKingsguard.AssignRole(PlayerRole.Court);
            Debug.Log($"[RoleManager] {currentKingsguard.gameObject.name} was demoted from Kingsguard.");
        }

        // 2. Assign the new Kingsguard
        currentKingsguard = newGuard;
        currentKingsguard.AssignRole(PlayerRole.Kingsguard);
        
        Debug.Log($"--- THE KING HAS APPOINTED {currentKingsguard.gameObject.name} AS THE NEW KINGSGUARD ---");
    }
    void Update()
    {
        // Only run the timer if we have a living King who isn't already cursed
        if (currentKing != null && !currentKing.isGhost && !isKingCursed)
        {
            kingCurseTimer -= Time.deltaTime;
            
            if (kingCurseTimer <= 0f)
            {
                Debug.Log("<color=#8E44AD>The King failed to act in time!</color>");
                CurseTheKing();
            }
        }
    }

    public void CurseTheKing()
    {
        if (currentKing == null || isKingCursed) return;

        Debug.Log($"<color=#8E44AD>THE KING'S CURSE HAS STRUCK! {currentKing.gameObject.name} has been stripped of their crown!</color>");
        isKingCursed = true;
        
        // Demote the King to standard Court (your AssignRole method natively strips the bonus HP when assigning Court!)
        currentKing.AssignRole(PlayerRole.Court);
        
        currentKing = null; 
    }
    
    public void ResetKingTimer()
    {
        kingCurseTimer = 300f; // Reset back to 5 minutes
        Debug.Log("<color=#F1C40F>The King's Curse timer has been reset!</color>");
    }
}