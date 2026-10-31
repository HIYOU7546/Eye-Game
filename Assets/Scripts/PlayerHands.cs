using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Put this on Character Head (next to PlayerController).
//
// RULES
//  * Each hand holds exactly ONE thing: your own eye, an item, or an eye stolen from another player.
//    The left hand holds the LEFT eye, the right hand the RIGHT eye.
//  * An eye that is away from the head (in a jar, in the scanner, or stolen) leaves its hand EMPTY.
//  * OneHand item  : needs one empty hand.
//  * Backpack item : needs one empty hand, then goes into the backpack. Taking it out needs one empty hand.
//  * TwoHand item  : needs BOTH hands empty, slows you down while carried.
//  * A stolen eye can not be controlled by the thief OR by its owner.
[DefaultExecutionOrder(-50)]
public class PlayerHands : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Found automatically through PlayerController if empty.")]
    public AdaptiveEyeTracker eyeTracker;
    public PlayerController playerController;

    [Header("Hand Anchors")]
    [Tooltip("Empty children of the head where held things sit. Falls back to this object if empty.")]
    public Transform leftHandAnchor;
    public Transform rightHandAnchor;
    [Tooltip("Where two-handed items sit (between the hands).")]
    public Transform twoHandAnchor;
    [Tooltip("Where backpack items are parked (hidden). Falls back to this object.")]
    public Transform backpackRoot;

    [Header("Backpack")]
    public int backpackCapacity = 6;

    [Header("Dropping")]
    public float dropThrowSpeed = 2f;

    [Header("Stolen Eyes")]
    [Tooltip("Units per second a stolen eye flies to / from the hand.")]
    public float stolenEyeFlySpeed = 6f;
    [Tooltip("When both eyes are back on the head, the targets start this far straight ahead.")]
    public float returnFocusDistance = 10f;

    [Header("Input (optional actions, fallbacks used if missing)")]
    public InputActionAsset inputActions;
    [Tooltip("Drop what the selected hand holds (or give a stolen eye back). Fallback: Q / D-pad down.")]
    public string dropActionName = "Drop";
    [Tooltip("Put a backpack item away, or take one out. Fallback: R / D-pad up.")]
    public string backpackActionName = "Backpack";

    [Header("Debug")]
    public bool showHandsOnScreen = true;

    // ------------------------------------------------------------ state

    class Hand
    {
        public PickupItem item;
        public StolenEye stolen;
        public bool reserved; // an eye is flying back into this hand
    }

    class StolenEye
    {
        public PlayerHands victim;
        public AdaptiveEyeTracker tracker;
        public bool eyeLeft;
        public Transform eyeball, target, cam, holder, oldParent;
        public Vector3 oldLocalPos, oldLocalScale;
        public bool wasControlled;
        public bool handLeft;
        public bool returning;
    }

    readonly Vector3[] homeEyePos = new Vector3[2]; // where each eye sits on the head: [0] left, [1] right
    public Vector3 HomeEyeLocalPos(bool left) => homeEyePos[left ? 0 : 1];
    public bool IsEyeLoaned(bool left) => eyeLoaned[left ? 0 : 1];

    readonly Hand left = new Hand();
    readonly Hand right = new Hand();
    PickupItem twoHanded;
    readonly List<PickupItem> backpack = new List<PickupItem>();
    readonly List<StolenEye> stolenEyes = new List<StolenEye>();

    InputAction dropAction, backpackAction;
    string message;
    float messageUntil;

    Hand HandFor(bool l) => l ? left : right;

    // -------------------------------------- temp location ---------------
    public event System.Action<PlayerHands, bool, bool> EyeStolen;   // victim, eyeLeft, handLeft
    public event System.Action<PlayerHands, bool> EyeReturned;       // victim, eyeLeft

    readonly bool[] eyeLoaned = new bool[2]; // [0] left eye / [1] right eye is in someone else's hand
    public void SetEyeLoaned(bool leftEye, bool loaned) { eyeLoaned[leftEye ? 0 : 1] = loaned; }

    public int StolenEyeCount => stolenEyes.Count;
    public bool GetStolenEye(int i, out PlayerHands victim, out bool eyeLeft, out Transform eyeball)
    {
        victim = null; eyeLeft = false; eyeball = null;
        if (i < 0 || i >= stolenEyes.Count) return false;
        StolenEye s = stolenEyes[i];
        if (s.eyeball == null || s.victim == null) return false;
        victim = s.victim; eyeLeft = s.eyeLeft; eyeball = s.eyeball;
        return true;
    }

    Transform AnchorFor(bool l)
    {
        Transform a = l ? leftHandAnchor : rightHandAnchor;
        return a != null ? a : transform;
    }

    Transform TwoHandAnchor => twoHandAnchor != null ? twoHandAnchor : AnchorFor(false);

    public bool ActiveHandIsLeft =>
        playerController == null || playerController.CurrentEye == PlayerController.ActiveEye.Left;

    public int BackpackCount => backpack.Count;

    // ------------------------------------------------------------ setup

    void Awake()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (eyeTracker == null && playerController != null) eyeTracker = playerController.eyeTracker;
        if (eyeTracker == null) eyeTracker = GetComponentInParent<AdaptiveEyeTracker>();

        var actions = inputActions != null ? inputActions : InputSystem.actions;
        if (actions != null)
        {
            dropAction = actions.FindAction(dropActionName, false);
            backpackAction = actions.FindAction(backpackActionName, false);
            if (dropAction == null) Debug.LogWarning($"PlayerHands: action '{dropActionName}' not found in the Input Actions asset.");
            if (backpackAction == null) Debug.LogWarning($"PlayerHands: action '{backpackActionName}' not found in the Input Actions asset.");
        }

        if (eyeTracker != null)
        {
            if (eyeTracker.leftEyeball != null) homeEyePos[0] = eyeTracker.leftEyeball.localPosition;
            if (eyeTracker.rightEyeball != null) homeEyePos[1] = eyeTracker.rightEyeball.localPosition;
        }
    }

    void OnEnable()
    {
        dropAction?.Enable();
        backpackAction?.Enable();
    }

    void OnDisable()
    {
        dropAction?.Disable();
        backpackAction?.Disable();
    }

    void OnDestroy()
    {
        // never leave someone's eye stranded
        foreach (var s in stolenEyes) ForceHome(s);
        stolenEyes.Clear();
    }

    // ------------------------------------------------------------ queries

    // Is this player's own eye sitting in its hand (on the head, not in a jar / scanner / stolen)?
    public bool OwnEyeInHand(bool l) => eyeTracker != null && !eyeTracker.IsEyeParked(l);

    // Nothing at all in that hand (own eye included)
    public bool IsHandEmpty(bool l)
    {
        Hand h = HandFor(l);
        return h.item == null && h.stolen == null && !h.reserved && twoHanded == null && !OwnEyeInHand(l) && !eyeLoaned[l ? 0 : 1];
    }

    // Can the eye of this side come back into its hand? (the eye itself is away, so only items matter)
    public bool CanReceiveEye(bool l)
    {
        Hand h = HandFor(l);
        return h.item == null && h.stolen == null && twoHanded == null;
    }

    public int EmptyHandCount => (IsHandEmpty(true) ? 1 : 0) + (IsHandEmpty(false) ? 1 : 0);

    // Slow-down while carrying things (two-handed items). The PlayerController multiplies its speed by this.
    public float MoveSpeedMultiplier
    {
        get
        {
            float m = 1f;
            if (twoHanded != null) m *= twoHanded.SpeedMultiplier;
            if (left.item != null) m *= left.item.SpeedMultiplier;
            if (right.item != null) m *= right.item.SpeedMultiplier;
            return m;
        }
    }

    // Kept for the scanner: the item being held (two-handed first, then the selected hand)
    public GameObject HeldObject
    {
        get
        {
            if (twoHanded != null) return twoHanded.gameObject;
            Hand a = HandFor(ActiveHandIsLeft), b = HandFor(!ActiveHandIsLeft);
            if (a.item != null) return a.item.gameObject;
            if (b.item != null) return b.item.gameObject;
            return null;
        }
    }

    // True if a held item is 'obj', is part of it (child), or contains it. Backpack items do NOT count.
    public bool IsHolding(GameObject obj)
    {
        if (obj == null) return false;
        return Matches(twoHanded, obj) || Matches(left.item, obj) || Matches(right.item, obj);
    }

    static bool Matches(PickupItem held, GameObject obj)
    {
        if (held == null) return false;
        GameObject h = held.gameObject;
        return h == obj || h.transform.IsChildOf(obj.transform) || obj.transform.IsChildOf(h.transform);
    }

    bool PickEmptyHand(out bool l)
    {
        bool pref = ActiveHandIsLeft;
        if (IsHandEmpty(pref)) { l = pref; return true; }
        if (IsHandEmpty(!pref)) { l = !pref; return true; }
        l = pref;
        return false;
    }

    // ------------------------------------------------------------ picking up

    public bool TryPickUp(PickupItem it)
    {
        if (it == null || it.IsHeld) return false;

        switch (it.kind)
        {
            case PickupItem.Kind.TwoHand:
                if (EmptyHandCount < 2) { Notify("Both hands must be empty"); return false; }
                twoHanded = it;
                it.Attach(TwoHandAnchor);
                return true;

            case PickupItem.Kind.Backpack:
                if (EmptyHandCount < 1) { Notify("You need a free hand"); return false; }
                if (backpack.Count >= backpackCapacity) { Notify("Backpack is full"); return false; }
                backpack.Add(it);
                it.Store(backpackRoot != null ? backpackRoot : transform);
                return true;

            default: // OneHand
                if (!PickEmptyHand(out bool l)) { Notify("You need a free hand"); return false; }
                HandFor(l).item = it;
                it.Attach(AnchorFor(l));
                return true;
        }
    }

    public bool RetrieveFromBackpack()
    {
        if (backpack.Count == 0) { Notify("Backpack is empty"); return false; }
        if (!PickEmptyHand(out bool l)) { Notify("You need a free hand"); return false; }

        PickupItem it = backpack[backpack.Count - 1];
        backpack.RemoveAt(backpack.Count - 1);
        HandFor(l).item = it;
        it.Attach(AnchorFor(l));
        return true;
    }

    public bool StowFromHand(bool l)
    {
        PickupItem it = HandFor(l).item;
        if (it == null || it.kind != PickupItem.Kind.Backpack) return false;
        if (backpack.Count >= backpackCapacity) { Notify("Backpack is full"); return false; }

        HandFor(l).item = null;
        backpack.Add(it);
        it.Store(backpackRoot != null ? backpackRoot : transform);
        return true;
    }

    // ------------------------------------------------------------ dropping

    bool TryDropHand(bool l)
    {
        if (twoHanded != null)
        {
            DropItem(twoHanded, TwoHandAnchor);
            twoHanded = null;
            return true;
        }

        Hand h = HandFor(l);
        if (h.item != null)
        {
            DropItem(h.item, AnchorFor(l));
            h.item = null;
            return true;
        }

        if (h.stolen != null) return ReturnStolenEye(l);
        return false;
    }

    void DropItem(PickupItem it, Transform from)
    {
        it.Release(from.position, transform.forward * dropThrowSpeed);
    }

    // ------------------------------------------------------------ input

    void Update()
    {
        if (dropAction != null && dropAction.WasPressedThisFrame())
        {
            bool a = ActiveHandIsLeft;
            if (!TryDropHand(a)) TryDropHand(!a);
        }

        if (backpackAction != null && backpackAction.WasPressedThisFrame())
        {
            bool a = ActiveHandIsLeft;
            // holding a backpack item: put it away. Otherwise take the last one out.
            if (!StowFromHand(a) && !StowFromHand(!a))
                RetrieveFromBackpack();
        }
    }

    // ------------------------------------------------------------ stealing eyes

    // Take 'victim's eye from THEIR hand into one of OUR empty hands.
    public bool TryStealEye(PlayerHands victim, bool eyeLeft, bool fromJar = false)
    {
        if (fromJar ? !StealRules.FromJars : !StealRules.FromPlayers) return false;
        if (victim == null || victim == this || victim.eyeTracker == null) return false;

        // from a jar the eye is "parked"; from a head it is sitting in its owner's hand
        bool available = fromJar ? victim.eyeTracker.IsEyeParked(eyeLeft) : victim.OwnEyeInHand(eyeLeft);
        if (!available) { Notify("That eye is not available"); return false; }
        if (!PickEmptyHand(out bool handLeft)) { Notify("You need a free hand to take an eye"); return false; }

        AdaptiveEyeTracker t = victim.eyeTracker;
        var s = new StolenEye
        {
            victim = victim,
            tracker = t,
            eyeLeft = eyeLeft,
            eyeball = eyeLeft ? t.leftEyeball : t.rightEyeball,
            target = eyeLeft ? t.leftEyeTarget : t.rightEyeTarget,
            cam = eyeLeft ? t.leftCamera : t.rightCamera,
            handLeft = handLeft
        };
        if (s.eyeball == null) return false;

        s.oldParent = s.eyeball.parent;
        s.oldLocalPos = fromJar ? victim.HomeEyeLocalPos(eyeLeft) : s.eyeball.localPosition;
        s.oldLocalScale = s.eyeball.localScale;
        s.wasControlled = eyeLeft ? t.leftEyeControlledByPlayer : t.rightEyeControlledByPlayer;

        // the eye is now away from its owner and nobody controls it
        t.SetEyeParked(eyeLeft, true);
        t.SetFocusSuspended(eyeLeft, true);
        SetControlled(t, eyeLeft, false);

        // a plain, unscaled holder follows our hand so the eye can never get skewed
        Transform anchor = AnchorFor(handLeft);
        s.holder = new GameObject("StolenEyeHolder").transform;
        s.holder.SetPositionAndRotation(anchor.position, anchor.rotation);
        s.eyeball.SetParent(s.holder, true);

        HandFor(handLeft).stolen = s;
        stolenEyes.Add(s);
        Notify("Took their " + (eyeLeft ? "left" : "right") + " eye");
        EyeStolen?.Invoke(victim, eyeLeft, handLeft);
        return true;
    }

    // Give a stolen eye back to its owner (needs the owner's hand for that eye to be free).
    public bool ReturnStolenEye(bool handLeft)
    {
        StolenEye s = HandFor(handLeft).stolen;
        if (s == null) return false;

        Hand ownerHand = s.victim.HandFor(s.eyeLeft);
        if (!s.victim.CanReceiveEye(s.eyeLeft) || ownerHand.reserved)
        {
            Notify("Their hand is full");
            return false;
        }

        HandFor(handLeft).stolen = null;
        ownerHand.reserved = true;
        s.returning = true;

        s.eyeball.SetParent(s.oldParent, true);
        if (s.holder != null) Destroy(s.holder.gameObject);
        s.holder = null;
        return true;
    }

    static void SetControlled(AdaptiveEyeTracker t, bool left, bool value)
    {
        if (left) t.leftEyeControlledByPlayer = value;
        else t.rightEyeControlledByPlayer = value;
    }

    void LateUpdate()
    {
        for (int i = stolenEyes.Count - 1; i >= 0; i--)
        {
            StolenEye s = stolenEyes[i];
            if (s.eyeball == null || s.victim == null)
            {
                if (HandFor(s.handLeft).stolen == s) HandFor(s.handLeft).stolen = null;
                if (s.holder != null) Destroy(s.holder.gameObject);
                stolenEyes.RemoveAt(i);
                continue;
            }

            if (!s.returning) UpdateHeldEye(s);
            else if (UpdateReturningEye(s))
            {
                FinishReturn(s);
                stolenEyes.RemoveAt(i);
                EyeReturned?.Invoke(s.victim, s.eyeLeft);
            }
        }
    }

    void UpdateHeldEye(StolenEye s)
    {
        Transform a = AnchorFor(s.handLeft);
        s.holder.SetPositionAndRotation(a.position, a.rotation);

        s.eyeball.localPosition = Vector3.MoveTowards(s.eyeball.localPosition, Vector3.zero, stolenEyeFlySpeed * Time.deltaTime);

        // nobody controls it, so we aim it (and its owner's camera) ourselves
        s.eyeball.rotation = a.rotation;
        if (s.cam != null) s.cam.rotation = s.eyeball.rotation;
    }

    // returns true when the eye has arrived
    bool UpdateReturningEye(StolenEye s)
    {
        s.eyeball.localPosition = Vector3.MoveTowards(s.eyeball.localPosition, s.oldLocalPos, stolenEyeFlySpeed * Time.deltaTime);

        Vector3 d = s.target != null ? s.target.position - s.eyeball.position : Vector3.zero;
        if (d.sqrMagnitude > 0.0001f)
            s.eyeball.rotation = Quaternion.Slerp(s.eyeball.rotation, Quaternion.LookRotation(d), Time.deltaTime * 10f);
        if (s.cam != null) s.cam.rotation = s.eyeball.rotation;

        return (s.eyeball.localPosition - s.oldLocalPos).sqrMagnitude < 0.0001f;
    }

    void FinishReturn(StolenEye s)
    {
        s.eyeball.localPosition = s.oldLocalPos;
        s.eyeball.localScale = s.oldLocalScale;

        SetControlled(s.tracker, s.eyeLeft, s.wasControlled);
        s.tracker.SetEyeParked(s.eyeLeft, false);
        s.tracker.SetFocusSuspended(s.eyeLeft, false);
        s.victim.HandFor(s.eyeLeft).reserved = false;

        // both eyes home: start from a clean state, straight ahead of the head
        if (!s.tracker.AnyEyeParked)
            s.tracker.SnapEyesForward(s.tracker.transform, returnFocusDistance);
    }

    void ForceHome(StolenEye s)
    {
        if (s.eyeball == null || s.tracker == null) return;
        if (s.holder != null) Destroy(s.holder.gameObject);
        s.eyeball.SetParent(s.oldParent, true);
        FinishReturn(s);
    }

    // ------------------------------------------------------------ feedback

    public void Notify(string text)
    {
        message = text;
        messageUntil = Time.time + 2f;
    }

    string Describe(Hand h, bool l)
    {
        if (twoHanded != null) return "[" + twoHanded.name + " (2 hands)]";
        if (h.item != null) return h.item.name;
        if (h.stolen != null) return "stolen " + (h.stolen.eyeLeft ? "L" : "R") + " eye";
        if (h.reserved) return "(eye returning)";
        if (OwnEyeInHand(l)) return "own eye";
        return "empty";
    }

    void OnGUI()
    {
        if (showHandsOnScreen)
            GUI.Label(new Rect(10, 40, 600, 30),
                "L hand: " + Describe(left, true) + "   R hand: " + Describe(right, false) +
                "   Backpack: " + backpack.Count + "/" + backpackCapacity);

        if (Time.time < messageUntil)
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22 };
            GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40), message, style);
        }
    }
}