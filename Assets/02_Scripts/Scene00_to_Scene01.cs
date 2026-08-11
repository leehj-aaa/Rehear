using UnityEngine;
using UnityEngine.SceneManagement; // 씬 전환을 위해 반드시 필요한 네임스페이스

public class Scene00_to_Scene01 : MonoBehaviour
{
    // [게임 시작하기] 버튼에 연결할 함수
    public void ClickGameStart()
    {
        // 지정된 이름의 백업 인트로 씬으로 이동합니다.
        SceneManager.LoadScene("Scene_00_5_Tutorial");
    }
}