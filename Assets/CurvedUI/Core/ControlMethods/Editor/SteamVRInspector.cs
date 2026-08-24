using UnityEditor;
using UnityEngine;

namespace CurvedUI.Core.ControlMethods.Editor
{
    public class SteamVRInspector : ControlMethodInspector
    {
        public SteamVRInspector(CurvedUIControlMethod controlMethod) : base(controlMethod) { }
        
        #if CURVEDUI_STEAMVR
        public override void Draw()
        {
            if(Method is not SteamVRControlMethod settings) return;
            
            GUILayout.Label("Use SteamVR controllers to interact with canvas. Requires SteamVR Plugin 2.0 or later.",
                EditorStyles.helpBox);
            
            if (settings.steamVRActions != null)
            {
                CurvedUIInputModule.Instance.UsedHand =
                    (Hand)EditorGUILayout.EnumPopup("Hand", CurvedUIInputModule.Instance.UsedHand);

                //Find currently selected action in CurvedUIInputModule
                int curSelected = settings.steamVRActionsPaths.Length - 1;
                for (int i = 0; i < settings.steamVRActions.Length; i++)
                {
                    //no action selected? select one that most likely deals with UI
                    if (settings.SteamVRClickAction == null && settings.steamVRActions[i].GetShortName().Contains("UI"))
                        settings.SteamVRClickAction = settings.steamVRActions[i];

                    //otherwise show currently selected
                    if (settings.steamVRActions[i] == settings.SteamVRClickAction) //otherwise show selected
                        curSelected = i;
                }

                //Show popup
                int newSelected = EditorGUILayout.Popup("Click With", curSelected, settings.steamVRActionsPaths,
                    EditorStyles.popup);

                //assign selected SteamVR Action to CurvedUIInputModule
                if (curSelected != newSelected)
                {
                    //none has been selected
                    if (newSelected >= settings.steamVRActions.Length)
                        settings.SteamVRClickAction = null;
                    else
                        settings.SteamVRClickAction = settings.steamVRActions[newSelected];
                }
            }
            else
            {
                //draw error
                EditorGUILayout.HelpBox("No SteamVR Actions set up. Configure your SteamVR plugin first in Window > Steam VR Input", MessageType.Error);
            }
        }
        #endif
    }
}