using UnityEditor;
using UnityEngine;

namespace CurvedUI.Core.ControlMethods.Editor
{
    public class MouseInspector : ControlMethodInspector
    {
        public MouseInspector(CurvedUIControlMethod m) : base(m) { }
        
        public override void Draw()
        {
            if(Method is not MouseControlMethod settings) return;
            
            GUILayout.Label("Basic Controller. Mouse on screen", EditorStyles.helpBox);
        }
    }
}

