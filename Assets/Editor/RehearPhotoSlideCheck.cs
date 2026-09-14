using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using Object=UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearPhotoSlideCheck
{
    const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
    static RehearPhotoSlideCheck(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string request="Temp/RehearPhotoSlideCheck.request";
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(request))return;
        File.Delete(request);
        try{Run();}catch(Exception e){File.WriteAllText("Temp/RehearPhotoSlideCheck.txt",e.ToString());}
    }
    static void Run()
    {
        typeof(RehearQuestionFlowPreview).GetMethod("Stop",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        var catalog=AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
        var shared=new Dictionary<string,AudienceAnimationCatalog.Entry>(StringComparer.OrdinalIgnoreCase);
        typeof(AudienceAnimationCatalogBuilder).GetMethod("AddSharedActClips",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{shared});
        var replacement=shared["ACT_06.smallstretch"];
        var entries=catalog.Entries.Select(e=>e.variationId==replacement.variationId?replacement:e).ToList();
        catalog.EditorSetEntries(entries);EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        var report=new StringBuilder();
        if(replacement.maleClip==replacement.femaleClip || !AssetDatabase.GetAssetPath(replacement.femaleClip).EndsWith("SmallStretch_F.fbx"))throw new Exception("Gender mapping failed.");
        report.AppendLine("SmallStretch male="+AssetDatabase.GetAssetPath(replacement.maleClip)+" female="+AssetDatabase.GetAssetPath(replacement.femaleClip));
        foreach(var path in Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation","Aud_*.prefab"))
        {
            var root=PrefabUtility.LoadPrefabContents(path.Replace('\\','/'));
            root.name=Path.GetFileNameWithoutExtension(path);
            try{
                var body=root.GetComponentInChildren<AudienceAnimationPlayer>();
                catalog.TryGetClip("ACT_06.smallstretch",body.Gender,out var stretch);
                if(stretch!=(body.Gender==AudienceGender.Female?replacement.femaleClip:replacement.maleClip))throw new Exception("Wrong gender: "+root.name);
                report.AppendLine("PASS gender "+root.name+" "+stretch.name);
                var phone=(GameObject)typeof(AudienceAnimationPlayer).GetField("photoPhone",F).GetValue(body);
                catalog.TryGetClip("ACT_02.photoslide",body.Gender,out var clip);
                typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",F).Invoke(body,null);
                var graph=(PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",F).GetValue(body);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>()){skin.updateWhenOffscreen=true;skin.forceMatrixRecalculationPerRender=true;}
                body.PlayServerVariation("ACT_02.photoslide",clip.length,1);
                var bones=root.GetComponentsInChildren<Transform>();
                var left=bones.Single(t=>t.name=="middle_02_l");var right=bones.Single(t=>t.name=="middle_02_r");
                float time=0;
                for(int step=1;step<=3;step++){
                    float until=clip.length*step/4;
                    while(time<until){float dt=Mathf.Min(1f/60,until-time);typeof(AudienceAnimationPlayer).GetMethod("Advance",F).Invoke(body,new object[]{dt});graph.Evaluate(dt);body.ApplyPropPoses();time+=dt;}
                    report.AppendLine($"PHOTO {root.name} t={time:F2}/{clip.length:F2} active={phone.activeSelf} parent={phone.transform.parent.name} left={phone.transform.InverseTransformPoint(left.position):F3} right={phone.transform.InverseTransformPoint(right.position):F3} scale={phone.transform.lossyScale:F3}");
                    if(root.name=="Aud_W_03"||root.name=="Aud_M_01"){
                        typeof(RehearPhoneSetup).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{root,phone});
                        File.Copy("Temp/RehearPhonePreview-"+root.name+".png",$"Temp/PhotoSlide-{root.name}-{step}.png",true);
                        if(root.name=="Aud_W_03"&&step==2){
                            var saved=phone.transform.position;
                            for(int n=0;n<3;n++){
                                phone.transform.position=saved+phone.transform.TransformVector(new Vector3(-.055f+n*.005f,0,.021f+n*.007f));
                                typeof(RehearPhoneSetup).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{root,phone});
                                File.Copy("Temp/RehearPhonePreview-"+root.name+".png",$"Temp/PhotoSlide-palm-{n}.png",true);
                            }
                            phone.transform.position=saved;
                        }
                    }
                }
                body.StopAction();typeof(AudienceAnimationPlayer).GetMethod("Advance",F).Invoke(body,new object[]{1f});graph.Evaluate(1);body.ApplyPropPoses();
                if(phone.activeSelf)throw new Exception("Photo phone remained visible after stop.");
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        File.WriteAllText("Temp/RehearPhotoSlideCheck.txt",report.ToString());
    }
}
