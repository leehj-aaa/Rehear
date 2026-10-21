using UnityEngine;
using UnityEngine.EventSystems;
using CurvedUI.Core;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

// Use the same origin as the visible near/far ray, not its movable grab anchor.
[RequireComponent(typeof(NearFarInteractor))]
public sealed class RehearUIReticle : MonoBehaviour
{
    [SerializeField] GameObject reticlePrefab;
    NearFarInteractor interactor;
    GameObject reticle;
    readonly RaycastHit[] hits = new RaycastHit[32];
    Vector3 velocity;
    CurveVisualController curveVisual;
    LineRenderer originalLine;
    LineRenderer uiLine;
    bool usingUIRay;
    Material pointerMaterial;

    void ConfigureUIRenderer(Renderer renderer)
    {
        if (!pointerMaterial)
        {
            var shader = Resources.Load<Shader>("RehearUIPointerOverlay");
            if (shader) pointerMaterial = new Material(shader);
        }
        renderer.gameObject.layer = LayerMask.NameToLayer("UI");
        renderer.sortingOrder = 32700;
        if (pointerMaterial) renderer.sharedMaterial = pointerMaterial;
    }

    void Awake()
    {
        interactor = GetComponent<NearFarInteractor>();
        curveVisual = GetComponentInChildren<CurveVisualController>(true);
        if (curveVisual) originalLine = curveVisual.GetComponent<LineRenderer>();
    }

    void LateUpdate()
    {
        if (!reticlePrefab) return;
        if (!reticle)
        {
            reticle = Instantiate(reticlePrefab);
            foreach (var renderer in reticle.GetComponentsInChildren<Renderer>(true))
                ConfigureUIRenderer(renderer);
            reticle.SetActive(false);
        }
        var origin = interactor.curveOrigin;
        if (!origin || (interactor.disableVisualsWhenBlockedInGroup && interactor.IsBlockedByInteractionWithinGroup()))
        {
            reticle.SetActive(false);
            if (uiLine) uiLine.enabled = false;
            return;
        }
        Vector3 point, normal;
        bool hit = false;
        point = normal = Vector3.zero;
        if (EventSystem.current && EventSystem.current.currentInputModule is CurvedUIInputModule curved)
        {
            UseUIRay(true);
            // CurvedUI owns hover. Use its active hand, ray and canvas consistently.
            var pointer = curved.PointerTransform;
            var controller = GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRInteractionGroup>();
            bool ownsPointer = pointer && (controller ? pointer.IsChildOf(controller.transform) :
                Vector3.Distance(pointer.position, origin.position) < 0.1f);
            var target = curved.CurrentRaycast;
            var ray = pointer ? curved.GetEventRay() : new Ray(origin.position, origin.forward);
            if (ownsPointer && target.isValid && target.module)
            {
                int count = Physics.RaycastNonAlloc(ray, hits, 20f, curved.RaycastLayerMask, QueryTriggerInteraction.Ignore);
                float closest = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (!hits[i].transform.IsChildOf(target.module.transform) || hits[i].distance >= closest) continue;
                    closest = hits[i].distance;
                    point = hits[i].point;
                    normal = hits[i].normal;
                    hit = true;
                }
            }
            if (uiLine)
            {
                uiLine.enabled = ownsPointer;
                uiLine.SetPosition(0, ray.origin);
                uiLine.SetPosition(1, hit ? point : ray.GetPoint(20f));
            }
        }
        else
        {
            UseUIRay(false);
            hit = interactor.TryGetCurveEndPoint(out point) == EndPointType.UI;
            interactor.TryGetCurveEndNormal(out normal);
        }
        bool wasVisible = reticle.activeSelf;
        reticle.SetActive(hit);
        if (!hit) { velocity = Vector3.zero; return; }
        if (normal.sqrMagnitude < 0.001f) normal = -origin.forward;
        normal.Normalize();
        if (Vector3.Dot(normal, origin.forward) > 0f) normal = -normal;
        // Lift the ring slightly off the panel to prevent depth flicker.
        var position = point + normal * 0.005f;
        if (wasVisible) position = Vector3.SmoothDamp(reticle.transform.position, position, ref velocity, 0.025f,
            Mathf.Infinity, Time.unscaledDeltaTime);
        reticle.transform.SetPositionAndRotation(position,
            Quaternion.FromToRotation(Vector3.up, normal));
        if (usingUIRay && uiLine && uiLine.enabled) uiLine.SetPosition(1, position);
    }

    void UseUIRay(bool value)
    {
        if (value && !uiLine && originalLine)
        {
            uiLine = new GameObject("UI Pointer Ray").AddComponent<LineRenderer>();
            uiLine.sharedMaterial = originalLine.sharedMaterial;
            ConfigureUIRenderer(uiLine);
            uiLine.useWorldSpace = true;
            uiLine.positionCount = 2;
            uiLine.startWidth = uiLine.endWidth = 0.003f;
            uiLine.startColor = uiLine.endColor = new Color(0.65f, 0.85f, 1f, 0.9f);
            uiLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            uiLine.receiveShadows = false;
        }
        if (usingUIRay == value) return;
        usingUIRay = value;
        if (curveVisual) curveVisual.enabled = !value;
        if (originalLine) originalLine.enabled = !value;
        if (!value && uiLine) uiLine.enabled = false;
    }

    void OnDisable() { if (reticle) reticle.SetActive(false); if (uiLine) uiLine.enabled = false; }
    void OnDestroy() { if (reticle) Destroy(reticle); if (uiLine) Destroy(uiLine.gameObject); if (pointerMaterial) Destroy(pointerMaterial); }
}
