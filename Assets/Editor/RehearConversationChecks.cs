using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Object=UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearConversationChecks
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static RehearConversationChecks()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        const string request="Temp/RehearConversationChecks.request";
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request))return;
        try {File.Delete(request);Run();}catch(Exception e){File.WriteAllText("Temp/RehearConversationChecks.txt",e.ToString());}
    }
    static void Run()
    {
        var source=EditorSceneManager.GetActiveScene().GetRootGameObjects().Where(g=>g.name!="Audience Preview (Editor Only)")
            .SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>(true)).Single();
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
        var report=new StringBuilder();
        void Require(bool value,string message){if(!value)throw new Exception(message);}
        try {
            root=Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var layout=root.GetComponent<AudienceSeating>();layout.Initialize(1234);
            var bodies=root.GetComponentsInChildren<AudienceAnimationPlayer>();
            var graphs=bodies.Select(b=>{
                b.enabled=true;typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",Flags).Invoke(b,null);
                var graph=(PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",Flags).GetValue(b);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);return graph;
            }).ToArray();
            void Step(int frames) {for(int f=0;f<frames;f++)for(int i=0;i<bodies.Length;i++) {
                typeof(AudienceAnimationPlayer).GetMethod("Advance",Flags).Invoke(bodies[i],new object[]{.02f});
                graphs[i].Evaluate(.02f);bodies[i].ApplyPropPoses();
            }}
            double ClipTime(AudienceAnimationPlayer b) {
                var target=typeof(AudienceAnimationPlayer).GetField("target",Flags).GetValue(b);
                return ((AnimationClipPlayable)target.GetType().GetField("playable").GetValue(target)).GetTime();
            }
            var pair=layout.seats.Where(s=>s.row=="rear").Select(s=>s.Occupant.GetComponent<AudienceAnimationPlayer>()).ToArray();
            foreach(var b in bodies.Except(pair))Require(!b.PlayServerVariation("ACT_08.side_conversation_r",4,1),"Front/middle audience accepted conversation.");
            foreach(var b in pair) {
                Require(b.GetComponent<AudienceSeatAssignment>().TryGetConversationVariation(out var id),"Missing rear partner.");
                report.AppendLine(b.name+" -> "+id);
            }
            var sides=pair.Select(b=>{b.GetComponent<AudienceSeatAssignment>().TryGetConversationVariation(out var id);return id;}).ToArray();
            Require(sides.Distinct().Count()==2,"Both people received the same direction.");
            Require(pair[0].PlayServerVariation("ACT_08.side_conversation_l",4,1),"Pair rejected.");
            Step(60);
            Require(pair.All(b=>b.ConversationWeight>.99f),"One person did not join.");
            Capture(pair);
            var before=ClipTime(pair[0]);
            Require(pair[1].PlayServerVariation("ACT_08.side_conversation_r",4,1),"Second command rejected.");
            Require(Math.Abs(ClipTime(pair[0])-before)<.0001,"Duplicate pair command rewound animation.");
            Require(bodies.Except(pair).All(b=>b.ConversationWeight==0),"Unrelated audience joined.");
            pair[0].PlayServerVariation("BL_03.quiet_stable_posture",4,1);Step(45);
            Require(pair.All(b=>b.ConversationWeight<.001f),"Partner remained talking after interruption.");
            pair[1].ReserveQuestionTurn();
            Require(!pair[0].PlayServerVariation("ACT_08.side_conversation",4,1),"Conversation interrupted a Q&A turn.");
            pair[1].ReleaseQuestionTurn();
            Require(pair[0].PlayServerVariation("ACT_08.side_conversation",2,1),"Pair failed to resume.");Step(150);
            Require(pair.All(b=>b.ConversationWeight<.001f),"Pair did not finish together.");
            report.AppendLine("PASS rear-only; opposite L/R; one command starts both; duplicate does not rewind; interruption ends both; Q&A protected; automatic return.");
            File.WriteAllText("Temp/RehearConversationChecks.txt",report.ToString());
        }finally {if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Capture(AudienceAnimationPlayer[] pair)
    {
        foreach(var b in pair)foreach(var skin in b.GetComponentsInChildren<SkinnedMeshRenderer>()) {
            skin.updateWhenOffscreen=true;skin.forceMatrixRecalculationPerRender=true;
        }
        var heads=pair.Select(b=>b.GetComponentsInChildren<Transform>().Single(t=>t.name=="head")).ToArray();
        var center=(heads[0].position+heads[1].position)*.5f-Vector3.up*.12f;
        var forward=pair[0].GetComponent<AudienceSeatAssignment>().Seat.transform.forward;
        var go=new GameObject("Conversation inspection camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,pair[0].gameObject.scene);
        var camera=go.AddComponent<Camera>();camera.scene=go.scene;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;
        camera.transform.position=center+forward*1.5f+Vector3.up*.15f;
        camera.transform.LookAt(center);camera.nearClipPlane=.01f;
        camera.orthographic=true;camera.orthographicSize=.55f;
        var light=go.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;
        var rt=new RenderTexture(1200,800,24);var old=RenderTexture.active;
        try {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(1200,800,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,1200,800),0,0);tex.Apply();
            File.WriteAllBytes("Temp/RehearConversationPair.png",tex.EncodeToPNG());Object.DestroyImmediate(tex);
        }finally {RenderTexture.active=old;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
    }
}
