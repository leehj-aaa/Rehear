using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem; // XR Input System 사용 시

public class ScriptScroller : MonoBehaviour
{
    public ScrollRect scrollRect;
    public float scrollSpeed = 0.1f;
    
    // XR 컨트롤러 입력 액션
    public InputActionProperty verticalScrollAction;

    

    void Update()
    {
        float yAxis = Input.GetAxis("Vertical"); // 또는 조이스틱 입력 이름
        if (yAxis > 0.5f) { 
    // PPT 넘기는 함수 호출
            }
        // 조이스틱의 Y축 입력값 가져오기
        float scrollInput = verticalScrollAction.action.ReadValue<Vector2>().y;

        if (Mathf.Abs(scrollInput) > 0.1f) // 조이스틱 움직임이 있을 때만
        {
            // 스크롤 위치 조절 (0~1 사이)
            scrollRect.verticalNormalizedPosition += scrollInput * scrollSpeed * Time.deltaTime;
            
            // 0과 1 사이로 값 제한
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollRect.verticalNormalizedPosition);
        }
    }
}