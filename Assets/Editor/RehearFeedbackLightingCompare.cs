using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearFeedbackLightingCompare
{
    static RehearFeedbackLightingCompare()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        const string request="Temp/RehearFeedbackLightingCompare.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var command=File.ReadAllText(request).Trim();File.Delete(request);
        try { Compare(); if(command=="compare-panel")AdjustPanel(); }catch(Exception e){File.WriteAllText("Temp/RehearFeedbackLightingCompare.txt",e.ToString());}
    }
    static void AdjustPanel()
    {
        var scene=SceneManager.GetSceneByPath("Assets/01_Scene/Scene_03_Feedback.unity");
        var panel=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<UnityEngine.UI.Image>(true)).Single(i=>i.name=="Panel_Result");
        Undo.RecordObject(panel,"Increase feedback panel transparency");
        var color=panel.color;color.a=.68f;panel.color=color;EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.AppendAllText("Temp/RehearFeedbackLightingCompare.txt","\nPANEL background opacity 1 -> .68; text opacity unchanged.\n");
    }
    static void Compare()
    {
        typeof(RehearFeedbackPreview).GetMethod("Stop",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var original=SceneManager.GetActiveScene();
        var scenes=new[]{"Scene_02_Presentation","Scene_03_Feedback"}.Select(n=>SceneManager.GetSceneByPath("Assets/01_Scene/"+n+".unity")).ToArray();
        for(int i=0;i<scenes.Length;i++)
            if(!scenes[i].isLoaded)scenes[i]=EditorSceneManager.OpenScene("Assets/01_Scene/"+(i==0?"Scene_02_Presentation":"Scene_03_Feedback")+".unity",OpenSceneMode.Additive);
        var roots=scenes.SelectMany(s=>s.GetRootGameObjects()).ToArray();
        var states=roots.Select(g=>g.activeSelf).ToArray();
        var canvases=roots.SelectMany(g=>g.GetComponentsInChildren<Canvas>(true)).ToArray();
        var canvasStates=canvases.Select(c=>c.gameObject.activeSelf).ToArray();
        var cam=scenes[1].GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First(c=>c.CompareTag("MainCamera"));
        var position=cam.transform.position;var rotation=cam.transform.rotation;
        var report=new StringBuilder();
        string Path(Transform t)=>AnimationUtility.CalculateTransformPath(t,null);
        try
        {
            foreach(var s in scenes)
            {
                SceneManager.SetActiveScene(s);
                for(int i=0;i<roots.Length;i++)roots[i].SetActive(states[i] && roots[i].scene==s);
                foreach(var c in canvases)c.gameObject.SetActive(false);
                var all=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
                report.AppendLine("SCENE "+s.name+" ambient="+RenderSettings.ambientIntensity+" reflection="+RenderSettings.reflectionIntensity+" sky="+AssetDatabase.GetAssetPath(RenderSettings.skybox));
                foreach(var l in all.Select(t=>t.GetComponent<Light>()).Where(l=>l))report.AppendLine($"LIGHT {Path(l.transform)} active={l.isActiveAndEnabled} color={l.color} intensity={l.intensity} temp={l.useColorTemperature}/{l.colorTemperature} bake={l.lightmapBakeType} pos={l.transform.position} rot={l.transform.eulerAngles}");
                foreach(var v in all.Select(t=>t.GetComponent<Volume>()).Where(v=>v))report.AppendLine("VOLUME "+Path(v.transform)+" active="+v.isActiveAndEnabled+" weight="+v.weight+" profile="+AssetDatabase.GetAssetPath(v.sharedProfile));
                foreach(var c in all.Select(t=>t.GetComponent<Camera>()).Where(c=>c))report.AppendLine("CAMERA "+Path(c.transform)+" post="+c.GetComponent<UniversalAdditionalCameraData>()?.renderPostProcessing);
                foreach(var p in all.Select(t=>t.GetComponent<ReflectionProbe>()).Where(p=>p))report.AppendLine("PROBE "+Path(p.transform)+" intensity="+p.intensity+" texture="+AssetDatabase.GetAssetPath(p.bakedTexture));
                var renderers=all.Select(t=>t.GetComponent<MeshRenderer>()).Where(r=>r && r.gameObject.activeInHierarchy).ToArray();
                report.AppendLine("RENDERERS baked="+renderers.Count(r=>r.lightmapIndex>=0 && r.lightmapIndex<LightmapSettings.lightmaps.Length)+" total="+renderers.Length);
                File.WriteAllLines("Temp/"+s.name+"-renderers.txt",renderers.Select(r=>Path(r.transform)+"|"+string.Join(",",r.sharedMaterials.Select(AssetDatabase.GetAssetPath))+"|"+r.transform.position+"|map="+r.lightmapIndex+" uv="+r.lightmapScaleOffset));
                Render(s,position,rotation,s.name);
                var indices=renderers.Select(r=>r.lightmapIndex).Where(i=>i>=0 && i<LightmapSettings.lightmaps.Length).Distinct();
                foreach(var i in indices)report.AppendLine("MAP "+i+" "+AssetDatabase.GetAssetPath(LightmapSettings.lightmaps[i].lightmapColor));
                // Remove baked lighting temporarily, distinguishing a bake difference
                // from direct lights/materials/camera settings without altering assets.
                var mapIndices=renderers.Select(r=>r.lightmapIndex).ToArray();
                try {foreach(var r in renderers)r.lightmapIndex=-1;Render(s,position,rotation,s.name+"-unbaked");}
                finally {for(int i=0;i<renderers.Length;i++)renderers[i].lightmapIndex=mapIndices[i];}
            }
            File.WriteAllText("Temp/RehearFeedbackLightingCompare.txt",report.ToString());
        }
        finally
        {
            for(int i=0;i<roots.Length;i++)if(roots[i])roots[i].SetActive(states[i]);
            for(int i=0;i<canvases.Length;i++)if(canvases[i])canvases[i].gameObject.SetActive(canvasStates[i]);
            SceneManager.SetActiveScene(original);
        }
    }
    static void Render(Scene s,Vector3 position,Quaternion rotation,string name)
    {
        var go=new GameObject("Lighting comparison camera");SceneManager.MoveGameObjectToScene(go,s);
        var camera=go.AddComponent<Camera>();camera.scene=s;camera.transform.SetPositionAndRotation(position,rotation);
        camera.fieldOfView=70;camera.nearClipPlane=.05f;camera.farClipPlane=100;camera.allowHDR=true;camera.stereoTargetEye=StereoTargetEyeMask.None;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var rt=new RenderTexture(1200,675,24);var old=RenderTexture.active;
        try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(1200,675,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1200,675),0,0);tex.Apply();
            File.WriteAllBytes("Temp/Lighting-"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);
        }finally{RenderTexture.active=old;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
    }
}
