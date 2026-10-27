using UnityEngine;
using UnityEngine.Events;

// Put this on anything the player can interact with (door, switch, pickup...).
// The object also needs a Collider (a trigger collider is fine).
public class Interactable : MonoBehaviour
{
    [Header("Info")]
    [Tooltip("Text shown to the player while looking at this object.")]
    public string prompt = "Interact";

    [Header("Rules")]
    public bool singleUse = false;
    [Tooltip("Seconds before it can be used again.")]
    public float cooldown = 0.25f;

    [Header("Events")]
    public UnityEvent onInteract;
    public UnityEvent onFocus;
    public UnityEvent onUnfocus;

    bool used;
    float nextUseTime;

    public bool CanInteract => enabled && !(singleUse && used) && Time.time >= nextUseTime;

    public virtual void Interact(PlayerInteractor interactor)
    {
        if (!CanInteract) return;

        used = true;
        nextUseTime = Time.time + cooldown;
        onInteract?.Invoke();
    }

    public virtual void OnFocus() => onFocus?.Invoke();
    public virtual void OnUnfocus() => onUnfocus?.Invoke();
}