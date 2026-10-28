using UnityEngine;

public class AdaptiveEyeTracker : MonoBehaviour
{
    [Header("Physical Eyeballs")]
    public Transform leftEyeball;
    public Transform rightEyeball;

    [Header("Detached Cameras")]
    public Transform leftCamera;
    public Transform rightCamera;

    [Header("Targets to Track")]
    public Transform leftEyeTarget;
    public Transform rightEyeTarget;

    [Header("Auto-Focus / Raycast Settings")]
    [Tooltip("LayerMask to filter what the eyes can focus on.")]
    public LayerMask focusLayerMask = ~0;
    [Tooltip("Maximum distance to search for a surface to look at.")]
    public float maxFocusDistance = 50.0f;
    [Tooltip("Smoothing speed for moving the targets to the hit point.")]
    public float targetMoveSpeed = 10.0f;
    [Tooltip("When the eyes are independent, each eye focuses on whatever it is looking at on its own.")]
    public bool independentAutoFocus = true;
    [Tooltip("Seconds of looking at nothing before the eyes settle on the default focus distance.")]
    public float noHitDelay = 1.5f;
    [Tooltip("How far away the eyes focus when there is nothing to look at.")]
    public float defaultFocusDistance = 10f;
    public Color leftEyeDebugColor = Color.cyan;
    public Color rightEyeDebugColor = Color.magenta;

    [Header("State Control Overrides")]
    [Tooltip("Uncheck this if the Left Eye gets blinded, knocked out, or controlled by an external source.")]
    public bool leftEyeControlledByPlayer = true;
    [Tooltip("Uncheck this if the Right Eye gets blinded, knocked out, or controlled by an external source.")]
    public bool rightEyeControlledByPlayer = true;

    [Header("Distance Thresholds (Eye-to-Eye)")]
    [Tooltip("When eye distance is less than this, targets are locked together.")]
    public float lockThreshold = 2.0f;
    [Tooltip("When eye distance is greater than this, unlocks completely.")]
    public float unlockThreshold = 5.0f;

    [Header("Movement Settings")]
    public float lookSpeed = 12.0f;

    [Header("Half-Screen Offset (one eye away from the head)")]
    [Tooltip("When only one eye is on the head it only fills half the screen. This turns its camera sideways so the CENTER OF ITS HALF is where you aim.")]
    public bool offsetHeadEyeToHalfScreen = true;
    [Tooltip("On = the left eye is shown on the left half of the screen (right eye on the right). Untick if yours is the other way round.")]
    public bool leftEyeShowsLeftHalf = true;
    [Tooltip("0 = work it out from the camera's field of view. Otherwise use this many degrees.")]
    public float halfScreenOffsetDegrees = 0f;
    [Tooltip("How quickly the offset eases in and out.")]
    public float offsetBlendSpeed = 6f;
    private float leftOffsetCurrent;
    private float rightOffsetCurrent;

    private bool isCurrentlyLocked = true;
    private float lockedMissTimer;
    private float leftMissTimer;
    private float rightMissTimer;
    public bool IsLocked => isCurrentlyLocked;

    // Eyes that have been left somewhere else (e.g. in a jar). While any eye is parked the eyes stay independent.
    private bool leftEyeParked;
    private bool rightEyeParked;
    public bool AnyEyeParked => leftEyeParked || rightEyeParked;

    // While an eye is flying between the head and a jar its auto-focus is paused, so the raycast can't
    // drag its target onto whatever it passes through on the way.
    private bool leftFocusSuspended;
    private bool rightFocusSuspended;
    public void SetFocusSuspended(bool left, bool suspended)
    {
        if (left) leftFocusSuspended = suspended; else rightFocusSuspended = suspended;
    }
    public bool IsEyeParked(bool left) => left ? leftEyeParked : rightEyeParked;
    public void SetEyeParked(bool left, bool parked)
    {
        if (left) leftEyeParked = parked; else rightEyeParked = parked;
    }

    [Header("Debug Settings")]
    [Tooltip("Color of the raycast lines in the scene view.")]
    public bool enableDebug = true;
    public Color debugRayColor = Color.green;


