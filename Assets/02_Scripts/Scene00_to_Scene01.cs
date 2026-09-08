using UnityEngine;
using Rehear.Evc.Presentation;
using UnityEngine.UI;

public class Scene00_to_Scene01 : MonoBehaviour
{
    [SerializeField] private GameObject openingPinRoot;
    [SerializeField] private GameObject openingLogo;
    [SerializeField] private Button startButton;
    private bool opened;
    // [게임 시작하기] 버튼에 연결할 함수
    public void ClickGameStart()
    {
        if (opened || (startButton && !startButton.IsInteractable())) return;
        if (!openingPinRoot)
        {
            Debug.LogError("Opening PIN screen is not connected.", this);
            return;
        }
        opened = true;
        RuntimeSessionData.Clear();
        PresentationSessionContext.Current.ClearAll();
        var camera = Camera.main;
        if (camera)
        {
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            openingPinRoot.transform.SetPositionAndRotation(camera.transform.position + forward * 3f,
                Quaternion.LookRotation(forward, Vector3.up));
        }
        if (openingLogo) openingLogo.SetActive(false);
        if (startButton) startButton.gameObject.SetActive(false);
        openingPinRoot.SetActive(true);
    }
}
