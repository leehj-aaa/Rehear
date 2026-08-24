using System.Collections;
using UnityEngine;

public class OpeningUIPlacement : MonoBehaviour
{
    [SerializeField] private Transform xrCamera;
    [SerializeField] private Transform openingUiRoot;

    [Header("Placement")]
    [SerializeField] private float distance = 3f;
    [SerializeField] private float verticalOffset = 0f;

    private IEnumerator Start()
    {
        // Quest의 초기 HMD 위치와 방향이 적용될 때까지 잠시 대기
        yield return null;
        yield return null;

        PlaceInFrontOfUser();
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

        Vector3 targetPosition =
            xrCamera.position +
            forward * distance +
            Vector3.up * verticalOffset;

        openingUiRoot.position = targetPosition;

        Vector3 directionToCamera =
            xrCamera.position - openingUiRoot.position;

        directionToCamera.y = 0f;

        if (directionToCamera.sqrMagnitude > 0.001f)
        {
            openingUiRoot.rotation =
                Quaternion.LookRotation(-directionToCamera.normalized);
        }
    }
}