using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
[InitializeOnLoad] static class RehearBlendTests
{
 static readonly BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
 static RehearBlendTests(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/RehearBlend.request"))return;File.Delete("Temp/RehearBlend.request");try{Run();}catch(Exception e){File.WriteAllText("Temp/RehearBlendTests.txt",e.ToString());}}
 static void Run(){int count=0;foreach(var path in Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation","Aud_*.prefab")){
 var root=PrefabUtility.LoadPrefabContents(path.Replace('\\','/'));try{
 var p=root.GetComponentInChildren<AudienceAnimationPlayer>();typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",F).Invoke(p,null);
 var graph=(PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",F).GetValue(p);graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
 var mixer=(AnimationMixerPlayable)typeof(AudienceAnimationPlayer).GetField("mixer",F).GetValue(p);
 var step=typeof(AudienceAnimationPlayer).GetMethod("Advance",F);
 Action<float> advance=d=>{step.Invoke(p,new object[]{d});graph.Evaluate(d);};
 Func<float[]> weights=()=>Enumerable.Range(0,mixer.GetInputCount()).Select(i=>mixer.GetInput(i).IsValid()?mixer.GetInputWeight(i):0).ToArray();
 Action<string> command=id=>{graph.Evaluate(0);var bones=root.GetComponentsInChildren<Transform>();var rotations=bones.Select(t=>t.localRotation).ToArray();var before=weights();if(!p.PlayServerVariation(id,4,1))throw new Exception("Command rejected");graph.Evaluate(0);for(int i=0;i<before.Length;i++)if(Mathf.Abs(before[i]-weights()[i])>.00001f)throw new Exception("Weight discontinuity");for(int i=0;i<bones.Length;i++)if(Quaternion.Angle(rotations[i],bones[i].localRotation)>.1f)throw new Exception("Pose jumped at command arrival");};
 command("ACT_02.photoslide");advance(.3f);command("ACT_04.drowsynod");advance(.2f);command("ACT_05.seatadjust");
 for(int i=0;i<10;i++){advance(.1f);if(Mathf.Abs(weights().Sum()-1)>.0001f)throw new Exception("Weights not normalized: "+string.Join(",",weights()));}
 var current=mixer.GetInput(Enumerable.Range(1,mixer.GetInputCount()-1).First(i=>mixer.GetInput(i).IsValid() && mixer.GetInputWeight(i)>.5f));var time=current.GetTime();command("ACT_05.seatadjust");if(Math.Abs(current.GetTime()-time)>.001)throw new Exception("Same command restarted clip");
 p.StopAction();for(int i=0;i<12;i++)advance(.1f);if(p.IsBusy||Math.Abs(mixer.GetInputWeight(0)-1)>.001)throw new Exception("Stop did not blend to idle");
 command("ACT_02.photoslide");for(int i=0;i<60;i++)advance(.1f);if(p.IsBusy)throw new Exception("Duration expiry failed");var phone=(GameObject)typeof(AudienceAnimationPlayer).GetField("photoPhone",F).GetValue(p);if(phone.activeSelf)throw new Exception("Phone left visible");count++;
 }finally{PrefabUtility.UnloadPrefabContents(root);}}
 File.WriteAllText("Temp/RehearBlendTests.txt",$"PASS {count} actors: pose/weight continuity during A->B->C interruption, normalized weights, same-action time preserved, smooth stop, duration expiry and phone cleanup.");
 RunCoreChecks();}

 static void RunCoreChecks()
 {
 var log = new System.Text.StringBuilder();
 var catalog = AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
 foreach (var entry in catalog.Entries)
     foreach(var clip in new[]{entry.maleClip,entry.femaleClip}.Where(c=>c))
         log.AppendLine($"{entry.variationId} {clip.name}: length={clip.length:F2}, looping={clip.isLooping}");
 foreach(var path in Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation","Aud_*.prefab"))
 {
 var root=PrefabUtility.LoadPrefabContents(path.Replace('\\','/'));
 try {
 var p=root.GetComponentInChildren<AudienceAnimationPlayer>();
 typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",F).Invoke(p,null);
 var graph=(PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",F).GetValue(p);
 graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
 var mixer=(AnimationMixerPlayable)typeof(AudienceAnimationPlayer).GetField("mixer",F).GetValue(p);
 var step=typeof(AudienceAnimationPlayer).GetMethod("Advance",F);
 Action<float> advance=d=>{step.Invoke(p,new object[]{d});graph.Evaluate(d);};
 Action<string,float> play=(id,d)=>{if(!p.PlayServerVariation(id,d,1))throw new Exception("Rejected "+id);};
 play("AL_01.stable_attention",2);
 for(int i=0;i<120;i++) { if(i%3==0)play("AL_01.stable_attention",2); advance(1f/60); }
 if((bool)typeof(AudienceAnimationPlayer).GetField("transitioning",F).GetValue(p))throw new Exception("Same-target refresh stalled blending");
 var core=typeof(AudienceAnimationPlayer).GetField("core",F).GetValue(p);
 var coreType=core.GetType();
 int port=(int)coreType.GetField("port").GetValue(core);
 for(int i=0;i<180;i++)advance(1f/60);
 if(mixer.GetInputWeight(port)<.999f)throw new Exception("Listening core expired into default idle");
 play("ACT_02.photoslide",1);
 for(int i=0;i<180;i++)advance(1f/60);
 if(mixer.GetInputWeight(port)<.999f)throw new Exception("Action failed to return to evaluated core");
 play("ACT_04.drowsynod",20);advance(.2f);play("CT_01.stable_comprehension",4);
 for(int i=0;i<60;i++)advance(1f/60);
 var target=typeof(AudienceAnimationPlayer).GetField("target",F).GetValue(p);
 if((string)target.GetType().GetField("variation").GetValue(target)!="CT_01.stable_comprehension")throw new Exception("New evaluation failed to interrupt action");
 // Loop flags are imported source data: long requests must never freeze looping inputs.
 var entry=catalog.Entries.FirstOrDefault(e=>e.variationId.StartsWith("BL_") && (p.Gender==AudienceGender.Male?e.maleClip:e.femaleClip).isLooping);
 if(entry!=null) {
 play(entry.variationId,60);var loop=typeof(AudienceAnimationPlayer).GetField("target",F).GetValue(p);
 var clip=(AnimationClip)loop.GetType().GetField("clip").GetValue(loop);
 for(int i=0;i<(clip.length+1)*60;i++)advance(1f/60);
 var playable=(UnityEngine.Animations.AnimationClipPlayable)loop.GetType().GetField("playable").GetValue(loop);
 if(playable.GetSpeed()==0)throw new Exception("Looping core frozen at clip end");
 }
 log.AppendLine("PASS "+root.name+": repeated refresh, persistent core, action return, active interruption, imported loop playback");
 } finally {PrefabUtility.UnloadPrefabContents(root);}
 }
 File.WriteAllText("Temp/RehearCoreBlendTests.txt",log.ToString());
 }
}


