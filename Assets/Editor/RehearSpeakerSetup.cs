using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad] static class RehearSpeakerSetup
{
 static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static RehearSpeakerSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/RehearSpeaker.request"))return;File.Delete("Temp/RehearSpeaker.request");try{Run();}catch(Exception e){File.WriteAllText("Temp/RehearSpeakerTests.txt",e.ToString());}}
 static void Run(){int count=0;foreach(var path in Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation","Aud_*.prefab")){
 var p=path.Replace('\\','/');var root=PrefabUtility.LoadPrefabContents(p);try{
 var speaker=root.GetComponent<AudienceQuestionSpeaker>();if(!speaker)speaker=root.AddComponent<AudienceQuestionSpeaker>();PrefabUtility.SaveAsPrefabAsset(root,p);
 typeof(AudienceQuestionSpeaker).GetMethod("Initialize",F).Invoke(speaker,null);
 if(!speaker.Voice||speaker.Voice.transform.parent.name!="head")throw new Exception("Head source missing");
 var context=speaker.Voice.GetComponent<OVRLipSyncContext>();if(!context.audioLoopback)throw new Exception("Voice muted");
 var mouth=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(m=>m.sharedMesh&&Enumerable.Range(0,m.sharedMesh.blendShapeCount).Any(i=>m.sharedMesh.GetBlendShapeName(i).ToLowerInvariant().EndsWith("jawopen")));
 int jaw=Enumerable.Range(0,mouth.sharedMesh.blendShapeCount).First(i=>mouth.sharedMesh.GetBlendShapeName(i).ToLowerInvariant().EndsWith("jawopen"));
 var v=new float[15];v[10]=1;typeof(AudienceQuestionSpeaker).GetMethod("ApplyVisemes",F).Invoke(speaker,new object[]{v,.1f});if(mouth.GetBlendShapeWeight(jaw)<50)throw new Exception("Viseme did not drive jaw");
 typeof(AudienceQuestionSpeaker).GetField("driving",F).SetValue(speaker,true);speaker.StopSpeech();if(mouth.GetBlendShapeWeight(jaw)!=0)throw new Exception("Mouth did not reset");count++;
 }finally{PrefabUtility.UnloadPrefabContents(root);}}
 File.WriteAllText("Temp/RehearSpeakerTests.txt",$"PASS {count} audience prefabs: head AudioSource, audible OVR context, ARKit viseme mapping, stop resets mouth. TTS backend not configured.");}
}
