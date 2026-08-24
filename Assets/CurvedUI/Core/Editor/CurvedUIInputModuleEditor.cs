using CurvedUI.Core;
using UnityEngine;
using UnityEditor;

namespace CurvedUI { 

	[CustomEditor(typeof(CurvedUIInputModule))]
	public class CurvedUIInputModuleEditor : Editor 
    {
        private bool _opened;
        
        public override void OnInspectorGUI()
		{
            EditorGUILayout.HelpBox($"Use {nameof(CurvedUISettings)} component on your Canvas to configure CurvedUI", MessageType.Info);
            
            if (_opened)
            {
                if (GUILayout.Button("Hide Fields"))
                    _opened = !_opened;

                DrawDefaultInspector();
            }
            else
            {
                if (GUILayout.Button("Show Fields"))
                    _opened = !_opened;
            }
       
            GUILayout.Space(20);
        }
	}
}
