#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Kamgam.UGUIBlurredBackground
{
    public static class EditorUtils
    {
        static System.Type s_gameViewType;

        [InitializeOnLoadMethod]
        static void init()
        {
            System.Reflection.Assembly assembly = typeof(UnityEditor.EditorWindow).Assembly;
            s_gameViewType = assembly.GetType("UnityEditor.GameView");
        }
        
        public static EditorWindow GetGameView()
        {
            if(s_gameViewType != null)
            {
                return EditorWindow.GetWindow(s_gameViewType);
            }

            return null;
        }

        public static void RefreshGameView()
        {
            var gameView = GetGameView();
            if (gameView != null)
            {
                gameView.Repaint();
            }
        }

        public static bool WasDestroyedWhileEditing(Component comp)
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                && comp != null
                && comp.gameObject != null
                && comp.gameObject.scene.isLoaded
                && comp.gameObject.scene.IsValid();
        }
    }
}
#endif