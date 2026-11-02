using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioBusSource : MonoBehaviour
{
    public enum Bus { Music, Sfx }
    public Bus bus = Bus.Sfx;

    AudioSource src;
    float baseVolume;

    void Awake() { src = GetComponent<AudioSource>(); baseVolume = src.volume; }

    void Update() => src.volume = baseVolume * (bus == Bus.Music ? GameSettings.Music : GameSettings.Sfx);
}