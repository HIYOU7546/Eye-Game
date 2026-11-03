using Photon.Pun;
using Photon.Voice.PUN;
using Photon.Voice.Unity;
using UnityEngine;

[RequireComponent(typeof(PhotonView))]
public class ProximityVoice : MonoBehaviourPun
{
    [Tooltip("The AudioSource next to the Speaker component. Found automatically if empty.")]
    public AudioSource voiceSource;
    public float minDistance = 2f;
    public float maxDistance = 25f;
    public AudioRolloffMode rolloff = AudioRolloffMode.Linear;

    void Start()
    {
        if (voiceSource == null)
            voiceSource = GetComponentInChildren<AudioSource>(true);
        if (voiceSource == null)
            return;

        voiceSource.spatialBlend = 1f;
        voiceSource.rolloffMode = rolloff;
        voiceSource.minDistance = minDistance;
        voiceSource.maxDistance = maxDistance;
        voiceSource.dopplerLevel = 0f;
        if (photonView.IsMine)
            voiceSource.mute = true;
    }

    void Update()
    {
        if (photonView.IsMine)
        {
            var net = PunVoiceClient.Instance;
            if (net != null && net.PrimaryRecorder != null)
                net.PrimaryRecorder.TransmitEnabled = GameSettings.VoiceEnabled;
            return;
        }

        if (voiceSource == null || photonView.Owner == null)
            return;

        float player = GameSettings.GetPlayerVolume(GameSession.NameOf(photonView.Owner));
        voiceSource.volume = Mathf.Clamp01(player * 0.5f) * GameSettings.Voice;
    }
}
