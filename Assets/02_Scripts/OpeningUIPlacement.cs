using UnityEngine;

public class OpeningUIPlacement : MonoBehaviour
{
    [SerializeField] private Transform xrCamera;
    [SerializeField] private Transform openingUiRoot;

    [Header("Placement")]
    [SerializeField] private float distance = 3f;
    [SerializeField] private float verticalOffset = 0f;

    private bool placed;
    private Transform xrRig;
    private Vector3 lockedRigLocalPosition;
    private Quaternion lockedRigLocalRotation;

    private void Awake()
    {
        LockOpeningRig();
    }

    private void OnEnable()
    {
        Application.onBeforeRender += PlaceBeforeFirstRender;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= PlaceBeforeFirstRender;
    }

    private void LateUpdate()
    {
        if (xrRig == null)
            return;

        // 오프닝에는 이동이 필요 없으므로 추적 원점의 낙하를 차단한다.
        xrRig.localPosition = lockedRigLocalPosition;
        xrRig.localRotation = lockedRigLocalRotation;
    }

    private void LockOpeningRig()
    {
        if (xrCamera == null && Camera.main != null)
            xrCamera = Camera.main.transform;

        Transform candidate = xrCamera;
        while (candidate != null)
        {
            if (candidate.name.Contains("XR Origin"))
            {
                xrRig = candidate;
                break;
            }

            candidate = candidate.parent;
        }

        if (xrRig == null)
        {
            Debug.LogWarning(
                "[Opening UI] XR Origin을 찾지 못했습니다.",
                this
            );
            return;
        }

        lockedRigLocalPosition = xrRig.localPosition;
        lockedRigLocalRotation = xrRig.localRotation;

        CharacterController controller =
            xrRig.GetComponent<CharacterController>();
        if (controller != null)
            controller.enabled = false;

        foreach (MonoBehaviour behaviour in
            xrRig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName.Contains("MoveProvider") ||
                typeName.Contains("CharacterControllerDriver") ||
                typeName.Contains("GravityProvider"))
            {
                behaviour.enabled = false;
            }
        }
    }

    private void PlaceBeforeFirstRender()
    {
        if (placed)
            return;

        if (xrCamera == null || openingUiRoot == null)
            return;

        PlaceInFrontOfUser();

        placed = true;

        // 최초 배치가 끝나면 더 이상 실행하지 않음
        Application.onBeforeRender -= PlaceBeforeFirstRender;
    }

    [ContextMenu("Place In Front Of User")]
    public void PlaceInFrontOfUser()
    {
        if (xrCamera == null || openingUiRoot == null)
        {
            Debug.LogWarning(
                "[Opening UI] 카메라 또는 UI Root가 연결되지 않았습니다.",
                this
            );
            return;
        }

        Vector3 forward = xrCamera.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        forward.Normalize();

        openingUiRoot.position =
            xrCamera.position +
            forward * distance +
            Vector3.up * verticalOffset;

        // Canvas의 앞면이 사용자를 향하도록 배치
        openingUiRoot.rotation =
            Quaternion.LookRotation(forward, Vector3.up);
    }
}
