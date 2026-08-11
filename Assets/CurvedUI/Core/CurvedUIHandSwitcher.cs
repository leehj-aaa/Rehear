using CurvedUI.Core.ControlMethods;
using UnityEngine;

namespace CurvedUI.Core {
    /// <summary>
    /// This script switches the hand controlling the UI when a click on the other controller's trigger is detected.
    /// This emulates the functionality seen in SteamVR Overlay or Oculus Home.
    /// </summary>
    public class CurvedUIHandSwitcher : MonoBehaviour
    {
#pragma warning disable 0649
#pragma warning disable 414
        [SerializeField]
        GameObject LaserBeam;

        [SerializeField]
        [Tooltip("If true, when player clicks the trigger on the other hand, we'll instantly set it as UI controlling hand and move the pointer to it.")]
        bool autoSwitchHands = true;

        [Header("Optional")] 
        [SerializeField] [Tooltip("If set, pointer will be placed as a child of this transform, instead of the current VR SDKs Camera Rig.")]
        private Transform leftHandOverride;
        [SerializeField] [Tooltip("If set, pointer will be placed as a child of this transform, instead of the current VR SDKs Camera Rig.")]
        private Transform rightHandOverride;
        
#pragma warning restore 414
#pragma warning restore 0649


        #region LIFECYCLE
        private void Start() => SwitchHandTo(CurvedUIInputModule.Instance.UsedHand);

        private void Update()
        {
            if (!autoSwitchHands) return;
            
            switch (CurvedUIInputModule.ActiveControlMethod)
            {
                case ControlMethod.UNITY_XR: CheckUnityXRHands(); break;
                case ControlMethod.STEAM_VR: CheckSteamVRHands(); break;
                case ControlMethod.META_XR: CheckMetaXRHands(); break;
            }
        }
        #endregion
        
        
        #region PRIVATE
        private void CheckUnityXRHands()
        {
            #if CURVEDUI_UNITY_XR && ENABLE_INPUT_SYSTEM && CURVEDUI_NEW_INPUT 
            var usedHand = CurvedUIInputModule.Instance.UsedHand;
            var settings = CurvedUIInputModule.Instance.GetControlMethodSettings<UnityXRControlMethod>();
           
            if (settings.rightController != null && usedHand != Hand.Right)
            {
                if(settings.GetXrControllerButtonState(Hand.Right)) SwitchHandTo(Hand.Right);
            } 
            if (settings.leftController != null && usedHand != Hand.Left)
            {
                if(settings.GetXrControllerButtonState(Hand.Right)) SwitchHandTo(Hand.Left);
            } 
            #endif
        }
        
        private void CheckSteamVRHands()
        {
            #if CURVEDUI_STEAMVR
            var settings = CurvedUIInputModule.Instance.GetControlMethodSettings<SteamVRControlMethod>();

            if (settings.SteamVRClickAction == null) return;

            if (settings.SteamVRClickAction.GetState(Valve.VR.SteamVR_Input_Sources.RightHand) &&
                CurvedUIInputModule.Instance.UsedHand != Hand.Right)
                SwitchHandTo(Hand.Right);
            else if (settings.SteamVRClickAction.GetState(Valve.VR.SteamVR_Input_Sources.LeftHand) &&
                     CurvedUIInputModule.Instance.UsedHand != Hand.Left)
                SwitchHandTo(Hand.Left);
            #endif
        }
        
        private void CheckMetaXRHands()
        {
            #if CURVEDUI_META_XR
            var settings = CurvedUIInputModule.Instance.GetControlMethodSettings<MetaXRControlMethod>();
            var usedHand = CurvedUIInputModule.Instance.UsedHand;
      
            //todo: needed?
            // //initialization
            // if (!initialized && _inputModule.PointerTransform != null)
            // {
            //     SwitchHandTo(_inputModule.UsedHand);
            //
            //     initialized = true;
            // }
            // //did our current parent go missing? Trigger a switch to find the right one
            // else if (LaserBeam.transform.parent != _inputModule.PointerTransform)
            // {
            //     SwitchHandTo(_inputModule.UsedHand);
            // }
            

            switch (OVRInput.GetActiveController())
            {
                //Switch automatically if a different controller is connected.
                case OVRInput.Controller.LTouch or OVRInput.Controller.LHand when usedHand != Hand.Left:
                    SwitchHandTo(Hand.Left);
                    return;
                case OVRInput.Controller.RTouch or OVRInput.Controller.RHand when usedHand != Hand.Right:
                    SwitchHandTo(Hand.Right);
                    return;
            }
            
            if(autoSwitchHands)
            {
                //for OVR Hand Interaction SDK, we look for a pinch a trigger to a switch
                if (settings.IsUsingHandTracking)
                {
                    if (settings.IsHandPinching(Hand.Left) && usedHand != Hand.Left)
                    {
                        SwitchHandTo(Hand.Left);
                        return;
                    }
                    if (settings.IsHandPinching(Hand.Right) && usedHand != Hand.Right)
                    {
                        SwitchHandTo(Hand.Right);
                        return;
                    }
                }
                
                //For Quest Controllers, we wait for the click before we change the pointer to the other hand
                if (settings.IsInteractionPressedOnController(OVRInput.Controller.LTouch) && usedHand != Hand.Left)
                {
                    SwitchHandTo(Hand.Left);
                    return;
                }
                if (settings.IsInteractionPressedOnController(OVRInput.Controller.RTouch) && usedHand != Hand.Right)
                {
                    SwitchHandTo(Hand.Right);
                    return;
                }
            }  
            #endif
        }
        
        
        private void SwitchHandTo(Hand newHand)
        {
            CurvedUIInputModule.Instance.UsedHand = newHand;

            if (CurvedUIInputModule.Instance.PointerTransform)
            {
                //hand overrides
                if (newHand ==  Hand.Left && leftHandOverride) 
                    CurvedUIInputModule.Instance.PointerTransformOverride = leftHandOverride;
                if (newHand ==  Hand.Right && rightHandOverride) 
                    CurvedUIInputModule.Instance.PointerTransformOverride = rightHandOverride;

                LaserBeam.transform.SetParent(CurvedUIInputModule.Instance.PointerTransform);
                LaserBeam.transform.ResetTransform();
                LaserBeam.transform.LookAt(LaserBeam.transform.position + CurvedUIInputModule.Instance.PointerDirection);
            }
            else 
                Debug.LogError("CURVEDUI: No active PointerTransform that can be used as a parent of the Laser prefab." +
                                "Is the GameObject present on the scene and active?");
        }
        #endregion
    }
}


