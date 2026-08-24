using UnityEditor;
using UnityEngine;

namespace CurvedUI.Core.ControlMethods.Editor
{
    public class GazeInspector : ControlMethodInspector
    {
        public GazeInspector(CurvedUIControlMethod m) : base(m) { }
        
        public override void Draw()
        {
            if(Method is not GazeControlMethod settings) return;
            
            GUILayout.Label("Center of Canvas's Event Camera acts as a pointer. Can be used with any headset. ", EditorStyles.helpBox);
            settings.GazeUseTimedClick = EditorGUILayout.Toggle("Use Timed Click", settings.GazeUseTimedClick);
            if (settings.GazeUseTimedClick)
            {
                GUILayout.Label("Clicks a button if player rests his gaze on it for a period of time. You can assign an image to be used as a progress bar.", EditorStyles.helpBox);
                settings.GazeClickTimer = EditorGUILayout.FloatField("Click Timer (seconds)", settings.GazeClickTimer);
                settings.GazeClickTimerDelay = EditorGUILayout.FloatField("Timer Start Delay", settings.GazeClickTimerDelay);
                settings.GazeTimedClickProgressImage = (UnityEngine.UI.Image)EditorGUILayout.ObjectField("Progress Image To FIll", settings.GazeTimedClickProgressImage, typeof(UnityEngine.UI.Image), true);
            }
        }
    }
}

