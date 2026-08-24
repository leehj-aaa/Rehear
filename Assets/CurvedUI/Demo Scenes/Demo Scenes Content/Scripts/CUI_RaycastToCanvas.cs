using UnityEngine;

namespace CurvedUI
{
    public class CUI_RaycastToCanvas : MonoBehaviour
    {
        private CurvedUISettings _settings;

        // Use this for initialization
        private void Start()
        {
            _settings = GetComponentInParent<CurvedUISettings>();
        }

        // Update is called once per frame
        private void Update()
        {
            _settings.RaycastToCanvasSpace(Camera.main.ScreenPointToRay(Input.mousePosition), out var pos);
            transform.localPosition = pos;
        }
    }
}
