using System;
using UnityEditor;

// VIVE's implicit layers can replace Quest system properties with an SRanipal
// system that has no display. Limit the workaround to this Unity editor process.
[InitializeOnLoad]
internal static class RehearQuestLinkEnvironment
{
    static RehearQuestLinkEnvironment()
    {
#if UNITY_EDITOR_WIN
        Environment.SetEnvironmentVariable("DISABLE_XR_APILAYER_VIVE_HAND_TRACKING_1", "1");
        Environment.SetEnvironmentVariable("DISABLE_XR_APILAYER_VIVE_FACIAL_TRACKING_1", "1");
        Environment.SetEnvironmentVariable("DISABLE_XR_APILAYER_VIVE_SRWORKS_1", "1");
        Environment.SetEnvironmentVariable("DISABLE_XR_APILAYER_VIVE_XRTRACKER_1", "1");
#endif
    }
}
