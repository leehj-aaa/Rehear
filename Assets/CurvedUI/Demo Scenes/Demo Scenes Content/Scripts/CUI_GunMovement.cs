using CurvedUI.Core;
using UnityEngine;

namespace CurvedUI
{
    /// <summary>
    /// A simple script to make the pointer follow mouse movement and pass the control ray to canvsa
    /// </summary>
    public class CUI_GunMovement : MonoBehaviour
    {
#pragma warning disable 0649
        [SerializeField] private CurvedUISettings mySettings;
        [SerializeField] private Transform pivot;
        [SerializeField] private float sensitivity = 0.1f;
        private Vector2 lastMouse;
#pragma warning restore 0649

        // Use this for initialization
        private void Start()
        {
            lastMouse = CurvedUIInputModule.MousePosition;
        }

        // Update is called once per frame
        private void Update()
        {
            //find mouse delta
            Vector3 mouseDelta = CurvedUIInputModule.MousePosition - lastMouse;
            lastMouse = CurvedUIInputModule.MousePosition;
            
            //adjust transform angle
            pivot.localEulerAngles += new Vector3(-mouseDelta.y, mouseDelta.x, 0) * sensitivity;
            
            //pass ray and button state to CurvedUIInputModule
            var myRay = new Ray(transform.position, transform.forward);
            CurvedUIInputModule.CustomRay = myRay;
            CurvedUIInputModule.CustomRayButtonState = CurvedUIInputModule.LeftMouseButton;
        }
    }
}