    void LateUpdate()
    {
        if (leftEyeball == null || rightEyeball == null || leftCamera == null || rightCamera == null || leftEyeTarget == null || rightEyeTarget == null)
            return;

        // 1. Instantly match camera coordinates to physical eyeball paths
        leftCamera.position = leftEyeball.position;
        rightCamera.position = rightEyeball.position;

        // 2. Perform auto-focus raycast using the midpoint or average camera view
        UpdateAutoTargets();

        // 3. Measure the physical distance between the EYE OBJECTS themselves
        float eyeDistance = Vector3.Distance(leftEyeball.position, rightEyeball.position);

        // 4. Evaluate the locking state machine based on eye distance
        if (eyeDistance <= lockThreshold)
        {
            isCurrentlyLocked = true;
        }
        else if (eyeDistance >= unlockThreshold)
        {
            isCurrentlyLocked = false;
        }

        // One eye away (e.g. in a jar) and the other not: always independent, however close they are.
        // If BOTH are away they use the normal distance rules, like they do on the head.
        if (leftEyeParked != rightEyeParked)
            isCurrentlyLocked = false;

        // 5. Determine look targets independently based on active controls
        Vector3 finalLeftLookTarget = leftEyeTarget.position;
        Vector3 finalRightLookTarget = rightEyeTarget.position;

        // ONLY apply the convergence locking system if BOTH eyes are actively controlled by the player
        if (isCurrentlyLocked && leftEyeControlledByPlayer && rightEyeControlledByPlayer)
        {
            // Find the central midpoint between the two active user targets
            Vector3 sharedCenterPoint = (leftEyeTarget.position + rightEyeTarget.position) / 2f;
            finalLeftLookTarget = sharedCenterPoint;
            finalRightLookTarget = sharedCenterPoint;

            // Snap the actual physical transform objects so they stay grouped together
            leftEyeTarget.position = sharedCenterPoint;
            rightEyeTarget.position = sharedCenterPoint;
        }

        UpdateHalfScreenOffsets();

        // 6. Compute vector angles and smoothly execute camera rotations ONLY if player has control
        if (leftEyeControlledByPlayer)
        {
            Vector3 leftDir = Quaternion.AngleAxis(leftOffsetCurrent, Vector3.up) * (finalLeftLookTarget - leftEyeball.position);
            Quaternion targetLeftRot = Quaternion.LookRotation(leftDir);
            leftEyeball.rotation = Quaternion.Slerp(leftEyeball.rotation, targetLeftRot, Time.deltaTime * lookSpeed);
            leftCamera.rotation = leftEyeball.rotation;
        }

        if (rightEyeControlledByPlayer)
        {
            Vector3 rightDir = Quaternion.AngleAxis(rightOffsetCurrent, Vector3.up) * (finalRightLookTarget - rightEyeball.position);
            Quaternion targetRightRot = Quaternion.LookRotation(rightDir);
            rightEyeball.rotation = Quaternion.Slerp(rightEyeball.rotation, targetRightRot, Time.deltaTime * lookSpeed);
            rightCamera.rotation = rightEyeball.rotation;
        }
    }

    // Puts both targets straight ahead of 'head' at 'distance', clears the half-screen offset and points both eyes
    // (and cameras) at it immediately. Auto-focus then moves the targets to whatever is really in front of you.
    // Used when both eyes are back on the head, so they start from a clean, centred state.
    public void SnapEyesForward(Transform head, float distance)
    {
        if (head == null || leftEyeball == null || rightEyeball == null || leftEyeTarget == null || rightEyeTarget == null)
            return;

        Vector3 point = head.position + head.forward * distance;
        leftEyeTarget.position = point;
        rightEyeTarget.position = point;

        leftOffsetCurrent = 0f;
        rightOffsetCurrent = 0f;
        lockedMissTimer = 0f;
        leftMissTimer = 0f;
        rightMissTimer = 0f;

        AimNow(leftEyeball, leftCamera, point);
        AimNow(rightEyeball, rightCamera, point);
    }

    private static void AimNow(Transform eye, Transform cam, Vector3 point)
    {
        Vector3 dir = point - eye.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        eye.rotation = Quaternion.LookRotation(dir);
        if (cam != null) cam.rotation = eye.rotation;
    }

