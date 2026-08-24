using System.Collections.Generic;
using System.Linq;
using CurvedUI.Core.Utilities.Editor;
using UnityEditor;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && CURVEDUI_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace CurvedUI.Core.ControlMethods.Editor
{
    public class UnityXRInspector : ControlMethodInspector
    {
        public UnityXRInspector(CurvedUIControlMethod m) : base(m) { }
        
        #if CURVEDUI_UNITY_XR && ENABLE_INPUT_SYSTEM && CURVEDUI_NEW_INPUT 
        #region PUBLIC
        public override void Draw()
        {
            if(Method is not UnityXRControlMethod settings) return;
            
            //  enabled, we can show settings
            GUILayout.Label("Use Unity XR Toolkit to interact with the canvas.", EditorStyles.helpBox);
            if (GUILayout.Button("Unity XR step-by-step guide"))DocsUtility.OpenDocs(DocsUtility.Bookmark.UnityXR);
            GUILayout.Space(20);
            
            //hand property
            CurvedUIInputModule.Instance.UsedHand = (Hand)EditorGUILayout.EnumPopup("Hand", CurvedUIInputModule.Instance.UsedHand);
            
            //controller fields
            settings.rightController = (UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ControllerInputActionManager)EditorGUILayout.ObjectField("Right Controller",
                settings.rightController, typeof(UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ControllerInputActionManager), true);
            settings.leftController = (UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ControllerInputActionManager)EditorGUILayout.ObjectField("Left Controller",
                settings.leftController, typeof(UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ControllerInputActionManager), true);
            
            //try to find default click actions if missing
            if(settings.rightClickActionReference == null || settings.leftClickActionReference == null)
                FindDefaultActionReferences(settings,"Left" ,"Right", "XRI", "UI Press");
            
            //click action fields
            settings.rightClickActionReference = (InputActionReference)EditorGUILayout.ObjectField("Right Click Action", 
                settings.rightClickActionReference, typeof(InputActionReference), true);
            
            settings.leftClickActionReference = (InputActionReference)EditorGUILayout.ObjectField("Left Click Action", 
                settings.leftClickActionReference, typeof(InputActionReference), true);
        }
        #endregion

    
        #region PRIVATE
        private void FindDefaultActionReferences(UnityXRControlMethod settings, string leftHand, string rightHand, params string[] commonActionSubstrings)
        {
            var allXRPressActions = FindAllXRPressActions(commonActionSubstrings);
            
            if(settings.rightClickActionReference == null 
               && allXRPressActions.FirstOrDefault(x => x.name.Contains(rightHand)) is { } rightAction)
                settings.rightClickActionReference = rightAction;
            
            if(settings.leftClickActionReference == null 
               && allXRPressActions.FirstOrDefault(x => x.name.Contains(leftHand)) is { } leftAction)
                settings.leftClickActionReference = leftAction;
        }
        
        private List<InputActionReference> FindAllXRPressActions(params string[] commonActionSubstrings)
        {
            //find all actions in all assets in the project whose name contains commonActionSubstrings
            return CurvedUI.Core.Utilities.Editor.AssetUtility.FindAssetsOfType<InputActionAsset>()
                .SelectMany(CurvedUI.Core.Utilities.Editor.AssetUtility.GetSubAssetsOfType<InputActionReference>)
                .Where(x => commonActionSubstrings == null || commonActionSubstrings.Length == 0 || commonActionSubstrings.All(x.name.Contains))
                .ToList();
        }
        #endregion
        #endif
    }
}

