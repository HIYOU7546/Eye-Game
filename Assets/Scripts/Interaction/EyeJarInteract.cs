using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Put this on the jar INSTEAD of the Interactable component (it inherits from Interactable).
// While you look at the jar a menu shows what you can do:
//   Left bumper  = put / take the LEFT eye
//   Right bumper = put / take the RIGHT eye
//   Interact     = put / take BOTH eyes
// Eyes in the jar keep working: you can still look around with them, but only within a limited range.
[DefaultExecutionOrder(-50)] // runs before the eye tracker so the range limit is applied first
public class EyeJarInteract : Interactable
{
    [Header("Eye Anchors")]
    [Tooltip("Empty child objects inside the jar. Each eye sits at its anchor and looks along its blue (forward) arrow.")]
    public Transform leftEyeAnchor;
    public Transform rightEyeAnchor;

    [Header("Eye Movement")]
    [Tooltip("How far in front of the anchor the eye first focuses (it then auto-focuses on what it sees).")]
    public float lookDistance = 2f;
    [Tooltip("Seconds an eye takes to fly to / from the jar.")]
    public float travelTime = 0.6f;
    [Tooltip("When BOTH eyes are back on the head, the targets start this far straight ahead of the head, then auto-focus takes over.")]
    public float returnFocusDistance = 10f;
    [Tooltip("How far left/right an eye in the jar can look, from its anchor's forward.")]
    [Range(0f, 90f)] public float yawLimit = 60f;
    [Tooltip("How far up/down an eye in the jar can look, from its anchor's forward.")]
    [Range(0f, 90f)] public float pitchLimit = 35f;

    [Header("Controls")]
    [Header("    > Controller")]
    [Tooltip("Button names shown when playing with a gamepad.")]
    public string leftControllerLabel = "LB";
    public string rightControllerLabel = "RB";
    public string bothControllerLabel = "X";
    [Tooltip("Optional button icons. If empty, the label text is shown in a box instead.")]
    public Texture2D leftControllerIcon;
    public Texture2D rightControllerIcon;
    public Texture2D bothControllerIcon;
    [Header("    > Keyboard")]
    [Tooltip("Button names shown when playing with keyboard and mouse.")]
    public string leftKeyLabel = "LMB";
    public string rightKeyLabel = "RMB";
    public string bothKeyLabel = "E";
    public Texture2D leftKeyIcon;
    public Texture2D rightKeyIcon;
    public Texture2D bothKeyIcon;
    public int fontSize = 20;

    enum Opt { None, Put, Take, Steal, Busy }

    class Slot
    {
        public AdaptiveEyeTracker tracker; // whose eye this is
        public PlayerHands owner;          // whose eye this is (null = single player)
        public bool inJar;   // true from the moment the eye is sent until it is taken back
        public bool busy;    // true while the eye is flying
        public Transform eyeball, target;
        public Transform eyeOldParent, targetOldParent;
        public Vector3 eyeOldLocalPos, targetOldLocalPos;
        public Vector3 eyeOldLocalScale, targetOldLocalScale;
        public Transform holder; // un-scaled object that follows the anchor; the eye and target live under it
    }

    readonly Slot left = new Slot();
    readonly Slot right = new Slot();

    // ---- multiplayer hooks (used by NetworkJar) ----
    public event System.Action<bool, bool> SlotChanged;   // (isLeft, occupied): raised for THIS player's own eyes
    readonly bool[] remoteOccupied = new bool[2];          // a slot another player's eye is sitting in
    public void SetRemoteOccupied(bool isLeft, bool occupied) { remoteOccupied[isLeft ? 0 : 1] = occupied; }
    AdaptiveEyeTracker tracker; // the player currently using the jar
    PlayerHands hands;          // the player currently using the jar

    // This jar can always be used (no cooldown), the eyes' own busy state limits it instead
    public override bool CanInteract => enabled;

    // ------------------------------------------------------------ input

    // Interact button = both eyes
    public override void Interact(PlayerInteractor interactor)
    {
        if (!Setup(interactor)) return;

        Opt both = GetBothOpt();
        /*if (both == Opt.Steal)
        {
            StealEye(true);
            StealEye(false);
        }
        else*/ if (both == Opt.Take)
        {
            onInteract?.Invoke();
            StartCoroutine(TakeBack(true));
            StartCoroutine(TakeBack(false));
        }
        else if (both == Opt.Put)
        {
            onInteract?.Invoke();
            StartCoroutine(Place(true));
            StartCoroutine(Place(false));
        }
    }

    public override bool UsesBumper(PlayerInteractor interactor, bool leftBumper)
    {
        if (!Setup(interactor)) return false;
        return GetOpt(leftBumper) != Opt.None;
    }

