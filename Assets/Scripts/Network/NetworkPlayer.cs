using UnityEngine;
using Photon.Pun;

// Put this on the ROOT of the player prefab (next to the PhotonView).
//
// LOCAL player : the local-only components/objects below get switched on (input, cameras, interaction...).
// REMOTE player: they stay off. The body and both eyes are driven by the data the owner sends instead.
//
// IMPORTANT: in the prefab, leave every "local only" component DISABLED and every "local only" object INACTIVE.
// This script turns them on for the owner in Start. (PUN only knows who owns the object after Awake/OnEnable,
// so a component that is already on would already have hooked up to your input.)
[RequireComponent(typeof(PhotonView))]
public class NetworkPlayer : MonoBehaviourPun, IPunObservable
{
    public static NetworkPlayer Local { get; private set; }

    [Header("Local player only")]
    [Tooltip("PlayerController, PlayerInteractor, PlayerHands, AdaptiveEyeTracker, DynamicSplitScreen. Keep them DISABLED in the prefab.")]
    public Behaviour[] localOnlyComponents;
    [Tooltip("Left/Right camera objects, anything with an AudioListener, the HUD. Keep them INACTIVE in the prefab.")]
    public GameObject[] localOnlyObjects;

    [Header("References (found automatically if empty)")]
    public PlayerController controller;
    public PlayerHands hands;
    public AdaptiveEyeTracker tracker;

    [Header("Remote smoothing")]
    public float smoothing = 15f;
    [Tooltip("If a remote player is further than this from where they should be, snap instead of sliding.")]
    public float teleportDistance = 5f;

    Transform head;

    // latest state received from the owner
    bool hasState;
    Vector3 nHeadPos, nMovePos, nCrouchPos;
    Quaternion nHeadRot;
    bool nLeftParked, nRightParked;
    Vector3 nLeftPos, nRightPos;
    Quaternion nLeftRot, nRightRot;

    void Awake()
    {
        if (controller == null) controller = GetComponentInChildren<PlayerController>(true);
        if (hands == null) hands = GetComponentInChildren<PlayerHands>(true);
        if (tracker == null) tracker = GetComponentInChildren<AdaptiveEyeTracker>(true);
        head = controller != null ? controller.transform : transform;
    }

    void Start()
    {
        bool mine = photonView.IsMine;
        if (mine) Local = this;

        if (localOnlyComponents != null)
            foreach (var b in localOnlyComponents) if (b != null) b.enabled = mine;
        if (localOnlyObjects != null)
            foreach (var g in localOnlyObjects) if (g != null) g.SetActive(mine);
    }

    void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    // ------------------------------------------------------------ hand anchors (used by NetworkPickup)

    // 0 = left hand, 1 = right hand, 2 = both hands, 3 = backpack
    public Transform AnchorByIndex(int i)
    {
        if (hands == null) return transform;
        Transform left = hands.leftHandAnchor != null ? hands.leftHandAnchor : hands.transform;
        Transform right = hands.rightHandAnchor != null ? hands.rightHandAnchor : hands.transform;
        switch (i)
        {
            case 0: return left;
            case 1: return right;
            case 2: return hands.twoHandAnchor != null ? hands.twoHandAnchor : right;
            default: return hands.backpackRoot != null ? hands.backpackRoot : hands.transform;
        }
    }

    public int AnchorIndexOf(Transform t)
    {
        for (int i = 0; i < 3; i++)
            if (AnchorByIndex(i) == t) return i;
        return 1;
    }

    // ------------------------------------------------------------ sending / receiving

    Transform MoveT => controller != null && controller.moveTarget != null && controller.moveTarget != head ? controller.moveTarget : null;
    Transform CrouchT => controller != null && controller.crouchTarget != null && controller.crouchTarget != head ? controller.crouchTarget : null;

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (head == null) return;

        if (stream.IsWriting)
        {
            stream.SendNext(head.position);
            stream.SendNext(head.rotation);
            if (MoveT != null) stream.SendNext(MoveT.position);
            if (CrouchT != null) stream.SendNext(CrouchT.position);

            if (tracker != null && tracker.leftEyeball != null && tracker.rightEyeball != null)
            {
                WriteEye(stream, tracker.leftEyeball, tracker.IsEyeParked(true));
                WriteEye(stream, tracker.rightEyeball, tracker.IsEyeParked(false));
            }
        }
        else
        {
            nHeadPos = (Vector3)stream.ReceiveNext();
            nHeadRot = (Quaternion)stream.ReceiveNext();
            if (MoveT != null) nMovePos = (Vector3)stream.ReceiveNext();
            if (CrouchT != null) nCrouchPos = (Vector3)stream.ReceiveNext();

            if (tracker != null && tracker.leftEyeball != null && tracker.rightEyeball != null)
            {
                ReadEye(stream, ref nLeftParked, ref nLeftPos, ref nLeftRot);
                ReadEye(stream, ref nRightParked, ref nRightPos, ref nRightRot);
                tracker.SetEyeParked(true, nLeftParked);
                tracker.SetEyeParked(false, nRightParked);
            }

            if (!hasState)
            {
                hasState = true;
                SnapToState();
            }
        }
    }

    // An eye on the head is sent relative to its parent. An eye that is away (jar, scanner) is sent in world space.
    static void WriteEye(PhotonStream stream, Transform eye, bool parked)
    {
        stream.SendNext(parked);
        stream.SendNext(parked ? eye.position : eye.localPosition);
        stream.SendNext(eye.rotation);
    }

    static void ReadEye(PhotonStream stream, ref bool parked, ref Vector3 pos, ref Quaternion rot)
    {
        parked = (bool)stream.ReceiveNext();
        pos = (Vector3)stream.ReceiveNext();
        rot = (Quaternion)stream.ReceiveNext();
    }

    // ------------------------------------------------------------ remote players

    void LateUpdate()
    {
        if (photonView.IsMine || !hasState || head == null) return;

        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        if ((head.position - nHeadPos).sqrMagnitude > teleportDistance * teleportDistance) k = 1f;

        Apply(k);
    }

    void SnapToState() => Apply(1f);

    void Apply(float k)
    {
        // parents first, then the head
        if (MoveT != null) MoveT.position = Vector3.Lerp(MoveT.position, nMovePos, k);
        if (CrouchT != null) CrouchT.position = Vector3.Lerp(CrouchT.position, nCrouchPos, k);
        head.position = Vector3.Lerp(head.position, nHeadPos, k);
        head.rotation = Quaternion.Slerp(head.rotation, nHeadRot, k);

        if (tracker != null && tracker.leftEyeball != null && tracker.rightEyeball != null)
        {
            ApplyEye(tracker.leftEyeball, nLeftParked, nLeftPos, nLeftRot, k);
            ApplyEye(tracker.rightEyeball, nRightParked, nRightPos, nRightRot, k);
        }
    }

    static void ApplyEye(Transform eye, bool parked, Vector3 pos, Quaternion rot, float k)
    {
        if (parked) eye.position = Vector3.Lerp(eye.position, pos, k);
        else eye.localPosition = Vector3.Lerp(eye.localPosition, pos, k);
        eye.rotation = Quaternion.Slerp(eye.rotation, rot, k);
    }
}