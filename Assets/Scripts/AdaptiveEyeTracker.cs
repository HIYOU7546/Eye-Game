using UnityEngine;

public class EyeDistanceTargetLocker : MonoBehaviour
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

    private bool isCurrentlyLocked = true;
    public bool IsLocked => isCurrentlyLocked;

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

        // 6. Compute vector angles and smoothly execute camera rotations ONLY if player has control
        if (leftEyeControlledByPlayer)
        {
            Quaternion targetLeftRot = Quaternion.LookRotation(finalLeftLookTarget - leftEyeball.position);
            leftEyeball.rotation = Quaternion.Slerp(leftEyeball.rotation, targetLeftRot, Time.deltaTime * lookSpeed);
            leftCamera.rotation = leftEyeball.rotation;
        }

        if (rightEyeControlledByPlayer)
        {
            Quaternion targetRightRot = Quaternion.LookRotation(finalRightLookTarget - rightEyeball.position);
            rightEyeball.rotation = Quaternion.Slerp(rightEyeball.rotation, targetRightRot, Time.deltaTime * lookSpeed);
            rightCamera.rotation = rightEyeball.rotation;
        }
    }

    private void UpdateAutoTargets()
    {
        if (!isCurrentlyLocked)
        {
            // Eyes are apart: each eye focuses on its own
            if (independentAutoFocus)
            {
                if (leftEyeControlledByPlayer)
                    FocusSingleEye(leftEyeball, leftEyeTarget, leftEyeDebugColor);
                if (rightEyeControlledByPlayer)
                    FocusSingleEye(rightEyeball, rightEyeTarget, rightEyeDebugColor);
            }
            return;
        }

        // Cast a ray from the center of the two cameras forward
        Vector3 avgCameraPos = (leftCamera.position + rightCamera.position) / 2f;
        Vector3 avgCameraForward = (leftCamera.forward + rightCamera.forward).normalized;

        Ray ray = new Ray(avgCameraPos, avgCameraForward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxFocusDistance, focusLayerMask))
        {
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
        }
    }

    // Independent focus: the player's input decides the DIRECTION the eye looks (the target's angle),
    // and the raycast along that direction decides the DEPTH (how far away the target sits).
    private void FocusSingleEye(Transform eye, Transform target, Color debugColor)
    {
        Vector3 toTarget = target.position - eye.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Ray ray = new Ray(eye.position, toTarget.normalized);

        if (Physics.Raycast(ray, out RaycastHit hit, maxFocusDistance, focusLayerMask))
        {
            if (enableDebug)
            {
                Debug.DrawRay(ray.origin, ray.direction * hit.distance, debugColor);
                Debug.DrawLine(hit.point + Vector3.up * 0.1f, hit.point + Vector3.down * 0.1f, Color.red);
                Debug.DrawLine(hit.point + Vector3.left * 0.1f, hit.point + Vector3.right * 0.1f, Color.red);
            }

            // Slide the target along the eye's ray to the surface it hit
            target.position = Vector3.Lerp(target.position, hit.point, Time.deltaTime * targetMoveSpeed);
        }
        else if (enableDebug)
        {
            Debug.DrawRay(ray.origin, ray.direction * maxFocusDistance, Color.yellow);
        }
    }

}