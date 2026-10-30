using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

// Put this on the ROOT of the player prefab (next to PhotonView + NetworkPlayer), and drag it into the
// PhotonView's "Observed Components" list as well.
//
// How it works:
//  * The THIEF's machine runs the real steal (PlayerHands.TryStealEye) and is the authority for where the
//    stolen eye is. It tells everybody "this eye was taken / given back" with an RPC and streams the eye's position.
//  * Every other machine (the victim's included) parks the eye, takes control away from the victim,
//    and moves the eye to wherever the thief's machine says it is.
//  * Nothing runs on a remote machine's PlayerHands (it is disabled there), so everything is done here.
[DefaultExecutionOrder(-100)] // before AdaptiveEyeTracker so the camera follows the eye this frame
[RequireComponent(typeof(PhotonView))]
public class NetworkEyeSteal : MonoBehaviourPun, IPunObservable
{
    [Tooltip("Found automatically if empty.")]
    public PlayerHands hands;
    public float smoothing = 15f;
    [Tooltip("When both eyes are back on the head, the targets start this far straight ahead.")]
    public float returnFocusDistance = 10f;

    struct EyeState
    {
        public int victimId;
        public bool eyeLeft;
        public Vector3 pos;
        public Quaternion rot;
    }

    // what we remember about an eye that is currently in someone else's hand (remote machines only)
    class Loan
    {
        public NetworkPlayer victim;
        public bool eyeLeft;
        public bool wasControlled;
        public Vector3 oldLocalPos;
    }

    readonly List<Loan> loans = new List<Loan>();
    readonly EyeState[] outgoing = new EyeState[2];
    readonly EyeState[] incoming = new EyeState[2];
    int incomingCount;

    void Awake()
    {
        if (hands == null) hands = GetComponentInChildren<PlayerHands>(true);
    }

    void Start()
    {
        // only the owner's PlayerHands ever steals
        if (photonView.IsMine && hands != null)
        {
            hands.EyeStolen += OnLocalStolen;
            hands.EyeReturned += OnLocalReturned;
        }
    }

    void OnDestroy()
    {
        if (hands != null)
        {
            hands.EyeStolen -= OnLocalStolen;
            hands.EyeReturned -= OnLocalReturned;
        }

        // the thief left with eyes in their hands: send them home
        foreach (var l in loans) Release(l);
        loans.Clear();
    }

    static NetworkPlayer FindPlayer(int viewId)
    {
        PhotonView pv = PhotonView.Find(viewId);
        return pv != null ? pv.GetComponent<NetworkPlayer>() : null;
    }

    static void SetControlled(AdaptiveEyeTracker t, bool left, bool value)
    {
        if (left) t.leftEyeControlledByPlayer = value;
        else t.rightEyeControlledByPlayer = value;
    }

    // ------------------------------------------------------------ thief's machine -> everybody

    void OnLocalStolen(PlayerHands victim, bool eyeLeft, bool handLeft)
    {
        NetworkPlayer v = victim != null ? victim.GetComponentInParent<NetworkPlayer>() : null;
        if (v == null) return;

        v.SetEyeStolen(eyeLeft, true); // stop applying the victim's own data for that eye on this machine
        photonView.RPC(nameof(RpcStolen), RpcTarget.OthersBuffered, v.photonView.ViewID, eyeLeft);
    }

    void OnLocalReturned(PlayerHands victim, bool eyeLeft)
    {
        NetworkPlayer v = victim != null ? victim.GetComponentInParent<NetworkPlayer>() : null;
        if (v == null) return;

        v.SetEyeStolen(eyeLeft, false);
        photonView.RPC(nameof(RpcReturned), RpcTarget.OthersBuffered, v.photonView.ViewID, eyeLeft);
    }

    // ------------------------------------------------------------ everybody else

