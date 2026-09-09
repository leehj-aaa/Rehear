using System;
using System.Collections.Generic;
using UnityEngine;

public enum AudienceGender
{
    Male,
    Female
}

[CreateAssetMenu(
    fileName = "AudienceAnimationCatalog",
    menuName = "Rehear/AI/Audience Animation Catalog"
)]
public class AudienceAnimationCatalog :
    ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string variationId;
        public AnimationClip maleClip;
        public AnimationClip femaleClip;
    }

    [SerializeField]
    private List<Entry> entries =
        new List<Entry>();

    private Dictionary<string, Entry>
        entryMap;

    public IReadOnlyList<Entry> Entries =>
        entries;

    private void OnEnable()
    {
        BuildRuntimeMap();
    }

    public bool TryGetClip(
        string variationId,
        AudienceGender gender,
        out AnimationClip clip)
    {
        return TryResolveClip(variationId, gender, out clip, out _);
    }

    public bool TryResolveClip(string variationId, AudienceGender gender,
        out AnimationClip clip, out string canonicalId)
    {
        clip = null;
        canonicalId = null;

        if (string.IsNullOrWhiteSpace(
                variationId))
        {
            return false;
        }

        if (entryMap == null)
            BuildRuntimeMap();

        string key =
            NormalizeKey(variationId);

        if (!entryMap.TryGetValue(
                key,
                out Entry entry))
        {
            return false;
        }

        clip =
            gender == AudienceGender.Male
                ? entry.maleClip
                : entry.femaleClip;

        canonicalId = entry.variationId;
        return clip != null;
    }

    private void BuildRuntimeMap()
    {
        entryMap =
            new Dictionary<string, Entry>();

        if (entries == null)
            return;

        foreach (Entry entry in entries)
        {
            if (entry == null ||
                string.IsNullOrWhiteSpace(
                    entry.variationId))
            {
                continue;
            }

            string key =
                NormalizeKey(
                    entry.variationId
                );

            entryMap[key] = entry;
        }
    }

    private string NormalizeKey(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty);
    }

#if UNITY_EDITOR
    public void EditorSetEntries(
        List<Entry> newEntries)
    {
        entries =
            newEntries ??
            new List<Entry>();

        BuildRuntimeMap();
    }
#endif
}