    public override void OnBumper(PlayerInteractor interactor, bool leftBumper)
    {
        if (!Setup(interactor)) return;

        Opt o = GetOpt(leftBumper);
        if (o == Opt.Put)
        {
            onInteract?.Invoke();
            StartCoroutine(Place(leftBumper));
        }
        else if (o == Opt.Take)
        {
            onInteract?.Invoke();
            StartCoroutine(TakeBack(leftBumper));
        }
        else if (o == Opt.Steal)
        {
            // StealEye(leftBumper);
        }
    }

    // ------------------------------------------------------------ state

    bool Setup(PlayerInteractor interactor)
    {
        // whoever is using the jar right now (several players can share one jar)
        if (interactor != null)
        {
            tracker = interactor.GetComponentInParent<AdaptiveEyeTracker>();
            hands = interactor.GetComponentInParent<PlayerHands>();
        }
        if (tracker == null) tracker = FindFirstObjectByType<AdaptiveEyeTracker>();
        if (hands == null) hands = FindFirstObjectByType<PlayerHands>();
        return tracker != null;
    }

    Slot SlotFor(bool isLeft) => isLeft ? left : right;
    Transform AnchorFor(bool isLeft) => isLeft ? leftEyeAnchor : rightEyeAnchor;

    Opt GetOpt(bool isLeft)
    {
        if (AnchorFor(isLeft) == null) return Opt.None;

        Slot s = SlotFor(isLeft);
        if (s.busy) return Opt.Busy;
        if (!s.inJar && remoteOccupied[isLeft ? 0 : 1]) return Opt.None; // another player's eye is in there
        if (s.inJar)
        {
            bool mine = s.owner == null || s.owner == hands;
            if (mine) return (hands == null || hands.CanReceiveEye(isLeft)) ? Opt.Take : Opt.None;
            return hands != null ? Opt.Steal : Opt.None; // someone else's eye: it can be stolen
        }
        if (tracker.IsEyeParked(isLeft)) return Opt.None; // that eye is in a different jar
        return Opt.Put;
    }

    Opt GetBothOpt()
    {
        Opt l = GetOpt(true), r = GetOpt(false);
        if (l == Opt.Take && r == Opt.Take) return Opt.Take; // jar full
        if (l == Opt.Put && r == Opt.Put) return Opt.Put;    // jar empty and both eyes free
        if (l == Opt.Steal && r == Opt.Steal) return Opt.Steal;
        return Opt.None;
    }

    static string Verb(Opt o) => o == Opt.Put ? "Put" : o == Opt.Steal ? "Steal" : "Take";

    // Takes an eye that ANOTHER player put in this jar, into one of our empty hands.
    // (Eyes can only be stolen from jars.)
    // void StealEye(bool isLeft)
    // {
    //     Slot s = SlotFor(isLeft);
    //     if (hands == null || !s.inJar || s.busy || s.owner == null || s.owner == hands || s.tracker == null) return;

    //     if (!hands.TryStealEyeFromJar(s.owner, s.tracker, isLeft, s.eyeball, s.target,
    //                                   s.eyeOldParent, s.eyeOldLocalPos, s.eyeOldLocalScale))
    //         return; // no free hand (the hands script shows the message)

    //     // the eye has left; give its look-target back to its owner's head, as it was
    //     s.target.SetParent(s.targetOldParent, false);
    //     s.target.localPosition = s.targetOldLocalPos;
    //     s.target.localScale = s.targetOldLocalScale;

    //     if (s.holder != null) Destroy(s.holder.gameObject);
    //     s.holder = null;
    //     s.inJar = false;
    //     s.eyeball = null;
    //     s.target = null;
    //     s.owner = null;
    //     s.tracker = null;
    //     onInteract?.Invoke();
    // }

    // ------------------------------------------------------------ moving eyes

