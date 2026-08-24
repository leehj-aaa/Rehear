using System;
using CurvedUI.Core.Utilities;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && CURVEDUI_NEW_INPUT // enabled new input backends != imported package
using UnityEngine.InputSystem;
#endif
#if CURVEDUI_UNITY_XR
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
#endif
[assembly: OptionalDependency("UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager", "CURVEDUI_UNITY_XR")]

namespace CurvedUI.Core.ControlMethods
{
    [Serializable]
    public class UnityXRControlMethod : CurvedUIControlMethod
    {
        public override string scriptingDefineSymbol => "CURVEDUI_UNITY_XR";
        
        public override string[] requiredAssetsNames => new[]{
            "Input System package v1.11.0 or later",
            "Unity XR Interaction Toolkit v3.0.3 or later",
            "Unity XR Interaction Toolkit Starter Assets" 
        };
        
        #if CURVEDUI_UNITY_XR && ENABLE_INPUT_SYSTEM && CURVEDUI_NEW_INPUT 
        
        #region VARIABLES
        public ControllerInputActionManager rightController; 
        public ControllerInputActionManager leftController; 
        public InputActionReference rightClickActionReference; 
        public InputActionReference leftClickActionReference; 
        #endregion
        
        
        #region PUBLIC
        public override void Initialize(bool isPlayMode)
        {
            //Are we missing controllers? (Actions are found by inspector, b/c it needs editor script)
            if (rightController == null || leftController == null)
                FindControllerReferences();
            
            if(isPlayMode && (rightController != null || leftController != null))
                Debug.LogError("CURVEDUI: Unity XR Control Method is missing controller references. " +
                               "Please assign them in the inspector.");
        }
        
        public override ControlArgs Process(Hand usedHand, Camera mainEventCamera) 
            => new() {
                Ray = GetEventRay(usedHand, mainEventCamera),
                ButtonState = GetXrControllerButtonState(usedHand),
            };

        public override Transform GetPointerTransform(Hand usedHand) 
            => usedHand == Hand.Left ? leftController.transform : rightController.transform;

        public override Ray GetEventRay(Hand usedHand, Camera eventCam = null) 
            => GetPointerTransform(usedHand).ToRay();
        
        public bool GetXrControllerButtonState(Hand usedHand) {
            switch ( usedHand ) {
                default:
                case Hand.Any:
                case Hand.Right: return rightClickActionReference.action.IsPressed();
                case Hand.Left: return leftClickActionReference.action.IsPressed();
            }
        }
        #endregion
        
        
        #region PRIVATE
        private void FindControllerReferences()
        {
            //Try to find controller references using XRInputModalityManager, if it is available
            if (UnityEngine.Object.FindFirstObjectByType<XRInputModalityManager>() is not { } manager) return;
            
            if (rightController == null && manager.rightController is {  } rc)
                rightController = rc.GetComponent<ControllerInputActionManager>();
            if (leftController == null && manager.leftController is { } lc)
                leftController = lc.GetComponent<ControllerInputActionManager>();
        }
        #endregion
        #endif //end of CURVEDUI_UNITY_XR && CURVEDUI_NEW_INPUT
    }
}
