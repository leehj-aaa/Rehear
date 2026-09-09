using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

[InitializeOnLoad] internal static class RehearPhotoFitCheck
{
    static RehearPhotoFitCheck(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/RehearPhotoFit.request"))return;
        File.Delete("Temp/RehearPhotoFit.request");
        try{Run();}catch(Exception e){File.WriteAllText("Temp/RehearPhotoFit.txt",e.ToString());}
    }
    static void Run(){
        var report=new StringBuilder();var clouds=new List<Vector3[]>();var boxes=new List<Bounds>();
        var thumbs=new List<Vector3[]>();var tips=new List<Vector3[]>();
        var catalog=AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
        foreach(var path in Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation","Aud_*.prefab")){
            if(!path.EndsWith("Aud_M_01.prefab")&&!path.EndsWith("Aud_W_01.prefab"))continue;
            var root=PrefabUtility.LoadPrefabContents(path.Replace('\\','/'));
            try{
                var body=root.GetComponentInChildren<AudienceAnimationPlayer>();
                var phone=(GameObject)typeof(AudienceAnimationPlayer).GetField("photoPhone",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(body);
                catalog.TryGetClip("ACT_02.photoslide",body.Gender,out var clip);
                foreach(float time in new[]{4.933333f}){
                    clip.SampleAnimation(body.GetComponentInChildren<Animator>().gameObject,time);
                    var grip=phone.GetComponent<AudiencePhotoGrip>();
                    var actorBones=root.GetComponentsInChildren<Transform>();
                    var scale=phone.transform.lossyScale;var native=phone.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var box=new Bounds(Vector3.Scale(native.center,scale),Vector3.Scale(native.size,scale));
                    var points=new List<Vector3>();
                    var thumbPoints=new List<Vector3>();var tipPoints=new List<Vector3>();
                    var collisions=new Dictionary<string,int>();
                    foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>()){
                        var weights=renderer.sharedMesh.boneWeights;var rb=renderer.bones;
                        bool Left(int i)=>i>=0&&i<rb.Length&&rb[i]&&rb[i].name.EndsWith("_l")&&new[]{"hand_","thumb_","index_","middle_","ring_","pinky_"}.Any(p=>rb[i].name.StartsWith(p));
                        var mesh=new Mesh();renderer.BakeMesh(mesh);var vertices=mesh.vertices;
                        for(int i=0;i<weights.Length&&i<vertices.Length;i++){
                            var w=weights[i];float influence=(Left(w.boneIndex0)?w.weight0:0)+(Left(w.boneIndex1)?w.weight1:0)+(Left(w.boneIndex2)?w.weight2:0)+(Left(w.boneIndex3)?w.weight3:0);
                            if(influence>.5f){
                                var point=Vector3.Scale(phone.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i])),scale);points.Add(point);
                                if(rb[w.boneIndex0].name=="thumb_03_l")thumbPoints.Add(point);
                                if(rb[w.boneIndex0].name=="middle_03_l")tipPoints.Add(point);
                                if(box.Contains(point)){
                                    var name=rb[w.boneIndex0].name;
                                    if(!collisions.ContainsKey(name))collisions[name]=0;collisions[name]++;
                                }
                            }
                        }
                        Object.DestroyImmediate(mesh);
                    }
                    clouds.Add(points.Where((p,i)=>i%3==0).ToArray());boxes.Add(box);
                    thumbs.Add(thumbPoints.ToArray());tips.Add(tipPoints.ToArray());
                    report.AppendLine($"{root.name} t={time:F3} handVerts={points.Count} inside={points.Count(p=>box.Contains(p))} phone={box}");
                    foreach(var collision in collisions)report.AppendLine($"  {collision.Key}: {collision.Value}");
                }
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        var candidates=new List<(Vector3 delta,Vector3 angles,int count,float score)>();
        for(int pitch=-1;pitch<=1;pitch++)for(int yaw=-2;yaw<=2;yaw++)for(int roll=-1;roll<=1;roll++)for(int x=-2;x<=2;x++)for(int y=-2;y<=2;y++)for(int z=-3;z<=6;z++){
            var delta=new Vector3(x*.01f,y*.01f,z*.01f);var angles=new Vector3(pitch*30,yaw*30,roll*30);
            var inverse=Quaternion.Inverse(Quaternion.Euler(angles));int count=0;float gaps=0;
            for(int i=0;i<clouds.Count;i++)foreach(var p in clouds[i])if(boxes[i].Contains(inverse*(p-delta)))count++;
            for(int i=0;i<clouds.Count;i++){
                float thumbGap=float.PositiveInfinity,tipGap=float.PositiveInfinity;
                foreach(var p in thumbs[i])thumbGap=Mathf.Min(thumbGap,boxes[i].SqrDistance(inverse*(p-delta)));
                foreach(var p in tips[i])tipGap=Mathf.Min(tipGap,boxes[i].SqrDistance(inverse*(p-delta)));
                gaps+=thumbGap+tipGap;
            }
            candidates.Add((delta,angles,count,count+gaps*100000f));
        }
        var best=candidates.OrderBy(c=>c.score).ThenBy(c=>c.delta.sqrMagnitude+c.angles.sqrMagnitude*.0000001f).Take(8).ToArray();
        foreach(var candidate in best)report.AppendLine($"CANDIDATE delta={candidate.delta:F4} rotation={candidate.angles} penetrations={candidate.count} contactScore={candidate.score:F3}");
        File.WriteAllText("Temp/RehearPhotoFit.txt",report.ToString());
        var preview=PrefabUtility.LoadPrefabContents("Assets/03_Prefabs/Audience/Presentation/Aud_W_01.prefab");
        try{
            var body=preview.GetComponentInChildren<AudienceAnimationPlayer>();
            var phone=(GameObject)typeof(AudienceAnimationPlayer).GetField("photoPhone",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(body);
            catalog.TryGetClip("ACT_02.photoslide",body.Gender,out var clip);clip.SampleAnimation(body.GetComponentInChildren<Animator>().gameObject,4.933333f);phone.SetActive(true);
            var pos=phone.transform.position;var rot=phone.transform.rotation;
            for(int i=0;i<3;i++){
                phone.transform.SetPositionAndRotation(pos+rot*best[i].delta,rot*Quaternion.Euler(best[i].angles));
                typeof(RehearPhoneSetup).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{preview,phone});
                File.Copy("Temp/RehearPhonePreview-"+preview.name+".png","Temp/PhotoFit-"+i+".png",true);
            }
        }finally{PrefabUtility.UnloadPrefabContents(preview);}
    }
}
