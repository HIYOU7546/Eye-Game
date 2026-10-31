using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

// Put this on ONE object in your game scene. It connects, joins (or creates) a room and spawns the player.
// The player prefab must be inside a folder called "Resources" and its name must match 'playerPrefabName'.
//
// While connecting / joining / spawning, the whole screen is covered with a plain message so nothing else
// is drawn on top of it. Once your player exists the cover goes away and a small status box stays in the corner.
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
    [Tooltip("Small status box in the corner once you are in the game.")]
    public bool showStatus = true;
    [Tooltip("Cover the screen with a message until you are connected and your player has spawned.")]
    public bool showConnectingScreen = true;

    string problem; // the last thing that went wrong (shown on the connecting screen)

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
        problem = null;
        PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions { MaxPlayers = maxPlayers }, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        problem = null;
        SpawnPlayer();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning("Photon disconnected: " + cause);
        problem = "Disconnected: " + cause;
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        problem = "Could not join the room: " + message;
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        problem = "Could not create the room: " + message;
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

    // ------------------------------------------------------------ on-screen text

    void OnGUI()
    {
        bool spawned = NetworkPlayer.Local != null;

        if (!spawned && showConnectingScreen)
        {
            DrawConnectingScreen();
            return;
        }

        if (showStatus) DrawCornerStatus();
    }

    string StatusText()
    {
        if (problem != null) return problem;
        if (PhotonNetwork.InRoom) return "Spawning player...";

        switch (PhotonNetwork.NetworkClientState)
        {
            case ClientState.ConnectedToMasterServer:
            case ClientState.JoinedLobby:
            case ClientState.Joining:
                return "Joining room...";
            default:
                return "Connecting to server...";
        }
    }

    // Full black screen with big centered text. Nothing underneath can show through.
    void DrawConnectingScreen()
    {
        // No camera is rendering yet, so the screen is never cleared for us: paint over everything (opaque,
        // no blending) at the start of every repaint, otherwise last frame's text stays behind and smears.
        Color previous = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
        GUI.color = previous;

        int size = Mathf.Max(18, Mathf.RoundToInt(Screen.height * 0.04f));
        var title = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = size,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        title.normal.textColor = Color.white;

        var small = new GUIStyle(title)
        {
            fontSize = Mathf.Max(12, Mathf.RoundToInt(size * 0.6f)),
            fontStyle = FontStyle.Normal
        };
        small.normal.textColor = new Color(1f, 1f, 1f, 0.7f);

        float w = Screen.width * 0.8f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.5f - size * 2f;

        GUI.Label(new Rect(x, y, w, size * 2.2f), StatusText(), title);

        string detail = problem != null ? "Check your internet connection and the Photon App Id." : "Network: " + PhotonNetwork.NetworkClientState;
        GUI.Label(new Rect(x, y + size * 2.4f, w, size), detail, small);

        // after a failure there is a way to try again
        if (problem != null && !PhotonNetwork.IsConnected)
        {
            var button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(14, Mathf.RoundToInt(size * 0.7f)) };
            if (GUI.Button(new Rect(Screen.width * 0.5f - 110f, y + size * 4f, 220f, size * 1.8f), "Retry", button))
            {
                problem = null;
                PhotonNetwork.ConnectUsingSettings();
            }
        }
    }

    // Small status box (dark background so it can be read over any scene).
    void DrawCornerStatus()
    {
        string text = PhotonNetwork.InRoom
            ? "Room: " + PhotonNetwork.CurrentRoom.Name + "   Players: " + PhotonNetwork.CurrentRoom.PlayerCount
            : "Network: " + PhotonNetwork.NetworkClientState;

        var style = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        style.normal.textColor = Color.white;

        Vector2 textSize = style.CalcSize(new GUIContent(text));
        var box = new Rect(10, 70, textSize.x + 16f, textSize.y + 8f);

        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = old;

        GUI.Label(new Rect(box.x + 8f, box.y + 4f, textSize.x, textSize.y), text, style);
    }
}