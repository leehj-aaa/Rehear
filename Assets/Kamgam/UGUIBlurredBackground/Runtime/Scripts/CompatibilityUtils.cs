using System.Collections.Generic;
using UnityEngine;

namespace Kamgam.UGUIBlurredBackground
{
    public static class CompatibilityUtils
    {
        public static T FindObjectOfType<T>(bool includeInactive = false) where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
#if UNITY_6000_5_OR_NEWER
            return Object.FindAnyObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
            return GameObject.FindFirstObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#endif
#else
            return GameObject.FindObjectOfType<T>(includeInactive);
#endif
        }
        
        public static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            var include = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType<T>(include);
#else
            return GameObject.FindObjectsByType<T>(include, FindObjectsSortMode.None);
#endif
#else
            return GameObject.FindObjectsOfType<T>(includeInactive);
#endif
        }

        public static IEnumerable<System.Reflection.Assembly> GetAssemblies()
        {
#if UNITY_6000_6_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
#else
            return System.AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

#if UNITY_EDITOR
        public static void ImportPackage(string path, bool interactive)
        {
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Import(path, interactive);
#else
            UnityEditor.AssetDatabase.ImportPackage(path, interactive);
#endif
        }
#endif
    }
}