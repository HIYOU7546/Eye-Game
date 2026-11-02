using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class SettingsMenu : MonoBehaviour
{
    [Header("Tabs (button -> panel), same order")]
    public Button multiplayerTab, videoTab, audioTab, moreTab;
    public GameObject multiplayerPanel, videoPanel, audioPanel, morePanel;
    public int startTab = 0;

    [Header("Audio sliders (0..1)")]
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;
    [Tooltip("The 'Extra' slider = voice chat volume")]
    public Slider voiceSlider;
    [Tooltip("Optional percentage texts next to the sliders")]
    public TMP_Text masterLabel, musicLabel, sfxLabel, voiceLabel;

    [Header("Optional extras (leave empty if you don't add them)")]
    public Toggle fullscreenToggle;
    public TMP_Dropdown qualityDropdown;
    public Toggle micToggle;

    [Header("Bottom buttons")]
    public Button saveButton, resetButton, closeButton;

    [Header("Multiplayer tab")]
    public PlayerVolumeList playerList;

    [Tooltip("Escape / gamepad B closes the panel. The pause menu turns this off and handles it itself.")]
    public bool closeOnCancel = true;

    public event Action Closed;

    readonly List<Action> refreshers = new List<Action>();
    GameObject[] panels;

    public void Open() => gameObject.SetActive(true);

    public void Close()
    {
        GameSettings.Load();                 // anything not saved is thrown away
        gameObject.SetActive(false);
        Closed?.Invoke();
    }

    void Awake()
    {
        panels = new[] { multiplayerPanel, videoPanel, audioPanel, morePanel };
        var tabs = new[] { multiplayerTab, videoTab, audioTab, moreTab };
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            if (tabs[i] != null) tabs[i].onClick.AddListener(() => ShowTab(index));
        }

        BindSlider(masterSlider, masterLabel, v => GameSettings.Master = v, () => GameSettings.Master);
        BindSlider(musicSlider, musicLabel, v => GameSettings.Music = v, () => GameSettings.Music);
        BindSlider(sfxSlider, sfxLabel, v => GameSettings.Sfx = v, () => GameSettings.Sfx);
        BindSlider(voiceSlider, voiceLabel, v => GameSettings.Voice = v, () => GameSettings.Voice);

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.AddListener(v => { GameSettings.Fullscreen = v; GameSettings.Apply(); });
            refreshers.Add(() => fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen));
        }
        if (micToggle != null)
        {
            micToggle.onValueChanged.AddListener(v => GameSettings.VoiceEnabled = v);
            refreshers.Add(() => micToggle.SetIsOnWithoutNotify(GameSettings.VoiceEnabled));
        }
        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(new List<string>(QualitySettings.names));
            qualityDropdown.onValueChanged.AddListener(v => { GameSettings.Quality = v; GameSettings.Apply(); });
            refreshers.Add(() => qualityDropdown.SetValueWithoutNotify(GameSettings.Quality));
        }

        if (saveButton != null) saveButton.onClick.AddListener(GameSettings.Save);
        if (resetButton != null) resetButton.onClick.AddListener(() => { GameSettings.ResetToDefaults(); Refresh(); });
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    void OnEnable()
    {
        Refresh();
        ShowTab(startTab);
    }

    void Update()
    {
        if (!closeOnCancel) return;
        bool cancel = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                      (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
        if (cancel) Close();
    }

    void BindSlider(Slider s, TMP_Text label, Action<float> set, Func<float> get)
    {
        if (s == null) return;
        s.minValue = 0f; s.maxValue = 1f; s.wholeNumbers = false;
        s.onValueChanged.AddListener(v =>
        {
            set(v);
            if (label != null) label.text = Mathf.RoundToInt(v * 100f) + "%";
            GameSettings.Apply();
        });
        refreshers.Add(() =>
        {
            s.SetValueWithoutNotify(get());
            if (label != null) label.text = Mathf.RoundToInt(get() * 100f) + "%";
        });
    }

    void Refresh() { foreach (var r in refreshers) r(); }

    public void ShowTab(int index)
    {
        for (int i = 0; i < panels.Length; i++)
            if (panels[i] != null) panels[i].SetActive(i == index);
    }
}