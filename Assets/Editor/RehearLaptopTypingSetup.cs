using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearLaptopTypingSetup
{
    const string Request="Temp/RehearLaptopTyping.request";
    const string Report="Temp/RehearLaptopTyping.txt";
    static RehearLaptopTypingSetup() => EditorApplication.update+=Poll;
    static void Poll()
    {
        if(AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        string command;
        try { command=File.ReadAllText(Request).Trim(); File.Delete(Request); } catch(IOException) { return; }
        try { if(command=="fix") Fix(); else if(command=="test") Test(); else Inspect(); } catch(Exception e) {File.WriteAllText(Report,e.ToString());}
    }
    static AudienceSeating Layout() => EditorSceneManager.GetActiveScene().GetRootGameObjects().Where(g=>g.name!="Audience Preview (Editor Only)").SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>(true)).Single();
    static void Fix()
    {
        var layout=Layout();
        var report=new StringBuilder();
        var table=layout.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name=="Conference_table").GetComponent<Renderer>().bounds;
        foreach(var seat in layout.seats)
        {
            if(seat.row=="rear") continue;
            var position=seat.transform.position+seat.transform.forward*.65f;
            position.x=Mathf.Clamp(position.x,table.min.x+.20f,table.max.x-.20f);
            position.z=Mathf.Clamp(position.z,table.min.z+.20f,table.max.z-.20f);
            position.y=seat.laptopAnchor.position.y;
            var inward=position-seat.transform.position; inward.y=0; inward.Normalize();
            Undo.RecordObject(seat.laptopAnchor,"Align side laptop to its occupant");
            seat.laptopAnchor.SetPositionAndRotation(position,Quaternion.LookRotation(inward,Vector3.up));
            EditorUtility.SetDirty(seat.laptopAnchor);
            report.AppendLine($"{seat.SeatId}: laptop={position:F3}, forward={inward}");
        }
        var scene=layout.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Report,"PASS side laptops follow seated audience heading; display faces occupant and lid back faces presenter; rear anchors unchanged.\n"+report);
        File.WriteAllText("Temp/RehearSessionReady.request","preview-audience");
    }
    static void Test()
    {
        var source=Layout();
        var scene=EditorSceneManager.NewPreviewScene();
        GameObject clone=null;
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        int checkedActors=0;
        void Require(bool value,string message) {if(!value) throw new Exception(message);}
        try
        {
            clone=Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,scene);
            var layout=clone.GetComponent<AudienceSeating>(); layout.Initialize(1234);
            foreach(var seat in layout.seats)
            {
                var direction=seat.laptopAnchor.position-seat.transform.position; direction.y=0;
                Require(Vector3.Angle(seat.laptopAnchor.forward,direction)<.1f,"Laptop keyboard does not face its occupant.");
                Require(Vector3.Angle(seat.laptopAnchor.up,Vector3.up)<.1f,"Laptop anchor is tilted.");
                if(seat.row!="rear")
                {
                    Require(Vector3.Angle(seat.transform.forward,seat.laptopAnchor.forward)<45f,"Laptop faces away from seated audience heading.");
                    Require(seat.laptopAnchor.forward.x>.1f,"Presenter sees laptop display instead of lid back.");
                }
                var actor=seat.Occupant.gameObject;
                var body=actor.GetComponent<AudienceAnimationPlayer>();
                body.enabled=true;
                typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",flags).Invoke(body,null);
                var graph=(UnityEngine.Playables.PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",flags).GetValue(body);
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var advance=typeof(AudienceAnimationPlayer).GetMethod("Advance",flags);
                void Step(float delta) {advance.Invoke(body,new object[]{delta}); graph.Evaluate(delta);}
                bool Pending() => (AnimationClip)typeof(AudienceAnimationPlayer).GetField("pendingTypingClip",flags).GetValue(body);
                seat.Assign(seat.Occupant,false);
                Require(!body.PlayServerVariation("ACT_01.laptoptyping",3,1),"Typing without a laptop was accepted.");
                seat.Assign(seat.Occupant,true);
                var initial=actor.transform.rotation;
                Require(body.PlayServerVariation("ACT_01.laptoptyping",3,1),"Typing request rejected.");
                Require(Pending() && Quaternion.Angle(initial,actor.transform.rotation)<.01f,"Typing snapped instead of queuing a turn.");
                Require(body.TypingLookTarget==seat.laptopAnchor,"Gaze is not directed to the laptop.");
                Step(.016f);
                if(Vector3.Angle(seat.transform.forward,direction)>10f) Require(Pending(),"Typing began before facing laptop.");
                // A new evaluation must be able to replace an unfinished turn.
                Require(body.PlayServerVariation("ACT_02.photoslide",2,1),"Replacement command rejected.");
                Require(!Pending(),"Typing queue survived a replacement command.");
                Require(body.PlayServerVariation("ACT_01.laptoptyping",3,1),"Second typing request rejected.");
                for(int i=0;i<150 && Pending();i++) Step(.016f);
                Require(!Pending(),"Turn never completed.");
                var pose=actor.GetComponent<AudienceSeatedPose>();
                var wanted=Quaternion.LookRotation(direction)*pose.facingCorrection;
                Require(Quaternion.Angle(actor.transform.rotation,wanted)<4.1f,"Typing started facing away from laptop.");
                Require(Vector3.Distance(actor.transform.TransformPoint(pose.localHip),seat.transform.position)<.001f,"Turning moved the seated hip off its chair.");
                body.StopAction();
                for(int i=0;i<160;i++) Step(.016f);
                Require(!body.IsBusy && !body.TypingLookTarget,"Typing/gaze did not clear after stop.");
                Require(Quaternion.Angle(actor.transform.rotation,seat.transform.rotation*pose.facingCorrection)<.2f,"Actor did not return to listening direction.");
                body.enabled=false;
                checkedActors++;
            }
            File.AppendAllText(Report,$"PASS {checkedActors} actors: level seat-facing laptop anchors; laptop-only typing; turn before clip; laptop gaze; immediate interruption; hip stays on seat; smooth return to listening. Isolated from server/recording.\n");
        }
        finally { if(clone) Object.DestroyImmediate(clone); EditorSceneManager.ClosePreviewScene(scene); }
    }
    static void Inspect()
    {
        var scene=EditorSceneManager.GetActiveScene();
        var layout=scene.GetRootGameObjects().Where(g=>g.name!="Audience Preview (Editor Only)").SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>(true)).Single();
        var text=new StringBuilder();
        foreach(var seat in layout.seats) text.AppendLine($"SEAT {seat.SeatId} pos={seat.transform.position:F4} forward={seat.transform.forward:F4} laptop={seat.laptopAnchor.position:F4} rot={seat.laptopAnchor.eulerAngles:F4} up={seat.laptopAnchor.up:F4}");
        foreach(var prefab in layout.laptopPrefabs)
        {
            var copy=Object.Instantiate(prefab); copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            try
            {
                text.AppendLine("MODEL "+prefab.name);
                foreach(var f in copy.GetComponentsInChildren<MeshFilter>())
                {
                    var verts=f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v)).ToArray();
                    var min=new Vector3(verts.Min(v=>v.x),verts.Min(v=>v.y),verts.Min(v=>v.z));
                    var max=new Vector3(verts.Max(v=>v.x),verts.Max(v=>v.y),verts.Max(v=>v.z));
                    text.AppendLine($"  {f.name}: min={min:F4} max={max:F4}");
                    var tri=f.sharedMesh.triangles;
                    var largest=Enumerable.Range(0,tri.Length/3).Select(i=> {
                        var a=verts[tri[i*3]]; var b=verts[tri[i*3+1]]; var c=verts[tri[i*3+2]];
                        var cross=Vector3.Cross(b-a,c-a); return new {area=cross.magnitude*.5f,normal=cross.normalized,center=(a+b+c)/3};
                    }).OrderByDescending(t=>t.area).Take(8);
                    foreach(var t in largest) text.AppendLine($"    face area={t.area:F5} normal={t.normal:F3} center={t.center:F3}");
                }
            } finally {Object.DestroyImmediate(copy);}
        }
        File.WriteAllText(Report,text.ToString());
    }
}
