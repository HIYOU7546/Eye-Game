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

    [Header("Menu")]
    public string leftLabel = "LB";
    public string rightLabel = "RB";
    public string bothLabel = "X";
    [Tooltip("Optional button icons. If empty, the label text is shown in a box instead.")]
    public Texture2D leftIcon;
    public Texture2D rightIcon;
    public Texture2D bothIcon;
    public int fontSize = 20;

    enum Opt { None, Put, Take, Busy }

    class Slot
    {
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
    AdaptiveEyeTracker tracker;

    // This jar can always be used (no cooldown), the eyes' own busy state limits it instead
    public override bool CanInteract => enabled;

    // ------------------------------------------------------------ input

    // Interact button = both eyes
    public override void Interact(PlayerInteractor interactor)
    {
        if (!Setup(interactor)) return;

        Opt both = GetBothOpt();
        if (both == Opt.Take)
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
    }

    // ------------------------------------------------------------ state

    bool Setup(PlayerInteractor interactor)
    {
        if (tracker == null && interactor != null) tracker = interactor.GetComponentInParent<AdaptiveEyeTracker>();
        if (tracker == null) tracker = FindFirstObjectByType<AdaptiveEyeTracker>();
        return tracker != null;
    }

    Slot SlotFor(bool isLeft) => isLeft ? left : right;
    Transform AnchorFor(bool isLeft) => isLeft ? leftEyeAnchor : rightEyeAnchor;

    Opt GetOpt(bool isLeft)
    {
        if (AnchorFor(isLeft) == null) return Opt.None;

        Slot s = SlotFor(isLeft);
        if (s.busy) return Opt.Busy;
        if (s.inJar) return Opt.Take;
        if (tracker.IsEyeParked(isLeft)) return Opt.None; // that eye is in a different jar
        return Opt.Put;
    }

    Opt GetBothOpt()
    {
        Opt l = GetOpt(true), r = GetOpt(false);
        if (l == Opt.Take && r == Opt.Take) return Opt.Take; // jar full
        if (l == Opt.Put && r == Opt.Put) return Opt.Put;    // jar empty and both eyes free
        return Opt.None;
    }

    // ------------------------------------------------------------ moving eyes

    IEnumerator Place(bool isLeft)
    {
        Slot s = SlotFor(isLeft);
        Transform anchor = AnchorFor(isLeft);
        s.busy = true;

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
        s.busy = true;
        s.inJar = false; // stop limiting its range
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

        if (bothSettled && tracker != null && tracker.IsLocked)
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

        Opt l = GetOpt(true), r = GetOpt(false), both = GetBothOpt();
        if (l == Opt.Put || l == Opt.Take)
            rows.Add(new Row { label = leftLabel, icon = leftIcon, text = (l == Opt.Put ? "Put" : "Take") + " left eye" });
        if (r == Opt.Put || r == Opt.Take)
            rows.Add(new Row { label = rightLabel, icon = rightIcon, text = (r == Opt.Put ? "Put" : "Take") + " right eye" });
        if (both == Opt.Put || both == Opt.Take)
            rows.Add(new Row { label = bothLabel, icon = bothIcon, text = (both == Opt.Put ? "Put" : "Take") + " both eyes" });

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