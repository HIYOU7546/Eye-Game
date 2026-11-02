using UnityEngine;
using Photon.Realtime;

// Remembers what the player typed on the home screen. Survives scene loads and game restarts.
public static class GameSession
{
    public enum JoinMode { Quick, Create, Join }   // Quick = used when you press Play in the game scene directly

    public static JoinMode Mode = JoinMode.Quick;

    public static string PlayerName
    {
        get
        {
            string n = PlayerPrefs.GetString("playerName", "");
            if (string.IsNullOrWhiteSpace(n))
            {
                n = "Player" + Random.Range(1000, 10000);
                PlayerPrefs.SetString("playerName", n);
            }
            return n;
        }
        set => PlayerPrefs.SetString("playerName", Clean(value, 16));
    }

    public static string RoomName
    {
        get => PlayerPrefs.GetString("roomName", "EyeRoom");
        set => PlayerPrefs.SetString("roomName", Clean(value, 20));
    }

    public static string Clean(string s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length > max ? s.Substring(0, max) : s;
    }

    // Name shown in the multiplayer tab
    public static string NameOf(Player p)
    {
        if (p == null) return "Player";
        return string.IsNullOrWhiteSpace(p.NickName) ? "Player " + p.ActorNumber : p.NickName;
    }
}