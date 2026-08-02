using UnityEngine;
using UnityEngine.SceneManagement;

public class Scene03Manager : MonoBehaviour
{
    // 버튼 OnClick에 연결할 함수들
    
    // 씬 2(발표 화면)로 돌아가기
    public void GoToScene2()
    {
        SceneManager.LoadScene("Scene_02_Presentation"); // 실제 씬 이름으로 변경하세요
    }

    // 씬 1(PIN 입력 화면)로 돌아가기
    public void GoToScene1()
    {
        SceneManager.LoadScene("Scene_01_Intro"); // 실제 씬 이름으로 변경하세요
    }
}