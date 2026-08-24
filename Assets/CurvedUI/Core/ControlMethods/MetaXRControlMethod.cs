using System;
using System.Linq;
using CurvedUI.Core.Utilities;
using UnityEngine;
using Object = UnityEngine.Object;
[assembly: OptionalDependency("OVRCameraRig", "CURVEDUI_META_XR")] 
[assembly: OptionalDependency("Oculus.Interaction.Input.HandSkeletonOVR", "CURVEDUI_OVR_HANDS")]

namespace CurvedUI.Core.ControlMethods
{
    [Serializable]
    public class MetaXRControlMethod : CurvedUIControlMethod
    {
        public override string scriptingDefineSymbol => "CURVEDUI_META_XR";
        
        public override string[] requiredAssetsNames => new[] {"Meta XR All-in-One SDK v72.0.0 or later"};
        
        #if CURVEDUI_META_XR
        //References
        [SerializeField] private OVRInput.Button interactionButton = OVRInput.Button.PrimaryIndexTrigger;
        [SerializeField] private OVRCameraRig cameraRig;
        
        #if CURVEDUI_OVR_HANDS
        //References - Hands SDK
        private Oculus.Interaction.Input.Hand _ovrLeftHand;
        private Oculus.Interaction.Input.Hand _ovrRightHand;
        private Oculus.Interaction.HandPointerPose _ovrHandPointerPose;
        #endif
        
        //Variables
        private OVRInput.Controller _activeOvrController;
        private bool _isUsingHandTracking;

        
        #region OVERRIDES
        public override void Initialize(bool isPlayMode)
        {
            //find the oculus rig - via manager or by findObjectOfType, if unavailable
            if (cameraRig == null)
            {
                if (OVRManager.instance != null) cameraRig = OVRManager.instance.GetComponent<OVRCameraRig>();
                if (cameraRig == null)  cameraRig = Object.FindFirstObjectByType<OVRCameraRig>();
                if (cameraRig == null && isPlayMode) 
                    Debug.LogError($"CURVEDUI: {nameof(OVRCameraRig)} Prefab missing." +
                                   $"Import Meta SDK and drag OVRCameraRig Prefab onto the scene.");
            }
        
            #if CURVEDUI_OVR_HANDS
            //auto find OVR Hands if possible
            if (_ovrLeftHand == null || _ovrRightHand == null)
            {
                if (Object.FindFirstObjectByType<Oculus.Interaction.Input.HandSkeletonOVR>() is var skeleton 
                    && skeleton != null 
                    && skeleton.GetComponentsInChildren<Oculus.Interaction.Input.Hand>(true) is var hands 
                    && hands.Length > 0)
                {
                    if (_ovrLeftHand == null)
                        _ovrLeftHand = hands.FirstOrDefault(x => x.Handedness == Oculus.Interaction.Input.Handedness.Left);
                    if (_ovrRightHand == null)
                        _ovrRightHand = hands.FirstOrDefault(x => x.Handedness == Oculus.Interaction.Input.Handedness.Right);
                }
                if (isPlayMode && (_ovrLeftHand == null || _ovrRightHand == null))
                    Debug.LogWarning("CURVEDUI: OVR Interaction and/or OVR Hands prefabs are missing from the scene.");
            }
            #endif
        }
        
        
      

