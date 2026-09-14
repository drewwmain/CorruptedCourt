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
            GameEvents.PlayerGhosted       += OnAnyPlayerGhosted;
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
            GameEvents.PlayerGhosted       -= OnAnyPlayerGhosted;
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

            // Ghost spectator HUD: repaint only when Haunt readiness actually flips, so the panel
            // never rebuilds a string per-frame. The ghostHudActive flag keeps this free for the living.
            if (ghostHudActive)
            {
                PlayerController local = LocalPlayer;
                if (local != null && local.Vitals != null && local.Vitals.isGhost)
                {
                    bool ready = local.Ghost != null && local.Ghost.IsHauntReady;
                    if (ready != lastHauntReady) RenderGhostHud(local);
                }
                else
                {
                    ghostHudActive = false;
                }
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

            // Dead players don't have tasks - they get the spectator roster instead (G3.2).
            if (player != null && player.Vitals != null && player.Vitals.isGhost)
            {
                RenderGhostHud(player);
                return;
            }

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
            string text = task.GetCurrentObjectiveText();

            // ObjectiveTarget.Hint is how R2's fallback ("polish a sword first") and R4's edge case
            // ("every candle is lit") reach the player (ARCHITECTURE.md §12) - there's no separate
            // hint UI, so it rides along on this same line rather than the waypoint marker itself.
            string hint = task.GetCurrentObjectiveTarget(player).Hint;
            if (!string.IsNullOrEmpty(hint)) text += $" ({hint})";

            return text;
        }

        // --- GHOST SPECTATOR HUD (G3.2) ---
        // The dead get full role visibility: the task panel becomes a roster of every court member
        // and their true faction. Painted on death, on any later death, and when Haunt readiness
        // flips (see Update). No gameplay call originates here - it is a pure view.

        private bool ghostHudActive;
        private bool lastHauntReady;

        // A player died. If the local player is (or just became) a ghost, repaint their roster.
        private void OnAnyPlayerGhosted(PlayerController ghosted)
        {
            PlayerController local = LocalPlayer;
            if (local != null && local.Vitals != null && local.Vitals.isGhost)
                RenderGhostHud(local);
        }

        // When a Stolen Heraldry disguise makes two players share a display name, a plain list would
        // show the name twice and read as a UI bug. Given the display names about to be listed (order
        // preserved), this returns each one suffixed " (1)", " (2)", ... IF that name occurs more than
        // once, so the duplication reads as deliberate. Fires on meeting / panel events, never per-frame.
        private static List<string> DisambiguateNameList(List<string> names)
        {
            Dictionary<string, int> totals = new Dictionary<string, int>();
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i] ?? "?";
                totals[n] = totals.TryGetValue(n, out int c) ? c + 1 : 1;
            }

            Dictionary<string, int> seen = new Dictionary<string, int>();
            List<string> result = new List<string>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i] ?? "?";
                if (totals[n] > 1)
                {
                    int idx = seen.TryGetValue(n, out int s) ? s + 1 : 1;
                    seen[n] = idx;
                    result.Add($"{n} ({idx})");
                }
                else
                {
                    result.Add(n);
                }
            }
            return result;
        }

        private void RenderGhostHud(PlayerController local)
        {
            if (taskListText == null || local == null) return;

            ghostHudActive = true;
            lastHauntReady = local.Ghost != null && local.Ghost.IsHauntReady;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b><color=#B39DDB>YOU ARE A GHOST</color></b>");
            sb.AppendLine("<size=80%><color=#BDC3C7>Fly with WASD + mouse. Help the Court by watching - " +
                          "you now see every court member's true allegiance.</color></size>");
            sb.AppendLine();
            sb.AppendLine(lastHauntReady
                ? "<b><color=#8E44AD>HAUNT READY</color></b> <size=80%><color=#BDC3C7>- Left Click near the living</color></size>"
                : "<size=90%><color=#7F8C8D>Haunt is recharging...</color></size>");
            sb.AppendLine();
            sb.AppendLine("<b><color=#5DADE2>THE COURT</color></b>");

            if (RoleManager.Instance != null)
            {
                List<PlayerController> roster = new List<PlayerController>();
                foreach (PlayerController p in RoleManager.Instance.allPlayers)
                    if (p != null && p.Vitals != null) roster.Add(p);

                List<string> names = new List<string>(roster.Count);
                foreach (PlayerController p in roster) names.Add(p.DisplayName);
                List<string> shown = DisambiguateNameList(names);

                for (int i = 0; i < roster.Count; i++)
                {
                    PlayerController p = roster[i];
                    bool corrupt = p.Vitals.faction == Faction.Corrupted;
                    string factionName = corrupt ? "Corrupted" : "Court";
                    string titleTag = p.Vitals.courtTitle == CourtTitle.King ? " (King)"
                                    : p.Vitals.courtTitle == CourtTitle.Kingsguard ? " (Kingsguard)"
                                    : "";

                    if (p.Vitals.isGhost)
                        sb.AppendLine($"<s><color=#7F8C8D>{shown[i]} - {factionName}{titleTag} (dead)</color></s>");
                    else
                        sb.AppendLine($"<color={(corrupt ? "#E74C3C" : "#58D68D")}>{shown[i]} - {factionName}{titleTag}</color>");
                }
            }

            taskListText.text = sb.ToString();
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

        private void OnAbsentPlayersChanged(IReadOnlyList<AbsentMember> absentMembers, IReadOnlyList<string> deadPlayers)
        {
            if (absentMembersText == null) return;

            absentMembersText.gameObject.SetActive(true);
            StringBuilder sb = new StringBuilder();

            // Disambiguate across the WHOLE roll-call (absent + dead) so a disguised player and the
            // player they copied read as two deliberate entries, not one duplicated bug - even when
            // one is absent and the other dead.
            int absentCount = absentMembers != null ? absentMembers.Count : 0;
            int deadCount = deadPlayers != null ? deadPlayers.Count : 0;

            List<string> rollCall = new List<string>(absentCount + deadCount);
            for (int i = 0; i < absentCount; i++) rollCall.Add(absentMembers[i].Name);
            for (int i = 0; i < deadCount; i++) rollCall.Add(deadPlayers[i]);
            List<string> shown = DisambiguateNameList(rollCall);

            sb.AppendLine("<color=#E74C3C><b>Absent court members:</b></color>");

            if (absentCount == 0)
            {
                sb.AppendLine("<color=#BDC3C7>None (all living members present)</color>");
            }
            else
            {
                // Show WHERE each absent player started the scramble, not just that they're missing:
                // someone who began across the map had a long way to come, not a bare accusation.
                for (int i = 0; i < absentCount; i++)
                {
                    string origin = absentMembers[i].StartDistance == ScrambleStartDistance.AcrossTheMap
                        ? "<color=#E67E22>from across the map</color>"
                        : "<color=#7F8C8D>nearby</color>";
                    sb.AppendLine($"<color=#BDC3C7>{shown[i]}</color>  <size=80%>({origin})</size>");
                }
            }

            // Dead members are listed separately so every name in the lobby is accounted for:
            // present in the room, absent from it, or dead.
            sb.AppendLine();
            sb.AppendLine("<color=#922B21><b>Confirmed dead:</b></color>");

            if (deadCount == 0)
            {
                sb.AppendLine("<color=#BDC3C7>None</color>");
            }
            else
            {
                for (int i = 0; i < deadCount; i++)
                {
                    sb.AppendLine($"<s><color=#7F8C8D>{shown[absentCount + i]}</color></s>");
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
                // Inquest nomination phase: one button per living court member. Disambiguate the
                // labels so a Stolen Heraldry disguise shows as two deliberate entries, not a dupe.
                if (RoleManager.Instance != null)
                {
                    List<PlayerController> living = new List<PlayerController>();
                    foreach (PlayerController p in RoleManager.Instance.allPlayers)
                        if (p != null && p.Vitals != null && !p.Vitals.isGhost) living.Add(p);

                    List<string> names = new List<string>(living.Count);
                    foreach (PlayerController p in living) names.Add(p.DisplayName);
                    List<string> shown = DisambiguateNameList(names);

                    for (int i = 0; i < living.Count; i++)
                    {
                        PlayerController nominee = living[i]; // capture per-iteration for the closure
                        CreateVoteButton($"Nominate {shown[i]}", () =>
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
