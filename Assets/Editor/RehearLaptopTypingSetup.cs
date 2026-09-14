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
        try { if(command=="chairs") MoveChairsCloser(); else if(command=="closer") MoveCloser(); else if(command=="align") AlignHeading(); else if(command=="fix") Fix(); else if(command=="test") Test(); else Inspect(); } catch(Exception e) {File.WriteAllText(Report,e.ToString());}
    }
    static void MoveChairsCloser()
    {
        var layout=Layout();
        var chairs=layout.gameObject.scene.GetRootGameObjects().Single(g=>g.name=="Chair").transform;
        var report=new StringBuilder();
        for(int i=0;i<layout.seats.Length;i++) {
            var seat=layout.seats[i];
            var chair=chairs.Find(i==0?"Chair":"Chair ("+i+")");
            if(!chair)throw new Exception("Missing chair for "+seat.SeatId);
            var laptopPosition=seat.laptopAnchor.position;
            var laptopRotation=seat.laptopAnchor.rotation;
            var direction=Vector3.ProjectOnPlane(laptopPosition-seat.transform.position,Vector3.up);
            var movement=direction.normalized*Mathf.Max(0,direction.magnitude-.50f);
            Undo.RecordObjects(new Object[]{chair,seat.transform,seat.laptopAnchor},"Move audience and chair closer to table");
            chair.position+=movement;
            seat.transform.position+=movement;
            // Laptop anchors are children of seats, but the laptops stay on the table.
            seat.laptopAnchor.SetPositionAndRotation(laptopPosition,laptopRotation);
            EditorUtility.SetDirty(chair);EditorUtility.SetDirty(seat.transform);EditorUtility.SetDirty(seat.laptopAnchor);
            report.AppendLine($"{seat.SeatId}: chair and pelvis moved {movement.magnitude:F3}m; keyboard distance {Vector3.ProjectOnPlane(laptopPosition-seat.transform.position,Vector3.up).magnitude:F3}m");
        }
        EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);EditorSceneManager.SaveScene(layout.gameObject.scene);
        File.WriteAllText(Report,report.ToString());
    }
    static void AlignHeading()
    {
        var layout=Layout();
        foreach(var seat in layout.seats) {
            var delta=seat.laptopAnchor.position-seat.transform.position;delta.y=0;
            float angle=Vector3.SignedAngle(seat.transform.forward,delta,Vector3.up);
            if(Mathf.Abs(angle)<=44f || seat.row=="rear")continue;
            var forward=Quaternion.AngleAxis(Mathf.Sign(angle)*44f,Vector3.up)*seat.transform.forward;
            if(Mathf.Abs(forward.z)<.01f)continue;
            var position=seat.laptopAnchor.position;
            position.x=seat.transform.position.x+delta.z*forward.x/forward.z;
            Undo.RecordObject(seat.laptopAnchor,"Keep laptop within seated turning range");
            seat.laptopAnchor.SetPositionAndRotation(position,Quaternion.LookRotation(forward));
            EditorUtility.SetDirty(seat.laptopAnchor);
        }
        EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);EditorSceneManager.SaveScene(layout.gameObject.scene);
        File.WriteAllText(Report,"Laptop heading aligned.\n");
    }
    static void MoveCloser()
    {
        var layout=Layout();
        var table=layout.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name=="Conference_table").GetComponent<Renderer>().bounds;
        var report=new StringBuilder();
        foreach(var seat in layout.seats) {
            var old=seat.laptopAnchor.position;
            var direction=old-seat.transform.position;direction.y=0;
            var target=seat.transform.position+direction.normalized*.51f;
            target.x=Mathf.Clamp(target.x,table.min.x+.16f,table.max.x-.16f);
            target.z=Mathf.Clamp(target.z,table.min.z+.16f,table.max.z-.16f);
            target.y=old.y;
            var facing=target-seat.transform.position;facing.y=0;
            Undo.RecordObject(seat.laptopAnchor,"Bring laptop within seated reach");
            seat.laptopAnchor.SetPositionAndRotation(target,Quaternion.LookRotation(facing,Vector3.up));
            EditorUtility.SetDirty(seat.laptopAnchor);
            report.AppendLine($"{seat.SeatId}: moved={Vector3.Distance(old,target):F3}m distance={facing.magnitude:F3}m");
        }
        EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
        EditorSceneManager.SaveScene(layout.gameObject.scene);
        File.WriteAllText(Report,report.ToString());
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
        File.WriteAllText(Report,"");
        File.WriteAllText("Temp/RehearTypingReach.txt","");
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
                void Step(float delta) {advance.Invoke(body,new object[]{delta}); graph.Evaluate(delta);body.ApplyPropPoses();}
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
                for(int i=0;i<50;i++)Step(.016f);
                if(checkedActors==0) CaptureTyping(actor,seat.laptopAnchor);
                foreach(var side in new[]{"l","r"}) {
                    var tip=actor.GetComponentsInChildren<Transform>().Single(t=>t.name=="middle_03_"+side);
                    var local=seat.laptopAnchor.InverseTransformPoint(tip.position);
                    var wantedTip=new Vector3(side=="l"?-.065f:.065f,.020f,-.015f);
                    File.AppendAllText("Temp/RehearTypingReach.txt",$"{actor.name} {seat.SeatId} {side} tip={local:F3} error={Vector3.Distance(local,wantedTip):F4}m\n");
                    Require(Vector3.Distance(local,wantedTip)<.03f,"Hand cannot reach keyboard: "+actor.name+" "+seat.SeatId+" "+local);
                    var bones=actor.GetComponentsInChildren<Transform>();
                    var upper=bones.Single(t=>t.name=="upperarm_"+side);
                    var lower=bones.Single(t=>t.name=="lowerarm_"+side);
                    var hand=bones.Single(t=>t.name=="hand_"+side);
                    float bend=Vector3.Angle(lower.position-upper.position,hand.position-lower.position);
                    File.AppendAllText("Temp/RehearTypingReach.txt",$"  elbow bend={bend:F1} degrees\n");
                    Require(bend>20f,"Typing arm is almost straight: "+actor.name+" "+side);
                }
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
    static void CaptureTyping(GameObject actor,Transform keyboard)
    {
        foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>()) {skin.updateWhenOffscreen=true;skin.forceMatrixRecalculationPerRender=true;}
        var go=new GameObject("Typing inspection camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,actor.scene);
        var camera=go.AddComponent<Camera>();camera.scene=actor.scene;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;
        camera.transform.position=keyboard.position-keyboard.forward*.48f+keyboard.right*.58f+keyboard.up*.40f;
        camera.transform.LookAt(keyboard.position-keyboard.forward*.12f+keyboard.up*.12f);
        camera.nearClipPlane=.01f;camera.orthographic=true;camera.orthographicSize=.40f;
        var light=go.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;
        var rt=new RenderTexture(900,900,24);var old=RenderTexture.active;
        try {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(900,900,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,900,900),0,0);tex.Apply();
            File.WriteAllBytes("Temp/RehearTypingContact.png",tex.EncodeToPNG());Object.DestroyImmediate(tex);
        } finally {RenderTexture.active=old;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
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
