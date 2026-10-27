using System.Collections;
using UnityEngine;

// Put this on the jar INSTEAD of the Interactable component (it inherits from Interactable).
// Interact once: one of your eyes flies into the jar and stays there, looking out from it.
// Interact again: the eye flies back to your head.
public class EyeJar : Interactable
{
    public enum EyeChoice { Selected, Left, Right }

    [Header("Jar")]
    [Tooltip("Empty child object inside the jar. The eye sits here and looks along this object's blue (forward) arrow.")]
    public Transform eyeAnchor;
    [Tooltip("How far in front of the anchor the eye first focuses (it then auto-focuses on what it sees).")]
    public float lookDistance = 2f;
    [Tooltip("Seconds the eye takes to fly to / from the jar.")]
    public float travelTime = 0.6f;
    [Tooltip("Which eye goes in the jar. 'Selected' uses the eye last chosen with the bumpers.")]
    public EyeChoice eyeToPlace = EyeChoice.Selected;

    [Header("Prompts")]
    public string placePrompt = "Leave eye in jar";
    public string takePrompt = "Take eye back";

    AdaptiveEyeTracker tracker;
    bool holdsEye;
    bool holdsLeft;
    bool busy;

    Transform eyeball, target;
    Transform eyeOldParent, targetOldParent;
    Vector3 eyeOldLocalPos, targetOldLocalPos;

    void Awake()
    {
        prompt = placePrompt;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (!CanInteract || busy) return;

        if (eyeAnchor == null)
        {
            Debug.LogWarning("EyeJar: assign the Eye Anchor.", this);
            return;
        }

        if (tracker == null) tracker = interactor.GetComponentInParent<AdaptiveEyeTracker>();
        if (tracker == null) tracker = FindFirstObjectByType<AdaptiveEyeTracker>();
        if (tracker == null)
        {
            Debug.LogWarning("EyeJar: no AdaptiveEyeTracker found.", this);
            return;
        }

        if (holdsEye)
        {
            base.Interact(interactor);
            StartCoroutine(TakeBack());
            return;
        }

        // Only one eye can be away at a time, so one always stays on the head
        if (tracker.AnyEyeParked)
        {
            Debug.Log("EyeJar: one eye is already away, you must keep one on your head.");
            return;
        }

        bool left;
        switch (eyeToPlace)
        {
            case EyeChoice.Left: left = true; break;
            case EyeChoice.Right: left = false; break;
            default:
                var pc = interactor.GetComponentInParent<PlayerController>();
                left = pc == null || pc.CurrentEye == PlayerController.ActiveEye.Left;
                break;
        }

        base.Interact(interactor);
        StartCoroutine(Place(left));
    }

    IEnumerator Place(bool left)
    {
        busy = true;
        holdsLeft = left;

        eyeball = left ? tracker.leftEyeball : tracker.rightEyeball;
        target = left ? tracker.leftEyeTarget : tracker.rightEyeTarget;

        // remember where everything came from
        eyeOldParent = eyeball.parent;
        eyeOldLocalPos = eyeball.localPosition;
        targetOldParent = target.parent;
        targetOldLocalPos = target.localPosition;

        tracker.SetEyeParked(left, true);

        // the eye and its target now belong to the jar, so they stay put when the head moves
        eyeball.SetParent(eyeAnchor, true);
        target.SetParent(eyeAnchor, false);
        target.localPosition = Vector3.forward * lookDistance;

        yield return Fly(eyeball, Vector3.zero);

        holdsEye = true;
        prompt = takePrompt;
        busy = false;
    }

    IEnumerator TakeBack()
    {
        busy = true;

        // hand the target and eye back to the head
        target.SetParent(targetOldParent, false);
        target.localPosition = targetOldLocalPos;
        eyeball.SetParent(eyeOldParent, true);

        yield return Fly(eyeball, eyeOldLocalPos);

        tracker.SetEyeParked(holdsLeft, false);
        holdsEye = false;
        prompt = placePrompt;
        busy = false;
    }

    // Slides an eye's local position to 'endLocal' over travelTime
    IEnumerator Fly(Transform eye, Vector3 endLocal)
    {
        Vector3 start = eye.localPosition;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, travelTime);
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            eye.localPosition = Vector3.Lerp(start, endLocal, eased);
            yield return null;
        }
        eye.localPosition = endLocal;
    }
}