using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Vision : MonoBehaviour
{
    InputAction moveAction;
    InputAction lookAction;
    InputAction switchEyeAction;

    [Header("Players camera Targets")]
    public Transform leftEyeTarget;
    public Transform rightEyeTarget;

    [Header("Control Variables")]
    [SerializeField] float lookSpeed = 100f;
    float lookInputX;

    void Awake()
    {
        moveAction = InputSystem.actions.FindAction("Move");
        lookAction = InputSystem.actions.FindAction("Look");
        switchEyeAction = InputSystem.actions.FindAction("SwitchEye");

        if (switchEyeAction != null)
        {
            switchEyeAction.performed += OnSwitchEye;
        }
    }

    // enable and disable the input actions when the script is enabled or disabled 
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
        // 1. Look Logic (Right Stick Left/Right)
        if (lookAction != null)
        {
            Vector2 lookVector = lookAction.ReadValue<Vector2>();
            lookInputX = lookVector.x; // Only care about the horizontal axis
        }

        // Rotate the object around its Y-axis based on horizontal right stick input
        float rotation = lookInputX * lookSpeed * Time.deltaTime;
        transform.Rotate(0, rotation, 0);
    }

    void OnSwitchEye(InputAction.CallbackContext context)
    {
        // 2. Switch Target Logic (Bumpers)
        // Check the control path string to see exactly which bumper was pressed
        string controlName = context.control.name;

        if (controlName == "leftShoulder" || controlName == "leftBumper")
        {
            Debug.Log("Left Bumper Pressed - Switching to PREVIOUS target");
            // Insert your custom logic to cycle control backward here
        }
        else if (controlName == "rightShoulder" || controlName == "rightBumper")
        {
            Debug.Log("Right Bumper Pressed - Switching to NEXT target");
            // Insert your custom logic to cycle control forward here
        }
    }

    private void OnDestroy()
    {
        if (switchEyeAction != null)
        {
            switchEyeAction.performed -= OnSwitchEye;
        }
    }
}