    [PunRPC]
    void RpcStolen(int victimViewId, bool eyeLeft)
    {
        NetworkPlayer v = FindPlayer(victimViewId);
        if (v == null || v.tracker == null) return;

        AdaptiveEyeTracker t = v.tracker;
        Transform eye = eyeLeft ? t.leftEyeball : t.rightEyeball;
        if (eye == null) return;

        loans.Add(new Loan
        {
            victim = v,
            eyeLeft = eyeLeft,
            oldLocalPos = eye.localPosition,
            wasControlled = eyeLeft ? t.leftEyeControlledByPlayer : t.rightEyeControlledByPlayer
        });

        v.SetEyeStolen(eyeLeft, true);
        t.SetEyeParked(eyeLeft, true);
        t.SetFocusSuspended(eyeLeft, true);
        SetControlled(t, eyeLeft, false);

        // the victim's hand for that eye is not free to pick things up while the eye is away
        if (v.photonView.IsMine && v.hands != null) v.hands.SetEyeLoaned(eyeLeft, true);
    }

    [PunRPC]
    void RpcReturned(int victimViewId, bool eyeLeft)
    {
        int i = loans.FindIndex(l => l.victim != null && l.eyeLeft == eyeLeft && l.victim.photonView.ViewID == victimViewId);
        if (i < 0) return;

        Release(loans[i]);
        loans.RemoveAt(i);
    }

    void Release(Loan l)
    {
        NetworkPlayer v = l.victim;
        if (v == null) return;

        v.SetEyeStolen(l.eyeLeft, false);
        if (v.photonView.IsMine && v.hands != null) v.hands.SetEyeLoaned(l.eyeLeft, false);

        AdaptiveEyeTracker t = v.tracker;
        if (t == null) return;

        Transform eye = l.eyeLeft ? t.leftEyeball : t.rightEyeball;
        if (eye != null) eye.localPosition = l.oldLocalPos;

        SetControlled(t, l.eyeLeft, l.wasControlled);
        t.SetEyeParked(l.eyeLeft, false);
        t.SetFocusSuspended(l.eyeLeft, false);

        // both eyes home: start from a clean state, straight ahead of the head
        if (v.photonView.IsMine && !t.AnyEyeParked)
            t.SnapEyesForward(t.transform, returnFocusDistance);
    }

    // ------------------------------------------------------------ position of the stolen eyes

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            int n = 0;
            if (hands != null)
            {
                for (int i = 0; i < hands.StolenEyeCount && n < outgoing.Length; i++)
                {
                    if (!hands.GetStolenEye(i, out PlayerHands victim, out bool eyeLeft, out Transform eye)) continue;

                    NetworkPlayer v = victim.GetComponentInParent<NetworkPlayer>();
                    if (v == null || eye == null) continue;

                    outgoing[n++] = new EyeState
                    {
                        victimId = v.photonView.ViewID,
                        eyeLeft = eyeLeft,
                        pos = eye.position,
                        rot = eye.rotation
                    };
                }
            }

            stream.SendNext(n);
            for (int i = 0; i < n; i++)
            {
                stream.SendNext(outgoing[i].victimId);
                stream.SendNext(outgoing[i].eyeLeft);
                stream.SendNext(outgoing[i].pos);
                stream.SendNext(outgoing[i].rot);
            }
        }
        else
        {
            int n = (int)stream.ReceiveNext();
            incomingCount = 0;
            for (int i = 0; i < n; i++)
            {
                var s = new EyeState();
                s.victimId = (int)stream.ReceiveNext();
                s.eyeLeft = (bool)stream.ReceiveNext();
                s.pos = (Vector3)stream.ReceiveNext();
                s.rot = (Quaternion)stream.ReceiveNext();
                if (incomingCount < incoming.Length) incoming[incomingCount++] = s;
            }
        }
    }

    void LateUpdate()
    {
        if (photonView.IsMine) return; // the thief's own machine moves the eye through PlayerHands

        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);

        for (int i = 0; i < incomingCount; i++)
        {
            EyeState s = incoming[i];
            NetworkPlayer v = FindPlayer(s.victimId);
            if (v == null || v.tracker == null || !v.IsEyeStolen(s.eyeLeft)) continue;

            Transform eye = s.eyeLeft ? v.tracker.leftEyeball : v.tracker.rightEyeball;
            Transform cam = s.eyeLeft ? v.tracker.leftCamera : v.tracker.rightCamera;
            if (eye == null) continue;

            eye.position = Vector3.Lerp(eye.position, s.pos, k);
            eye.rotation = Quaternion.Slerp(eye.rotation, s.rot, k);

            // nobody "controls" a stolen eye, so move the victim's camera with it ourselves
            if (cam != null)
            {
                cam.position = eye.position;
                cam.rotation = eye.rotation;
            }
        }
    }
}