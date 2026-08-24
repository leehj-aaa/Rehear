using CurvedUI.Core;
using UnityEditor;

namespace CurvedUI
{
    [CustomEditor(typeof(CurvedUIHandSwitcher))]
    public class CurvedUIHandSwitcherEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("This script moves the Laser Beam to the proper hand of OculusVR or SteamVR rig. Keep it active on the scene.", MessageType.Info);
            EditorGUILayout.HelpBox("The Laser Beam is just a visual guide - it does not handle interactions.", MessageType.Info);
            
            DrawDefaultInspector();
        }
    }

}
