using UnityEngine;
using CurvedUI.Core;

namespace CurvedUI
{
    public class CUI_CameraController : MonoBehaviour
    {
        //settings
#pragma warning disable 0649
        [SerializeField] private Transform CameraObject;
		[SerializeField] private float rotationMargin = 25;
        [SerializeField] private bool runInEditorOnly = true;
#pragma warning restore 0649
        
        private void Update()
        {
            #if UNITY_EDITOR
            if((Application.isEditor || !runInEditorOnly) && !UnityEngine.XR.XRSettings.enabled)
            {
                var mouse = CurvedUIInputModule.MousePosition;
                CameraObject.localEulerAngles 
                    = new Vector3(mouse.y.Remap(0, Screen.height, rotationMargin, -rotationMargin),
                    mouse.x.Remap(0, Screen.width, -rotationMargin, rotationMargin), 0);
            }
            #endif
        }
    }
}
