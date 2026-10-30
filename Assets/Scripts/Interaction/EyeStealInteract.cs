using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

// Put this on the player prefab's "Character Head" (the object whose children hold the body / eye colliders).
// Do NOT add it to NetworkPlayer's "Local Only Components": it must stay enabled so other players can use it.
//
// Look at ANOTHER player:
//   Left bumper  / LMB = take their left eye
//   Right bumper / RMB = take their right eye
// The stolen eye goes into one of your free hands. Drop (Q / D-pad down) gives it back.
public class EyeStealInteract : Interactable
{
    [Header("Menu")]
    public string leftControllerLabel = "LB";
    public string rightControllerLabel = "RB";
    public string leftKeyLabel = "LMB";
    public string rightKeyLabel = "RMB";
    public int fontSize = 20;

    NetworkPlayer owner;   // the player this body belongs to (the victim)
    PlayerHands thief;     // whoever is looking at it right now

    void Awake()
    {
        owner = GetComponentInParent<NetworkPlayer>();
        prompt = "Take eye";
    }

    // never targetable on your own body
    public override bool CanInteract => enabled && owner != null && !owner.photonView.IsMine;

    // E does nothing here, the bumpers do the work
    public override void Interact(PlayerInteractor interactor) { }

    bool Setup(PlayerInteractor interactor)
    {
        thief = interactor != null ? interactor.GetComponentInParent<PlayerHands>() : null;
        return thief != null && owner != null && owner.hands != null && thief != owner.hands;
    }

    // Keeps the press away from "switch eye" while you look at a player whose eye is (or just was) available
    public override bool UsesBumper(PlayerInteractor interactor, bool leftBumper)
    {
        if (!Setup(interactor)) return false;
        return owner.hands.OwnEyeInHand(leftBumper) || owner.IsEyeStolen(leftBumper);
    }

    public override void OnBumper(PlayerInteractor interactor, bool leftBumper)
    {
        if (!Setup(interactor)) return;
        if (!owner.hands.OwnEyeInHand(leftBumper)) return;

        if (thief.TryStealEye(owner.hands, leftBumper))
            onInteract?.Invoke();
    }

    public override bool DrawPromptGUI(PlayerInteractor interactor)
    {
        if (!Setup(interactor)) return true;

        bool keys = interactor == null || interactor.UsingKeyboardMouse;
        var rows = new List<string>();
        if (owner.hands.OwnEyeInHand(true))
            rows.Add((keys ? leftKeyLabel : leftControllerLabel) + "    Take left eye");
        if (owner.hands.OwnEyeInHand(false))
            rows.Add((keys ? rightKeyLabel : rightControllerLabel) + "    Take right eye");

        if (rows.Count == 0) return true; // nothing to offer, skip the default prompt

        var style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = fontSize,
            padding = new RectOffset(14, 14, 8, 8)
        };

        float w = 380f;
        float h = rows.Count * (fontSize + 12f) + 16f;
        GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.68f, w, h), string.Join("\n", rows), style);
        return true;
    }
}