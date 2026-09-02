using System.Collections.Generic;
using UnityEngine;

public class VotingManager : MonoBehaviour
{
    public static VotingManager Instance { get; private set; }
    // NEW: Tracks the player locked to the Gallows
    public PlayerController condemnedPlayer;

   // Dictionary tracking [The Voter] -> ["Confirm", "Deny", or "Skip"]
    private Dictionary<PlayerController, string> playerVotes = new Dictionary<PlayerController, string>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Called by the MatchManager when the Meeting phase starts
    public void StartMeeting()
    {
        playerVotes.Clear(); // Wipe votes from the previous meeting
        
        // TELEPORT REMOVED: Players must already be in the room!

        // NEW: Unlock cursor and show UI
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        // CHANGED: Only enable the top-right button, do not force the panel open yet
        if (UIManager.Instance != null) UIManager.Instance.EnableVotingPhase();

        Debug.Log("--- VOTING STARTED: Players can now cast their votes ---");
    }

    // Called by the UI buttons (Options: "Confirm", "Deny", "Skip")
    public void CastVote(PlayerController voter, string voteOption)
    {
        if (voter.isGhost) return; // Ghosts cannot vote

        if (playerVotes.ContainsKey(voter))
        {
            playerVotes[voter] = voteOption;
            Debug.Log($"{voter.gameObject.name} changed their vote to {voteOption}.");
        }
        else
        {
            playerVotes.Add(voter, voteOption);
            Debug.Log($"{voter.gameObject.name} voted to {voteOption}.");
        }
    }

    // Called by the MatchManager when the meeting timer hits zero
    public void TallyVotes()
    {
        Debug.Log("--- TALLYING VOTES ---");
        
        int confirmVotes = 0;
        int denyVotes = 0;
        int skipVotes = 0;

        // Count up the dictionary
        foreach (string vote in playerVotes.Values)
        {
            if (vote == "Confirm") confirmVotes++;
            else if (vote == "Deny") denyVotes++;
            else if (vote == "Skip") skipVotes++;
        }

        Debug.Log($"Results - Confirm: {confirmVotes} | Deny: {denyVotes} | Skip: {skipVotes}");

        // Result Logic: Confirm must outright win to execute
        if (confirmVotes > denyVotes && confirmVotes > skipVotes)
        {
            if (condemnedPlayer != null)
            {
                Debug.Log($"RESULT: The Court has confirmed the execution! {condemnedPlayer.gameObject.name} is EXECUTED!");
                condemnedPlayer.BecomeGhost();
                condemnedPlayer.isArrested = false; // Clear their state
                // --- NEW: THE KING'S CURSE EVALUATION ---
                if (RoleManager.Instance != null && RoleManager.Instance.currentKing != null)
                {
                    if (condemnedPlayer.currentRole == PlayerRole.Corrupted)
                    {
                        Debug.Log("The King successfully executed a Corrupted player!");
                        RoleManager.Instance.ResetKingTimer();
                    }
                    else
                    {
                        Debug.Log("<color=#E74C3C>The King led an Innocent to the slaughter! The curse timer continues ticking.</color>");
                        // The King is no longer punished with an instant demotion, so we do nothing here!
                    }
                }
            }
        }
        else
        {
            Debug.Log("RESULT: The Court did not confirm the execution. The prisoner is freed!");
            
            if (condemnedPlayer != null)
            {
                condemnedPlayer.isArrested = false;
                Debug.Log($"{condemnedPlayer.gameObject.name} steps down from the gallows.");
            }
        }

        // Reset the condemned player for the next round
        condemnedPlayer = null;

        // Hide UI
        if (UIManager.Instance != null) UIManager.Instance.HideVotingPanel();
    }
}