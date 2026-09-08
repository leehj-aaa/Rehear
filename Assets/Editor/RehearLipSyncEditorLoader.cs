#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

// Match Unity's Windows Mono import name to the installed SDK. Android uses
// its ARM64 PluginImporter library and does not compile this editor code.
[InitializeOnLoad]
internal static class RehearLipSyncEditorLoader
{
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void mono_dllmap_insert(IntPtr assembly,
        [MarshalAs(UnmanagedType.LPStr)] string library,
        [MarshalAs(UnmanagedType.LPStr)] string function,
        [MarshalAs(UnmanagedType.LPStr)] string targetLibrary,
        [MarshalAs(UnmanagedType.LPStr)] string targetFunction);

    static RehearLipSyncEditorLoader()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath,"Oculus/LipSync/Plugins/Win64/OVRLipSync.dll"));
        if (!File.Exists(path)) return;
        mono_dllmap_insert(IntPtr.Zero,"OVRLipSync",null,path,null);
        mono_dllmap_insert(IntPtr.Zero,"OVRLipSync.dll",null,path,null);
    }
}
#endif
