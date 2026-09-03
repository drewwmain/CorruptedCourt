using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Text;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Task UI")]
    public TextMeshProUGUI taskListText;
    public Slider courtProgressMeter;

    [Header("Transition & Meeting UI")]
    public GameObject transitionPanel; // Shows during the 20s scramble
    public TextMeshProUGUI transitionTimerText;
    public TextMeshProUGUI absentMembersText; // Shows during the actual meeting

    [Header("Voting UI")]
    public GameObject openVoteButton; // NEW: The button in the top right to open the panel
    public GameObject votingPanel;
    public Transform votingButtonContainer; 
    public GameObject votingButtonPrefab;

    [Header("Game Over UI")]
    public GameObject gameOverPanel;
    public TextMeshProUGUI winnerText;
    public TextMeshProUGUI reasonText;

    [Header("Data Retrieval UI")]
    public GameObject dataPopupPanel; // A simple panel that says "Your code is: 123"
    public TextMeshProUGUI dataCodeText; 
    
    public GameObject dataInputPanel; // A panel with an InputField and a Submit button
    public TMP_InputField dataInputField;
    
    // We need to remember who is typing and for what task
    private PlayerController currentDataPlayer;
    private TaskInstance currentDataTask;

    [Header("Corrupted UI")]
    public GameObject corruptedUIPanel;
    // Arrays strictly sized to 3 to match the Corrupted inventory slots
    public TextMeshProUGUI[] powerUpNameTexts = new TextMeshProUGUI[3];
    public UIIconSpawner[] powerUpIconSpawners = new UIIconSpawner[3];

    [Tooltip("The UI Panels/Images used to highlight the active slot")]
    public GameObject[] slotHighlightPanels = new GameObject[3];

   [Header("In-Game Settings")]
    public GameObject inGameSettingsPanel;
    public string mainMenuSceneName = "MainMenu";
    private bool isSettingsOpen = false;
    public bool IsSettingsOpen => isSettingsOpen;

    // True if something else (e.g. a minigame) had the player frozen before the menu opened,
    // so we don't un-freeze them when the menu closes.
    private bool controlsWereLockedBeforeMenu = false;

    private PlayerController localPlayerCache;

    // Resolved lazily - PlayerController.Local may not be set yet when UIManager wakes.
    private PlayerController LocalPlayer
    {
        get
        {
            if (localPlayerCache == null) localPlayerCache = PlayerController.Local;
            return localPlayerCache;
        }
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        // Listen for the Escape key to open/close the in-game settings
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleInGameSettings();
        }
    }

    // --- TASK UI LOGIC ---

    public void UpdateGlobalMeter(float current, float max)
    {
        if (courtProgressMeter != null)
        {
            courtProgressMeter.maxValue = max;
            courtProgressMeter.value = current;
        }
    }

    public void UpdatePlayerTaskList(PlayerController player, List<TaskInstance> allTasks, List<TaskInstance> activeTasks, PlayerRole role)
    {
        if (taskListText == null) return;

        if (role == PlayerRole.King)
        {
            taskListText.text = "<b><color=#F4D03F>ROLE: KING</color></b>\n<size=80%><color=#D5D8DC>Rule the kingdom and stay alive.</color></size>";
            return;
        }

        StringBuilder sb = new StringBuilder();
        
        if (activeTasks == null || activeTasks.Count == 0)
        {
            sb.AppendLine("<b><color=#58D68D>ALL TASKS COMPLETE!</color></b>");
        }
        else
        {
            sb.AppendLine("<b><color=#5DADE2>YOUR TASKS</color></b>");
        }
        
        sb.AppendLine();

        for (int i = 0; i < allTasks.Count; i++)
        {
            TaskInstance task = allTasks[i];
            if (task == null || task.Definition == null) continue;

            int taskNumber = i + 1;
            bool isCompleted = !activeTasks.Contains(task);

            if (isCompleted)
            {
                sb.AppendLine($"<s><b><color=#7F8C8D>{taskNumber}. {task.Definition.taskName}</color></b></s>");
            }
            else
            {
                string dynamicDescription = GetDynamicTaskDescription(player, task);
                sb.AppendLine($"<b>{taskNumber}. {task.Definition.taskName}</b>");
                sb.AppendLine($"   <size=80%><color=#BDC3C7>{dynamicDescription}</color></size>");
            }
            
            sb.AppendLine();
        }

        taskListText.text = sb.ToString();
    }

    private string GetDynamicTaskDescription(PlayerController player, TaskInstance task)
    {
        // Because we moved all the logic into the TaskStep classes,
        // the UI Manager simply asks the task what to display!
        return task.GetCurrentObjectiveText();
    }

    // --- TRANSITION & ABSENT UI LOGIC ---

    public void ShowTransitionWarning()
    {
        if (transitionPanel != null) transitionPanel.SetActive(true);
    }

    public void UpdateTransitionTimer(float timeRemaining)
    {
        if (transitionTimerText != null)
        {
            // Format to show clean seconds (e.g., "15")
            transitionTimerText.text = $"Meeting starts in: {Mathf.Ceil(timeRemaining)}s";
        }
    }

    public void HideTransitionUI()
    {
        if (transitionPanel != null) transitionPanel.SetActive(false);
    }

    public void ShowAbsentMembers(List<string> absentPlayers)
    {
        if (absentMembersText == null) return;
        
        absentMembersText.gameObject.SetActive(true);
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("<color=#E74C3C><b>Absent court members:</b></color>");
        
        if (absentPlayers.Count == 0)
        {
            sb.AppendLine("<color=#BDC3C7>None (All members present)</color>");
        }
        else
        {
            foreach (string name in absentPlayers)
            {
                sb.AppendLine($"<color=#BDC3C7>{name}</color>");
            }
        }

        absentMembersText.text = sb.ToString();
    }

    // --- VOTING UI LOGIC ---

    // NEW: Called by the VotingManager when the meeting phase officially begins
    public void EnableVotingPhase()
    {
        if (openVoteButton != null) openVoteButton.SetActive(true);
    }

    // CHANGED: This is now triggered manually by the player clicking the Top-Right button
    public void ShowVotingPanel()
    {
        votingPanel.SetActive(true);
        
        if (openVoteButton != null) openVoteButton.SetActive(false);
        
        foreach (Transform child in votingButtonContainer)
        {
            Destroy(child.gameObject);
        }

        // NEW: The Trial Voting Options
        CreateVoteButton("Confirm Execution", "Confirm");
        CreateVoteButton("Deny Execution", "Deny");
        CreateVoteButton("Skip Vote", "Skip");
    }

    public void HideVotingPanel()
    {
        votingPanel.SetActive(false);
        if (openVoteButton != null) openVoteButton.SetActive(false); // Ensure this hides when the meeting ends
    }

    private void CreateVoteButton(string buttonText, string voteOption)
    {
        GameObject newBtnObj = Instantiate(votingButtonPrefab, votingButtonContainer);
        Button btn = newBtnObj.GetComponent<Button>();
        TextMeshProUGUI tmpText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();

        if (tmpText != null) tmpText.text = buttonText;

        btn.onClick.AddListener(() =>
        {
            PlayerController local = LocalPlayer;
            if (VotingManager.Instance != null && local != null)
            {
                VotingManager.Instance.CastVote(local, voteOption);
                btn.image.color = Color.gray;
            }
        });
    }

    public void HideGameOverScreen()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void ShowGameOverScreen(string winner, string reason)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        
        if (winnerText != null) winnerText.text = $"WINNER: {winner}";
        if (reasonText != null) reasonText.text = reason;
    }

    public void RestartMatch()
    {
        Debug.Log("--- RESTARTING MATCH ---");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // --- DATA RETRIEVAL UI LOGIC ---

    public void ShowDataCodePopup(string code)
    {
        if (dataPopupPanel != null && dataCodeText != null)
        {
            dataPopupPanel.SetActive(true);
            dataCodeText.text = $"MEMORIZE CODE:\n<b>{code}</b>";
            
            // Automatically hide it after 4 seconds to force them to memorize it!
            Invoke(nameof(HideDataCodePopup), 4f); 
        }
    }

    private void HideDataCodePopup()
    {
        if (dataPopupPanel != null) dataPopupPanel.SetActive(false);
    }

    public void OpenDataInputPanel(PlayerController player, TaskInstance task)
    {
        if (dataInputPanel != null && dataInputField != null)
        {
            currentDataPlayer = player;
            currentDataTask = task;
            
            dataInputField.text = ""; // Clear old text
            dataInputPanel.SetActive(true);
        }
    }

    public void CloseDataInputPanel()
    {
        if (dataInputPanel != null) dataInputPanel.SetActive(false);
        
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Link this to the "Submit" Button on your Data Input Panel in the Unity Inspector!
    public void SubmitDataCode()
    {
        if (currentDataPlayer != null && currentDataTask != null)
        {
            string playerInput = dataInputField.text;

            // Compare against THIS player's code, held on the runtime state - never on the asset.
            TaskStep activeStep = currentDataTask.GetCurrentStep();
            TaskStepRuntime runtime = currentDataTask.CurrentStepRuntime;

            if (activeStep is DataRetrievalStep && runtime != null)
            {
                if (playerInput == runtime.GeneratedCode)
                {
                    Debug.Log("Code Accepted! Data Retrieval Complete.");

                    // We must manually complete the step here because clicking a UI button
                    // doesn't trigger the PlayerController's physical interaction raycast!
                    currentDataTask.CompleteActiveStep();

                    if (TaskManager.Instance != null)
                    {
                        // Check if that was the final step in the task
                        if (currentDataTask.IsComplete)
                        {
                            TaskManager.Instance.CompleteTask(currentDataPlayer, currentDataTask);
                        }
                    }
                    currentDataPlayer.RefreshLocalWaypoints();
                }
                else
                {
                    Debug.Log("INCORRECT CODE. Connection failed.");
                }
            }
        }
        
        CloseDataInputPanel();
    }

    // --- CORRUPTED UI LOGIC ---
    public void UpdateCorruptedInventory(PowerUpData[] inventory)
    {
        // Automatically show the panel if they pick up their first item
        if (corruptedUIPanel != null && !corruptedUIPanel.activeSelf)
        {
            corruptedUIPanel.SetActive(true);
        }

        // We know there are exactly 3 slots, so we loop through them
        for (int i = 0; i < 3; i++)
        {
            PowerUpData powerUp = inventory[i];
            
            // If the slot has an item in it
            if (powerUp != null)
            {
                if (powerUpNameTexts[i] != null) 
                    powerUpNameTexts[i].text = $"[{i + 1}] {powerUp.powerUpName}";
                
                if (powerUpIconSpawners[i] != null) 
                    powerUpIconSpawners[i].SetIcon(powerUp.iconPrefab);
            }
            // If the slot is empty
            else
            {
                if (powerUpNameTexts[i] != null) 
                    powerUpNameTexts[i].text = $"[{i + 1}] Empty";
                
                if (powerUpIconSpawners[i] != null) 
                    powerUpIconSpawners[i].SetIcon(null);
            }
        }
    }

     // --- NEW: HIGHLIGHT LOGIC ---
    public void HighlightSlot(int activeIndex)
    {
        for (int i = 0; i < 3; i++)
        {
            if (slotHighlightPanels[i] != null)
            {
                // Turn on the highlight only for the active slot. Turn off the rest.
                slotHighlightPanels[i].SetActive(i == activeIndex);
            }
        }
    }

    // --- IN-GAME SETTINGS LOGIC ---
    public void ToggleInGameSettings()
    {
        isSettingsOpen = !isSettingsOpen;
        if (inGameSettingsPanel != null) inGameSettingsPanel.SetActive(isSettingsOpen);

        // Freeze / unfreeze the local player so the menu can be used independently.
        PlayerController local = LocalPlayer;
        if (local != null)
        {
            if (isSettingsOpen)
            {
                controlsWereLockedBeforeMenu = local.controlsLocked; // e.g. a minigame already froze them
                local.SetControlsLocked(true);
            }
            else if (!controlsWereLockedBeforeMenu)
            {
                local.SetControlsLocked(false); // only un-freeze if the menu was what froze them
            }
        }

        if (isSettingsOpen)
        {
            // Free the mouse so they can click the button
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // Only re-lock the mouse if the game is actively being played (not during a meeting/game over)
            if (MatchManager.Instance != null && MatchManager.Instance.currentState == MatchManager.MatchState.ActionStage)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    public void ReturnToMainMenu()
    {
        Debug.Log("Returning to Main Menu...");
        SceneManager.LoadScene(mainMenuSceneName);
    }
}