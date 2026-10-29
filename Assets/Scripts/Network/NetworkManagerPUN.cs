using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

// Put this on ONE object in your game scene. It connects, joins (or creates) a room and spawns the player.
// The player prefab must be inside a folder called "Resources" and its name must match 'playerPrefabName'.
public class NetworkManagerPUN : MonoBehaviourPunCallbacks
{
    [Header("Room")]
    public string roomName = "EyeRoom";
    public byte maxPlayers = 4;
    public string gameVersion = "1";

    [Header("Spawning")]
    [Tooltip("Name of the player prefab inside a Resources folder.")]
    public string playerPrefabName = "NetworkPlayer";
    [Tooltip("One spawn point per player slot. Player N uses point N (wraps around).")]
    public Transform[] spawnPoints;

    [Header("Debug")]
    public bool showStatus = true;

    void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.GameVersion = gameVersion;

        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();
        else if (PhotonNetwork.InRoom)
            SpawnPlayer();
    }

    public override void OnConnectedToMaster()
    {
        PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions { MaxPlayers = maxPlayers }, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        SpawnPlayer();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning("Photon disconnected: " + cause);
    }

    void SpawnPlayer()
    {
        if (NetworkPlayer.Local != null) return;

        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform p = spawnPoints[Mathf.Max(0, PhotonNetwork.LocalPlayer.ActorNumber - 1) % spawnPoints.Length];
            pos = p.position;
            rot = p.rotation;
        }

        PhotonNetwork.Instantiate(playerPrefabName, pos, rot);
    }

    void OnGUI()
    {
        if (!showStatus) return;
        GUI.Label(new Rect(10, 70, 600, 30),
            PhotonNetwork.InRoom
                ? "Room: " + PhotonNetwork.CurrentRoom.Name + "  Players: " + PhotonNetwork.CurrentRoom.PlayerCount
                : "Network: " + PhotonNetwork.NetworkClientState);
    }
}