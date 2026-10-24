#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace LeTai.Asset.TranslucentImage.UniversalRP
{
// Preview the same frosted glass against the editor camera, without headset textures.
internal sealed class RehearSceneViewBlurPass : ScriptableRenderPass
{
    static readonly int A=Shader.PropertyToID("_RehearGlassBlurA");
    static readonly int B=Shader.PropertyToID("_RehearGlassBlurB");
    Material material;
    public RehearSceneViewBlurPass(){renderPassEvent=RenderPassEvent.BeforeRenderingTransparents;ConfigureInput(ScriptableRenderPassInput.Color);}
    public override void Execute(ScriptableRenderContext context,ref RenderingData data)
    {
        if(!material)material=CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Rehear/Scene View Glass Blur"));
        var descriptor=data.cameraData.cameraTargetDescriptor;
        descriptor.width=Mathf.Max(1,descriptor.width/2);descriptor.height=Mathf.Max(1,descriptor.height/2);
        descriptor.depthBufferBits=0;descriptor.msaaSamples=1;
        var cmd=CommandBufferPool.Get("ReHear scene glass blur");
        cmd.GetTemporaryRT(A,descriptor,FilterMode.Bilinear);cmd.GetTemporaryRT(B,descriptor,FilterMode.Bilinear);
        cmd.Blit(data.cameraData.renderer.cameraColorTargetHandle.nameID,A);
        for(int i=0;i<3;i++)
        {
            cmd.SetGlobalVector("_RehearBlurDirection",new Vector4(2.5f,0,0,0));cmd.Blit(A,B,material);
            cmd.SetGlobalVector("_RehearBlurDirection",new Vector4(0,2.5f,0,0));cmd.Blit(B,A,material);
        }
        cmd.SetGlobalTexture("_RehearSceneViewBlurTex",A);
        context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
    }
    public override void OnCameraCleanup(CommandBuffer cmd){cmd.ReleaseTemporaryRT(A);cmd.ReleaseTemporaryRT(B);}
    public void Dispose(){CoreUtils.Destroy(material);}
}
}
#endif
