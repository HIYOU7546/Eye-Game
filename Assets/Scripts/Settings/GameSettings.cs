using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameSettings
{
    public static float Master = 1f, Music = 1f, Sfx = 1f, Voice = 1f;   // 0..1
    public static bool VoiceEnabled = true;                               // mic on/off
    public static bool Fullscreen = true;
    public static int Quality;

    public static event Action Changed;

    static readonly Dictionary<string, float> playerVolume = new Dictionary<string, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() => Load();

    public static void Load()
    {
        Master = PlayerPrefs.GetFloat("set_master", 1f);
        Music = PlayerPrefs.GetFloat("set_music", 1f);
        Sfx = PlayerPrefs.GetFloat("set_sfx", 1f);
        Voice = PlayerPrefs.GetFloat("set_voice", 1f);
        VoiceEnabled = PlayerPrefs.GetInt("set_voiceOn", 1) == 1;
        Fullscreen = PlayerPrefs.GetInt("set_fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        Quality = PlayerPrefs.GetInt("set_quality", QualitySettings.GetQualityLevel());
        Apply();
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("set_master", Master);
        PlayerPrefs.SetFloat("set_music", Music);
        PlayerPrefs.SetFloat("set_sfx", Sfx);
        PlayerPrefs.SetFloat("set_voice", Voice);
        PlayerPrefs.SetInt("set_voiceOn", VoiceEnabled ? 1 : 0);
        PlayerPrefs.SetInt("set_fullscreen", Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("set_quality", Quality);
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        Master = Music = Sfx = Voice = 1f;
        VoiceEnabled = true;
        Fullscreen = true;
        Quality = QualitySettings.names.Length - 1;
        Apply();
    }

    public static void Apply()
    {
        AudioListener.volume = Master;
        if (Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
        if (QualitySettings.GetQualityLevel() != Quality) QualitySettings.SetQualityLevel(Quality, true);
        Changed?.Invoke();
    }

    // Per-player volume as a multiplier: 1 = 100%, 0 = muted, 2 = 200%. Remembered by player name.
    public static float GetPlayerVolume(string name)
    {
        name = name ?? "";
        if (!playerVolume.TryGetValue(name, out float v))
        {
            v = PlayerPrefs.GetFloat("pvol_" + name, 1f);
            playerVolume[name] = v;
        }
        return v;
    }

    public static void SetPlayerVolume(string name, float v)
    {
        name = name ?? "";
        playerVolume[name] = v;
        PlayerPrefs.SetFloat("pvol_" + name, v);
    }
}