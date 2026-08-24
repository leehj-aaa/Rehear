using System;
using System.Linq;
using CurvedUI.Core.Utilities;
using UnityEngine;
#if CURVEDUI_STEAMVR
using Valve.VR;
#endif
[assembly: OptionalDependency("Valve.VR.SteamVR_PlayArea", "CURVEDUI_STEAMVR")]
[assembly: OptionalDependency("Valve.VR.InteractionSystem.Player", "CURVEDUI_STEAMVR_INT")]


namespace CurvedUI.Core.ControlMethods
{
    [Serializable]
    public class SteamVRControlMethod : CurvedUIControlMethod
    {
        public override string scriptingDefineSymbol => "CURVEDUI_STEAMVR";

        public override string[] requiredAssetsNames => new[] { "SteamVR Plugin 2.0 or later" };

#if CURVEDUI_STEAMVR
        //references
        [SerializeField] SteamVR_Action_Boolean steamVRClickAction;
        [SerializeField] SteamVR_PlayArea steamVRPlayArea;

        //variables - used by Settings Inspector
        private SteamVR_Action_Boolean[] _steamVRActions;
        private string[] _steamVRActionsPaths;
        
        //variables
        private GameObject _rightController;
        private GameObject _leftController;

        
        #region PUBLIC
        public override void Initialize(bool isPlayMode)
        {
            if (steamVRPlayArea == null)
                steamVRPlayArea = UnityEngine.Object.FindFirstObjectByType<SteamVR_PlayArea>();

            if (steamVRPlayArea != null)
            {
                foreach (var poseComp in steamVRPlayArea.GetComponentsInChildren<SteamVR_Behaviour_Pose>(true))
                {
                    switch (poseComp.inputSource)
                    {
                        case SteamVR_Input_Sources.RightHand: _rightController = poseComp.gameObject; break;
                        case SteamVR_Input_Sources.LeftHand: _leftController = poseComp.gameObject; break;
                    }
                }
            }
            else
            {
#if CURVEDUI_STEAMVR_INT
                //Optional - SteamVR Interaction System
                if (UnityEngine.Object.FindFirstObjectByType<Valve.VR.InteractionSystem.Player>() is { } player)
                {
                    _rightController = player.rightHand.gameObject;
                    _leftController = player.leftHand.gameObject;
                }
                else
#endif
                if(isPlayMode) Debug.LogError($"CURVEDUI: Can't find {nameof(SteamVR_PlayArea)} component " +
                                              $"or InteractionSystem.Player component on the scene. One of these is required. " +
                                              $"Add a reference to it manually to CurvedUIInputModule on EventSystem GameObject.");
            }

            if (steamVRClickAction == null)
            {
                if(isPlayMode) Debug.LogError("CURVEDUI: No SteamVR action to use for button interactions. " +
                                              "Choose the action you want to use to click the buttons on CurvedUISettings component.");

            }
        }

        public override ControlArgs Process(Hand usedHand, Camera mainEventCamera)
        {
            if (steamVRClickAction != null)
            {
                return new ControlArgs
                {
                    Ray = GetEventRay(usedHand, mainEventCamera),
                    ButtonState = steamVRClickAction.GetState(GetSteamVRInputSource(usedHand))
                };
            }
            
            Debug.LogError("CURVEDUI: Choose which SteamVR_Action will be used for a Click on CurvedUISettings component.");
            return null;
        }
        
        public override Transform GetPointerTransform(Hand usedHand)
            => usedHand == Hand.Left ? _leftController.transform : _rightController.transform;

        public override Ray GetEventRay(Hand usedHand, Camera eventCam = null)
            => GetPointerTransform(usedHand).ToRay();
        #endregion
        
        
        
        #region PRIVATE
        private SteamVR_Input_Sources GetSteamVRInputSource(Hand usedHand) 
            => usedHand == Hand.Left ? Valve.VR.SteamVR_Input_Sources.LeftHand : Valve.VR.SteamVR_Input_Sources.RightHand;

        private void CacheSteamVRActionsAndPaths()
        {
            //Get action and their paths to show in the popup.
            _steamVRActions = SteamVR_Input.GetActions<SteamVR_Action_Boolean>();
            
            //add all action paths to list.
            _steamVRActionsPaths = _steamVRActions is { Length: > 0 }
                //anything to show? Sanitize it.
                ? _steamVRActions
                    .Select(t => t.fullPath)
                    .Select(x => x.Replace('/', '\\')) //replace slashes, or they will not show up.
                    .Append("None") //need a way to null that field, so add None as last pick.
                    .ToArray()
                //show empty
                : new[] { "None" };
        }
        #endregion
        
        
        #region GETTERS AND SETTERS
        public SteamVR_Action_Boolean[] steamVRActions {
            get {
                if (_steamVRActions == null) CacheSteamVRActionsAndPaths();
                return _steamVRActions;
            }
        }
        
        public string[] steamVRActionsPaths {
            get {
                if (_steamVRActionsPaths == null) CacheSteamVRActionsAndPaths();
                return _steamVRActionsPaths;
            }
        }
        
        public SteamVR_PlayArea SteamVRPlayArea {
            get => steamVRPlayArea;
            set => steamVRPlayArea = value;
        }
        
        public SteamVR_Action_Boolean SteamVRClickAction {
            get => steamVRClickAction;
            set => steamVRClickAction = value;
        }
        #endregion
#endif
    }
}    