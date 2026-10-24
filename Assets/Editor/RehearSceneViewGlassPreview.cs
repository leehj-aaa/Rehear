using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
[InitializeOnLoad]
internal static class RehearSceneViewGlassPreview
{
    static RehearSceneViewGlassPreview()
    {
        RenderPipelineManager.beginCameraRendering += (context,camera) => Shader.SetGlobalFloat("_RehearSceneViewBlurEnabled",camera.cameraType==CameraType.SceneView?1:0);
        AssemblyReloadEvents.beforeAssemblyReload += () => Shader.SetGlobalFloat("_RehearSceneViewBlurEnabled",0);
    }
}
