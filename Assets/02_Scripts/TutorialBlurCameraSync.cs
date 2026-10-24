using LeTai.Asset.TranslucentImage;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>Keeps the UI-only camera aligned without a second XR pose driver.</summary>
[ExecuteAlways]
public sealed class TutorialBlurCameraSync : MonoBehaviour
{
    public Camera sceneCamera;
    public Camera uiCamera;
    public TranslucentImageSource blurSource;
    public TranslucentImage[] panels;
    public void RegisterPanels(GameObject root)
    {
        var registered = new List<TranslucentImage>();
        if (panels != null) foreach (var panel in panels)
            if (panel) registered.Add(panel);
        foreach (var panel in root.GetComponentsInChildren<TranslucentImage>(true))
        {
            panel.source = blurSource;
            if (!registered.Contains(panel)) registered.Add(panel);
        }
        panels = registered.ToArray();
        Update();
    }

    private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCamera;
    private void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeCamera;

    private void Update()
    {
        bool visible = false;
        if (panels != null) foreach (var panel in panels)
        {
            if (!panel || !panel.IsActive() || !panel.canvas || !panel.canvas.enabled) continue;
            visible = true;
            // Our rounded shader uses a material property instead of the package's packed vertex tint.
            // Keep the Translucent Image Foreground Opacity control effective after Inspector/runtime edits.
            var material = panel.materialForRendering;
            if (material && material.HasProperty("_GlassTint") &&
                !Mathf.Approximately(material.GetFloat("_GlassTint"), panel.foregroundOpacity))
                material.SetFloat("_GlassTint", panel.foregroundOpacity);
        }
        if (blurSource) blurSource.MaxUpdateRate = visible ? float.PositiveInfinity : 0f;
    }

    private void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != sceneCamera || !uiCamera) return;
        // The overlay must never draw the room or audience a second time.
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
        {
            uiCamera.cullingMask = 1 << uiLayer;
            sceneCamera.cullingMask &= ~(1 << uiLayer);
        }
        uiCamera.transform.SetPositionAndRotation(sceneCamera.transform.position, sceneCamera.transform.rotation);
        uiCamera.nearClipPlane = sceneCamera.nearClipPlane;
        uiCamera.farClipPlane = sceneCamera.farClipPlane;
        uiCamera.fieldOfView = sceneCamera.fieldOfView;
        uiCamera.orthographic = sceneCamera.orthographic;
        uiCamera.orthographicSize = sceneCamera.orthographicSize;
        uiCamera.rect = sceneCamera.rect;
        // XR supplies each eye's projection to both cameras in the URP stack.
        if (!sceneCamera.stereoEnabled) uiCamera.projectionMatrix = sceneCamera.projectionMatrix;
        else uiCamera.ResetProjectionMatrix();
        Update();
    }
}
