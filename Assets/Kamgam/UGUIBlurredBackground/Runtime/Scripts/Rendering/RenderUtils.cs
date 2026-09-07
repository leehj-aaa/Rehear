using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Kamgam.UGUIBlurredBackground
{
    public static class RenderUtils
    {
        static Camera _cachedGameViewCam;

        static Camera[] _tmpAllCameras = new Camera[10];

        public static Camera GetGameViewCamera(BlurredBackgroundImage image = null)
        {
            var cam = Camera.main;

            // Use the camera of the last activated image in a screen space camera canvas (if there is one).
            if (image != null
                && image.GetRenderMode() != RenderMode.ScreenSpaceOverlay
                && image.canvas != null
                && image.canvas.worldCamera != null
                && image.canvas.worldCamera.cameraType == CameraType.Game)
            {
                cam = image.canvas.worldCamera;
            }

            if (image != null && image.CameraOverride != null)
            {
                cam = image.CameraOverride;
            }

            if (cam == null)
            {
                // Fetch cameras
                int allCamerasCount = Camera.allCamerasCount;
                // Alloc new array only if needed
                if (allCamerasCount > _tmpAllCameras.Length)
                {
                    _tmpAllCameras = new Camera[allCamerasCount + 5];
                }
                Camera.GetAllCameras(_tmpAllCameras);

                // We sort by depth and start from the back because we assume
                // that among cameras with equal depth the last takes precedence.
                float maxDepth = float.MinValue;
                for (int i = _tmpAllCameras.Length - 1; i >= 0; i--)
                {
                    // Null out old references
                    if (i >= allCamerasCount)
                    {
                        _tmpAllCameras[i] = null;
                        continue;
                    }

                    var cCam = _tmpAllCameras[i];

                    if (!cCam.isActiveAndEnabled)
                        continue;

                    // Only take full screen cameras that are not rendering into render textures
                    if (cCam.depth > maxDepth && cCam.targetTexture == null && cCam.rect.width >= 1f && cCam.rect.height >= 1f)
                    {
                        maxDepth = cCam.depth;
                        cam = cCam;
                    }
                }
            }

            // cache game view camera
            if (cam != null && cam.cameraType == CameraType.Game)
                _cachedGameViewCam = cam;

            if (cam == null)
                return _cachedGameViewCam;

            return cam;
        }
    }
}