    IEnumerator Place(bool isLeft)
    {
        Slot s = SlotFor(isLeft);
        Transform anchor = AnchorFor(isLeft);
        s.busy = true;
        s.tracker = this.tracker;
        s.owner = hands;
        var tracker = s.tracker; // this slot always uses the tracker of the eye's owner

        s.eyeball = isLeft ? tracker.leftEyeball : tracker.rightEyeball;
        s.target = isLeft ? tracker.leftEyeTarget : tracker.rightEyeTarget;

        // remember exactly how everything was set up on the head
        s.eyeOldParent = s.eyeball.parent;
        s.eyeOldLocalPos = s.eyeball.localPosition;
        s.eyeOldLocalScale = s.eyeball.localScale;
        s.targetOldParent = s.target.parent;
        s.targetOldLocalPos = s.target.localPosition;
        s.targetOldLocalScale = s.target.localScale;

        tracker.SetEyeParked(isLeft, true);
        tracker.SetFocusSuspended(isLeft, true);
        s.inJar = true;
        SlotChanged?.Invoke(isLeft, true);

        // A plain holder object (scale 1, no parent) follows the anchor. Putting the eye under this instead of
        // directly under the (possibly scaled) jar means the eye can never get squashed or skewed.
        var holderGO = new GameObject((isLeft ? "Left" : "Right") + "EyeHolder");
        s.holder = holderGO.transform;
        s.holder.SetPositionAndRotation(anchor.position, anchor.rotation);

        s.eyeball.SetParent(s.holder, true);
        s.target.SetParent(s.holder, false);
        s.target.localScale = Vector3.one;
        s.target.localPosition = Vector3.forward * lookDistance;

        yield return Fly(s.eyeball, Vector3.zero, s.target);

        s.target.localPosition = Vector3.forward * lookDistance; // start focused straight out of the jar
        tracker.SetFocusSuspended(isLeft, false);
        s.busy = false;
    }

    IEnumerator TakeBack(bool isLeft)
    {
        Slot s = SlotFor(isLeft);
        var tracker = s.tracker != null ? s.tracker : this.tracker;
        s.busy = true;
        s.inJar = false; // stop limiting its range
        SlotChanged?.Invoke(isLeft, false);
        tracker.SetFocusSuspended(isLeft, true);

        // hand the target and eye back to the head, exactly as they were
        s.target.SetParent(s.targetOldParent, false);
        s.target.localPosition = s.targetOldLocalPos;
        s.target.localScale = s.targetOldLocalScale;
        s.eyeball.SetParent(s.eyeOldParent, true);
        s.eyeball.localScale = s.eyeOldLocalScale;

        if (s.holder != null) Destroy(s.holder.gameObject);
        s.holder = null;

        yield return Fly(s.eyeball, s.eyeOldLocalPos, s.target);

        // put the target exactly back where it was on the head, so the eyes look the way they did before
        s.target.localPosition = s.targetOldLocalPos;
        if (TryLookRotation(s.eyeball, s.target, out Quaternion arrived))
            s.eyeball.rotation = arrived;

        tracker.SetEyeParked(isLeft, false);
        tracker.SetFocusSuspended(isLeft, false);

        // Both eyes home: start from a clean state, targets straight ahead of the head
        if (!tracker.AnyEyeParked)
            tracker.SnapEyesForward(tracker.transform, returnFocusDistance);

        s.owner = null;
        s.tracker = null;
        s.busy = false;
    }

    // Slides an eye to 'endLocal' over travelTime while turning it toward 'lookAt' over the same time,
    // so it arrives already facing the right way instead of slowly catching up afterwards.
    IEnumerator Fly(Transform eye, Vector3 endLocal, Transform lookAt)
    {
        Vector3 start = eye.localPosition;
        Quaternion startRot = eye.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, travelTime);
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            eye.localPosition = Vector3.Lerp(start, endLocal, eased);
            if (TryLookRotation(eye, lookAt, out Quaternion want))
                eye.rotation = Quaternion.Slerp(startRot, want, eased);

