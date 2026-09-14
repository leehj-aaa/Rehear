using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using System.Reflection;

[InitializeOnLoad]
static class RehearSeatedClipAudit
{
    static RehearSeatedClipAudit() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/RehearSeatedClipAudit.request")) return;
        File.Delete("Temp/RehearSeatedClipAudit.request");
        try { Run(); } catch(Exception e) { File.WriteAllText("Temp/RehearSeatedClipAudit.txt",e.ToString()); }
    }
    static void Run()
    {
        var report=new StringBuilder();
        var catalog=AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
        var instance=PrefabUtility.LoadPrefabContents("Assets/03_Prefabs/Audience/Presentation/Aud_M_01.prefab");
        try {
            var player=instance.GetComponentInChildren<AudienceAnimationPlayer>(); player.enabled=false;
            var animator=instance.GetComponentInChildren<Animator>(); animator.enabled=false;
            var bones=instance.GetComponentsInChildren<Transform>(true);
            var pos=bones.Select(b=>b.localPosition).ToArray(); var rot=bones.Select(b=>b.localRotation).ToArray();
            var hip=bones.Single(b=>b.name=="pelvis"); var head=bones.Single(b=>b.name=="head");
            var foot=bones.Single(b=>b.name=="foot_r"); var rig=bones.Single(b=>b.name=="root");
            foreach(var entry in catalog.Entries) {
                var clip=entry.maleClip; if(!clip)continue;
                report.AppendLine($"CLIP {entry.variationId} {AssetDatabase.GetAssetPath(clip)} {clip.length:F2}s");
                foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path=="root" || b.path=="")) {
                    var curve=AnimationUtility.GetEditorCurve(clip,binding);
                    report.AppendLine($"  CURVE {binding.path} {binding.propertyName} {curve.Evaluate(0):F4} -> {curve.Evaluate(clip.length*.5f):F4}");
                }
                foreach(float t in new[]{0f,.25f,.5f,.75f,.99f}) {
                    for(int i=0;i<bones.Length;i++){bones[i].localPosition=pos[i];bones[i].localRotation=rot[i];}
                    clip.SampleAnimation(animator.gameObject,t*clip.length);
                    report.AppendLine($"  t={t:F2} rigRot={rig.localEulerAngles:F1} hip={instance.transform.InverseTransformPoint(hip.position):F3} head={instance.transform.InverseTransformPoint(head.position):F3} foot={instance.transform.InverseTransformPoint(foot.position):F3}");
                }
            }
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=pos[i];bones[i].localRotation=rot[i];}
            player.enabled=true; animator.enabled=true;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",flags).Invoke(player,null);
            var graph=(PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",flags).GetValue(player);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var advance=typeof(AudienceAnimationPlayer).GetMethod("Advance",flags);
            foreach(var id in new[]{"BL_01.neutral_listening","ACT_03.device_checking","ACT_04.drowsy_nod","ACT_05.seat_adjust","ACT_06.small_stretch","CT_08.collapsed_posture_confusion"}) {
                player.PlayServerVariation(id,4,.6f);
                for(int i=0;i<100;i++) {
                    advance.Invoke(player,new object[]{.05f});graph.Evaluate(.05f);
                    if(i%5==0)report.AppendLine($"MIX {id} t={i*.05f:F2} rig={rig.localEulerAngles:F1} hip={instance.transform.InverseTransformPoint(hip.position):F3} head={instance.transform.InverseTransformPoint(head.position):F3} foot={instance.transform.InverseTransformPoint(foot.position):F3}");
                }
            }
        } finally { PrefabUtility.UnloadPrefabContents(instance); }
        File.WriteAllText("Temp/RehearSeatedClipAudit.txt",report.ToString());
        InspectPhone();
        ReplayTrace();
    }
    static void InspectPhone()
    {
        var source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/06_Animation/Clips/ACT_03_DeviceChecking.fbx"));
        try {
            var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/06_Animation/Clips/ACT_03_DeviceChecking.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
            clip.SampleAnimation(source,clip.length*.5f);
            var hand=source.GetComponentsInChildren<Transform>().Single(t=>t.name=="hand_r");
            var phone=source.GetComponentsInChildren<Transform>().Single(t=>t.name=="Phone");
            var native=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/03_Prefabs/phone.prefab").GetComponent<MeshFilter>().sharedMesh;
            File.WriteAllText("Temp/RehearPhoneGeometry.txt",$"sourcePhone={phone.position:F4} rotation={phone.eulerAngles:F2} scale={phone.lossyScale:F4}\nhandRelativePosition={hand.InverseTransformPoint(phone.position):F4} rotation={(Quaternion.Inverse(hand.rotation)*phone.rotation).eulerAngles:F2}\nnativeBounds={native.bounds}");
        } finally {UnityEngine.Object.DestroyImmediate(source);}
    }
    static void ReplayTrace()
    {
        var trace=JsonUtility.FromJson<RehearAudienceMotionPreview.Trace>(File.ReadAllText("Temp/AudienceReactionPreview.json"));
        var report=new StringBuilder();
        var names=new[]{"Aud_W_01","Aud_M_01","Aud_W_03","Aud_M_02","Aud_W_02","Aud_M_03"};
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        for(int n=0;n<names.Length;n++) {
            var root=PrefabUtility.LoadPrefabContents($"Assets/03_Prefabs/Audience/Presentation/{names[n]}.prefab");
            try {
                var p=root.GetComponentInChildren<AudienceAnimationPlayer>();
                typeof(AudienceAnimationPlayer).GetField("printAnimationLog",flags).SetValue(p,false);
                typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",flags).Invoke(p,null);
                var graph=(PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",flags).GetValue(p);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var advance=typeof(AudienceAnimationPlayer).GetMethod("Advance",flags);
                var b=root.GetComponentsInChildren<Transform>();var hip=b.First(t=>t.name=="pelvis");var head=b.First(t=>t.name=="head");var foot=b.First(t=>t.name=="foot_r");
                int cursor=0,bad=0; string last="idle";float maxFoot=-100,minHeight=100;
                for(int tick=0;tick<120*60;tick++) {
                    float time=tick/60f;
                    while(cursor<trace.frames.Length && trace.frames[cursor].time<=time) {
                        foreach(var c in trace.frames[cursor++].response.commands)
                            if(c.agent_id==trace.audiences[n].agent_id && c.layer=="Body") {
                                p.PlayServerVariation(c.selected_variation_id,c.duration,c.intensity);last=c.selected_variation_id;
                            }
                    }
                    advance.Invoke(p,new object[]{1f/60});graph.Evaluate(1f/60);
                    var h=root.transform.InverseTransformPoint(hip.position);var hd=root.transform.InverseTransformPoint(head.position);var f=root.transform.InverseTransformPoint(foot.position);
                    maxFoot=Mathf.Max(maxFoot,f.y);minHeight=Mathf.Min(minHeight,hd.y-h.y);
                    if(hd.y-h.y<.4f || f.y>.3f) { if(bad++%60==0)report.AppendLine($"BAD {names[n]} {time:F2} {last} hip={h:F3} head={hd:F3} foot={f:F3}"); }
                }
                report.AppendLine($"RESULT {names[n]} badFrames={bad} minTorsoHeight={minHeight:F3} maxFoot={maxFoot:F3}");
            } finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        File.WriteAllText("Temp/RehearSeatedTraceAudit.txt",report.ToString());
    }
}
