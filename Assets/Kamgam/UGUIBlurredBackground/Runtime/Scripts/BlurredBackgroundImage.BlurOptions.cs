using System;
using UnityEngine;

namespace Kamgam.UGUIBlurredBackground
{
    public partial class BlurredBackgroundImage
    {
        [Header("Blur")]
        [HelpBox("NOTICE: The settings below are global. They will affect all blurred images." +
            "\nThe most recently activated image will take precedence.", HelpBoxMessageType.Info)]
        [SerializeField, Range(0f, 300f)]
        protected float _strength = 20f;

        public float Strength
        {
            get
            {
                return _strength;
            }

            set
            {
                if (value < 0f)
                    value = 0f;

                if (value != _strength)
                {
                    // Ensure that when switching from or to 0 then we refresh the vertices.
                    if (Mathf.Approximately(value, 0f) || Mathf.Approximately(_strength, 0f))
                    {
                        SetVerticesDirty();
                    }
                    _strength = value;
                }

                if (value != BlurManager.Instance.Offset)
                {
                    BlurManager.Instance.Offset = value;
                }
            }
        }

        [SerializeField]
        [Tooltip("If high blur strengths are used then you may notice visible artefacts. To avoid these increase the\n\n" +
            "quality. NOTICE: The higher the quality the more performance it will cost. As a rule of thumb\n\n" +
            "(low = a performance cost of 1, medium = a cost of 3, high = a cost of 10).")]
        protected ShaderQuality _quality;

        public ShaderQuality Quality
        {
            get
            {
                return _quality;
            }

            set
            {
                _quality = value;

                if (value != BlurManager.Instance.Quality)
                {
                    BlurManager.Instance.Quality = value;
                }
            }
        }

        [SerializeField]
        [Tooltip("Reducing the resolution is a great way to increase the blurryness of your image while also saving a LOT of performance.\n\n" +
            "Halfing the resolution usually makes the blur 4 times faster.")]
        protected SquareResolution _resolution = SquareResolution._512;

        public SquareResolution Resolution
        {
            get
            {
                return _resolution;
            }

            set
            {
                _resolution = value;

                var res = value.ToResolution();
                if (res != BlurManager.Instance.Resolution)
                {
                    BlurManager.Instance.Resolution = res;
                }
            }
        }

        [SerializeField, Range(1, 10)]
        [Tooltip("Blur iterations should be kept at 1. This defines how often the blur filter will be applied.\n\n" +
            "In terms of performance this the most expensive setting you can increase. Use with care (avoid if you can).")]
        protected int _iterations = 1;

        public int Iterations
        {
            get
            {
                return _iterations;
            }

            set
            {
                if (value < 0)
                    value = 0;

                if (_iterations != value)
                {
                    // Ensure that when switching from or to 0 then we refresh the vertices.
                    if (value == 0 || _strength == 0)
                    {
                        SetVerticesDirty();
                    }

                    _iterations = value;
                }
                

                if (value != BlurManager.Instance.Iterations)
                {
                    BlurManager.Instance.Iterations = value;
                }
            }
        }

        [SerializeField]
        protected Color _additiveColor = new Color(0,0,0,0);

        public Color AdditiveColor
        {
            get
            {
                return _additiveColor;
            }

            set
            {
                if (value == BlurManager.Instance.Renderer.AdditiveColor)
                    return;

                _additiveColor = value;
                BlurManager.Instance.Renderer.AdditiveColor = value;
            }
        }

        [Tooltip("If set then the blur renderer will use this camera´s output instead of auto-detecting the current camera.")]
        public Camera CameraOverride;

        [Header("Local Blur Settings")]
        
        [SerializeField]
        [Tooltip("Enable if you are using a camera viewport rect that is not 0/0 > 1/1.")]
        protected bool _useCameraRect = false;

        public bool UseCameraRect
        {
            get
            {
                return _useCameraRect;
            }

            set
            {
                if (value == _useCameraRect)
                    return;

                _useCameraRect = value;
                SetVerticesDirty();
            }
        }

        [SerializeField]
        protected Vector2 _FOVOffset = new Vector2(0f, 0f);

        public Vector2 FOVOffset
        {
            get
            {
                return _FOVOffset;
            }

            set
            {
                if (value == _FOVOffset)
                    return;

                _FOVOffset = value;
                SetVerticesDirty();
            }
        }

        [SerializeField]
        protected Vector2 _FOVScale = new Vector2(1f, 1f);

        public Vector2 FOVScale
        {
            get
            {
                return _FOVScale;
            }

            set
            {
                if (value == _FOVScale)
                    return;

                _FOVScale = value;
                SetVerticesDirty();
            }
        }

        internal void _EditorApplyChangedValues()
        {
            Iterations = _iterations;
            Strength = _strength;
            Resolution = _resolution;
            Quality = _quality;
            AdditiveColor = _additiveColor;
            FOVScale = _FOVScale;
        }
    }
}