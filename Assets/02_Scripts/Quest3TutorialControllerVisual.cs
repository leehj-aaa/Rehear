using UnityEngine;
using UnityEngine.XR;

/// <summary>Life-size tracked right-hand demonstration. Does not move XR inputs or rays.</summary>
public sealed class Quest3TutorialControllerVisual : MonoBehaviour
{
    public enum Cue { Hidden, Idle, Trigger, StickHorizontal, StickVertical, Grip }
    static readonly int TriggerParameter = Animator.StringToHash("Trigger");
    static readonly int GripParameter = Animator.StringToHash("Grip");
    static readonly int JoyXParameter = Animator.StringToHash("Joy X");
    static readonly int JoyYParameter = Animator.StringToHash("Joy Y");
    [SerializeField] GameObject controllerModelPrefab;
    [SerializeField] RuntimeAnimatorController controllerAnimator;
    [Header("Tracked right hand grip pose, not camera or ray aim")]
    [SerializeField] Transform rightHandTarget;
    [SerializeField] Renderer[] originalControllerRenderers;
    [SerializeField] Vector3 gripPoseOffset;
    [SerializeField] Vector3 gripPoseEuler;
    [Header("Button highlights")]
    [SerializeField] Mesh triggerHighlight;
    [SerializeField] Mesh stickHighlight;
    [SerializeField] Mesh gripHighlight;
    [SerializeField] Material highlightMaterial;
    [SerializeField, Min(.2f)] float cycleDuration = 1.35f;
    Transform visualRoot;
    Animator animator;
    SkinnedMeshRenderer bodySurface;
    Mesh runtimeSurfaceMesh;
    Material originalSurfaceMaterial;
    Material runtimeHighlight;
    bool[] previousForceOff;
    bool suppressingOriginal;
    Cue currentCue = Cue.Hidden;
    float cueStartedAt;

