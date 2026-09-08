using System;
using System.Collections.Generic;
using UnityEngine;

// Audio and viseme analysis share one source on this actor's head.
[DefaultExecutionOrder(10000)]
public sealed class AudienceQuestionSpeaker : MonoBehaviour
{
    private AudioSource voice;
    private OVRLipSyncContext lipsync;
    private bool driving;
    private readonly List<MouthShape> shapes = new List<MouthShape>();
    private sealed class MouthShape { public SkinnedMeshRenderer mesh; public int index; public string name; public float original, weight; }
    public AudioSource Voice => voice;
    private bool speechPaused;
    public bool IsSpeaking => driving && (speechPaused || (voice && voice.isPlaying));

    public void SetPaused(bool paused)
    {
        if (!driving || speechPaused == paused) return;
        speechPaused = paused;
        if (paused) voice.Pause(); else voice.UnPause();
    }

    private void Initialize()
    {
        if (voice) return;
        Transform head = transform;
        foreach (var bone in GetComponentsInChildren<Transform>(true))
            if (bone.name.Equals("head", StringComparison.OrdinalIgnoreCase)) { head = bone; break; }
        var source = new GameObject("Question Voice");
        source.SetActive(false);
        source.transform.SetParent(head, false);
        voice = source.AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.loop = false;
        voice.spatialBlend = 1; voice.minDistance = 2; voice.maxDistance = 20;
        lipsync = source.AddComponent<OVRLipSyncContext>();
        lipsync.audioSource = voice; lipsync.audioLoopback = true;
        lipsync.enableAcceleration = false;
        source.SetActive(true);
        foreach (var mesh in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!mesh.sharedMesh) continue;
            for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
            {
                string shape = mesh.sharedMesh.GetBlendShapeName(i).ToLowerInvariant();
                foreach (var name in new[]{"jawopen","mouthfunnel","mouthpucker","mouthclose","mouthstretchleft","mouthstretchright"})
                    if (shape.EndsWith(name, StringComparison.Ordinal))
                        shapes.Add(new MouthShape { mesh = mesh, index = i, name = name });
            }
        }
    }
    public bool Play(AudioClip clip)
    {
        if (!clip || !isActiveAndEnabled) return false;
        Initialize(); StopSpeech();
        if (lipsync.Context == 0 || shapes.Count == 0) return false;
        foreach (var shape in shapes) { shape.original = shape.mesh.GetBlendShapeWeight(shape.index); shape.weight = 0; }
        lipsync.ResetContext();
        driving = true; voice.clip = clip; voice.Play();
        return true;
    }
    private void LateUpdate()
    {
        if (!driving || speechPaused || AudioListener.pause) return;
        if (!IsSpeaking) { StopSpeech(); return; }
        lock (lipsync)
        {
            ApplyVisemes(lipsync.GetCurrentPhonemeFrame().Visemes, Time.unscaledDeltaTime);
        }
    }
    private void ApplyVisemes(float[] v, float delta)
    {
            foreach (var shape in shapes)
            {
                float value = 0;
                switch (shape.name)
                {
                    case "jawopen": value = Mathf.Max(v[10], v[11]*.5f, v[12]*.4f, v[13]*.6f, v[14]*.4f); break;
                    case "mouthfunnel": value = v[13]; break;
                    case "mouthpucker": value = v[14]; break;
                    case "mouthclose": value = v[1]; break;
                    default: value = Mathf.Max(v[6], v[7], v[11], v[12])*.45f; break;
                }
                shape.weight = Mathf.Lerp(shape.weight, value*100, 1-Mathf.Exp(-20*delta));
                shape.mesh.SetBlendShapeWeight(shape.index, Mathf.Clamp(shape.original+shape.weight,0,100));
            }
    }
    public void StopSpeech()
    {
        if (voice) voice.Stop();
        if (driving) foreach (var shape in shapes) if (shape.mesh) shape.mesh.SetBlendShapeWeight(shape.index,shape.original);
        driving = false;
        speechPaused = false;
    }
    private void OnDisable() => StopSpeech();
}


