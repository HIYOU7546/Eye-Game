using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

// The eyes themselves are already visible to everyone (NetworkPlayer sends where each eye is, even inside a jar).
// What these two components add is the SHARED state, so two players can't use the same slot at once,
// and the scan result reaches everybody (doors, alarms, ...).
// Put each one on the jar / scanner next to a PhotonView (nothing needs to be observed).

// ------------------------------------------------------------------ jar
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

// ------------------------------------------------------------------ scanner
public class NetworkScanner : MonoBehaviourPunCallbacks
{
    public EyeScanner scanner;

    int holderActor = -1;

    void Awake()
    {
        if (scanner == null) scanner = GetComponent<EyeScanner>();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        if (scanner == null) return;
        scanner.HoldChanged += OnLocalHoldChanged;
        scanner.ScanCompleted += OnLocalScanCompleted;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        if (scanner == null) return;
        scanner.HoldChanged -= OnLocalHoldChanged;
        scanner.ScanCompleted -= OnLocalScanCompleted;
    }

    void OnLocalHoldChanged(bool held) => photonView.RPC(nameof(RpcHold), RpcTarget.OthersBuffered, held);
    void OnLocalScanCompleted() => photonView.RPC(nameof(RpcScanComplete), RpcTarget.Others);

    [PunRPC]
    void RpcHold(bool held, PhotonMessageInfo info)
    {
        holderActor = held && info.Sender != null ? info.Sender.ActorNumber : -1;
        scanner.SetRemoteBusy(held);
    }

    // the scan finished on the player's machine: run the same reaction here (events + OnScanFinished)
    [PunRPC]
    void RpcScanComplete()
    {
        scanner.RemoteScanComplete();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (otherPlayer.ActorNumber != holderActor) return;
        holderActor = -1;
        scanner.SetRemoteBusy(false);
    }
}