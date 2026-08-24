using CurvedUI.Core.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace CurvedUI.Core.ControlMethods.Editor
{
    public class CustomRayInspector : ControlMethodInspector
    {
        public CustomRayInspector(CurvedUIControlMethod m) : base(m) { }
        
        public override void Draw()
        {
            if(Method is not CustomRayControlMethod settings) return;
            
            GUILayout.Label($"Set a ray used to interact with the canvas. " +
                            $"Assign ray to {nameof(CurvedUIInputModule.CustomRay)} and the button pressed state to {nameof(CurvedUIInputModule.CustomRayButtonState)}. " +
                            $"Find both in {nameof(CurvedUIInputModule)} class", EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("View Code Snippet")) 
                DocsUtility.OpenDocs(DocsUtility.Bookmark.CustomRay);
            GUILayout.EndHorizontal();
        }
    }
}

