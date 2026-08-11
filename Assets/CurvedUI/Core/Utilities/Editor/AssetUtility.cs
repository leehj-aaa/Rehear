using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CurvedUI.Core.Utilities.Editor
{
    public static class AssetUtility
    {
        public static T TryFindAssetOfType<T>(params string[] nameParts) where T : Object 
            => FindAssetsOfType<T>(nameParts).FirstOrDefault();

        public static IEnumerable<T> FindAssetsOfType<T>(params string[] nameParts) where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(x => x != null)
                .Where(x => nameParts == null || nameParts.Length == 0 || nameParts.All(x.name.Contains));

        public static IEnumerable<T> GetSubAssetsOfType<T>(Object mainAsset) where T : Object =>
            AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(mainAsset))
                .OfType<T>()
                .Where(x => x != null && (x.hideFlags & HideFlags.HideInHierarchy) == 0);
    }
}