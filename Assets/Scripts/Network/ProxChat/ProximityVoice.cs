using UnityEngine;
using Photon.Pun;
using Photon.Voice.PUN;
using Photon.Voice.Unity;

// Makes other players' voices 3D (louder when close, silent when far) and applies the per-player
// volume from the multiplayer tab. Also switches your own mic on/off from the settings.
[RequireComponent(typeof(PhotonView))]
public class ProximityVoice : MonoBehaviourPun
{
    [Tooltip("The AudioSource next to the Speaker component. Found automatically if empty.")]
    public AudioSource voiceSource;
    public float minDistance = 2f;      // full volume inside this distance
    public float maxDistance = 25f;     // silent beyond this distance
    public AudioRolloffMode rolloff = AudioRolloffMode.Linear;

    void Start()
    {
        if (voiceSource == null) voiceSource = GetComponentInChildren<AudioSource>(true);
        if (voiceSource == null) return;

        voiceSource.spatialBlend = 1f;          // fully 3D
        voiceSource.rolloffMode = rolloff;
        voiceSource.minDistance = minDistance;
        voiceSource.maxDistance = maxDistance;
        voiceSource.dopplerLevel = 0f;
        if (photonView.IsMine) voiceSource.mute = true;   // never hear yourself
    }

    void Update()
    {
        if (photonView.IsMine)
        {
            var net = PhotonVoiceNetwork.Instance;
            if (net != null && net.PrimaryRecorder != null)
                net.PrimaryRecorder.TransmitEnabled = GameSettings.VoiceEnabled;
            return;
        }

        if (voiceSource == null || photonView.Owner == null) return;

        // AudioSource.volume tops out at 1, so 100% in the slider = 0.5 and 200% = 1.0
        float player = GameSettings.GetPlayerVolume(GameSession.NameOf(photonView.Owner));
        voiceSource.volume = Mathf.Clamp01(player * 0.5f) * GameSettings.Voice;
    }
}