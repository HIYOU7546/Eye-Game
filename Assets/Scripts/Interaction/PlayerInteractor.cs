using UnityEngine;
using UnityEngine.InputSystem;

// Put this on Character Head (or any object that points where the player is looking).
// Looks for Interactable objects in a sphere ahead of the head and lets the player use the closest one.
public class PlayerInteractor : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Optional. If empty, the project-wide actions asset is used.")]
    public InputActionAsset inputActions;
    [Tooltip("Name of the button action in your Input Actions asset.")]
    public string interactActionName = "Interact";
    [Tooltip("Name of the bumper action (needed so objects like the eye jar can use the bumpers).")]
    public string switchEyeActionName = "Switch Eye";

    [Header("Detection")]
    [Tooltip("Where the check starts and which way is 'ahead'. Leave empty to use this object.")]
    public Transform origin;
    [Tooltip("How far ahead of the head the check sphere is centered.")]
    public float forwardOffset = 1.2f;
    [Tooltip("Radius of the check sphere. Bigger = easier to target.")]
    public float radius = 1.0f;
    [Tooltip("How closely the object must be in front of you. 1 = dead center, 0 = anywhere in front, -1 = anywhere.")]
    [Range(-1f, 1f)] public float minFacingDot = 0.4f;
    public LayerMask interactMask = ~0;
    [Tooltip("Block interaction if a wall is between you and the object.")]
    public bool requireLineOfSight = true;

    [Header("Debug")]
    public bool showPromptOnScreen = true;
    [Tooltip("Shows a small line in the top-left corner saying what you are currently targeting.")]
    public bool debugStatus = true;
    public bool drawGizmo = true;

    public Interactable Current { get; private set; }

    InputAction interactAction;
    InputAction switchEyeAction;
    readonly Collider[] hits = new Collider[32];

    Transform Origin => origin != null ? origin : transform;

    void Awake()
    {
        var actions = inputActions != null ? inputActions : InputSystem.actions;
        if (actions == null)
        {
            Debug.LogError("PlayerInteractor: no Input Actions asset found.");
            return;
        }

        switchEyeAction = actions.FindAction(switchEyeActionName);
        interactAction = actions.FindAction(interactActionName);
        if (interactAction == null)
            Debug.LogWarning($"PlayerInteractor: action '{interactActionName}' not found.");
    }

    void OnEnable()
    {
        if (interactAction != null)
        {
            interactAction.performed += OnInteract;
            interactAction.Enable();
        }

        // PlayerController enables/disables this action; we only listen to it
        if (switchEyeAction != null)
            switchEyeAction.performed += OnBumperPressed;
    }

    void OnDisable()
    {
        if (interactAction != null)
        {
            interactAction.performed -= OnInteract;
            interactAction.Disable();
        }

        if (switchEyeAction != null)
            switchEyeAction.performed -= OnBumperPressed;

        SetCurrent(null);
    }

    void Update()
    {
        SetCurrent(FindBest());
    }

    // True when the focused object wants this bumper press (so it shouldn't switch eyes)
    public bool ConsumesBumper(bool leftBumper)
    {
        return Current != null && Current.UsesBumper(this, leftBumper);
    }

    void OnBumperPressed(InputAction.CallbackContext ctx)
    {
        string n = ctx.control.name;
        bool isLeft = n == "leftShoulder" || n == "leftBumper";
        bool isRight = n == "rightShoulder" || n == "rightBumper";
        if (!isLeft && !isRight) return;

        if (Current != null && Current.UsesBumper(this, isLeft))
            Current.OnBumper(this, isLeft);
    }

    void OnInteract(InputAction.CallbackContext ctx)
    {
        if (Current != null && Current.CanInteract)
            Current.Interact(this);
    }

    Interactable FindBest()
    {
        Transform o = Origin;
        Vector3 center = o.position + o.forward * forwardOffset;
        int count = Physics.OverlapSphereNonAlloc(center, radius, hits, interactMask, QueryTriggerInteraction.Collide);

        Interactable best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var it = hits[i].GetComponentInParent<Interactable>();
            if (it == null || !it.CanInteract) continue;

            Vector3 point = hits[i].ClosestPoint(o.position);
            Vector3 toObj = point - o.position;
            float dist = toObj.magnitude;

            // must be in front of the head
            if (dist > 0.01f && Vector3.Dot(o.forward, toObj / dist) < minFacingDot) continue;

            // must not be behind a wall
            if (requireLineOfSight && dist > 0.01f &&
                Physics.Linecast(o.position, point, out RaycastHit block, interactMask, QueryTriggerInteraction.Ignore))
            {
                if (block.collider.GetComponentInParent<Interactable>() != it) continue;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                best = it;
            }
        }

        return best;
    }

    void SetCurrent(Interactable next)
    {
        if (next == Current) return;

        if (Current != null) Current.OnUnfocus();
        Current = next;
        if (Current != null) Current.OnFocus();
    }

    void OnGUI()
    {
        if (debugStatus)
            GUI.Label(new Rect(10, 10, 600, 30), Current != null ? "Interact target: " + Current.name : "Interact target: none");

        if (!showPromptOnScreen || Current == null) return;

        // objects can draw their own menu
        if (Current.DrawPromptGUI(this)) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22
        };
        GUI.Label(new Rect(0, Screen.height * 0.7f, Screen.width, 40), Current.prompt, style);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmo) return;
        Transform o = Origin;
        Gizmos.color = Current != null ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(o.position + o.forward * forwardOffset, radius);
    }
}