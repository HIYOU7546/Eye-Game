using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

// Each row needs a Slider and a TMP text (your P1..P4 objects already have both).
public class PlayerVolumeList : MonoBehaviourPunCallbacks
{
    [Tooltip("Drag P1, P2, P3, P4 here. P1 is copied when more rows are needed.")]
    public GameObject[] rows;
    public float rowSpacing = 60f;
    [Tooltip("Optional text shown when nobody else is in the room")]
    public TMP_Text emptyLabel;

    readonly List<GameObject> spawned = new List<GameObject>();

    public override void OnEnable() { base.OnEnable(); Rebuild(); }
    public override void OnPlayerEnteredRoom(Player p) => Rebuild();
    public override void OnPlayerLeftRoom(Player p) => Rebuild();

    void Rebuild()
    {
        if (rows == null || rows.Length == 0 || rows[0] == null) return;

        var others = new List<Player>();
        if (PhotonNetwork.InRoom)
            foreach (var p in PhotonNetwork.PlayerListOthers) others.Add(p);

        var all = new List<GameObject>(rows);
        all.AddRange(spawned);

        while (all.Count < others.Count)
        {
            var clone = Instantiate(rows[0], rows[0].transform.parent);
            clone.name = "P" + (all.Count + 1);
            spawned.Add(clone);
            all.Add(clone);
        }

        Vector2 first = ((RectTransform)rows[0].transform).anchoredPosition;
        for (int i = 0; i < all.Count; i++)
        {
            bool used = i < others.Count;
            all[i].SetActive(used);
            if (!used) continue;
            ((RectTransform)all[i].transform).anchoredPosition = first + Vector2.down * rowSpacing * i;
            Bind(all[i], others[i]);
        }

        if (emptyLabel != null)
        {
            emptyLabel.gameObject.SetActive(others.Count == 0);
            emptyLabel.text = PhotonNetwork.InRoom ? "No other players yet" : "Join a game to change other players' volume";
        }
    }

    void Bind(GameObject row, Player p)
    {
        var slider = row.GetComponentInChildren<Slider>(true);
        var label = row.GetComponentInChildren<TMP_Text>(true);
        string name = GameSession.NameOf(p);

        slider.minValue = 0f; slider.maxValue = 200f; slider.wholeNumbers = true;
        slider.onValueChanged.RemoveAllListeners();
        slider.SetValueWithoutNotify(Mathf.Round(GameSettings.GetPlayerVolume(name) * 100f));
        SetLabel(label, name, slider.value);

        slider.onValueChanged.AddListener(v =>
        {
            GameSettings.SetPlayerVolume(name, v / 100f);
            SetLabel(label, name, v);
        });
    }

    static void SetLabel(TMP_Text label, string name, float percent)
    {
        if (label != null) label.text = name + "  " + Mathf.RoundToInt(percent) + "%";
    }
}