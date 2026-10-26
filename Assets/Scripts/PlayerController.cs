using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    InputAction moveAction;
    InputAction lookAction;
    InputAction switchEyeAction;

    [Header("Input")]
    [Tooltip("Optional. If empty, the project-wide actions asset is used.")]
    public InputActionAsset inputActions;

    [Header("Players camera Targets")]
    public Transform leftEyeTarget;
    public Transform rightEyeTarget;

    [Header("Eye System")]
    [Tooltip("The EyeDistanceTargetLocker component. Used to know when the eyes are independent.")]
    public EyeDistanceTargetLocker eyeTracker;

    [Header("Control Variables")]
    [SerializeField] float lookSpeed = 100f;           // head rotation speed (eyes locked together)
    [SerializeField] float independentLookSpeed = 80f; // target orbit speed (eyes independent)
    [SerializeField] float maxPitch = 80f;             // how far up/down you can look (degrees)
    [SerializeField] bool invertY = false;

    Vector2 lookInput;
    float headPitch; // current head pitch in degrees (+ = looking down)

    public enum ActiveEye { Left, Right }
    [SerializeField] ActiveEye activeEye = ActiveEye.Left;
    public ActiveEye CurrentEye => activeEye;

    bool EyesIndependent => eyeTracker != null && !eyeTracker.IsLocked;

    void Awake()
    {
        var actions = inputActions != null ? inputActions : InputSystem.actions;
        if (actions == null)
        {
            Debug.LogError("No Input Actions asset assigned. Set one in the Inspector or in Project Settings > Input System Package.");
            return;
        }

        moveAction = actions.FindAction("Move");
        lookAction = actions.FindAction("Look");
        switchEyeAction = actions.FindAction("Switch Eye");

        if (switchEyeAction != null)
            switchEyeAction.performed += OnSwitchEye;

        // Start pitch from wherever the head is currently pointing
        headPitch = NormalizeAngle(transform.eulerAngles.x);

        Debug.Log($"SwitchEye found: {switchEyeAction != null}");
        if (switchEyeAction != null)
        {
            Debug.Log($"SwitchEye type: {switchEyeAction.type}, bindings: {switchEyeAction.bindings.Count}");
            foreach (var b in switchEyeAction.bindings)
                Debug.Log($"  binding: {b.path}");
        }
    }

    private void OnEnable()
    {
        moveAction?.Enable();
        lookAction?.Enable();
        switchEyeAction?.Enable();
    }
    private void OnDisable()
    {
        moveAction?.Disable();
        lookAction?.Disable();
        switchEyeAction?.Disable();
    }

    void Update()
    {
        if (lookAction != null)
            lookInput = lookAction.ReadValue<Vector2>();

        // Stick up = look up. Positive pitch below means looking DOWN, so flip the sign.
        float yInput = invertY ? lookInput.y : -lookInput.y;

        if (EyesIndependent)
            SteerActiveEyeTarget(lookInput.x, yInput);
        else
            RotateHead(lookInput.x, yInput);
    }

    // Eyes together: turn the whole head object around the midpoint between the eyes.
    void RotateHead(float xInput, float yInput)
    {
        Vector3 pivot = transform.position;
        if (eyeTracker != null && eyeTracker.leftEyeball != null && eyeTracker.rightEyeball != null)
            pivot = (eyeTracker.leftEyeball.position + eyeTracker.rightEyeball.position) * 0.5f;

        // Yaw around world up so it never adds roll
        float yaw = xInput * lookSpeed * Time.deltaTime;
        transform.RotateAround(pivot, Vector3.up, yaw);

        // Pitch around the head's right axis, clamped
        float pitchDelta = yInput * lookSpeed * Time.deltaTime;
        float newPitch = Mathf.Clamp(headPitch + pitchDelta, -maxPitch, maxPitch);
        pitchDelta = newPitch - headPitch;
        headPitch = newPitch;
        transform.RotateAround(pivot, transform.right, pitchDelta);
    }

    // Eyes apart: orbit ONLY the selected eye's target around that eye.
    void SteerActiveEyeTarget(float xInput, float yInput)
    {
        if (Mathf.Approximately(xInput, 0f) && Mathf.Approximately(yInput, 0f)) return;

        bool left = activeEye == ActiveEye.Left;

        if (left && !eyeTracker.leftEyeControlledByPlayer) return;
        if (!left && !eyeTracker.rightEyeControlledByPlayer) return;

        Transform eye = left ? eyeTracker.leftEyeball : eyeTracker.rightEyeball;
        Transform target = left ? leftEyeTarget : rightEyeTarget;
        if (eye == null || target == null) return;

        Vector3 offset = target.position - eye.position;
        if (offset.sqrMagnitude < 0.0001f) return;

        // Yaw
        float yaw = xInput * independentLookSpeed * Time.deltaTime;
        offset = Quaternion.AngleAxis(yaw, Vector3.up) * offset;

        // Pitch (clamped by the target's current elevation angle)
        float elevation = -Mathf.Asin(Mathf.Clamp(offset.normalized.y, -1f, 1f)) * Mathf.Rad2Deg; // + = down
        float pitchDelta = yInput * independentLookSpeed * Time.deltaTime;
        float newElevation = Mathf.Clamp(elevation + pitchDelta, -maxPitch, maxPitch);
        pitchDelta = newElevation - elevation;

        Vector3 right = Vector3.Cross(Vector3.up, offset).normalized;
        if (right.sqrMagnitude > 0.0001f)
            offset = Quaternion.AngleAxis(pitchDelta, right) * offset;

        target.position = eye.position + offset;
    }

    void OnSwitchEye(InputAction.CallbackContext context)
    {
        // Debug.Log($"SwitchEye fired: {context.control.name}, independent: {EyesIndependent}");
        if (!EyesIndependent) return;

        string controlName = context.control.name;

        if (controlName == "leftShoulder" || controlName == "leftBumper")
        {
            activeEye = ActiveEye.Left;
            Debug.Log("Left Bumper Pressed - Controlling LEFT eye");
        }
        else if (controlName == "rightShoulder" || controlName == "rightBumper")
        {
            activeEye = ActiveEye.Right;
            Debug.Log("Right Bumper Pressed - Controlling RIGHT eye");
        }
    }

    static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        return a;
    }

    private void OnDestroy()
    {
        if (switchEyeAction != null)
            switchEyeAction.performed -= OnSwitchEye;
    }
}