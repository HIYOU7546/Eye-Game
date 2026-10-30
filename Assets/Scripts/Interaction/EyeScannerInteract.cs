using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// An eye scanner with ONE holder.
//   Interact (gamepad south / E) when empty : if the player is holding a required object, one eye flies into the
//                                             holder and a scan runs for 'scanLength' seconds. The eye can not be
//                                             controlled while it is in the scanner.
//   Interact when the scan has finished     : the eye flies back to the head.
//   If the player is NOT holding a required object, nothing is placed and the "missing object" reaction runs.
[DefaultExecutionOrder(-50)] // runs before the eye tracker
public class EyeScanner : Interactable
{
    public enum EyeChoice { Selected, Left, Right }
    enum Opt { None, Scan, Take, Busy }

    [Header("Holder")]
    [Tooltip("Empty child object where the eye sits. The eye looks along its blue (forward) arrow.")]
    public Transform eyeAnchor;
    public float lookDistance = 2f;
    [Tooltip("Seconds an eye takes to fly to / from the scanner.")]
    public float travelTime = 0.6f;
    [Tooltip("When BOTH eyes are back on the head, the targets start this far straight ahead of the head.")]
    public float returnFocusDistance = 10f;
    [Tooltip("Which eye is scanned. 'Selected' uses the eye last chosen with the bumpers.")]
    public EyeChoice eyeToScan = EyeChoice.Selected;

    [Header("Scan")]
    [Tooltip("How many seconds the scan takes.")]
    public float scanLength = 3f;
    [Tooltip("Objects the player can be holding to start a scan. Holding ANY one of them is enough. Leave empty to need nothing.")]
    public List<GameObject> requiredObjects = new List<GameObject>();

    [Header("Events")]
    [Tooltip("The player tried to scan without holding a required object.")]
    public UnityEvent onMissingObject;
    public UnityEvent onScanStarted;
    public UnityEvent onScanComplete;
    public UnityEvent onEyeTaken;

    [Header("Menu")]
    public string interactLabel = "A";
    public string interactKeyLabel = "E";
    public Texture2D interactIcon;
    public int fontSize = 20;

    // 0 to 1 while scanning
    public float ScanProgress { get; private set; }

    // ---- multiplayer hooks (used by NetworkScanner) ----
    public event System.Action<bool> HoldChanged;   // this player put an eye in (true) / took it back (false)
    public event System.Action ScanCompleted;        // this player's scan finished
    bool remoteBusy;                                 // another player is using the scanner
    public void SetRemoteBusy(bool value) { remoteBusy = value; }
    public void RemoteScanComplete() { onScanComplete?.Invoke(); OnScanFinished(); }
    AdaptiveEyeTracker holdTracker;                  // tracker of the player whose eye is in the scanner

    AdaptiveEyeTracker tracker;
    PlayerController playerController;
    PlayerHands hands;

    bool holdsEye;       // an eye is in (or flying in/out of) the holder
    bool holdsLeft;
    bool busy;           // flying or scanning
    bool settled;        // eye is in the holder and locked in place
    bool wasControlled;

    Transform eyeball, target, holder;
    Transform eyeOldParent, targetOldParent;
    Vector3 eyeOldLocalPos, targetOldLocalPos, eyeOldLocalScale, targetOldLocalScale;

    public override bool CanInteract => enabled;

    // ------------------------------------------------------------ input

    public override void Interact(PlayerInteractor interactor)
    {
        if (!Setup(interactor)) return;

        switch (GetOpt())
        {
            case Opt.Scan:
                if (!IsHoldingRequiredObject())
                {
                    onMissingObject?.Invoke();
                    OnMissingObject(interactor);
                    return;
                }

                if (TryPickEye(out bool left))
                {
                    onInteract?.Invoke();
                    StartCoroutine(ScanRoutine(left));
                }
                break;

            case Opt.Take:
                onInteract?.Invoke();
                StartCoroutine(TakeBack());
                break;
        }
    }

    // ============================== YOUR REACTIONS GO HERE ==============================

    // Called when the player tries to scan but is NOT holding any of the required objects.
    protected virtual void OnMissingObject(PlayerInteractor interactor)
    {
        // TODO: add your reaction here (sound, message, alarm, ...)
    }

