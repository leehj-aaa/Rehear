using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeTai.Asset.TranslucentImage
{
// Keep scene-owned targets alive until submitted GPU work has finished.
internal sealed class BlurTextureRetirement : MonoBehaviour
{
    static BlurTextureRetirement instance;
    int pending;

    internal static void Retire(RenderTexture texture)
    {
        if (!instance)
        {
            var owner = new GameObject("Blur texture retirement");
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<BlurTextureRetirement>();
        }
        instance.StartCoroutine(instance.ReleaseAfterRendering(texture));
    }

    IEnumerator ReleaseAfterRendering(RenderTexture texture)
    {
        pending++;
        // OnDestroy can run in scene activation before the current frame is submitted.
        yield return new WaitForEndOfFrame();
        if (SystemInfo.supportsGraphicsFence)
        {
            var command = new CommandBuffer { name = "Retire blur texture" };
            // We poll from the CPU; Quest does not support async-compute queue fences.
            var fence = command.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,
                SynchronisationStageFlags.AllGPUOperations);
            Graphics.ExecuteCommandBuffer(command);
            command.Release();
            while (!fence.passed) yield return null;
        }
        if (texture) Destroy(texture);
        pending--;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        Debug.Log($"[QuestBlur] Retired texture after rendering; pending={pending}");
#endif
    }
}
}
