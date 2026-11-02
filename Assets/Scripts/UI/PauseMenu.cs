using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

// Escape / gamepad Start opens it. In multiplayer the game keeps running, only YOUR controls are switched off.
public class PauseMenu : MonoBehaviourPunCallbacks
{
    [Tooltip("Drag your Settings prefab here (the same panel the home menu uses).")]
    public SettingsMenu settingsMenuPrefab;
    public string menuSceneName = "Menu";

    GameObject dim, box;
    TMP_Text info;
    SettingsMenu settings;
    bool paused, leaving;

    void Start() => BuildUI();

    void Update()
    {
        bool cancel = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                      (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
        bool back = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;

        if (!paused)
        {
            if (cancel && NetworkPlayer.Local != null) SetPaused(true);
            return;
        }

        if (!(cancel || back)) return;
        if (settings != null && settings.gameObject.activeSelf) settings.Close();
        else if (cancel) SetPaused(false);
    }

    void SetPaused(bool value)
    {
        paused = value;
        dim.SetActive(value);
        box.SetActive(true);
        if (!value && settings != null) settings.gameObject.SetActive(false);

        // switch only the local player's controls off/on (enabling PlayerController locks the cursor again)
        var np = NetworkPlayer.Local;
        if (np != null)
        {
            if (np.controller != null) np.controller.enabled = !value;
            if (np.hands != null) np.hands.enabled = !value;
            var interactor = np.GetComponentInChildren<PlayerInteractor>(true);
            if (interactor != null) interactor.enabled = !value;
        }

        if (value)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            info.text = PhotonNetwork.InRoom
                ? "Room: " + PhotonNetwork.CurrentRoom.Name + "   Players: " + PhotonNetwork.CurrentRoom.PlayerCount
                : "";
        }
    }

    void OpenSettings()
    {
        if (settings == null) return;
        box.SetActive(false);
        settings.Open();
    }

    void LeaveRoom()
    {
        if (leaving) return;
        leaving = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (PhotonNetwork.IsConnected) PhotonNetwork.Disconnect();   // OnDisconnected loads the menu
        else SceneManager.LoadScene(menuSceneName);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(menuSceneName);
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------ UI built in code

    void BuildUI()
    {
        var canvasGO = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        dim = NewUI("Dim", canvasGO.transform, typeof(Image));
        Stretch(dim);
        dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        box = NewUI("Box", dim.transform, typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var rt = (RectTransform)box.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(460f, 0f);
        box.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.92f);
        var vlg = box.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 28, 28);
        vlg.spacing = 14f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        box.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        MakeText("PAUSED", box.transform, 48, FontStyles.Bold);
        info = MakeText("", box.transform, 22, FontStyles.Normal);
        MakeButton("Resume", box.transform, () => SetPaused(false));
        MakeButton("Settings", box.transform, OpenSettings);
        MakeButton("Leave Room", box.transform, LeaveRoom);
        MakeButton("Quit Game", box.transform, QuitGame);

        if (settingsMenuPrefab != null)
        {
            settings = Instantiate(settingsMenuPrefab, canvasGO.transform, false);
            settings.closeOnCancel = false;                 // this script handles Escape
            settings.Closed += () => box.SetActive(true);
            settings.gameObject.SetActive(false);
        }
        else Debug.LogWarning("PauseMenu: assign the Settings prefab to get a Settings button that works.");

        dim.SetActive(false);
    }

    static GameObject NewUI(string name, Transform parent, params System.Type[] components)
    {
        var go = new GameObject(name, typeof(RectTransform));
        foreach (var c in components) go.AddComponent(c);
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(GameObject go)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static TMP_Text MakeText(string text, Transform parent, float size, FontStyles style)
    {
        var go = NewUI("Text", parent, typeof(TextMeshProUGUI));
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        return t;
    }

    static void MakeButton(string label, Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        var go = NewUI(label, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
        go.GetComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f, 1f);
        go.GetComponent<LayoutElement>().preferredHeight = 56f;
        go.GetComponent<Button>().onClick.AddListener(onClick);

        var t = MakeText(label, go.transform, 26, FontStyles.Normal);
        t.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        Stretch(t.gameObject);
    }
}