    // Angle (degrees) that puts the centre of a half-width view in the middle of that half
    private float HalfScreenAngle(Transform cameraTransform)
    {
        if (halfScreenOffsetDegrees > 0f) return halfScreenOffsetDegrees;

        var cam = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
        if (cam == null) return 0f;

        float vFov = cam.fieldOfView * Mathf.Deg2Rad;
        float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * cam.aspect);
        return Mathf.Atan(0.5f * Mathf.Tan(hFov * 0.5f)) * Mathf.Rad2Deg;
    }

    // Only the eye that is still on the head (while the other is away) gets turned toward the middle of its half.
    private void UpdateHalfScreenOffsets()
    {
        float desiredLeft = 0f;
        float desiredRight = 0f;

        if (offsetHeadEyeToHalfScreen)
        {
            float side = leftEyeShowsLeftHalf ? 1f : -1f;
            if (rightEyeParked && !leftEyeParked)  desiredLeft = side * HalfScreenAngle(leftCamera);
            if (leftEyeParked && !rightEyeParked)  desiredRight = -side * HalfScreenAngle(rightCamera);
        }

        float k = Mathf.Clamp01(Time.deltaTime * offsetBlendSpeed);
        leftOffsetCurrent = Mathf.Lerp(leftOffsetCurrent, desiredLeft, k);
        rightOffsetCurrent = Mathf.Lerp(rightOffsetCurrent, desiredRight, k);
    }

    private void UpdateAutoTargets()
    {
        if (!isCurrentlyLocked)
        {
            // Eyes are apart: each eye focuses on its own
            lockedMissTimer = 0f;
            if (independentAutoFocus)
            {
                if (leftEyeControlledByPlayer && !leftFocusSuspended)
                    FocusSingleEye(leftEyeball, leftEyeTarget, leftEyeDebugColor, ref leftMissTimer);
                if (rightEyeControlledByPlayer && !rightFocusSuspended)
                    FocusSingleEye(rightEyeball, rightEyeTarget, rightEyeDebugColor, ref rightMissTimer);
            }
            return;
        }

        leftMissTimer = 0f;
        rightMissTimer = 0f;

        // an eye is in flight: leave the targets alone
        if (leftFocusSuspended || rightFocusSuspended) return;

        // Cast a ray from the center of the two cameras forward
        Vector3 avgCameraPos = (leftCamera.position + rightCamera.position) / 2f;
        Vector3 avgCameraForward = (leftCamera.forward + rightCamera.forward).normalized;

        Ray ray = new Ray(avgCameraPos, avgCameraForward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxFocusDistance, focusLayerMask))
        {
            lockedMissTimer = 0f;
            Vector3 hitPoint = hit.point;

            // DEBUG: Draws a solid line to the hit object and a small cross directly on the hit surface point
            if (enableDebug)
            {
                Debug.DrawRay(ray.origin, ray.direction * hit.distance, debugRayColor);
                Debug.DrawLine(hitPoint + Vector3.up * 0.1f, hitPoint + Vector3.down * 0.1f, Color.red);
                Debug.DrawLine(hitPoint + Vector3.left * 0.1f, hitPoint + Vector3.right * 0.1f, Color.red);
            }

            // Smoothly move the targets toward what we hit
            if (leftEyeControlledByPlayer)
            {
                leftEyeTarget.position = Vector3.Lerp(leftEyeTarget.position, hitPoint, Time.deltaTime * targetMoveSpeed);
            }
            if (rightEyeControlledByPlayer)
            {
                rightEyeTarget.position = Vector3.Lerp(rightEyeTarget.position, hitPoint, Time.deltaTime * targetMoveSpeed);
            }
        }
        else
        {
            // DEBUG: Draws a full-length line in the air if nothing is being hit
            Debug.DrawRay(ray.origin, ray.direction * maxFocusDistance, Color.yellow);

            // Nothing hit: after a delay, drift to the default focus distance
            lockedMissTimer += Time.deltaTime;
            if (lockedMissTimer >= noHitDelay)
            {
                Vector3 defaultPoint = ray.origin + ray.direction * defaultFocusDistance;
                if (leftEyeControlledByPlayer)
                    leftEyeTarget.position = Vector3.Lerp(leftEyeTarget.position, defaultPoint, Time.deltaTime * targetMoveSpeed);
                if (rightEyeControlledByPlayer)
                    rightEyeTarget.position = Vector3.Lerp(rightEyeTarget.position, defaultPoint, Time.deltaTime * targetMoveSpeed);
            }
        }
    }

    // Independent focus: the player's input decides the DIRECTION the eye looks (the target's angle),
    // and the raycast along that direction decides the DEPTH (how far away the target sits).
    private void FocusSingleEye(Transform eye, Transform target, Color debugColor, ref float missTimer)
    {
        Vector3 toTarget = target.position - eye.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Ray ray = new Ray(eye.position, toTarget.normalized);

        if (Physics.Raycast(ray, out RaycastHit hit, maxFocusDistance, focusLayerMask))
        {
            missTimer = 0f;
            if (enableDebug)
            {
                Debug.DrawRay(ray.origin, ray.direction * hit.distance, debugColor);
                Debug.DrawLine(hit.point + Vector3.up * 0.1f, hit.point + Vector3.down * 0.1f, Color.red);
                Debug.DrawLine(hit.point + Vector3.left * 0.1f, hit.point + Vector3.right * 0.1f, Color.red);
            }

            // Slide the target along the eye's ray to the surface it hit
            target.position = Vector3.Lerp(target.position, hit.point, Time.deltaTime * targetMoveSpeed);
        }
        else
        {
            if (enableDebug)
                Debug.DrawRay(ray.origin, ray.direction * maxFocusDistance, Color.yellow);

            // Nothing hit: after a delay, slide this eye's target to the default focus distance
            missTimer += Time.deltaTime;
            if (missTimer >= noHitDelay)
            {
                Vector3 defaultPoint = ray.origin + ray.direction * defaultFocusDistance;
                target.position = Vector3.Lerp(target.position, defaultPoint, Time.deltaTime * targetMoveSpeed);
            }
        }
    }

}