using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject mainMenuPanel;
    public GameObject characterAppearancePanel;
    public GameObject settingsPanel;

    [Header("Scene Settings")]
    [Tooltip("Type the exact name of your main gameplay scene here")]
    public string gameSceneName = "GameScene"; 

    void Start()
    {
        // Ensure only the main menu is active when the game boots up
        ShowMainMenu();
        
        // Unlock the cursor so the player can click the buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void PlayGame()
    {
        Debug.Log("Loading the Game...");
        SceneManager.LoadScene(gameSceneName);
    }

    public void OpenCharacterAppearance()
    {
        mainMenuPanel.SetActive(false);
        characterAppearancePanel.SetActive(true);
        settingsPanel.SetActive(false);
    }

    public void OpenSettings()
    {
        mainMenuPanel.SetActive(false);
        characterAppearancePanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    public void ShowMainMenu()
    {
        mainMenuPanel.SetActive(true);
        
        if (characterAppearancePanel != null) characterAppearancePanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void QuitGame()
    {
        Debug.Log("Quitting the Game...");
        Application.Quit();
    }
}