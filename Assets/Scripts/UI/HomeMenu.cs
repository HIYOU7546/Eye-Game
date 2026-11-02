using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class HomeMenu : MonoBehaviour
{
    [Header("Panels")]
    public GameObject homePanel;            // "Home"
    public SettingsMenu settingsMenu;       // "Settings"

    [Header("Inputs (add two TMP Input Fields to Home)")]
    public TMP_InputField playerNameField;
    public TMP_InputField roomNameField;
    [Tooltip("Optional: small text that shows problems like 'Enter a name'")]
    public TMP_Text messageLabel;

    [Header("Buttons")]
    public Button createButton, joinButton, storyButton, settingsButton, closeGameButton;

    [Header("Scenes")]
    public string gameSceneName = "TestScene";
    [Tooltip("Leave empty while story mode is 'coming soon'")]
    public string storySceneName = "";

    void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        homePanel.SetActive(true);
        settingsMenu.gameObject.SetActive(false);

        playerNameField.characterLimit = 16;
        roomNameField.characterLimit = 20;
        playerNameField.text = GameSession.PlayerName;
        roomNameField.text = GameSession.RoomName;
        playerNameField.onEndEdit.AddListener(t => GameSession.PlayerName = t);
        roomNameField.onEndEdit.AddListener(t => GameSession.RoomName = t);

        createButton.onClick.AddListener(() => StartGame(GameSession.JoinMode.Create));
        joinButton.onClick.AddListener(() => StartGame(GameSession.JoinMode.Join));
        storyButton.onClick.AddListener(OnStory);
        settingsButton.onClick.AddListener(OpenSettings);
        closeGameButton.onClick.AddListener(QuitGame);

        settingsMenu.Closed += () => homePanel.SetActive(true);
    }

    void StartGame(GameSession.JoinMode mode)
    {
        string player = GameSession.Clean(playerNameField.text, 16);
        string room = GameSession.Clean(roomNameField.text, 20);

        if (player.Length == 0) { Say("Enter a player name"); return; }

        if (room.Length == 0)
        {
            if (mode == GameSession.JoinMode.Join) { Say("Enter the name of the room to join"); return; }
            room = "Room" + Random.Range(1000, 10000);   // Create with no name: make one up
            roomNameField.text = room;
        }

        GameSession.PlayerName = player;
        GameSession.RoomName = room;
        GameSession.Mode = mode;
        SceneManager.LoadScene(gameSceneName);
    }

    void OnStory()
    {
        if (string.IsNullOrEmpty(storySceneName)) { Say("Story mode: coming soon"); return; }
        SceneManager.LoadScene(storySceneName);
    }

    void OpenSettings()
    {
        homePanel.SetActive(false);
        settingsMenu.Open();
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Say(string text)
    {
        if (messageLabel != null) messageLabel.text = text;
        else Debug.LogWarning(text);
    }
}