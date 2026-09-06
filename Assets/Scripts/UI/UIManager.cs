using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Text;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.UI
{
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
        [Tooltip("Optional. Shows the body-report line (who found whom, and where) for a meeting opened " +
                 "from a corpse report. Hidden again when the next action stage begins.")]
        public TextMeshProUGUI meetingAnnouncementText;
        [Tooltip("Optional. Shows the meeting verdict and the full vote breakdown (confirm / deny / " +
                 "abstained) after a tally. Cleared when a fresh meeting or action stage begins.")]
        public TextMeshProUGUI meetingResultText;

        [Tooltip("Optional. The global-sabotage alert banner: shows the active team sabotage and its " +
                 "auto-resolve countdown, then a brief resolution notice when it ends.")]
        public TextMeshProUGUI sabotageAlertText;

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

        // Label of the global sabotage currently showing in the alert banner, so per-second ticks can
        // rebuild the line without the event having to re-send it.
        private string activeSabotageLabel = "";

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

        void OnEnable()
        {
            GameEvents.LocalTasksChanged   += OnLocalTasksChanged;
            GameEvents.CourtProgressChanged += UpdateGlobalMeter;
            GameEvents.MatchStateChanged   += OnMatchStateChanged;
            GameEvents.AbsentPlayersChanged += OnAbsentPlayersChanged;
            GameEvents.MeetingAnnouncement  += OnMeetingAnnouncement;
            GameEvents.MeetingResult        += OnMeetingResult;
            GameEvents.GlobalSabotageStarted += OnGlobalSabotageStarted;
            GameEvents.GlobalSabotageTick    += OnGlobalSabotageTick;
            GameEvents.GlobalSabotageEnded   += OnGlobalSabotageEnded;
            GameEvents.CorruptedInventoryChanged += UpdateCorruptedInventory;
            GameEvents.CorruptedSlotHighlighted  += HighlightSlot;
            GameEvents.TransitionTimerTicked     += UpdateTransitionTimer;
            GameEvents.GameOverShown             += ShowGameOverScreen;
            GameEvents.GameOverHidden            += HideGameOverScreen;
            GameEvents.NominationPhaseStarted    += EnableVotingPhase;
            GameEvents.VotingPhaseStarted        += EnableVotingPhase;
            GameEvents.VotingPanelHidden         += HideVotingPanel;

            // We may have missed raises that happened before this object was enabled - pull the
            // current values so the view is correct on scene load and after a manual re-enable.
            if (TaskManager.Instance != null)
                UpdateGlobalMeter(TaskManager.Instance.currentCourtProgress, TaskManager.Instance.maxCourtProgress);
            if (MatchManager.Instance != null)
                OnMatchStateChanged(MatchManager.Instance.currentState);
            if (PlayerController.Local != null)
                OnLocalTasksChanged(PlayerController.Local);
            // Fresh UIManager => menu is closed; keep the shared flag in sync.
            GameEvents.RaiseSettingsMenuToggled(isSettingsOpen);
        }

        void OnDisable()
        {
            GameEvents.LocalTasksChanged   -= OnLocalTasksChanged;
            GameEvents.CourtProgressChanged -= UpdateGlobalMeter;
            GameEvents.MatchStateChanged   -= OnMatchStateChanged;
            GameEvents.AbsentPlayersChanged -= OnAbsentPlayersChanged;
            GameEvents.MeetingAnnouncement  -= OnMeetingAnnouncement;
            GameEvents.MeetingResult        -= OnMeetingResult;
            GameEvents.GlobalSabotageStarted -= OnGlobalSabotageStarted;
            GameEvents.GlobalSabotageTick    -= OnGlobalSabotageTick;
            GameEvents.GlobalSabotageEnded   -= OnGlobalSabotageEnded;
            GameEvents.CorruptedInventoryChanged -= UpdateCorruptedInventory;
            GameEvents.CorruptedSlotHighlighted  -= HighlightSlot;
            GameEvents.TransitionTimerTicked     -= UpdateTransitionTimer;
            GameEvents.GameOverShown             -= ShowGameOverScreen;
            GameEvents.GameOverHidden            -= HideGameOverScreen;
            GameEvents.NominationPhaseStarted    -= EnableVotingPhase;
            GameEvents.VotingPhaseStarted        -= EnableVotingPhase;
            GameEvents.VotingPanelHidden         -= HideVotingPanel;
        }

        // --- GAMEPLAY EVENT HANDLERS (views react; gameplay never calls us) ---

        private void OnLocalTasksChanged(PlayerController player)
        {
            if (player == null) return;

            UpdatePlayerTaskList(player, player.TaskBook.allAssignedTasks, player.TaskBook.activeTasks, player.Vitals.courtTitle);

            // One-shot signals from a DataRetrievalStep: part 1 flags the runtime for the "memorize"
            // popup, part 2 flags it to open the code-entry panel. The step never calls us directly.
            if (player.TaskBook.activeTasks != null)
            {
                foreach (TaskInstance task in player.TaskBook.activeTasks)
                {
                    if (task == null) continue;
                    TaskStepRuntime rt = task.CurrentStepRuntime;
                    if (rt == null || !(task.GetCurrentStep() is DataRetrievalStep)) continue;

                    if (rt.CodeRevealPending)
                    {
                        rt.CodeRevealPending = false;
                        ShowDataCodePopup(rt.GeneratedCode);
                    }
                    else if (rt.DataInputPending)
                    {
                        rt.DataInputPending = false;
                        OpenDataInputPanel(player, task);
                    }
                }
            }
        }

        private void OnMatchStateChanged(MatchManager.MatchState state)
        {
            // The 20s scramble panel is visible only during the transition-to-meeting state.
            if (transitionPanel != null)
                transitionPanel.SetActive(state == MatchManager.MatchState.TransitionToMeeting);

            // The body-report line belongs to one meeting only - clear it once play resumes.
            if (meetingAnnouncementText != null
                && (state == MatchManager.MatchState.ActionStage || state == MatchManager.MatchState.Initialization))
            {
                meetingAnnouncementText.text = "";
                meetingAnnouncementText.gameObject.SetActive(false);
            }

            // The tally is computed at meeting-end, right before the next action stage - so the
            // breakdown stays up through that action stage and only clears when the NEXT meeting's
            // transition begins (or the match resets).
            if (meetingResultText != null
                && (state == MatchManager.MatchState.TransitionToMeeting || state == MatchManager.MatchState.Initialization))
            {
                meetingResultText.text = "";
                meetingResultText.gameObject.SetActive(false);
            }

            // Sabotages don't run outside the action stage - clear the banner the moment play stops.
            if (state != MatchManager.MatchState.ActionStage)
            {
                CancelInvoke(nameof(HideSabotageAlert));
                HideSabotageAlert();
            }
        }

        // A meeting was opened by a corpse report - show who found the body and where.
        private void OnMeetingAnnouncement(string announcement)
        {
            if (meetingAnnouncementText == null) return;
            meetingAnnouncementText.gameObject.SetActive(true);
            meetingAnnouncementText.text = $"<color=#E74C3C><b>{announcement}</b></color>";
        }

        // A meeting resolved - show the verdict and the full confirm / deny / abstained breakdown.
        private void OnMeetingResult(string summary)
        {
            if (meetingResultText == null) return;
            meetingResultText.gameObject.SetActive(true);
            meetingResultText.text = summary;
        }

        // --- GLOBAL SABOTAGE ALERT ---

        private void OnGlobalSabotageStarted(string label, int seconds)
        {
            activeSabotageLabel = label;
            if (sabotageAlertText == null) return;

            CancelInvoke(nameof(HideSabotageAlert));
            sabotageAlertText.gameObject.SetActive(true);
            sabotageAlertText.text = $"<color=#E74C3C><b>SABOTAGE: {label}</b></color>  <color=#F4D03F>{seconds}s</color>";
        }

        private void OnGlobalSabotageTick(int seconds)
        {
            if (sabotageAlertText == null || !sabotageAlertText.gameObject.activeSelf) return;
            sabotageAlertText.text = $"<color=#E74C3C><b>SABOTAGE: {activeSabotageLabel}</b></color>  <color=#F4D03F>{seconds}s</color>";
        }

        private void OnGlobalSabotageEnded(string label, bool autoResolved)
        {
            if (sabotageAlertText == null) return;

            sabotageAlertText.gameObject.SetActive(true);
            sabotageAlertText.text = autoResolved
                ? $"<color=#F39C12><b>{label} ran its course.</b></color>"
                : $"<color=#2ECC71><b>{label} contained by the Court.</b></color>";

            CancelInvoke(nameof(HideSabotageAlert));
            Invoke(nameof(HideSabotageAlert), 4f);
        }

        private void HideSabotageAlert()
        {
            if (sabotageAlertText == null) return;
            sabotageAlertText.text = "";
            sabotageAlertText.gameObject.SetActive(false);
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

        public void UpdatePlayerTaskList(PlayerController player, List<TaskInstance> allTasks, List<TaskInstance> activeTasks, CourtTitle title)
        {
            if (taskListText == null) return;

            if (title == CourtTitle.King)
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
        // transitionPanel show/hide is driven by OnMatchStateChanged. Absent list by OnAbsentPlayersChanged.

        public void UpdateTransitionTimer(float timeRemaining)
        {
            if (transitionTimerText != null)
            {
                // Format to show clean seconds (e.g., "15")
                transitionTimerText.text = $"Meeting starts in: {Mathf.Ceil(timeRemaining)}s";
            }
        }

        private void OnAbsentPlayersChanged(IReadOnlyList<string> absentPlayers, IReadOnlyList<string> deadPlayers)
        {
            if (absentMembersText == null) return;

            absentMembersText.gameObject.SetActive(true);
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("<color=#E74C3C><b>Absent court members:</b></color>");

            if (absentPlayers == null || absentPlayers.Count == 0)
            {
                sb.AppendLine("<color=#BDC3C7>None (all living members present)</color>");
            }
            else
            {
                foreach (string name in absentPlayers)
                {
                    sb.AppendLine($"<color=#BDC3C7>{name}</color>");
                }
            }

            // Dead members are listed separately so every name in the lobby is accounted for:
            // present in the room, absent from it, or dead.
            sb.AppendLine();
            sb.AppendLine("<color=#922B21><b>Confirmed dead:</b></color>");

            if (deadPlayers == null || deadPlayers.Count == 0)
            {
                sb.AppendLine("<color=#BDC3C7>None</color>");
            }
            else
            {
                foreach (string name in deadPlayers)
                {
                    sb.AppendLine($"<s><color=#7F8C8D>{name}</color></s>");
                }
            }

            absentMembersText.text = sb.ToString();
        }

        // --- VOTING UI LOGIC ---

        // Called when the meeting opens for input - either the inquest nomination phase
        // (NominationPhaseStarted) or a trial vote (VotingPhaseStarted).
        public void EnableVotingPhase()
        {
            if (openVoteButton != null) openVoteButton.SetActive(true);

            // If the panel is already open (e.g. a nomination phase just resolved into the trial vote),
            // rebuild its contents for the phase we are now in.
            if (votingPanel != null && votingPanel.activeSelf) ShowVotingPanel();
        }

        // CHANGED: This is now triggered manually by the player clicking the Top-Right button. The panel
        // contents depend on the meeting's current phase: nominate a suspect, or cast a trial vote.
        public void ShowVotingPanel()
        {
            if (votingPanel == null || votingButtonContainer == null) return;

            votingPanel.SetActive(true);
            if (openVoteButton != null) openVoteButton.SetActive(false);

            foreach (Transform child in votingButtonContainer)
            {
                Destroy(child.gameObject);
            }

            VotingManager vm = VotingManager.Instance;
            if (vm != null && vm.AwaitingNominations)
            {
                // Inquest nomination phase: one button per living court member.
                if (RoleManager.Instance != null)
                {
                    foreach (PlayerController p in RoleManager.Instance.allPlayers)
                    {
                        if (p == null || p.Vitals == null || p.Vitals.isGhost) continue;
                        PlayerController nominee = p; // capture per-iteration for the closure
                        CreateVoteButton($"Nominate {p.gameObject.name}", () =>
                        {
                            PlayerController local = LocalPlayer;
                            if (VotingManager.Instance != null && local != null)
                                VotingManager.Instance.CastNomination(local, nominee);
                        });
                    }
                }
            }
            else if (vm != null && LocalPlayer != null && LocalPlayer == vm.condemnedPlayer)
            {
                // The defendant cannot vote in their own trial - say so instead of offering the options.
                CreateVoteButton("You are the defendant - you cannot vote", null);
            }
            else
            {
                // Trial vote: Gallows trial, or an inquest that has reached a defendant.
                CreateVoteButton("Confirm Execution", () => CastLocalVote(VotingManager.VoteConfirm));
                CreateVoteButton("Deny Execution",    () => CastLocalVote(VotingManager.VoteDeny));
                CreateVoteButton("Skip (abstain)",    () => CastLocalVote(VotingManager.VoteSkip));
            }
        }

        public void HideVotingPanel()
        {
            if (votingPanel != null) votingPanel.SetActive(false);
            if (openVoteButton != null) openVoteButton.SetActive(false); // Ensure this hides when the meeting ends
        }

        private void CastLocalVote(string voteOption)
        {
            PlayerController local = LocalPlayer;
            if (VotingManager.Instance != null && local != null)
                VotingManager.Instance.CastVote(local, voteOption);
        }

        private void CreateVoteButton(string buttonText, System.Action onClick)
        {
            GameObject newBtnObj = Instantiate(votingButtonPrefab, votingButtonContainer);
            Button btn = newBtnObj.GetComponent<Button>();
            TextMeshProUGUI tmpText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();

            if (tmpText != null) tmpText.text = buttonText;
            if (btn == null) return;

            btn.onClick.AddListener(() =>
            {
                onClick?.Invoke();
                btn.image.color = Color.gray;
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
            Log.Game("--- RESTARTING MATCH ---");
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
                        Log.Game("Code Accepted! Data Retrieval Complete.");

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
                        currentDataPlayer.TaskBook.RefreshLocalWaypoints();
                    }
                    else
                    {
                        Log.Game("INCORRECT CODE. Connection failed.");
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
            GameEvents.RaiseSettingsMenuToggled(isSettingsOpen);
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
            Log.Game("Returning to Main Menu...");
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}