    // Called when the scan finishes successfully (the eye is still in the scanner).
    protected virtual void OnScanFinished()
    {
        // TODO: add your reaction here (open a door, unlock something, ...)
    }

    // ====================================================================================

    // ------------------------------------------------------------ state

    bool Setup(PlayerInteractor interactor)
    {
        // always use whoever is interacting right now (several players share this scanner)
        if (interactor != null)
        {
            tracker = interactor.GetComponentInParent<AdaptiveEyeTracker>();
            playerController = interactor.GetComponentInParent<PlayerController>();
            hands = interactor.GetComponentInParent<PlayerHands>();
        }
        if (tracker == null) tracker = FindFirstObjectByType<AdaptiveEyeTracker>();
        if (hands == null) hands = FindFirstObjectByType<PlayerHands>();

        return tracker != null;
    }

    Opt GetOpt()
    {
        if (busy) return Opt.Busy;
        if (holdsEye) return Opt.Take;
        if (remoteBusy) return Opt.Busy;
        if (eyeAnchor == null) return Opt.None;
        return TryPickEye(out _) ? Opt.Scan : Opt.None;
    }

    // Decides which eye goes in. Returns false if that eye is not free.
    bool TryPickEye(out bool left)
    {
        bool wantLeft;
        switch (eyeToScan)
        {
            case EyeChoice.Left: wantLeft = true; break;
            case EyeChoice.Right: wantLeft = false; break;
            default: wantLeft = playerController == null || playerController.CurrentEye == PlayerController.ActiveEye.Left; break;
        }

        if (!tracker.IsEyeParked(wantLeft)) { left = wantLeft; return true; }

        // the chosen eye is away somewhere else: with 'Selected' use the other one if it is free
        if (eyeToScan == EyeChoice.Selected && !tracker.IsEyeParked(!wantLeft)) { left = !wantLeft; return true; }

        left = wantLeft;
        return false;
    }

    bool IsHoldingRequiredObject()
    {
        if (requiredObjects == null || requiredObjects.Count == 0) return true; // nothing required
        if (hands == null) return false;

        foreach (var obj in requiredObjects)
            if (hands.IsHolding(obj)) return true;

        return false;
    }

    void SetControlled(bool left, bool value)
    {
        if (left) holdTracker.leftEyeControlledByPlayer = value;
        else holdTracker.rightEyeControlledByPlayer = value;
    }

    // ------------------------------------------------------------ scan / take

    IEnumerator ScanRoutine(bool left)
    {
        busy = true;
        holdsEye = true;
        holdsLeft = left;
        ScanProgress = 0f;
        holdTracker = tracker;
        HoldChanged?.Invoke(true);

        eyeball = left ? holdTracker.leftEyeball : holdTracker.rightEyeball;
        target = left ? holdTracker.leftEyeTarget : holdTracker.rightEyeTarget;

        // remember exactly how things were on the head
        eyeOldParent = eyeball.parent;
        eyeOldLocalPos = eyeball.localPosition;
        eyeOldLocalScale = eyeball.localScale;
        targetOldParent = target.parent;
        targetOldLocalPos = target.localPosition;
        targetOldLocalScale = target.localScale;

        holdTracker.SetEyeParked(left, true);
        holdTracker.SetFocusSuspended(left, true);

        // a plain, unscaled holder follows the anchor so the eye can never get skewed
        holder = new GameObject("ScannerEyeHolder").transform;
        holder.SetPositionAndRotation(eyeAnchor.position, eyeAnchor.rotation);

        eyeball.SetParent(holder, true);
        target.SetParent(holder, false);
        target.localScale = Vector3.one;
        target.localPosition = Vector3.forward * lookDistance;

        yield return Fly(eyeball, Vector3.zero, target);

        // the eye is now fixed in place and can not be controlled
        wasControlled = left ? holdTracker.leftEyeControlledByPlayer : holdTracker.rightEyeControlledByPlayer;
        SetControlled(left, false);
        settled = true;

        onScanStarted?.Invoke();

        // the scan
        float t = 0f;
        while (t < scanLength)
        {
            t += Time.deltaTime;
            ScanProgress = Mathf.Clamp01(t / Mathf.Max(0.01f, scanLength));
            yield return null;
        }
        ScanProgress = 1f;

        onScanComplete?.Invoke();
        OnScanFinished();
        ScanCompleted?.Invoke();

        busy = false; // now the player can take the eye back
    }

