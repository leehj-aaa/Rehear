using System;
using UnityEngine;
using UnityEngine.UI;

namespace CurvedUI.Core.ControlMethods
{
    [Serializable]
    public class GazeControlMethod : CurvedUIControlMethod
    {
        [SerializeField] private bool gazeUseTimedClick;
        [SerializeField] private float gazeClickTimer = 2.0f;
        [SerializeField] private float gazeClickTimerDelay = 1.0f;
        [SerializeField] private Image gazeTimedClickProgressImage;

        //variables
        private float _gazeTimerProgress;
        
        
        #region OVERRIDES
        public override void Initialize(bool isPlayMode)
        {
            if (gazeTimedClickProgressImage != null)
                gazeTimedClickProgressImage.fillAmount = 0;
        }

        public override ControlArgs Process(Hand usedHand, Camera mainEventCamera) 
            => new()
            {
                Ray = GetEventRay(usedHand, mainEventCamera),
                ButtonState = false
            };

        public override Ray GetEventRay(Hand usedHand, Camera eventCam = null)
        {
            if (eventCam == null)
            {
                Debug.LogError("CurvedUI: No event camera passed to Gaze control method.");
                return new Ray();
            }
            
            //get a ray from the center of world camera.
            return eventCam.transform.ToRay();
        }

        public override Transform GetPointerTransform(Hand usedHand)
        {
            Debug.LogError($"CurvedUI: {nameof(GazeControlMethod)} does not have a pointer transform.");
            return null;
        }
        #endregion
        
        
        #region GETTERS AND SETTERS
        public bool GazeUseTimedClick
        {
            get => gazeUseTimedClick;
            set => gazeUseTimedClick = value;
        }

        /// <summary>
        /// Gaze Control Method. How long after user points on a button should we click it? Default 2 seconds.
        /// </summary>
        public float GazeClickTimer
        {
            get => gazeClickTimer;
            set => gazeClickTimer = Mathf.Max(value, 0);
        }

        /// <summary>
        /// Gaze Control Method. How long after user looks at a button should we start the timer? Default 1 second.
        /// </summary>
        public float GazeClickTimerDelay
        {
            get => gazeClickTimerDelay;
            set => gazeClickTimerDelay = Mathf.Max(value, 0);
        }

        /// <summary>
        /// Gaze Control Method. How long till Click method is executed on Buttons under gaze? Goes 0-1.
        /// </summary>
        public float GazeTimerProgress => _gazeTimerProgress;

        /// <summary>
        /// Gaze Control Method. This Image's fill will be animated 0-1 when OnClick events are about
        /// to be executed on buttons under the gaze.
        /// </summary>
        public Image GazeTimedClickProgressImage
        {
            get => gazeTimedClickProgressImage;
            set => gazeTimedClickProgressImage = value;
        }
        #endregion
    }
}
