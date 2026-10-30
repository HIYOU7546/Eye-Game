using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class NetworkJar : MonoBehaviourPunCallbacks
{
    public EyeJarInteract jar;

    readonly int[] occupant = { -1, -1 }; // actor number that put the eye in: [0] left slot, [1] right slot

    void Awake()
    {
        if (jar == null) jar = GetComponent<EyeJarInteract>();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        if (jar != null) jar.SlotChanged += OnLocalSlotChanged;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        if (jar != null) jar.SlotChanged -= OnLocalSlotChanged;
    }

    void OnLocalSlotChanged(bool left, bool occupied)
    {
        photonView.RPC(nameof(RpcSlot), RpcTarget.OthersBuffered, left, occupied);
    }

    [PunRPC]
    void RpcSlot(bool left, bool occupied, PhotonMessageInfo info)
    {
        occupant[left ? 0 : 1] = occupied && info.Sender != null ? info.Sender.ActorNumber : -1;
        jar.SetRemoteOccupied(left, occupied);
    }

    // someone left the room with an eye still in the jar: free the slot
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        for (int i = 0; i < 2; i++)
        {
            if (occupant[i] != otherPlayer.ActorNumber) continue;
            occupant[i] = -1;
            jar.SetRemoteOccupied(i == 0, false);
        }
    }
}