using UnityEngine;
using UnityEngine.UI;

public class DynamicSplitScreen : MonoBehaviour
{
    [Header("Cameras")]
    public Camera camera1;
    public Camera camera2;

    [Header("UI Output")]
    public RawImage displayImage; // Drag your UI Canvas Raw Image here

    private RenderTexture rt1;
    private RenderTexture rt2;
    private int lastWidth;
    private int lastHeight;

    void Start()
    {
        CreateTextures();
    }

    void Update()
    {
        // Check if the window resized or resolution changed
        if (Screen.width != lastWidth || Screen.height != lastHeight)
        {
            CreateTextures();
        }
    }

    void CreateTextures()
    {
        // Record current screen metrics
        lastWidth = Screen.width;
        lastHeight = Screen.height;

        // Clean up old textures if they exist to prevent memory leaks
        if (rt1 != null) { rt1.Release(); Destroy(rt1); }
        if (rt2 != null) { rt2.Release(); Destroy(rt2); }

        // Create new textures exactly matching the user's current resolution
        // 24 represents a standard 24-bit depth buffer
        rt1 = new RenderTexture(lastWidth, lastHeight, 24);
        rt2 = new RenderTexture(lastWidth, lastHeight, 24);

        // Assign the fresh textures to your cameras
        camera1.targetTexture = rt1;
        camera2.targetTexture = rt2;

        // Pass the new runtime textures into your UI Material parameters
        if (displayImage != null && displayImage.material != null)
        {
            displayImage.material.SetTexture("_LeftTex", rt1);
            displayImage.material.SetTexture("_RightTex", rt2);
        }
    }

    void OnDestroy()
    {
        // Always clean up native textures when switching scenes or quitting
        if (rt1 != null) rt1.Release();
        if (rt2 != null) rt2.Release();
    }
}
