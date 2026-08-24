using System.Linq;
using CurvedUI.Core.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace CurvedUI.Core.ControlMethods.Editor
{
    public class ControlMethodInspector
    {
        //variables
        protected readonly CurvedUIControlMethod Method;
        private static bool _isLoadingDefineSymbol;

        #region CONSTRUCTOR
        protected ControlMethodInspector(CurvedUIControlMethod m)
        {
            Method = m;
            _isLoadingDefineSymbol = false;
        }
        #endregion
        
        
        #region FACTORY
        public static ControlMethodInspector Create(CurvedUIControlMethod method) =>
            method switch
            {
                MouseControlMethod _ => new MouseInspector(method),
                GazeControlMethod _ => new GazeInspector(method),
                CustomRayControlMethod _ => new CustomRayInspector(method),
                MetaXRControlMethod _ => new MetaXRInspector(method),
                SteamVRControlMethod _ => new SteamVRInspector(method),
                UnityXRControlMethod _ => new UnityXRInspector(method),
                _ => throw new System.NotImplementedException()
            };
        #endregion
        
        
        #region PUBLIC
        public virtual void Draw()
        {
            if (Method.requiredAssetsNames.Any())
            {
                var str = "Project is missing Assets required to enable this Control Method:\n\n";
                str += Method.requiredAssetsNames.ToStringInline(true, "► ");
                str += "\n\n";
                str += "A version of CurvedUI that support previous versions of these Assets is available upon request.";
                EditorGUILayout.HelpBox(str, MessageType.Warning);

                if (Method.scriptingDefineSymbol != "")
                {
                    DrawForceEnableButton(Method.scriptingDefineSymbol);
                    DrawResetButtonAndInfo();
                }
            }
        }
        #endregion

        
        #region PRIVATE
        private void DrawResetButtonAndInfo()
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox("If you see compilation errors:", MessageType.None);
            if (GUILayout.Button("Reset", GUILayout.Width(100))) {
                DefineSymbolUtility.ClearCurvedUIDefineSymbols();
            }
            GUILayout.EndHorizontal();
        }
        
        private void DrawForceEnableButton(string define)
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox("If you're certain Project contains required Assets:", MessageType.None);
            if (GUILayout.Button( _isLoadingDefineSymbol ? "Loading..." : "Force Enable", GUILayout.Width(100)))
            {
                _isLoadingDefineSymbol = true;
                DefineSymbolUtility.AddDefineSymbolIfMissing(define);
            }
            GUILayout.EndHorizontal();
        }
        #endregion
    }
}
