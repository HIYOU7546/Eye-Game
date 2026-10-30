using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

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