            yield return null;
        }

        eye.localPosition = endLocal;
        if (TryLookRotation(eye, lookAt, out Quaternion final))
            eye.rotation = final;
    }

    static bool TryLookRotation(Transform eye, Transform lookAt, out Quaternion rot)
    {
        Vector3 d = lookAt != null ? lookAt.position - eye.position : Vector3.zero;
        if (d.sqrMagnitude < 0.0001f)
        {
            rot = eye.rotation;
            return false;
        }
        rot = Quaternion.LookRotation(d);
        return true;
    }

    // ------------------------------------------------------------ limited range

    void LateUpdate()
    {
        // keep each eye holder on its anchor (the jar may move or rotate)
        FollowAnchor(left, leftEyeAnchor);
        FollowAnchor(right, rightEyeAnchor);

        bool bothSettled = left.inJar && right.inJar && !left.busy && !right.busy;

        if (bothSettled && left.tracker != null && left.tracker == right.tracker && left.tracker.IsLocked)
            LimitShared();   // eyes together: limit the shared look direction
        else
        {
            LimitEye(true);  // eyes apart: limit each eye on its own
            LimitEye(false);
        }
    }

    void FollowAnchor(Slot s, Transform anchor)
    {
        if (s.holder != null && anchor != null)
            s.holder.SetPositionAndRotation(anchor.position, anchor.rotation);
    }

    // Clamps 'dir' to the yaw/pitch range around the frame's forward. Returns true if it had to change it.
    bool ClampToRange(Quaternion frame, ref Vector3 dir)
    {
        Vector3 local = Quaternion.Inverse(frame) * dir;
        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg; // + = up

        float cy = Mathf.Clamp(yaw, -yawLimit, yawLimit);
        float cp = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);
        if (Mathf.Approximately(cy, yaw) && Mathf.Approximately(cp, pitch)) return false;

        dir = frame * (Quaternion.Euler(-cp, cy, 0f) * Vector3.forward);
        return true;
    }

    void LimitEye(bool isLeft)
    {
        Slot s = SlotFor(isLeft);
        Transform anchor = AnchorFor(isLeft);
        if (!s.inJar || anchor == null || s.eyeball == null || s.target == null) return;

        Vector3 offset = s.target.position - s.eyeball.position;
        float dist = offset.magnitude;
        if (dist < 0.001f) return;

        Vector3 dir = offset / dist;
        if (ClampToRange(anchor.rotation, ref dir))
            s.target.position = s.eyeball.position + dir * dist;
    }

    void LimitShared()
    {
        if (leftEyeAnchor == null || rightEyeAnchor == null) return;
        if (left.eyeball == null || right.eyeball == null || left.target == null || right.target == null) return;

        Vector3 mid = (left.eyeball.position + right.eyeball.position) * 0.5f;
        Vector3 center = (left.target.position + right.target.position) * 0.5f;
        Vector3 offset = center - mid;
        float dist = offset.magnitude;
        if (dist < 0.001f) return;

        Quaternion frame = Quaternion.Slerp(leftEyeAnchor.rotation, rightEyeAnchor.rotation, 0.5f);
        Vector3 dir = offset / dist;
        if (ClampToRange(frame, ref dir))
        {
            Vector3 clamped = mid + dir * dist;
            left.target.position = clamped;
            right.target.position = clamped;
        }
    }

    // ------------------------------------------------------------ menu

    struct Row
    {
        public string label, text;
        public Texture2D icon;
    }

    public override bool DrawPromptGUI(PlayerInteractor interactor)
    {
        if (!Setup(interactor)) return false;

        var rows = new List<Row>();
        bool keys = interactor != null && interactor.UsingKeyboardMouse;
        string lLabel = keys ? leftKeyLabel : leftControllerLabel;
        string rLabel = keys ? rightKeyLabel : rightControllerLabel;
        string bLabel = keys ? bothKeyLabel : bothControllerLabel;
        Texture2D leftIcon = keys ? leftKeyIcon : leftControllerIcon;
        Texture2D rightIcon = keys ? rightKeyIcon : rightControllerIcon;
        Texture2D bothIcon = keys ? bothKeyIcon : bothControllerIcon;

        Opt l = GetOpt(true), r = GetOpt(false), both = GetBothOpt();
        if (l == Opt.Put || l == Opt.Take || l == Opt.Steal)
            rows.Add(new Row { label = lLabel, icon = keys ? null : leftIcon, text = Verb(l) + " left eye" });
        if (r == Opt.Put || r == Opt.Take || r == Opt.Steal)
            rows.Add(new Row { label = rLabel, icon = keys ? null : rightIcon, text = Verb(r) + " right eye" });
        if (both == Opt.Put || both == Opt.Take || both == Opt.Steal)
            rows.Add(new Row { label = bLabel, icon = keys ? null : bothIcon, text = Verb(both) + " both eyes" });

        if (rows.Count == 0)
        {
            // nothing to offer: say why, so it is not a silent failure
            string why = (leftEyeAnchor == null || rightEyeAnchor == null)
                ? "EyeJar: assign the Left/Right Eye Anchor"
                : "EyeJar: your eyes are busy or in another jar";
            GUI.Box(new Rect((Screen.width - 380f) * 0.5f, Screen.height * 0.68f, 380f, 36f), why);
            return true;
        }

        var badge = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize - 4,
            fontStyle = FontStyle.Bold
        };
        var text = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = fontSize
        };

        float rowH = fontSize + 20f;
        float w = 380f;
        float h = rows.Count * rowH + 16f;
        var panel = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.68f, w, h);
        GUI.Box(panel, GUIContent.none);

        for (int i = 0; i < rows.Count; i++)
        {
            float y = panel.y + 8f + i * rowH;
            var iconRect = new Rect(panel.x + 12f, y + 2f, rowH - 4f, rowH - 4f);

            if (rows[i].icon != null)
                GUI.DrawTexture(iconRect, rows[i].icon, ScaleMode.ScaleToFit);
            else
                GUI.Box(iconRect, rows[i].label, badge);

            GUI.Label(new Rect(iconRect.xMax + 12f, y, w - rowH - 36f, rowH), rows[i].text, text);
        }

        return true;
    }
}