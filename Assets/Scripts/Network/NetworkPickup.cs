using UnityEngine;
using Photon.Pun;

// Put this on every PickupItem, next to a PhotonView (add this component to the PhotonView's "Observed Components").
// It listens to the PickupItem's own events, so PickupItem and PlayerHands need no changes.
//
//  * Picked up / stowed / dropped is sent to everyone with an RPC.
//  * The last player to pick the item up OWNS it: their physics runs, everybody else just follows.
[RequireComponent(typeof(PickupItem), typeof(PhotonView))]
public class NetworkPickup : MonoBehaviourPun, IPunObservable
{
    public float smoothing = 15f;

    PickupItem item;
    Rigidbody rb;
    bool applying; // true while applying something that came from the network (so we don't send it back)

    bool hasState, nHeld;
    Vector3 nPos;
    Quaternion nRot;

    void Awake()
    {
        item = GetComponent<PickupItem>();
        rb = GetComponent<Rigidbody>();
        photonView.OwnershipTransfer = OwnershipOption.Takeover;

        item.onPickedUp.AddListener(OnPicked);
        item.onStowed.AddListener(OnStowed);
        item.onDropped.AddListener(OnDropped);
    }

    // ------------------------------------------------------------ local actions -> network

    void OnPicked()
    {
        if (applying) return;
        if (!photonView.IsMine) photonView.RequestOwnership();

        NetworkPlayer np = HolderFromParent(out Transform anchor);
        if (np == null) return;
        photonView.RPC(nameof(RpcHeld), RpcTarget.OthersBuffered, np.photonView.ViewID, np.AnchorIndexOf(anchor), false);
    }

    void OnStowed()
    {
        if (applying) return;
        if (!photonView.IsMine) photonView.RequestOwnership();

        NetworkPlayer np = HolderFromParent(out _);
        if (np == null) return;
        photonView.RPC(nameof(RpcHeld), RpcTarget.OthersBuffered, np.photonView.ViewID, 3, true);
    }

    void OnDropped()
    {
        if (applying) return;
        photonView.RPC(nameof(RpcDropped), RpcTarget.OthersBuffered, transform.position, transform.rotation);
    }

    NetworkPlayer HolderFromParent(out Transform anchor)
    {
        anchor = transform.parent;
        return anchor != null ? anchor.GetComponentInParent<NetworkPlayer>() : null;
    }

    // ------------------------------------------------------------ network -> local

    [PunRPC]
    void RpcHeld(int playerViewId, int anchorIndex, bool stowed)
    {
        PhotonView pv = PhotonView.Find(playerViewId);
        NetworkPlayer np = pv != null ? pv.GetComponent<NetworkPlayer>() : null;
        if (np == null) return; // that player has left

        applying = true;
        Transform anchor = np.AnchorByIndex(anchorIndex);
        if (stowed) item.Store(anchor);
        else item.Attach(anchor);
        applying = false;
    }

    [PunRPC]
    void RpcDropped(Vector3 position, Quaternion rotation)
    {
        applying = true;
        item.Release(position, Vector3.zero);
        transform.rotation = rotation;
        applying = false;
        hasState = false;
    }

    // ------------------------------------------------------------ loose items

    // Only the owner simulates physics. Everyone else follows the owner's position while the item is loose.
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(item.IsHeld);
            if (!item.IsHeld)
            {
                stream.SendNext(transform.position);
                stream.SendNext(transform.rotation);
            }
        }
        else
        {
            nHeld = (bool)stream.ReceiveNext();
            if (!nHeld)
            {
                nPos = (Vector3)stream.ReceiveNext();
                nRot = (Quaternion)stream.ReceiveNext();
                hasState = true;
            }
        }
    }

    void Update()
    {
        if (photonView.IsMine || item.IsHeld) return;

        if (rb != null) rb.isKinematic = true;
        if (!hasState || nHeld) return;

        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, nPos, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, nRot, k);
    }
}