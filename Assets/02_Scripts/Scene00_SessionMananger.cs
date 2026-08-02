using UnityEngine;
using UnityEngine.SceneManagement; // 씬 전환을 위해 필수

public class Scene00_SessionMananger : MonoBehaviour
{
    // "세션 시작하기" 버튼에 연결할 함수
    public void StartSession()
    {
        // "Scene2" 이름의 씬으로 이동 (빌드 설정에 Scene2가 등록되어 있어야 함)
        SceneManager.LoadScene("Scene2");
    }

    // "다시 입력하기" 버튼에 연결할 함수
    public void GoBackToStart()
    {
        // "Scene1" 이름의 씬으로 이동 (또는 현재 씬을 다시 로드)
        SceneManager.LoadScene("Scene1");
    }
}