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

    [Header("State Control Overrides")]
    [Tooltip("Uncheck this if the Left Eye gets blinded, knocked out, or controlled by an external source.")]
    public bool leftEyeControlledByPlayer = true;
    
    [Tooltip("Uncheck this if the Right Eye gets blinded, knocked out, or controlled by an external source.")]
    public bool rightEyeControlledByPlayer = true;

    [Header("Distance Thresholds (Eye-to-Eye)")]
    [Tooltip("When eye distance is less than this, targets are locked together.")]
    public float lockThreshold = 2.0f;
    
    [Tooltip("When eye distance is greater than this, targets unlock completely.")]
    public float unlockThreshold = 5.0f;

    [Header("Movement Settings")]
    public float lookSpeed = 12.0f;

    private bool isCurrentlyLocked = true;

    void LateUpdate()
    {
        if (leftEyeball == null || rightEyeball == null || leftCamera == null || rightCamera == null || leftEyeTarget == null || rightEyeTarget == null)
            return;

        // 1. Instantly match camera coordinates to physical eyeball paths
        leftCamera.position = leftEyeball.position;
        rightCamera.position = rightEyeball.position;

        // 2. Measure the physical distance between the EYE OBJECTS themselves
        float eyeDistance = Vector3.Distance(leftEyeball.position, rightEyeball.position);

        // 3. Evaluate the locking state machine based on eye distance
        if (eyeDistance <= lockThreshold)
        {
            isCurrentlyLocked = true;
        }
        else if (eyeDistance >= unlockThreshold)
        {
            isCurrentlyLocked = false;
        }

        // 4. Determine look targets independently based on active controls
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

        // 5. Compute vector angles and smoothly execute camera rotations ONLY if player has control
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
}
