using UnityEngine;

public class OpeningUIPlacement : MonoBehaviour
{
    [SerializeField] private Transform xrCamera;
    [SerializeField] private Transform openingUiRoot;

    [Header("Placement")]
    [SerializeField] private float distance = 3f;
    [SerializeField] private float verticalOffset = 0f;

    private bool placed;

    private void OnEnable()
    {
        Application.onBeforeRender += PlaceBeforeFirstRender;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= PlaceBeforeFirstRender;
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