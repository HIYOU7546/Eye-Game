using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    InputAction moveAction;
    InputAction lookAction;
    InputAction switchEyeAction;
    InputAction crouchAction;

    [Header("Input")]
    [Tooltip("Optional. If empty, the project-wide actions asset is used.")]
    public InputActionAsset inputActions;

    [Header("Players camera Targets")]
    public Transform leftEyeTarget;
    public Transform rightEyeTarget;

    [Header("Interaction")]
    [Tooltip("Optional. Found automatically on this object if empty. Lets objects like the eye jar take over the bumpers.")]
    public PlayerInteractor interactor;

    [Header("Eye System")]
    [Tooltip("The AdaptiveEyeTracker component. Used to know when the eyes are independent.")]
    public AdaptiveEyeTracker eyeTracker;

    [Header("Look Variables")]
    [SerializeField] float lookSpeed = 100f;           // head rotation speed (eyes locked together)
    [SerializeField] float independentLookSpeed = 80f; // target orbit speed (eyes independent)
    [SerializeField] float maxPitch = 80f;             // how far up/down you can look (degrees)
    [SerializeField] bool invertY = false;

    [Header("Movement")]
    [Tooltip("The object that gets moved. Leave empty to move this object (Character Head).")]
    public Transform moveTarget;
    [SerializeField] float moveSpeed = 5f;
    [Tooltip("How fast the head turns (degrees per second) to face the way you move while both eyes are away.")]
    [SerializeField] float bodyTurnSpeed = 360f;

    [Header("Crouch")]
    [Tooltip("The object that gets lowered. Leave empty to crouch this object (Character Head)")]
    public Transform crouchTarget;
    [SerializeField] float crouchSpeed = 2f;   // how fast you lower/raise (units per second)
    [SerializeField] float crouchDepth = 1f;   // how far down the crouch target drops (units)
    [Range(0.1f, 1f)]
    [SerializeField] float crouchMoveMultiplier = 0.5f; // walk speed while crouched (0.5 = half speed)

    Vector2 moveInput;
    Vector2 lookInput;
    float headPitch; // current head pitch in degrees (+ = looking down)
    bool isCrouching;
    Vector3 lastEyeMoveForward = Vector3.forward;
    float currentCrouchOffset; // how far we're currently lowered (0 = standing, crouchDepth = fully crouched)

    public enum ActiveEye { Left, Right }
    [SerializeField] ActiveEye activeEye = ActiveEye.Left;
    public ActiveEye CurrentEye => activeEye;

    bool EyesIndependent => eyeTracker != null && !eyeTracker.IsLocked;
    bool ActiveEyeParked => eyeTracker != null && eyeTracker.IsEyeParked(activeEye == ActiveEye.Left);

    void Awake()
    {
        if (interactor == null) interactor = GetComponent<PlayerInteractor>();

        var actions = inputActions != null ? inputActions : InputSystem.actions;
        if (actions == null)
        {
            Debug.LogError("No Input Actions asset assigned. Set one in the Inspector or in Project Settings > Input System Package.");
            return;
        }

        moveAction = actions.FindAction("Move");
        lookAction = actions.FindAction("Look");
        switchEyeAction = actions.FindAction("Switch Eye");
        crouchAction = actions.FindAction("Crouch");

        if (switchEyeAction != null)
            switchEyeAction.performed += OnSwitchEye;
        if (crouchAction != null)
            crouchAction.performed += OnCrouch;

        // Start pitch from wherever the head is currently pointing
        headPitch = NormalizeAngle(transform.eulerAngles.x);

        Debug.Log($"Crouch found: {crouchAction != null}");
        if (crouchAction != null)
        {
            foreach (var b in crouchAction.bindings)
                Debug.Log($"  crouch binding: {b.path}");
        }

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
        crouchAction?.Enable();
    }
    private void OnDisable()
    {
        moveAction?.Disable();
        lookAction?.Disable();
        switchEyeAction?.Disable();
        crouchAction?.Disable();
    }

    void Update()
    {
        if (lookAction != null)
            lookInput = lookAction.ReadValue<Vector2>();
        if (moveAction != null)
            moveInput = moveAction.ReadValue<Vector2>();

        // Fallback: if there is no "Crouch" action in the asset, read the left stick press directly
        if (crouchAction == null && Gamepad.current != null && Gamepad.current.leftStickButton.wasPressedThisFrame)
            isCrouching = !isCrouching;

        MovePlayer();
        UpdateCrouch();

        // Stick up = look up. Positive pitch below means looking DOWN, so flip the sign.
        float yInput = invertY ? lookInput.y : -lookInput.y;

        bool bothParked = eyeTracker != null && eyeTracker.IsEyeParked(true) && eyeTracker.IsEyeParked(false);

        if (!EyesIndependent)
        {
            // Eyes together. If both are away (in a jar) the stick aims them together, otherwise it turns the head.
            if (bothParked) SteerSharedTarget(lookInput.x, yInput);
            else RotateHead(lookInput.x, yInput);
        }
        else if (eyeTracker.AnyEyeParked && !bothParked && !ActiveEyeParked)
            RotateHead(lookInput.x, yInput);   // one eye in a jar, controlling the one still on the head: turn the head with it
        else
            SteerActiveEyeTarget(lookInput.x, yInput); // eyes apart: the stick moves the selected eye's target
    }

    // Which way is "forward" for movement.
    // Normally that is where the head faces. With BOTH eyes away from the head (in jars) it is where the eyes look:
    // forward moves away from the eyes, backward moves toward them.
    bool GetMoveDirections(out Vector3 forward, out Vector3 right)
    {
        forward = transform.forward;

        bool bothEyesAway = eyeTracker != null
            && eyeTracker.IsEyeParked(true) && eyeTracker.IsEyeParked(false)
            && eyeTracker.leftEyeball != null && eyeTracker.rightEyeball != null;

        if (bothEyesAway)
        {
            Vector3 eyesForward = eyeTracker.leftEyeball.forward + eyeTracker.rightEyeball.forward;
            eyesForward.y = 0f;
            if (eyesForward.sqrMagnitude > 0.0001f)
                forward = eyesForward;
            else
                forward = lastEyeMoveForward; // looking straight up/down: keep the last direction
        }

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        if (bothEyesAway) lastEyeMoveForward = forward;

        right = Vector3.Cross(Vector3.up, forward);
        return bothEyesAway;
    }

    // Turns the head (yaw only) toward a ground direction, at bodyTurnSpeed degrees per second.
    void TurnHeadToward(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Vector3 current = transform.forward;
        current.y = 0f;
        if (current.sqrMagnitude < 0.0001f) return;

        float angle = Vector3.SignedAngle(current, direction, Vector3.up);
        float step = Mathf.Clamp(angle, -bodyTurnSpeed * Time.deltaTime, bodyTurnSpeed * Time.deltaTime);
        transform.RotateAround(transform.position, Vector3.up, step);
    }

    // Left stick moves along the ground, relative to where the head is facing.
    void MovePlayer()
    {
        if (moveInput.sqrMagnitude < 0.0001f) return;

        // Flatten the head's direction so looking up/down doesn't fly or dig you into the floor
        bool eyesLead = GetMoveDirections(out Vector3 forward, out Vector3 right);

        Vector3 move = (forward * moveInput.y + right * moveInput.x);

        // Both eyes away: the head turns to face the way you are moving
        if (eyesLead) TurnHeadToward(move);

        float speed = isCrouching ? moveSpeed * crouchMoveMultiplier : moveSpeed;
        move = Vector3.ClampMagnitude(move, 1f) * speed * Time.deltaTime;

        Transform mover = moveTarget != null ? moveTarget : transform;
        mover.position += move;
    }

    // Crouch: press the left stick down to toggle crouch / stand
    void OnCrouch(InputAction.CallbackContext context)
    {
        isCrouching = !isCrouching;
        Debug.Log($"Crouch toggled: {isCrouching} (from {context.control.path})");
    }

    // Smoothly lowers/raises the crouch target. Applies only the change each frame,
    // so it plays nicely with movement (which also changes position).
    void UpdateCrouch()
    {
        float desiredOffset = isCrouching ? crouchDepth : 0f;
        if (Mathf.Approximately(currentCrouchOffset, desiredOffset)) return;

        float newOffset = Mathf.MoveTowards(currentCrouchOffset, desiredOffset, crouchSpeed * Time.deltaTime);
        float delta = newOffset - currentCrouchOffset;
        currentCrouchOffset = newOffset;

        Transform t = crouchTarget != null ? crouchTarget : transform;
        t.position += Vector3.down * delta;
    }

    // Eyes together: turn the whole head object around the midpoint between the eyes.
    void RotateHead(float xInput, float yInput)
    {
        Vector3 pivot = transform.position;
        if (eyeTracker != null && eyeTracker.leftEyeball != null && eyeTracker.rightEyeball != null)
        {
            bool leftParked = eyeTracker.IsEyeParked(true);
            bool rightParked = eyeTracker.IsEyeParked(false);

            if (leftParked && !rightParked)
                pivot = eyeTracker.rightEyeball.position;       // only the right eye is still on the head
            else if (rightParked && !leftParked)
                pivot = eyeTracker.leftEyeball.position;        // only the left eye is still on the head
            else
                pivot = (eyeTracker.leftEyeball.position + eyeTracker.rightEyeball.position) * 0.5f;
        }

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

    // Turns an offset (eye -> target) by the stick input, keeping the pitch inside +/- maxPitch.
    Vector3 OrbitOffset(Vector3 offset, float xInput, float yInput, float speed)
    {
        // Yaw
        float yaw = xInput * speed * Time.deltaTime;
        offset = Quaternion.AngleAxis(yaw, Vector3.up) * offset;

        // Pitch (clamped by the current elevation angle)
        float elevation = -Mathf.Asin(Mathf.Clamp(offset.normalized.y, -1f, 1f)) * Mathf.Rad2Deg; // + = down
        float pitchDelta = yInput * speed * Time.deltaTime;
        float newElevation = Mathf.Clamp(elevation + pitchDelta, -maxPitch, maxPitch);
        pitchDelta = newElevation - elevation;

        Vector3 right = Vector3.Cross(Vector3.up, offset).normalized;
        if (right.sqrMagnitude > 0.0001f)
            offset = Quaternion.AngleAxis(pitchDelta, right) * offset;

        return offset;
    }

    // Eyes apart: orbit ONLY the selected eye's target around that eye.
    void SteerActiveEyeTarget(float xInput, float yInput)
    {
        if (eyeTracker == null) return;
        if (Mathf.Approximately(xInput, 0f) && Mathf.Approximately(yInput, 0f)) return;

        bool left = activeEye == ActiveEye.Left;

        if (left && !eyeTracker.leftEyeControlledByPlayer) return;
        if (!left && !eyeTracker.rightEyeControlledByPlayer) return;

        Transform eye = left ? eyeTracker.leftEyeball : eyeTracker.rightEyeball;
        Transform target = left ? (leftEyeTarget != null ? leftEyeTarget : eyeTracker.leftEyeTarget)
                                : (rightEyeTarget != null ? rightEyeTarget : eyeTracker.rightEyeTarget);
        if (eye == null || target == null) return;

        Vector3 offset = target.position - eye.position;
        if (offset.sqrMagnitude < 0.0001f) return;

        offset = OrbitOffset(offset, xInput, yInput, independentLookSpeed);
        target.position = eye.position + offset;
    }

    // Both eyes away from the head but close together (locked): aim them as one, like the head does.
    void SteerSharedTarget(float xInput, float yInput)
    {
        if (eyeTracker == null) return;
        if (Mathf.Approximately(xInput, 0f) && Mathf.Approximately(yInput, 0f)) return;
        if (!eyeTracker.leftEyeControlledByPlayer || !eyeTracker.rightEyeControlledByPlayer) return;
        if (eyeTracker.leftEyeball == null || eyeTracker.rightEyeball == null) return;
        if (eyeTracker.leftEyeTarget == null || eyeTracker.rightEyeTarget == null) return;

        Vector3 mid = (eyeTracker.leftEyeball.position + eyeTracker.rightEyeball.position) * 0.5f;
        Vector3 center = (eyeTracker.leftEyeTarget.position + eyeTracker.rightEyeTarget.position) * 0.5f;

        Vector3 offset = center - mid;
        if (offset.sqrMagnitude < 0.0001f) return;

        offset = OrbitOffset(offset, xInput, yInput, lookSpeed);
        Vector3 newCenter = mid + offset;
        eyeTracker.leftEyeTarget.position = newCenter;
        eyeTracker.rightEyeTarget.position = newCenter;
    }

    void OnSwitchEye(InputAction.CallbackContext context)
    {
        string controlName = context.control.name;

        // If the object in front of you (e.g. the eye jar) wants this bumper, don't switch eyes
        bool leftBumper = controlName == "leftShoulder" || controlName == "leftBumper";
        bool rightBumper = controlName == "rightShoulder" || controlName == "rightBumper";
        if ((leftBumper || rightBumper) && interactor != null && interactor.ConsumesBumper(leftBumper))
            return;

        if (!EyesIndependent) return;

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
        if (crouchAction != null)
            crouchAction.performed -= OnCrouch;
    }
}