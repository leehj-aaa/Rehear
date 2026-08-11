using System;
using UnityEngine;

namespace CurvedUI.Core.ControlMethods
{
    [Serializable]
    public class CustomRayControlMethod : CurvedUIControlMethod
    {
        //variables
        private Ray _ray = new(Vector3.zero, Vector3.forward);
        private bool _buttonState;
        
        
        #region PUBLIC
        public override void Initialize(bool isPlayMode) { }

        public override ControlArgs Process(Hand usedHand, Camera mainEventCamera) =>
            new()
            {
                Ray = GetEventRay(usedHand, mainEventCamera),
                ButtonState = CustomControllerButtonState
            };

        public override Ray GetEventRay(Hand usedHand, Camera eventCam = null) => _ray;

        public override Transform GetPointerTransform(Hand usedHand)
        {
            Debug.LogError($"CurvedUI: {nameof(CustomRayControlMethod)} does not have a pointer transform.");
            return null;
        }
        #endregion

        
        
        #region SETTERS AND GETTERS
        public Ray CustomControllerRay
        {
            get => _ray;
            set => _ray = value;
        }

        /// <summary>
        /// Tell CurvedUI if controller button is pressed down. Input module will use this to interact with canvas.
        /// </summary>
        public bool CustomControllerButtonState
        {
            get => _buttonState;
            set => _buttonState = value;
        }
        #endregion
    }
}