    IEnumerator TakeBack()
    {
        busy = true;
        settled = false;

        SetControlled(holdsLeft, wasControlled);
        holdTracker.SetFocusSuspended(holdsLeft, true);

        // hand the target and eye back to the head, exactly as they were
        target.SetParent(targetOldParent, false);
        target.localPosition = targetOldLocalPos;
        target.localScale = targetOldLocalScale;
        eyeball.SetParent(eyeOldParent, true);
        eyeball.localScale = eyeOldLocalScale;

        if (holder != null) Destroy(holder.gameObject);
        holder = null;

        yield return Fly(eyeball, eyeOldLocalPos, target);

        target.localPosition = targetOldLocalPos;
        if (TryLookRotation(eyeball, target, out Quaternion arrived))
            eyeball.rotation = arrived;

        holdTracker.SetEyeParked(holdsLeft, false);
        holdTracker.SetFocusSuspended(holdsLeft, false);

        // both eyes home: start from a clean state, targets straight ahead of the head
        if (!holdTracker.AnyEyeParked)
            holdTracker.SnapEyesForward(holdTracker.transform, returnFocusDistance);

        holdsEye = false;
        ScanProgress = 0f;
        busy = false;
        HoldChanged?.Invoke(false);
        onEyeTaken?.Invoke();
    }

    void LateUpdate()
    {
        if (holder != null && eyeAnchor != null)
            holder.SetPositionAndRotation(eyeAnchor.position, eyeAnchor.rotation);

        // While the eye sits in the scanner it is not controlled, so we aim it (and its camera) ourselves
        if (settled && eyeball != null)
        {
            eyeball.rotation = eyeAnchor.rotation;

            Transform cam = holdsLeft ? holdTracker.leftCamera : holdTracker.rightCamera;
            if (cam != null) cam.rotation = eyeball.rotation;
        }
    }

    // Moves an eye to 'endLocal' over travelTime while turning it toward 'lookAt'
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

    // ------------------------------------------------------------ menu

    public override bool DrawPromptGUI(PlayerInteractor interactor)
    {
        if (!Setup(interactor)) return false;

        Opt opt = GetOpt();
        if (opt == Opt.None) return true; // nothing to offer, skip the default prompt

        bool keys = interactor != null && interactor.UsingKeyboardMouse;
        string label = keys ? interactKeyLabel : interactLabel;

        string text;
        switch (opt)
        {
            case Opt.Scan: text = "Scan eye"; break;
            case Opt.Take: text = "Take eye"; break;
            default:
                text = holdsEye && settled ? "Scanning... " + Mathf.RoundToInt(ScanProgress * 100f) + "%" : "Please wait...";
                break;
        }

        var badge = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize - 4,
            fontStyle = FontStyle.Bold
        };
        var labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = fontSize
        };

        float rowH = fontSize + 20f;
        float w = 380f;
        float h = rowH + 16f + (opt == Opt.Busy ? 14f : 0f);
        var panel = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.68f, w, h);
        GUI.Box(panel, GUIContent.none);

        float y = panel.y + 8f;
        var iconRect = new Rect(panel.x + 12f, y + 2f, rowH - 4f, rowH - 4f);

        if (opt != Opt.Busy)
        {
            if (!keys && interactIcon != null) GUI.DrawTexture(iconRect, interactIcon, ScaleMode.ScaleToFit);
            else GUI.Box(iconRect, label, badge);
            GUI.Label(new Rect(iconRect.xMax + 12f, y, w - rowH - 36f, rowH), text, labelStyle);
        }
        else
        {
            GUI.Label(new Rect(panel.x + 16f, y, w - 32f, rowH), text, labelStyle);

            // progress bar
            var bar = new Rect(panel.x + 16f, y + rowH + 2f, w - 32f, 8f);
            GUI.Box(bar, GUIContent.none);
            if (holdsEye && settled)
                GUI.Box(new Rect(bar.x, bar.y, bar.width * ScanProgress, bar.height), GUIContent.none);
        }

        return true;
    }
}