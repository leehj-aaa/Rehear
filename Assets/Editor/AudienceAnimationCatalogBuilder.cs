#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AudienceAnimationCatalogBuilder
{
    private const string ClipsRoot =
        "Assets/06_Animation/Clips";

    private const string MaleFolder =
        ClipsRoot + "/Male";

    private const string FemaleFolder =
        ClipsRoot + "/Female";

    private const string CatalogFolder =
        "Assets/Settings";

    private const string CatalogPath =
        CatalogFolder +
        "/AudienceAnimationCatalog.asset";

    [MenuItem(
        "Rehear/AI/Rebuild Audience Animation Catalog"
    )]
    public static void RebuildCatalog()
    {
        EnsureCatalogFolderExists();

        AudienceAnimationCatalog catalog =
            AssetDatabase.LoadAssetAtPath<
                AudienceAnimationCatalog
            >(CatalogPath);

        if (catalog == null)
        {
            catalog =
                ScriptableObject.CreateInstance<
                    AudienceAnimationCatalog
                >();

            AssetDatabase.CreateAsset(
                catalog,
                CatalogPath
            );
        }

        Dictionary<
            string,
            AudienceAnimationCatalog.Entry
        > entries =
            new Dictionary<
                string,
                AudienceAnimationCatalog.Entry
            >(
                StringComparer.OrdinalIgnoreCase
            );

        AddGenderFolder(
            MaleFolder,
            AudienceGender.Male,
            entries
        );

        AddGenderFolder(
            FemaleFolder,
            AudienceGender.Female,
            entries
        );

        AddSharedActClips(entries);

        List<AudienceAnimationCatalog.Entry>
            sortedEntries =
                entries.Values
                    .OrderBy(
                        entry =>
                            entry.variationId
                    )
                    .ToList();

        catalog.EditorSetEntries(
            sortedEntries
        );

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int maleCount =
            sortedEntries.Count(
                entry =>
                    entry.maleClip != null
            );

        int femaleCount =
            sortedEntries.Count(
                entry =>
                    entry.femaleClip != null
            );

        Debug.Log(
            "[Animation Catalog] 생성 완료" +
            "\n전체 Variation: " +
            sortedEntries.Count +
            "\nMale Clip: " +
            maleCount +
            "\nFemale Clip: " +
            femaleCount +
            "\n경로: " +
            CatalogPath
        );

        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
    }

    private static void AddGenderFolder(
        string folder,
        AudienceGender gender,
        Dictionary<
            string,
            AudienceAnimationCatalog.Entry
        > entries)
    {
        string[] guids =
            AssetDatabase.FindAssets(
                "t:Model",
                new[] { folder }
            );

        foreach (string guid in guids)
        {
            string assetPath =
                AssetDatabase.GUIDToAssetPath(
                    guid
                );

            string normalizedPath =
                assetPath.Replace("\\", "/");

            // 백업 폴더의 중복 클립은 제외한다.
            if (
                normalizedPath.Contains(
                    "/_backup_before_edit/",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                continue;
            }

            // 요청한 성별 폴더 아래의 FBX만 처리한다.
            if (!normalizedPath.EndsWith(
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName =
                Path.GetFileNameWithoutExtension(
                    normalizedPath
                );

            string variationId =
                CreateVariationId(
                    fileName
                );

            AnimationClip clip =
                LoadMainAnimationClip(
                    normalizedPath
                );

            if (clip == null ||
                string.IsNullOrWhiteSpace(
                    variationId))
            {
                continue;
            }

            AudienceAnimationCatalog.Entry entry =
                GetOrCreateEntry(
                    variationId,
                    entries
                );

            if (gender ==
                AudienceGender.Male)
            {
                entry.maleClip = clip;
            }
            else
            {
                entry.femaleClip = clip;
            }
        }
    }

    private static void AddSharedActClips(
        Dictionary<
            string,
            AudienceAnimationCatalog.Entry
        > entries)
    {
        string[] guids =
            AssetDatabase.FindAssets(
                "t:Model",
                new[] { ClipsRoot }
            );

        foreach (string guid in guids)
        {
            string assetPath =
                AssetDatabase.GUIDToAssetPath(
                    guid
                ).Replace("\\", "/");

            if (!assetPath.EndsWith(
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string parentFolder =
                Path.GetDirectoryName(
                    assetPath
                )?.Replace("\\", "/");

            // Male/Female 하위 폴더가 아닌
            // Clips 루트의 ACT 파일만 사용한다.
            if (!string.Equals(
                    parentFolder,
                    ClipsRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName =
                Path.GetFileNameWithoutExtension(
                    assetPath
                );

            if (!fileName.StartsWith(
                    "ACT_",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string variationId =
                CreateVariationId(
                    fileName
                );

            AnimationClip clip =
                LoadMainAnimationClip(
                    assetPath
                );

            if (clip == null ||
                string.IsNullOrWhiteSpace(
                    variationId))
            {
                continue;
            }

            AudienceAnimationCatalog.Entry entry =
                GetOrCreateEntry(
                    variationId,
                    entries
                );

            // Explicit root-folder gender variants override the shared fallback,
            // regardless of AssetDatabase enumeration order.
            if (fileName.EndsWith("_F", StringComparison.OrdinalIgnoreCase))
                entry.femaleClip = clip;
            else if (fileName.EndsWith("_M", StringComparison.OrdinalIgnoreCase))
                entry.maleClip = clip;
            else
            {
                if (entry.maleClip == null) entry.maleClip = clip;
                if (entry.femaleClip == null) entry.femaleClip = clip;
            }
        }
    }

    private static AudienceAnimationCatalog.Entry
        GetOrCreateEntry(
            string variationId,
            Dictionary<
                string,
                AudienceAnimationCatalog.Entry
            > entries)
    {
        if (entries.TryGetValue(
                variationId,
                out AudienceAnimationCatalog.Entry
                    existing))
        {
            return existing;
        }

        AudienceAnimationCatalog.Entry entry =
            new AudienceAnimationCatalog.Entry
            {
                variationId = variationId
            };

        entries.Add(
            variationId,
            entry
        );

        return entry;
    }

    private static AnimationClip LoadMainAnimationClip(
        string assetPath)
    {
        UnityEngine.Object[] assets =
            AssetDatabase.LoadAllAssetsAtPath(
                assetPath
            );

        AnimationClip clip =
            assets
                .OfType<AnimationClip>()
                .FirstOrDefault(
                    candidate =>
                        !candidate.name.StartsWith(
                            "__preview__",
                            StringComparison.OrdinalIgnoreCase
                        )
                );

        if (clip == null)
        {
            Debug.LogWarning(
                "[Animation Catalog] AnimationClip을 " +
                "찾지 못했습니다." +
                "\n" + assetPath
            );
        }

        return clip;
    }

    private static string CreateVariationId(
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            return "";
        }

        string normalized =
            fileName
                .Trim()
                .Replace(" ", "_")
                .Replace("-", "_");

        // 성별 접미사 제거
        normalized =
            RemoveEnding(
                normalized,
                "_M"
            );

        normalized =
            RemoveEnding(
                normalized,
                "_F"
            );

        // Keep ACT_08 L/R as distinct local clips. The server's common
        // ACT_08.side_conversation ID is resolved using the occupied seat.

        string[] parts =
            normalized.Split(
                new[] { '_' },
                StringSplitOptions.RemoveEmptyEntries
            );

        if (parts.Length < 3)
            return "";

        string behaviorId =
            (
                parts[0] +
                "_" +
                parts[1]
            ).ToUpperInvariant();

        string variationName =
            string.Join(
                "_",
                parts.Skip(2)
            ).ToLowerInvariant();

        return behaviorId +
               "." +
               variationName;
    }

    private static string RemoveEnding(
        string value,
        string ending)
    {
        if (value.EndsWith(
                ending,
                StringComparison.OrdinalIgnoreCase))
        {
            return value.Substring(
                0,
                value.Length - ending.Length
            );
        }

        return value;
    }

    private static void EnsureCatalogFolderExists()
    {
        if (AssetDatabase.IsValidFolder(
                CatalogFolder))
        {
            return;
        }

        AssetDatabase.CreateFolder(
            "Assets",
            "Settings"
        );
    }
}

#endif
