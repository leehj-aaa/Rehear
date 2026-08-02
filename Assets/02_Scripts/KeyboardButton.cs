using UnityEngine;
using UnityEngine.UI;

public class KeyboardButton : MonoBehaviour
{
    public string numberValue; // 인스펙터에서 0~9 각각 입력
    public PinInputManager pinManager; // 위에서 만든 PinInputManager를 연결

    void Start()
    {
        GetComponent<Button>().onClick.AddListener(() => {
            pinManager.AddNumber(numberValue);
        });
    }
}