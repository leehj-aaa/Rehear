using UnityEngine;
using UnityEngine.UI;

namespace Kamgam.UGUIBlurredBackground
{
    [ExecuteAlways]
    [AddComponentMenu("UI/Kamgam/Blurred Background Image")]
    public partial class BlurredBackgroundImage : Image
    {
        public RenderMode GetRenderMode()
        {
            var renderMode = RenderMode.ScreenSpaceOverlay;

            if (canvas == null)
                renderMode = RenderMode.ScreenSpaceOverlay; 
            else
                renderMode = canvas.renderMode;

            // Check if the camera matches the render mode and if not alter the render mode.
            if (renderMode == RenderMode.ScreenSpaceCamera && canvas != null)
            {
                Camera cam = canvas.worldCamera;
                // If no camera is specified then it will behave like screen scpae OVERLAY.
                if (cam == null)
                    renderMode = RenderMode.ScreenSpaceOverlay;
            }

            return renderMode;
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            SetVerticesDirty(); 
        }

        protected override void Awake()
        {
            base.Awake();

            BlurManager.Instance.RegisterImage(this);
#if UNITY_EDITOR
            refreshAfterLoadInEditor();
#endif
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            BlurManager.Instance.RegisterImage(this);
            BlurManager.Instance.ApplyValues(this);
            BlurManager.Instance.Renderer.OnPostRender += onPostRender;
        }

        protected override void Start()
        {
            base.Start();
            SetVerticesDirty();
        }

#if UNITY_EDITOR
        void refreshAfterLoadInEditor() 
        {
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && !EditorScheduler.HasId("UGUIBlurredBg.EditorRefresh"))
            {
                EditorScheduler.Schedule(0.2f, () =>
                {
                    if (this == null || gameObject == null)
                    {
                        return;
                    }

                    SetVerticesDirty();
                    EditorUtils.RefreshGameView();
                },
                "UGUIBlurredBg.EditorRefresh");
            }
        }
#endif

        [System.NonSerialized]
        protected Matrix4x4 _lastWorldToLocalMatrix = Matrix4x4.identity;

        [System.NonSerialized]
        protected Matrix4x4 _lastWorldToCameraMatrix = Matrix4x4.identity;
		
#if UNITY_EDITOR
        private static bool _editorWarnedAboutSlicedSprites = false;
#endif

        protected int _dirtyUntilFrame = -1; 
        
        protected void onPostRender()
        {
            if (this == null || this.gameObject == null)
                return;

            // Check
#if UNITY_EDITOR
            if (sprite != null && sprite.border.sqrMagnitude > 0 && !_editorWarnedAboutSlicedSprites)
            {
                _editorWarnedAboutSlicedSprites = true;
                Logger.LogWarning("The used sprite '" + sprite.name + "' is sliced but sliced images are not supported. Use the sliced image as a parent mask instead (see manual or SlicedSpriteDemo scene). You can also use the Create Mask button.");
            }
#endif

            // Since the texture is buffered (two render textures swapping all the time) we have
            // to update the used texture after every render. Using only the overridden "mainTexture"
            // sadly is not enough.
            var texture = BlurManager.Instance.GetBlurredTexture(GetRenderMode(), this);
            if (texture != null)
                canvasRenderer.SetTexture(texture);

            // Update UVs on world space canvases if camera changed or any movement happened.
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                var cam = RenderUtils.GetGameViewCamera(this);

#if UNITY_EDITOR
                if (!UnityEditor.EditorApplication.isPlaying)
                {
                    SetVerticesDirty();
                }
#endif

                if (cam != null && cam.cameraType == CameraType.Game && (cam.worldToCameraMatrix != _lastWorldToCameraMatrix || Time.frameCount <= _dirtyUntilFrame))
                {
                    // Camera changed
                    // Also this is called TWICE per frame (once for screen space and once for world space) so we mark it dirty for the whole frame (or longer, see below why).
                    _dirtyUntilFrame = Mathf.Max(Time.frameCount, _dirtyUntilFrame);
                    // Since cam.worldToCameraMatrix seems to not update every frame (or at least we got false equals here) we
                    // force the camera to be unequal for the next N frames to avoid the stutter that comes from these false positives.
                    if (cam.worldToCameraMatrix != _lastWorldToCameraMatrix)
                    {
                        _dirtyUntilFrame = Time.frameCount + 3;
                    }
                    _lastWorldToCameraMatrix = cam.worldToCameraMatrix;
                    SetVerticesDirty();
                }
            }

            // Update UVs if the transforms changed (pos, rot, scale).
            if (transform.worldToLocalMatrix != _lastWorldToLocalMatrix)
            {
                // World canvas changed
                _lastWorldToLocalMatrix = transform.worldToLocalMatrix;
                SetVerticesDirty();
            }

            // If the camera rect should be taken into account then check every for camera changes every frame.
            if (UseCameraRect)
            {
                checkForCameraChange();
            }
        }

        protected Camera _lastUsedGameViewCamera;

        private void checkForCameraChange()
        {
            var gameCam = RenderUtils.GetGameViewCamera(this);
            if (gameCam != _lastUsedGameViewCamera)
            {
                _lastUsedGameViewCamera = gameCam;
                // trigger redraw of UVs
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Useful to manually trigger a vertices redraw (use this after you changed the camera rect via script).
        /// </summary>
        public void ClearLastUsedGameViewCamera()
        {
            _lastUsedGameViewCamera = null;
        }

        public override Texture mainTexture
        {
            get
            {
                var texture = BlurManager.Instance.GetBlurredTexture(GetRenderMode(), this);
                if (texture != null)
                    return texture;
                else
                    return base.mainTexture;
            }
        }

        protected override void OnDisable()
        {
            if (BlurManager.HasInstance())
            {
                BlurManager.Instance.UnregisterImage(this);

                if (BlurManager.Instance.Renderer != null)
                    BlurManager.Instance.Renderer.OnPostRender -= onPostRender;
            }

            material = null;

            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}