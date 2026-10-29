using UnityEngine;
using UnityEngine.Events;

// Put this on anything the player can pick up. It needs a Collider (and optionally a Rigidbody).
//   OneHand  : held in one hand, needs one empty hand.
//   TwoHand  : held in both hands, needs BOTH hands empty, and slows the player down while carried.
//   Backpack : needs one empty hand to pick up, then goes straight into the backpack.
//              Taking it out again needs one empty hand (it is then held in that hand).
public class PickupItem : Interactable
{
    public enum Kind { OneHand, TwoHand, Backpack }

    [Header("Item")]
    public Kind kind = Kind.OneHand;
    [Tooltip("Only used by TwoHand items. 1 = normal speed, 0.5 = half speed while carrying it.")]
    [Range(0.1f, 1f)] public float twoHandSpeedMultiplier = 0.6f;
    [Tooltip("Where the item sits inside the hand anchor.")]
    public Vector3 heldLocalPosition;
    public Vector3 heldLocalEuler;

    [Header("Item Events")]
    public UnityEvent onPickedUp;
    public UnityEvent onDropped;
    public UnityEvent onStowed;

    public bool IsHeld { get; private set; }   // in a hand OR in the backpack
    public float SpeedMultiplier => kind == Kind.TwoHand ? twoHandSpeedMultiplier : 1f;

    Rigidbody rb;
    Collider[] colliders;
    bool[] colliderWasEnabled;
    bool rbWasKinematic;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        colliderWasEnabled = new bool[colliders.Length];
        if (rb != null) rbWasKinematic = rb.isKinematic;
    }

    public override bool CanInteract => enabled && !IsHeld;

    public override void Interact(PlayerInteractor interactor)
    {
        if (!CanInteract || interactor == null) return;

        var hands = interactor.GetComponentInParent<PlayerHands>();
        if (hands != null && hands.TryPickUp(this))
            onInteract?.Invoke();
    }

    // ---- called by PlayerHands ----

    void SetPhysics(bool held)
    {
        if (colliders == null) Awake();

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null) continue;
            if (held) { colliderWasEnabled[i] = colliders[i].enabled; colliders[i].enabled = false; }
            else colliders[i].enabled = colliderWasEnabled[i];
        }

        if (rb != null) rb.isKinematic = held ? true : rbWasKinematic;
    }

    public void Attach(Transform anchor)
    {
        if (!IsHeld) SetPhysics(true);
        IsHeld = true;

        gameObject.SetActive(true);
        transform.SetParent(anchor, false);
        transform.localPosition = heldLocalPosition;
        transform.localRotation = Quaternion.Euler(heldLocalEuler);
        onPickedUp?.Invoke();
    }

    public void Store(Transform backpackRoot)
    {
        if (!IsHeld) SetPhysics(true);
        IsHeld = true;

        transform.SetParent(backpackRoot, false);
        gameObject.SetActive(false);
        onStowed?.Invoke();
    }

    public void Release(Vector3 position, Vector3 velocity)
    {
        transform.SetParent(null, true);
        transform.position = position;
        gameObject.SetActive(true);

        SetPhysics(false);
        IsHeld = false;

        if (rb != null && !rb.isKinematic) rb.velocity = velocity; // use rb.velocity on Unity 2022 or older
        onDropped?.Invoke();
    }
}