        public override ControlArgs Process(Hand usedHand, Camera mainEventCamera)
        {
            var ret = new ControlArgs();
            
            _activeOvrController = OVRInput.GetActiveController();
            
            //Debug.Log($"device: {activeCont.ToString()} | hand: {usedHand.ToString()} | trans: {ControllerTransform?.name ?? "null"}");
            
            _isUsingHandTracking = _activeOvrController 
                is OVRInput.Controller.LHand 
                or OVRInput.Controller.RHand 
                or OVRInput.Controller.Hands;

            //Oculus Hand interaction --------------------------------------------------//
            #if CURVEDUI_OVR_HANDS
            if (_isUsingHandTracking)
            {
                //create pointer pose, if missing
                if (_ovrHandPointerPose == null)
                {
                    _ovrHandPointerPose = new GameObject("CurvedUI_OVRHandPointerPose")
                        .AddComponentIfMissing<Oculus.Interaction.HandPointerPose>();
                    _ovrHandPointerPose.InjectHand(usedHand is Hand.Any or Hand.Right ? _ovrRightHand : _ovrLeftHand); 
                    _ovrHandPointerPose.transform.SetParent(cameraRig.transform);
                }
                
                //buttons state and pointer pose
                if (usedHand is Hand.Any or Hand.Right && _ovrRightHand != null && _ovrRightHand.IsTrackedDataValid)
                {
                    _ovrHandPointerPose.InjectHand(_ovrRightHand); 
                    ret.ButtonState = _ovrRightHand.GetIndexFingerIsPinching();
                }
                else if (usedHand is Hand.Any or Hand.Left && _ovrLeftHand != null && _ovrLeftHand.IsTrackedDataValid)
                {
                    _ovrHandPointerPose.InjectHand(_ovrLeftHand); 
                    ret.ButtonState = _ovrLeftHand.GetIndexFingerIsPinching();
                }
                else ret.ButtonState = false;
            
                //get ray based on pointer pose
                ret.Ray = new Ray(_ovrHandPointerPose.transform.position, _ovrHandPointerPose.transform.forward);
            }
            #endif // end of CURVEDUI_OVR_HANDS
            
            
            if(!_isUsingHandTracking) // Oculus Controllers --------------------------------------------------//
            {
                //Find the currently used HandAnchor----------------------//
                //and set direction ray using its transform
                switch (_activeOvrController)
                {
                    //Oculus Touch
                    case OVRInput.Controller.RTouch: ret.Ray = new Ray(cameraRig.rightHandAnchor.position, cameraRig.rightHandAnchor.forward); break;
                    case OVRInput.Controller.LTouch: ret.Ray = new Ray(cameraRig.leftHandAnchor.position, cameraRig.leftHandAnchor.forward); break;
                    //edge cases
                    default: GetPointerTransform(usedHand); break; 
                }

                //Check if interaction button is pressed ---------------//
                //find if we're using Rift with touch. If yes, we'll have to check if the interaction button is pressed on the proper hand.
                var touchControllersUsed = _activeOvrController 
                    is OVRInput.Controller.Touch 
                    or OVRInput.Controller.LTouch 
                    or OVRInput.Controller.RTouch;
            
                if (usedHand == Hand.Any || !touchControllersUsed) //button is pressed on any controller
                {
                    ret.ButtonState = OVRInput.Get(interactionButton);
                }
                else if (usedHand == Hand.Right) // Right Oculus Touch
                {
                    ret.ButtonState = OVRInput.Get(interactionButton, OVRInput.Controller.RTouch);
                }
                else if (usedHand == Hand.Left)  // Left Oculus Touch
                {
                    ret.ButtonState = OVRInput.Get(interactionButton, OVRInput.Controller.LTouch);
                }
            }

            return ret;
        }

        public override Transform GetPointerTransform(Hand usedHand)
        {
            #if CURVEDUI_OVR_HANDS
            if (_isUsingHandTracking && _ovrHandPointerPose != null) return _ovrHandPointerPose.transform;
            #endif
            return usedHand == Hand.Left ? cameraRig.leftHandAnchor : cameraRig.rightHandAnchor;
        }

        public override Ray GetEventRay(Hand usedHand, Camera eventCam = null)
            => GetPointerTransform(usedHand).ToRay();
        #endregion
        
        
        
        
        #region SETTERS AND GETTERS
        public OVRCameraRig CameraRig {
            get => cameraRig;
            set => cameraRig = value;
        }

        public OVRInput.Button ControllerInteractionButton {
            get => interactionButton;
            set => interactionButton = value;
        }
        
        public bool IsInteractionPressedOnController(OVRInput.Controller cont) =>
            OVRInput.GetDown(ControllerInteractionButton, cont);

        public bool IsUsingHandTracking => _isUsingHandTracking;
    
        public bool IsHandPinching(Hand hand)
        {
            #if CURVEDUI_OVR_HANDS
            switch (hand)
            {
                default:
                case Hand.Any:
                case Hand.Right:
                    return _ovrRightHand != null && _ovrRightHand.IsTrackedDataValid && _ovrRightHand.GetIndexFingerIsPinching();
                case Hand.Left:
                    return _ovrLeftHand != null && _ovrLeftHand.IsTrackedDataValid && _ovrLeftHand.GetIndexFingerIsPinching();
            }
            #else
            return false;
            #endif
        }
        #endregion
        #endif
    }
}
