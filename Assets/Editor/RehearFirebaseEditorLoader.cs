#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity's Windows Mono resolver can miss Firebase's registered native plugins
/// despite compatible import settings. Bind the SDK names to the actual
/// project files before any Firebase static initializer runs. Editor only.
/// </summary>
[InitializeOnLoad]
internal static class RehearFirebaseEditorLoader
{
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mono_dllmap_insert(IntPtr assembly,
        [MarshalAs(UnmanagedType.LPStr)] string library,
        [MarshalAs(UnmanagedType.LPStr)] string function,
        [MarshalAs(UnmanagedType.LPStr)] string targetLibrary,
        [MarshalAs(UnmanagedType.LPStr)] string targetFunction);

    static RehearFirebaseEditorLoader()
    {
        try
        {
            string directory = Path.Combine(Application.dataPath, "Plugins/x86_64");
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "FirebaseCpp*.dll"))
            {
                string path = Path.GetFullPath(file);
                mono_dllmap_insert(IntPtr.Zero, Path.GetFileNameWithoutExtension(file), null, path, null);
                mono_dllmap_insert(IntPtr.Zero, Path.GetFileName(file), null, path, null);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[Rehear] Firebase Editor native library registration failed: " + exception);
        }
    }
}
#endif
