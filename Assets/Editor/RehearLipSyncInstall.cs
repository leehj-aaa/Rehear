using System;
using System.IO;
using System.Linq;
using UnityEditor;
[InitializeOnLoad] static class RehearLipSyncInstall
{
 static RehearLipSyncInstall(){if(File.Exists("Library/RehearLipSyncRestart.pending")){File.Delete("Library/RehearLipSyncRestart.pending");Directory.CreateDirectory("Temp");File.WriteAllText("Temp/RehearLipSync.request","validate");}EditorApplication.update+=Poll;}
 [System.Runtime.InteropServices.DllImport("kernel32",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibrary(string path);
 static void Validate()
 {
 if(LoadLibrary(System.IO.Path.GetFullPath("Assets/Oculus/LipSync/Plugins/Win64/OVRLipSync.dll"))==IntPtr.Zero)throw new Exception("Native load failed: "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());
 uint context=0;
 bool initializedHere=OVRLipSync.IsInitialized()!=OVRLipSync.Result.Success;
 try{
 var init=initializedHere?OVRLipSync.Initialize():OVRLipSync.Result.Success;
 if(init!=OVRLipSync.Result.Success)throw new Exception("Initialize: "+init);
 var result=OVRLipSync.CreateContext(ref context,OVRLipSync.ContextProviders.Enhanced);
 if(result!=OVRLipSync.Result.Success)throw new Exception("CreateContext: "+result);
 var frame=new OVRLipSync.Frame();
 result=OVRLipSync.ProcessFrame(context,new float[1024],frame,false);
 if(result!=OVRLipSync.Result.Success)throw new Exception("ProcessFrame: "+result);
 var android=(PluginImporter)AssetImporter.GetAtPath("Assets/Oculus/LipSync/Plugins/Android64/libOVRLipSync.so");
 if(!android.GetCompatibleWithPlatform(BuildTarget.Android))throw new Exception("Android plugin disabled");
 File.WriteAllText("Temp/RehearLipSyncInstall.txt","PASS Oculus LipSync 29.0.0 imported and compiled. Windows native Initialize/CreateContext/ProcessFrame succeeded. Android64 plugin enabled. Visemes="+frame.Visemes.Length);
 }finally{if(context!=0)OVRLipSync.DestroyContext(context);if(initializedHere && OVRLipSync.IsInitialized()==OVRLipSync.Result.Success)OVRLipSync.Shutdown();}
 }
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/RehearLipSync.request"))return;
 var command=File.ReadAllText("Temp/RehearLipSync.request").Trim();File.Delete("Temp/RehearLipSync.request");
 try{if(command=="install")AssetDatabase.ImportPackage("Temp/LipSync29/OVRLipSyncUnityPlugin/UnityPlugin/OculusLipSync.unitypackage",false);
 else if(command=="restart"){
 UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
 EditorApplication.update-=Poll;
 File.WriteAllText("Temp/RehearLipSync.request","validate");
 EditorApplication.OpenProject(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath));
 }
 else if(command=="configure"){
 foreach(var path in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/Oculus/LipSync/Plugins/"))){
 var plugin=AssetImporter.GetAtPath(path) as PluginImporter;if(plugin==null)continue;
 plugin.SetCompatibleWithAnyPlatform(false);
 bool win=path.EndsWith("Win64/OVRLipSync.dll");
 plugin.SetCompatibleWithEditor(win);
 if(win){plugin.SetEditorData("CPU","x86_64");plugin.SetEditorData("OS","Windows");plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64,true);plugin.SetPlatformData(BuildTarget.StandaloneWindows64,"CPU","x86_64");}
 if(path.Contains("Android64/")){plugin.SetCompatibleWithPlatform(BuildTarget.Android,true);plugin.SetPlatformData(BuildTarget.Android,"CPU","ARM64");}
 plugin.SaveAndReimport();
 }
 File.WriteAllText("Temp/RehearLipSyncInstall.txt","Native platform import settings updated.");
 }
 else {var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("OVRLipSync")).FirstOrDefault(t=>t!=null);if(type==null)throw new Exception("OVRLipSync type missing");Validate();}}
 catch(Exception e){File.WriteAllText("Temp/RehearLipSyncInstall.txt",e.ToString());}
 }
}





