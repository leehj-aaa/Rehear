using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace CurvedUI.Core.Utilities.Editor
{
    public static class DefineSymbolUtility
    {
        #region PUBLIC
        public static void AddDefineSymbolIfMissing(params string[] definesToSet)
        {
            //todo: simpler way
            // var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup).Split(';').ToList();
            // if (!defines.Contains(k_EnableCCU, StringComparer.OrdinalIgnoreCase))
            // {
            //     defines.Add(k_EnableCCU);
            //     PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, string.Join(";", defines.ToArray()));
            
            var str = PlayerSettings.GetScriptingDefineSymbols(GetActiveNamedBuildTarget());

            foreach (var def in definesToSet)
            {
                //add this one, if not present.
                if (def != "" && !str.Contains(def)) str += ";" + def;
            }
            
            //Submit defines. This will cause recompilation
            PlayerSettings.SetScriptingDefineSymbols(GetActiveNamedBuildTarget(), str);
        }
        
        public static bool ContainsDefineSymbol(string defineSymbol)
        {
            var allDefinesString = PlayerSettings.GetScriptingDefineSymbols(GetActiveNamedBuildTarget());
            return allDefinesString.Contains(defineSymbol);
        }
        
        public static void ClearCurvedUIDefineSymbols() => RemoveDefineSymbolsStartingWith("CURVEDUI");
        
        public static void RemoveDefineSymbolsStartingWith(string defineSymbol)
        {
            var allDefinesString = PlayerSettings.GetScriptingDefineSymbols(GetActiveNamedBuildTarget());

            var definesToRemove = allDefinesString
                .Split(';')
                .Where(x => x.StartsWith(defineSymbol))
                .ToList();

            foreach (var define in definesToRemove)
            {
                //remove define from string. Remove the ; if it's not the first define.
                allDefinesString = allDefinesString.Replace(define, "");
                allDefinesString = allDefinesString.Replace(";;", ";");
            }
            
            //Submit defines. This will cause recompilation
            PlayerSettings.SetScriptingDefineSymbols(GetActiveNamedBuildTarget(), allDefinesString);
        }
        #endregion
        
        
        #region PRIVATE
        private static NamedBuildTarget GetActiveNamedBuildTarget()
        {
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            return NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
        }
        #endregion
    }
}