using UnityEditor;
using UnityEngine;

namespace CurvedUI.Core.ControlMethods.Editor
{
    public class MetaXRInspector : ControlMethodInspector
    {
        public MetaXRInspector(CurvedUIControlMethod m) : base(m) { }
   
#if CURVEDUI_META_XR
        public override void Draw()
        {
            if(Method is not MetaXRControlMethod settings) return;

            // oculus enabled, we can show settings
            GUILayout.Label("Use Meta headset' controllers to interact with the canvas.",
                EditorStyles.helpBox);
#if CURVEDUI_OVR_HANDS
            GUILayout.Label("Hand Interaction support enabled.", EditorStyles.helpBox);
#else
            EditorGUILayout.HelpBox("Hand Interaction support disabled. OVR Interaction package missing.", MessageType.Warning);
#endif
            //hand property
            CurvedUIInputModule.Instance.UsedHand =
                (Hand)EditorGUILayout.EnumPopup("Hand", CurvedUIInputModule.Instance.UsedHand);
            //button property
            settings.ControllerInteractionButton =
                (OVRInput.Button)EditorGUILayout.EnumPopup("Interaction Button", settings.ControllerInteractionButton);

        }
#endif
    }
}
