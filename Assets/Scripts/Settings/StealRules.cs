using UnityEngine;

// The switches for eye stealing. Put this on ONE object in the scene (for example next to NetworkManagerPUN).
// Even without it in the scene the defaults below apply: jars on, players off.
//
// Later, a game mode can change the rules from code, for example:
//     StealRules.FromPlayers = true;
// (do that on every player's machine, for example from an RPC or a room property)
public class StealRules : MonoBehaviour
{
    // read by PlayerHands, EyeJarInteract and EyeStealInteract
    public static bool FromJars = true;
    public static bool FromPlayers = false;

    [Tooltip("Take an eye another player put in a jar.")]
    [SerializeField] bool allowStealFromJars = true;
    [Tooltip("Take an eye straight off another player's head. Needs EyeStealInteract on the player prefab's head.")]
    [SerializeField] bool allowStealFromPlayers = false;

    void Awake() => Apply();
    void OnValidate() => Apply(); // lets you tick the boxes while the game runs

    void Apply()
    {
        FromJars = allowStealFromJars;
        FromPlayers = allowStealFromPlayers;
    }
}