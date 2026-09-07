using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Kamgam.UGUIBlurredBackground
{
    public class MouseLook : MonoBehaviour
    {
        private float mouseSensitivity = 100.0f;
        private float clampAngle = 80.0f;

        private float rotX = 0.0f; // rotation around the right/x axis
        private float rotY = 0.0f; // rotation around the up/y axis

        private float startRotX = 0.0f; // rotation around the right/x axis

        void Start()
        {
            Vector3 rot = transform.localRotation.eulerAngles;
            startRotX = rot.x;
            rotY = rot.y;
            rotX = rot.x;
        }

        void Update()
        {
            // The mosue delta is sometimes off in the first few frames in the editor. So we ignore it.
            if (Time.frameCount < 5)
                return;

            float mouseX, mouseY;
            getMouseAxis(out mouseX, out mouseY);

            float dY = mouseX * mouseSensitivity * Time.deltaTime;
            float dX = mouseY * mouseSensitivity * Time.deltaTime;

            rotY += dY;
            rotX += dX;

            rotX = Mathf.Clamp(rotX, startRotX - clampAngle, startRotX + clampAngle);

            Quaternion localRotation = Quaternion.Euler(rotX, rotY, 0.0f);
            transform.rotation = localRotation;
        }

        private static void getMouseAxis(out float mouseX, out float mouseY)
        {
#if ENABLE_INPUT_SYSTEM
            mouseX = Mouse.current.delta.x.ReadValue() * 0.3f;
            mouseY = -Mouse.current.delta.y.ReadValue() * 0.3f;
#else
            mouseX = Input.GetAxis("Mouse X");
            mouseY = -Input.GetAxis("Mouse Y");
#endif
        }
    }
}