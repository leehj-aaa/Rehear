using UnityEngine;

namespace Kamgam.UGUIBlurredBackground
{
    public class LimitFps : MonoBehaviour
    {
        public int FrameRate = 60;

        void Awake()
        {
            Application.targetFrameRate = FrameRate;
        }
    }
}