    void Awake() { BuildVisual(); ApplyVisibility(); }
    void OnEnable() { ApplyVisibility(); }
    void OnDisable()
    {
        if (visualRoot) visualRoot.gameObject.SetActive(false);
        RestoreOriginal();
    }
    void OnDestroy()
    {
        RestoreOriginal();
        if (visualRoot) Destroy(visualRoot.gameObject);
        if (runtimeHighlight) Destroy(runtimeHighlight);
        if (runtimeSurfaceMesh) Destroy(runtimeSurfaceMesh);
    }
    void LateUpdate()
    {
        ApplyVisibility();
        if (!visualRoot || !visualRoot.gameObject.activeSelf) return;
        AnimateCue(Mathf.Repeat((Time.unscaledTime - cueStartedAt) / Mathf.Max(.2f, cycleDuration), 1f));
    }
    public void Show(Cue cue)
    {
        currentCue = cue;
        cueStartedAt = Time.unscaledTime;
        if (bodySurface)
        {
            int selected = cue == Cue.Trigger ? 1 : cue == Cue.Grip ? 3 :
                cue == Cue.StickHorizontal || cue == Cue.StickVertical ? 2 : -1;
            var materials = new Material[4];
            for (int i = 0; i < materials.Length; i++) materials[i] = i == selected ? runtimeHighlight : originalSurfaceMaterial;
            bodySurface.sharedMaterials = materials;
        }
        ApplyVisibility();
        if (animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController)
        {
            if (!animator.isInitialized) { animator.Rebind(); animator.Update(0f); }
            ResetAnimatorParameters();
        }
    }
    void BuildVisual()
    {
        if (visualRoot) return;
        if (!controllerModelPrefab || !rightHandTarget) return;
        visualRoot = new GameObject("Right Hand Tutorial Overlay").transform;
        // Parenting propagates before-render tracking without smoothing or bobbing.
        visualRoot.SetParent(rightHandTarget, false);
        visualRoot.SetLocalPositionAndRotation(gripPoseOffset, Quaternion.Euler(gripPoseEuler));
        var model = Instantiate(controllerModelPrefab, visualRoot);
        model.name = "Meta Quest Touch Plus Right Demonstration";
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = controllerModelPrefab.transform.localScale;
        animator = model.GetComponentInChildren<Animator>(true);
        if (animator)
        {
            animator.runtimeAnimatorController = controllerAnimator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            if (animator.isActiveAndEnabled && animator.runtimeAnimatorController)
            {
                animator.Rebind();
                animator.Update(0f);
            }
        }
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (r.name.Contains("batteryIndicator")) r.gameObject.SetActive(false);
        }
        var body = model.transform.Find("oculus_controller_r_MeshX")?.GetComponent<SkinnedMeshRenderer>();
        if (body && triggerHighlight && stickHighlight && gripHighlight)
        {
            // Assign a lit emissive material to the ORIGINAL button triangles.
            // No extra shell, duplicate face, silhouette expansion or unlit sticker.
            bodySurface = body;
            originalSurfaceMaterial = body.sharedMaterial;
            var selectedTriangles = new System.Collections.Generic.HashSet<(int, int, int)>();
            var parts = new[] { triggerHighlight.triangles, stickHighlight.triangles, gripHighlight.triangles };
            foreach (var part in parts)
                for (int i = 0; i < part.Length; i += 3) selectedTriangles.Add((part[i], part[i + 1], part[i + 2]));
            var remaining = new System.Collections.Generic.List<int>();
            var source = body.sharedMesh;
            var triangles = source.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                if (!selectedTriangles.Contains((triangles[i], triangles[i + 1], triangles[i + 2])))
                { remaining.Add(triangles[i]); remaining.Add(triangles[i + 1]); remaining.Add(triangles[i + 2]); }
            runtimeSurfaceMesh = Instantiate(source);
            runtimeSurfaceMesh.name = "Controller Native Button Surfaces";
            runtimeSurfaceMesh.subMeshCount = 4;
            runtimeSurfaceMesh.SetTriangles(remaining, 0);
            for (int i = 0; i < parts.Length; i++) runtimeSurfaceMesh.SetTriangles(parts[i], i + 1);
            body.sharedMesh = runtimeSurfaceMesh;
            body.updateWhenOffscreen = true;
            runtimeHighlight = new Material(highlightMaterial) { name = "Controller Blue Emissive Button" };
            if (originalSurfaceMaterial.HasProperty("_BaseMap"))
            {
                runtimeHighlight.SetTexture("_BaseMap", originalSurfaceMaterial.GetTexture("_BaseMap"));
                runtimeHighlight.SetTextureScale("_BaseMap", originalSurfaceMaterial.GetTextureScale("_BaseMap"));
                runtimeHighlight.SetTextureOffset("_BaseMap", originalSurfaceMaterial.GetTextureOffset("_BaseMap"));
            }
            runtimeHighlight.SetFloat("_Smoothness", .32f);
            runtimeHighlight.SetColor("_BaseColor", new Color(.025f, .10f, .32f));
            runtimeHighlight.SetColor("_EmissionColor", new Color(.005f, .20f, 1f) * 1.2f);
            body.sharedMaterials = new[] { originalSurfaceMaterial, originalSurfaceMaterial, originalSurfaceMaterial, originalSurfaceMaterial };
        }
    }
    void AnimateCue(float phase)
    {
        if (!animator || !animator.isActiveAndEnabled || !animator.isInitialized) return;
        float action = TutorialPulse(phase);
        switch (currentCue)
        {
            case Cue.Trigger: animator.SetFloat(TriggerParameter, action); break;
            case Cue.Grip: animator.SetFloat(GripParameter, action); break;
            case Cue.StickHorizontal: animator.SetFloat(JoyXParameter, Mathf.Sin(phase * Mathf.PI * 2f)); break;
            case Cue.StickVertical: animator.SetFloat(JoyYParameter, Mathf.Sin(phase * Mathf.PI * 2f)); break;
        }
        if (runtimeHighlight)
        {
            runtimeHighlight.SetColor("_BaseColor", Color.Lerp(new Color(.025f, .10f, .32f), new Color(.04f, .18f, .48f), action));
            runtimeHighlight.SetColor("_EmissionColor", new Color(.005f, .20f, 1f) * Mathf.Lerp(1.2f, 3.2f, action));
        }
    }
    static float TutorialPulse(float phase)
    {
        if (phase < .15f || phase > .85f) return 0;
        return Mathf.SmoothStep(0, 1, phase <= .5f ? Mathf.InverseLerp(.15f, .5f, phase) : Mathf.InverseLerp(.85f, .5f, phase));
    }
    void ResetAnimatorParameters()
    {
        if (!animator || !animator.isActiveAndEnabled || !animator.isInitialized) return;
        animator.SetFloat(TriggerParameter, 0);
        animator.SetFloat(GripParameter, 0);
        animator.SetFloat(JoyXParameter, 0);
        animator.SetFloat(JoyYParameter, 0);
    }
    void ApplyVisibility()
    {
        bool tracked = true;
        if (XRSettings.enabled)
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            tracked = device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool value) && value;
        }
        bool show = isActiveAndEnabled && currentCue != Cue.Hidden && rightHandTarget && rightHandTarget.gameObject.activeInHierarchy && tracked;
        if (visualRoot) visualRoot.gameObject.SetActive(show);
        if (show && visualRoot && !suppressingOriginal)
        {
            previousForceOff = new bool[originalControllerRenderers?.Length ?? 0];
            for (int i = 0; i < previousForceOff.Length; i++)
            {
                var r = originalControllerRenderers[i];
                if (!r) continue;
                previousForceOff[i] = r.forceRenderingOff;
                r.forceRenderingOff = true;
            }
            suppressingOriginal = true;
        }
        else if (!show) RestoreOriginal();
    }
    void RestoreOriginal()
    {
        if (!suppressingOriginal) return;
        for (int i = 0; i < previousForceOff.Length; i++)
            if (originalControllerRenderers[i]) originalControllerRenderers[i].forceRenderingOff = previousForceOff[i];
        suppressingOriginal = false;
    }

#if UNITY_EDITOR
    public void PreviewInScene(Cue cue)
    {
        if (Application.isPlaying) return;
        BuildVisual();
        if (!visualRoot) return;
        foreach (var t in visualRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
        Show(cue);
    }
    public void UpdateScenePreview(float phase, float delta)
    {
        if (Application.isPlaying || !visualRoot) return;
        ApplyVisibility();
        AnimateCue(phase);
        if (animator && animator.isActiveAndEnabled) animator.Update(delta);
    }
    public void EndScenePreview()
    {
        if (Application.isPlaying) return;
        RestoreOriginal();
        if (visualRoot) DestroyImmediate(visualRoot.gameObject);
        if (runtimeHighlight) DestroyImmediate(runtimeHighlight);
        if (runtimeSurfaceMesh) DestroyImmediate(runtimeSurfaceMesh);
        visualRoot = null;
        runtimeHighlight = null;
        runtimeSurfaceMesh = null;
        bodySurface = null;
        animator = null;
        currentCue = Cue.Hidden;
    }
#endif
}
