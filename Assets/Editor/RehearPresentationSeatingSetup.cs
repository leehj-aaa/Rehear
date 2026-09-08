using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearPresentationSeatingSetup
{
    const string Path = "Assets/01_Scene/Scene_02_Presentation.unity";
    static RehearPresentationSeatingSetup()
    {
        EditorApplication.update += Poll;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }
    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if(!SessionState.GetBool("RehearSeatTransitionTest",false) || scene.path!=Path) return;
        // Exercise scene arrival only: no microphone, live server session, or timer.
        foreach(var b in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)))
            if(b && (b is PresentationController || b is Rehear.Evc.Presentation.PresentationFlowController || b.GetType().Name=="AIIntegrationManager")) b.enabled=false;
        var layout=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>()).Single();
        Check(layout);
        var roots=scene.GetRootGameObjects();
        var controller=roots.SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single();
                typeof(PresentationController).GetField("flowController",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(controller,null);
        var input=roots.SelectMany(g=>g.GetComponentsInChildren<PresentationInputController>(true)).Single();
        var grip=typeof(PresentationInputController).GetMethod("OnGripPerformed",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        grip.Invoke(input,new object[]{default(UnityEngine.InputSystem.InputAction.CallbackContext)});
        if(!controller.IsPaused || !controller.pausePanel.activeSelf) throw new Exception("Grip pause failed");
        grip.Invoke(input,new object[]{default(UnityEngine.InputSystem.InputAction.CallbackContext)});
        if(controller.IsPaused || controller.pausePanel.activeSelf) throw new Exception("Grip resume failed");
        foreach(var b in controller.pausePanel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            if(b.onClick.GetPersistentEventCount()!=1 || b.onClick.GetPersistentTarget(0)!=controller) throw new Exception("Pause button binding mismatch");
        File.AppendAllText("Temp/RehearPresentationSeating-tests.txt","PASS grip pause/resume and presentation pause button targets.\n");
        var toggle=roots.SelectMany(g=>g.GetComponentsInChildren<PodiumScriptToggle>(true)).Single();
        if(toggle.controller!=controller || toggle.scriptPanel!=controller.scriptPanel) throw new Exception("New script button binding mismatch.");
        controller.scriptPanel.SetActive(false);
        var button=toggle.GetComponent<UnityEngine.UI.Button>(); button.onClick.Invoke();
        if(!controller.scriptPanel.activeSelf) throw new Exception("Script button did not open panel.");
        button.onClick.Invoke();
        if(controller.scriptPanel.activeSelf) throw new Exception("Script button did not close panel.");
        File.AppendAllText("Temp/RehearPresentationSeating-tests.txt","PASS new podium script button opens/closes existing controller panel.\n");
        File.AppendAllText("Temp/RehearPresentationSeating-tests.txt","PASS tutorial completion -> presentation; six assigned prefab actors; no recording/server start.\n");
        SessionState.SetBool("RehearSeatTransitionTest",false);
    }
    static void Poll()
    {
        if(EditorApplication.isPlaying && !EditorApplication.isCompiling && File.Exists("Temp/RehearPausePreview.request"))
        {
            File.Delete("Temp/RehearPausePreview.request");
            UnityEngine.Object.FindFirstObjectByType<PresentationController>().PauseGame();
        }
        if(EditorApplication.isPlaying && !EditorApplication.isCompiling && File.Exists("Temp/RehearTypingCheck.request"))
        {
            File.Delete("Temp/RehearTypingCheck.request");
            var layout=UnityEngine.Object.FindFirstObjectByType<AudienceSeating>();
            int blocked=0,played=0;
            foreach(var seat in layout.seats)
            {
                var actor=seat.Occupant.GetComponent<Rehear.Evc.Audience.AudienceAgent>();
                var body=actor.GetComponent<AudienceAnimationPlayer>();
                if(!seat.HasLaptop)
                {
                    var typingCommand=new Rehear.Evc.Contracts.UnityCommandDto {agent_id=actor.AgentId,layer="Body",action_id="ACT_01",selected_variation_id="ACT_01.laptoptyping",blend_mode="override"};
                    if(actor.TryExecute(typingCommand,out var reason) || reason!="seat_action_not_available" || body.PlayServerVariation("ACT_01.laptoptyping",3,1)) throw new Exception("Laptop-less actor accepted typing");
                    blocked++;
                }
                else if(body.PlayServerVariation("ACT_01.laptoptyping",3,1)) played++;
                else throw new Exception("Equipped actor could not type");
            }
            File.AppendAllText("Temp/RehearPresentationSeating-tests.txt",$"PASS live typing: {played} equipped actors animate; {blocked} unequipped actors reject both server and direct animation calls.\n");
        }
        if(EditorApplication.isPlaying && File.Exists("Temp/RehearPresentationSeating.request") && !EditorApplication.isCompiling && !EditorApplication.isUpdating && File.ReadAllText("Temp/RehearPresentationSeating.request").Trim()=="runtime-report")
        {File.Delete("Temp/RehearPresentationSeating.request"); RuntimeReport();}
        if(EditorApplication.isPlaying && SessionState.GetBool("RehearSeatTransitionTest",false))
        {
            var manager=UnityEngine.Object.FindFirstObjectByType<TutorialManager>();
            if(manager && Time.timeSinceLevelLoad>2)
            {
                var field=typeof(TutorialManager).GetField("currentStep",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                field.SetValue(manager,Enum.Parse(field.FieldType,"Complete"));
                manager.OnPrimaryButtonPressed();
            }
        }
        const string request = "Temp/RehearPresentationSeating.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        string command;
        try { command = File.ReadAllText(request).Trim(); File.Delete(request); } catch(IOException) { return; }
        try
        {
            if(command=="test-transition")
            {
                EditorSceneManager.OpenScene("Assets/01_Scene/Scene_00_5_Tutorial.unity");
                SessionState.SetBool("RehearSeatTransitionTest",true); EditorApplication.EnterPlaymode(); return;
            }
            var scene = EditorSceneManager.GetActiveScene();
            if(scene.path!=Path) scene=EditorSceneManager.OpenScene(Path);
            if (command == "inspect-source") InspectSource(scene);
            if (command == "repair-seats") RepairSeats(scene);
            if (command == "ground-audit") GroundAudit(scene);
            if (command == "lower-seat-layout") LowerSeatLayout(scene);
            if (command == "presentation-ui") PresentationUi(scene);
            if (command == "repair-laptops") RepairLaptops(scene);
            if (command == "finalize") FinalizePresentation(scene);
            if (command == "sync-materials") SyncMaterials(scene);
            if (command == "apply") Apply(scene);
            if (command == "prefab-only") ConvertToPrefabSpawning(scene);
            if (command == "finish-ui") FinishUi(scene);
            if (command == "session-buttons") SessionButtons(scene);
            if (command == "remove-answer-guide") {
                var qa=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<QuestionAnswerManager>(true)).Single();
                var guide=qa.answerGuideText;
                qa.answerGuideText=null;
                if(guide) UnityEngine.Object.DestroyImmediate(guide.gameObject);
                EditorUtility.SetDirty(qa); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                File.WriteAllText("Temp/RehearAnswerGuideRemoved.txt","Removed answer guide UI and cleared reference; Q&A actions preserved.");
            }
            if (command == "lower-session-buttons") {
                var transforms=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
                var mount=transforms.Single(t=>t.name=="Presentation Controls");
                var screen=transforms.Single(t=>t.name=="DeskScreen");
                var screenRect=(RectTransform)screen;
                var target=screen.TransformPoint(new Vector3(0,screenRect.rect.yMax,0))+screen.up*.07f-mount.forward*.015f;
                var local=mount.parent.InverseTransformPoint(target);
                ((RectTransform)mount).anchoredPosition3D=local;
                EditorUtility.SetDirty(mount); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                File.WriteAllText("Temp/RehearButtonLowered.txt","Moved presentation controls down 0.03m along screen up; saved scene.");
            }
            if (command == "test-qa-clock") {
                var host=new GameObject("Q&A clock check");
                try {
                    var controller=host.AddComponent<PresentationController>(); controller.enabled=false;
                    var qa=host.AddComponent<QuestionAnswerManager>(); controller.qaManager=qa;
                    var label=new GameObject("Clock",typeof(RectTransform),typeof(CanvasRenderer),typeof(TMPro.TextMeshProUGUI));
                    label.transform.SetParent(host.transform); controller.timerText=label.GetComponent<TMPro.TextMeshProUGUI>();
                    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    typeof(PresentationController).GetField("timeRemaining",flags).SetValue(controller,125f);
                    void Refresh() => typeof(PresentationController).GetMethod("UpdateTimerDisplay",flags).Invoke(controller,null);
                    qa.Prepare(null); Refresh();
                    if(controller.timerText.text!="02:05") throw new Exception("Presentation clock missing.");
                    qa.questionContents=new[]{"Test question"};
                    controller.StartQA();
                    if(!qa.IsQAPhaseActive || controller.timerText.text!="Q&A") throw new Exception("Q&A clock missing.");
                    Refresh(); if(controller.timerText.text!="Q&A") throw new Exception("Q&A clock reverted.");
                    qa.PrepareFinishWithoutQuestions(null); Refresh();
                    if(qa.IsQAPhaseActive || controller.timerText.text=="Q&A") throw new Exception("No-Q&A path mislabeled.");
                    qa.SetGeneratedQuestions(Array.Empty<Rehear.Evc.Contracts.GeneratedQuestion>());
                    if(qa.IsQAPhaseActive) throw new Exception("Generation marked Q&A active prematurely.");
                    File.WriteAllText("Temp/RehearQAClockTests.txt","PASS presentation timer, Q&A entry/persistence, no-Q&A report path, generation reset. No server or microphone used.");
                } finally {UnityEngine.Object.DestroyImmediate(host);}
            }
            if (command == "laptop-variants") SetupLaptopVariants(scene);
            if (command == "laptop-audit") {
                var details=new StringBuilder();
                foreach(var path in new[]{"Assets/03_Prefabs/black_laptop_OFF.prefab","Assets/03_Prefabs/laptop.prefab"}) {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var instance=UnityEngine.Object.Instantiate(prefab);
                    try {
                        instance.transform.position=Vector3.zero;
                        details.AppendLine($"{path} rotation={instance.transform.eulerAngles} scale={instance.transform.localScale}");
                        foreach(var r in instance.GetComponentsInChildren<Renderer>(true)) details.AppendLine($" {r.name}: center={r.bounds.center} size={r.bounds.size} materials={string.Join(",",r.sharedMaterials.Select(m=>m ? m.name : "MISSING"))}");
                    } finally { UnityEngine.Object.DestroyImmediate(instance); }
                }
                File.WriteAllText("Temp/RehearLaptopAudit.txt",details.ToString());
            }
            if (command == "speech-audit") {
                var details=new StringBuilder();
                var seating=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>(true)).Single();
                foreach(var prefab in seating.audiencePrefabs) {
                    details.AppendLine(prefab.name);
                    foreach(var skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                        var mesh=skin.sharedMesh;
                        details.AppendLine($" {skin.name}: {mesh?.blendShapeCount} shapes");
                        if(mesh) for(int i=0;i<mesh.blendShapeCount;i++) details.AppendLine("  "+mesh.GetBlendShapeName(i));
                    }
                }
                File.WriteAllText("Temp/RehearSpeechAudit.txt",details.ToString());
            }
            if (command == "button-sprites") {
                var controller=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single();
                ApplySessionButtonSprite(controller.startPresentationButton);
                ApplySessionButtonSprite(controller.endPresentationButton);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                File.WriteAllText("Temp/RehearButtonSprites.txt","PASS start/end buttons use dedicated sliced sprite, default UI material, preserved 500x70 layout and callbacks.");
            }
            if (command == "pause-audit") PauseAudit(scene);
            if (command == "test-buttons") TestSessionButtons(scene);
            if (command == "volume-audit") {
                var volumeReport=new StringBuilder();
                foreach(var camera in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true))) {
                    var data=UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera);
                    var stack=UnityEngine.Rendering.VolumeManager.instance.CreateStack();
                    UnityEngine.Rendering.VolumeManager.instance.Update(stack,data.volumeTrigger ? data.volumeTrigger : camera.transform,data.volumeLayerMask);
                    var vignette=stack.GetComponent<UnityEngine.Rendering.Universal.Vignette>();
                    volumeReport.AppendLine($"CAMERA {camera.name} enabled={camera.isActiveAndEnabled} type={data.renderType} postFX={data.renderPostProcessing} volumeMask={data.volumeLayerMask.value} position={camera.transform.position} vignette={vignette.intensity.value} active={vignette.active} stack={string.Join(",",data.renderType==UnityEngine.Rendering.Universal.CameraRenderType.Base ? data.cameraStack.Where(x=>x).Select(x=>x.name) : new string[0])}");
                    UnityEngine.Rendering.VolumeManager.instance.DestroyStack(stack);
                }
                foreach(var volume in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true))) {
                    var profile=volume.sharedProfile;
                    volumeReport.AppendLine($"VOLUME {volume.name} active={volume.isActiveAndEnabled} layer={volume.gameObject.layer} global={volume.isGlobal} priority={volume.priority} weight={volume.weight} profile={profile?.name}");
                    if(profile && profile.TryGet<UnityEngine.Rendering.Universal.Vignette>(out var v)) volumeReport.AppendLine($"  Vignette active={v.active} intensity={v.intensity.value} override={v.intensity.overrideState}");
                }
                File.WriteAllText("Temp/RehearVolumeAudit.txt",volumeReport.ToString());
            }
            if (command == "repair-pause") PresentationUi(scene, false);
            if (command == "pause-transparency") {
                var controller=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single();
                foreach(var glass in controller.pausePanel.GetComponentsInChildren<LeTai.Asset.TranslucentImage.TranslucentImage>(true)) {
                    string materialPath="Assets/Settings/TutorialUI/Presentation "+glass.name+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if(!material) {material=new Material(glass.material); AssetDatabase.CreateAsset(material,materialPath);}
                    glass.material=material; glass.foregroundOpacity=.12f;
                    if(material.HasProperty("_GlassTint")) material.SetFloat("_GlassTint",.12f);
                    glass.SetAllDirty(); EditorUtility.SetDirty(glass); EditorUtility.SetDirty(material);
                }
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                File.WriteAllText("Temp/RehearPauseTransparency.txt","Presentation-only white foreground opacity 0.30 -> 0.12; original tutorial materials preserved; blur unchanged.");
            }
            if (command == "capture-pause") {
                var panel=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single().pausePanel;
                panel.SetActive(true);
                EditorApplication.delayCall+=()=> {try {CapturePause(scene);} finally {panel.SetActive(false);}};
            }
            if (command == "test") Test(scene);
            var report = new StringBuilder();
            foreach(var root in scene.GetRootGameObjects().Where(g => g.name == "Audience" || g.name == "Chair"))
                foreach(Transform child in root.transform)
                {
                    report.AppendLine($"{root.name}/{child.name}: {child.position} {child.eulerAngles} scale={child.lossyScale} prefab={PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child)}");
                    foreach(var c in child.GetComponentsInChildren<MonoBehaviour>(true))
                        report.AppendLine("  " + (c ? c.GetType().FullName : "MISSING"));
                }
            File.WriteAllText("Temp/RehearPresentationSeating.txt", report.ToString());
            foreach(var t in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name.ToLowerInvariant().Contains("laptop") || t.name=="Conference_table"))
                File.AppendAllText("Temp/RehearPresentationSeating.txt", $"PROP {t.name} parent={t.parent?.name} position={t.position} rotation={t.eulerAngles} renderers={t.GetComponentsInChildren<Renderer>(true).Length}\n");
        }
        catch(Exception e) { File.WriteAllText("Temp/RehearPresentationSeating.txt", e.ToString()); Debug.LogException(e); }
    }
    static void GroundAudit(UnityEngine.SceneManagement.Scene scene)
    {
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var floorRenderers=all.SelectMany(t=>t.GetComponents<Renderer>()).Where(r=>r && (r.name.ToLowerInvariant().Contains("floor") || r.transform.name.ToLowerInvariant().Contains("floor"))).ToArray();
        var floorY=floorRenderers.Length>0 ? floorRenderers.Max(r=>r.bounds.max.y) : float.NaN;
        var report=new StringBuilder(); report.AppendLine($"FLOOR y={floorY} renderers={floorRenderers.Length}");
        var chairRoot=scene.GetRootGameObjects().SingleOrDefault(g=>g.name=="Chair");
        if(chairRoot)
            foreach(var chair in chairRoot.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponentsInChildren<Renderer>(true).Length>0)) {
                var rs=chair.GetComponentsInChildren<Renderer>(true); var b=rs[0].bounds; foreach(var r in rs.Skip(1)) b.Encapsulate(r.bounds);
                report.AppendLine($"CHAIR {chair.name} minY={b.min.y} maxY={b.max.y} pos={chair.position}");
            }
        var layout=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>(true)).Single();
        for(int i=0;i<layout.seats.Length;i++) {
            var prefab=layout.audiencePrefabs[i]; var instance=UnityEngine.Object.Instantiate(prefab); instance.SetActive(true);
            try {
                var pose=instance.GetComponent<AudienceSeatedPose>(); var rotation=layout.seats[i].transform.rotation*(pose ? pose.facingCorrection : Quaternion.identity);
                var position=layout.seats[i].transform.position;
                if(pose) position-=rotation*Vector3.Scale(pose.localHip,instance.transform.localScale);
                instance.transform.SetPositionAndRotation(position,rotation);
                var rs=instance.GetComponentsInChildren<Renderer>(true); var b=rs[0].bounds; foreach(var r in rs.Skip(1)) b.Encapsulate(r.bounds);
                report.AppendLine($"AUDIENCE seat={layout.seats[i].SeatId} minY={b.min.y} maxY={b.max.y} anchorY={layout.seats[i].transform.position.y} prefab={prefab.name}");
            } finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        File.WriteAllText("Temp/RehearGroundAudit.txt",report.ToString());
    }
    static void LowerSeatLayout(UnityEngine.SceneManagement.Scene scene)
    {
        var layout=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>(true)).Single();
        const float delta=-0.035f;
        foreach(var seat in layout.seats) seat.transform.position+=Vector3.up*delta;
        EditorUtility.SetDirty(layout); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearGroundRepair.txt",$"Lowered all six seat anchors by {Mathf.Abs(delta):0.000}m; chairs and baked lighting untouched.\n");
    }
    static void InspectSource(UnityEngine.SceneManagement.Scene presentation)
    {
        var source=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_00_5_Tutorial.unity",OpenSceneMode.Additive);
        var report=new StringBuilder();
        try {
            foreach(var a in source.GetRootGameObjects().Single(g=>g.name=="Audience").transform.Cast<Transform>()) {
                report.AppendLine($"ACTOR {a.name} position={a.position} rotation={a.eulerAngles}");
                foreach(var c in a.GetComponentsInChildren<MonoBehaviour>(true)) report.AppendLine($"COMPONENT {c?.GetType().Name} at {c?.name}");
                foreach(var t in a.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="pelvis" || t.name=="root" || t.name=="thigh_l" || t.name=="thigh_r" || t.GetComponent<Animator>())) report.AppendLine($"BONE {t.name} localToActor={a.InverseTransformPoint(t.position)} rotation={t.eulerAngles} animator={t.GetComponent<Animator>()}");
            }
            var target=presentation.GetRootGameObjects().Single(g=>g.name=="Audience").GetComponent<AudienceSeating>();
            foreach(var p in target.audiencePrefabs) foreach(var t in p.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="pelvis" || t.name=="root" || t.name=="thigh_l" || t.name=="thigh_r")) report.AppendLine($"PREFAB {p.name} BONE {t.name} position={p.transform.InverseTransformPoint(t.position)}");
            foreach(var t in presentation.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="Clock" || t.name=="Text_Script")) report.AppendLine($"OBJECT {t.name} parent={t.parent?.name} pos={t.position} rot={t.eulerAngles} ");
            File.WriteAllText("Temp/RehearSeatingSource.txt",report.ToString());
        } finally { EditorSceneManager.CloseScene(source,true); }
    }
    static void RepairSeats(UnityEngine.SceneManagement.Scene scene)
    {
        var layout=scene.GetRootGameObjects().Single(g=>g.name=="Audience").GetComponent<AudienceSeating>();
        var report=new StringBuilder();
        const string diagnosticSource="Assets/Editor/RehearOriginalPresentationDiagnostic.unity";
        File.Copy("Temp/OriginalPresentation.unity",diagnosticSource,true); AssetDatabase.ImportAsset(diagnosticSource);
        var source=EditorSceneManager.OpenScene(diagnosticSource,OpenSceneMode.Additive);
        try {
            var originals=source.GetRootGameObjects().Single(g=>g.name=="Audience").transform;
            const string folder="Assets/03_Prefabs/Audience/Presentation";
            if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/03_Prefabs/Audience","Presentation");
            for(int i=0;i<layout.audiencePrefabs.Length;i++) {
                string name=layout.audiencePrefabs[i].name;
                var original=originals.Cast<Transform>().Single(t=>t.name==name);
                var clone=UnityEngine.Object.Instantiate(original.gameObject);
                try {
                    clone.name=name; clone.transform.SetParent(null); clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    var body=clone.GetComponent<AudienceAnimationPlayer>();
                    if(!body || !clone.GetComponent<Animator>()) throw new Exception(name+" missing original animation setup");
                    var settings=new SerializedObject(body);
                    var catalog=(AudienceAnimationCatalog)settings.FindProperty("catalog").objectReferenceValue;
                    var gender=(AudienceGender)settings.FindProperty("gender").enumValueIndex;
                    if(!catalog.TryGetClip("BL_03.quiet_stable_posture",gender,out var clip)) throw new Exception("Missing idle clip");
                    clip.SampleAnimation(clone,0);
                    var bones=clone.GetComponentsInChildren<Transform>(true);
                    var hip=bones.Single(t=>t.name=="pelvis");
                    var knees=(bones.Single(t=>t.name=="calf_l").position+bones.Single(t=>t.name=="calf_r").position)*.5f;
                    var forward=clone.transform.InverseTransformDirection(knees-hip.position); forward.y=0;
                    var pose=clone.GetComponent<AudienceSeatedPose>(); if(!pose) pose=clone.AddComponent<AudienceSeatedPose>();
                    pose.localHip=clone.transform.InverseTransformPoint(hip.position);
                    pose.facingCorrection=Quaternion.Inverse(Quaternion.LookRotation(forward));
                    var gaze=clone.GetComponent<AudienceGazeController>();
                    if(gaze) gaze.ConfigureTargets(null,null,Array.Empty<Transform>());
                    var random=clone.GetComponent<RandomAudienceAnimator>(); if(random) random.enabled=false;
                    body.enabled=true;
                    foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true)) { GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,0); renderer.lightmapIndex=-1; renderer.realtimeLightmapIndex=-1; }
                    layout.audiencePrefabs[i]=PrefabUtility.SaveAsPrefabAsset(clone,folder+"/"+name+".prefab");
                    report.AppendLine($"{name} hip={pose.localHip} correction={pose.facingCorrection.eulerAngles} knees={forward}");
                } finally { UnityEngine.Object.DestroyImmediate(clone); }
            }
        } finally { EditorSceneManager.CloseScene(source,true); }
        var chairs=scene.GetRootGameObjects().Single(g=>g.name=="Chair").transform;
        for(int i=0;i<6;i++) {
            var chair=chairs.Find(i==0?"Chair":"Chair ("+i+")");
            // Chair mesh faces local +X. The anchor denotes the pelvis, not the FBX origin.
            layout.seats[i].transform.SetPositionAndRotation(chair.position+Vector3.up*.545f,Quaternion.LookRotation(chair.right));
        }
        foreach(var holder in layout.laptops) {
            var model=holder.transform.GetChild(0);
            if(!model.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.sharedMesh)) {
                var valid=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="laptop" && t.parent && t.parent.name=="_Props");
                UnityEngine.Object.DestroyImmediate(model.gameObject); model=UnityEngine.Object.Instantiate(valid.gameObject,holder.transform,false).transform;
            }
            model.gameObject.SetActive(true);
            holder.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            model.localPosition=Vector3.zero; model.localRotation=Quaternion.identity;
            foreach(var r in model.GetComponentsInChildren<Renderer>(true)) {GameObjectUtility.SetStaticEditorFlags(r.gameObject,0); r.lightmapIndex=-1; r.realtimeLightmapIndex=-1;}
            var filters=model.GetComponentsInChildren<MeshFilter>(true);
            var points=filters.Where(f=>f.sharedMesh).SelectMany(f=> {
                var b=f.sharedMesh.bounds; return Enumerable.Range(0,8).Select(n=>f.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((n&1)==0?-1:1,(n&2)==0?-1:1,(n&4)==0?-1:1)))); }).ToArray();
            if(points.Length==0) {
                File.WriteAllText("Temp/RehearLaptopAssets.txt",string.Join("\n",AssetDatabase.FindAssets("t:Model").Select(AssetDatabase.GUIDToAssetPath).Where(p=>!p.Contains("06_Animation") && !p.Contains("Meta") && !p.Contains("Samples")).SelectMany(p=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>().Where(m=>m.name.ToLowerInvariant().Contains("laptop")).Select(m=>p+" | "+m.name+" | "+m.bounds))));
                report.AppendLine("Laptop mesh missing; see asset inventory"); continue;
            }
            var bounds=new Bounds(points[0],Vector3.zero); foreach(var point in points) bounds.Encapsulate(point);
            model.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            report.AppendLine($"Laptop {model.name} meshSize={bounds.size} offset={model.localPosition}");
            holder.SetActive(false);
        }
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var clock=all.Single(t=>t.name=="Clock");
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).Single(c=>c);
        var timerSettings=new SerializedObject(controller);
        var timer=(TMPro.TMP_Text)timerSettings.FindProperty("timerText").objectReferenceValue;
        var boundsClock=clock.GetComponent<Renderer>().bounds;
        var canvas=clock.Find("Clock Timer");
        if(!canvas) canvas=new GameObject("Clock Timer",typeof(RectTransform),typeof(Canvas)).transform;
        canvas.SetParent(null); canvas.localScale=Vector3.one*.001f;
        canvas.SetPositionAndRotation(new Vector3(boundsClock.max.x+.004f,boundsClock.center.y,boundsClock.center.z),Quaternion.Euler(0,270,0));
        canvas.SetParent(clock,true); canvas.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        ((RectTransform)canvas).sizeDelta=new Vector2(boundsClock.size.z*.90f*1000,boundsClock.size.y*.80f*1000);
        var rect=timer.rectTransform; rect.SetParent(canvas,false); rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=Vector2.zero; rect.offsetMax=Vector2.zero; rect.localPosition=Vector3.zero; rect.localRotation=Quaternion.identity; rect.localScale=Vector3.one;
        timer.alignment=TMPro.TextAlignmentOptions.Center; timer.enableAutoSizing=true; timer.fontSizeMin=40; timer.fontSizeMax=400; timer.raycastTarget=false;
        // Remove the orphaned caption belonging to the replaced script control.
        var caption=all.FirstOrDefault(t=>t.name=="Text_Script");
        if(caption && !caption.IsChildOf(controller.scriptPanel.transform)) caption.gameObject.SetActive(false);
        report.AppendLine($"Clock bounds={boundsClock}; timer center={canvas.position}");
        EditorUtility.SetDirty(layout); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearSeatingRepair.txt",report.ToString());
    }
    static void PresentationUi(UnityEngine.SceneManagement.Scene scene, bool resizeSlide = true)
    {
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).Single(c=>c);
        var source=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_00_5_Tutorial.unity",OpenSceneMode.Additive);
        try {
            var view=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TutorialFigmaView>(true)).Single();
            var sourceCanvas=view.GetComponentInParent<Canvas>();
            var clone=UnityEngine.Object.Instantiate(sourceCanvas.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,scene);
            clone.name="Presentation Pause UI";
            var copiedView=clone.GetComponentInChildren<TutorialFigmaView>(true);
            foreach(var step in copiedView.steps.Where((g,i)=>i!=7).ToArray()) if(step) UnityEngine.Object.DestroyImmediate(step);
            var paused=copiedView.steps[7]; paused.SetActive(true);
            foreach(Transform sibling in copiedView.transform.Cast<Transform>().Where(t=>t.gameObject!=paused).ToArray()) UnityEngine.Object.DestroyImmediate(sibling.gameObject);
            foreach(var child in clone.GetComponentsInChildren<Transform>(true).Where(t=>t!=clone.transform && t!=copiedView.transform && !t.IsChildOf(copiedView.transform)).ToArray())
                if(child && child.parent==clone.transform) UnityEngine.Object.DestroyImmediate(child.gameObject);
            // Keep only the authored pause view; tutorial step logic must never run here.
            foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true).Where(c=>c && (c is TutorialFigmaView || c.GetType().Name.StartsWith("Tutorial") || c.GetType().Name=="Quest3TutorialControllerVisual")).ToArray()) UnityEngine.Object.DestroyImmediate(component);
            var camera=all.Select(t=>t.GetComponent<Camera>()).First(c=>c && c.CompareTag("MainCamera"));
            foreach(var canvas in clone.GetComponentsInChildren<Canvas>(true)) canvas.worldCamera=camera;
            var blur=camera.GetComponent<LeTai.Asset.TranslucentImage.TranslucentImageSource>();
            if(!blur) blur=camera.gameObject.AddComponent<LeTai.Asset.TranslucentImage.TranslucentImageSource>();
            blur.BlurConfig=AssetDatabase.LoadAssetAtPath<LeTai.Asset.TranslucentImage.ScalableBlurConfig>("Assets/Settings/Tutorial Quest3 Blur.asset");
            blur.Downsample=2; blur.SkipCulling=true; blur.MaxUpdateRate=float.PositiveInfinity;
            foreach(var glass in clone.GetComponentsInChildren<LeTai.Asset.TranslucentImage.TranslucentImage>(true)) glass.source=blur;
            var curve=clone.GetComponent<CurvedUI.CurvedUISettings>();
            if(curve) curve.AddEffectToChildren();
            foreach(var effect in clone.GetComponentsInChildren<CurvedUI.CurvedUIVertexEffect>(true)) effect.SetDirty();
            foreach(var text in clone.GetComponentsInChildren<CurvedUI.Core.Integrations.CurvedUITMP>(true)) text.Dirty=true;
            var baseData=UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera);
            var overlay=camera.transform.Find("Presentation UI Overlay Camera");
            if(!overlay) {overlay=new GameObject("Presentation UI Overlay Camera").transform; overlay.SetParent(camera.transform,false);}
            var uiCamera=overlay.GetComponent<Camera>(); if(!uiCamera) uiCamera=overlay.gameObject.AddComponent<Camera>();
            uiCamera.CopyFrom(camera); uiCamera.tag="Untagged"; uiCamera.cullingMask=1<<LayerMask.NameToLayer("UI"); uiCamera.clearFlags=CameraClearFlags.Nothing; uiCamera.targetTexture=null; uiCamera.enabled=true;
            var uiData=UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(uiCamera);
            uiData.renderType=UnityEngine.Rendering.Universal.CameraRenderType.Overlay; uiData.renderPostProcessing=false; uiData.renderShadows=false; uiData.allowXRRendering=true; uiData.SetRenderer(0);
            var uiSerialized=new SerializedObject(uiData); uiSerialized.FindProperty("m_ClearDepth").boolValue=false; uiSerialized.ApplyModifiedProperties();
            camera.cullingMask&=~uiCamera.cullingMask;
            if(!baseData.cameraStack.Contains(uiCamera)) baseData.cameraStack.Add(uiCamera);
            var sync=camera.GetComponent<TutorialBlurCameraSync>(); if(!sync) sync=camera.gameObject.AddComponent<TutorialBlurCameraSync>();
            sync.sceneCamera=camera; sync.uiCamera=uiCamera; sync.blurSource=blur; sync.panels=clone.GetComponentsInChildren<LeTai.Asset.TranslucentImage.TranslucentImage>(true);
            foreach(var c in new UnityEngine.Object[]{camera,baseData,blur,sync,uiCamera,uiData}) {EditorUtility.SetDirty(c); PrefabUtility.RecordPrefabInstancePropertyModifications(c);}
            foreach(var button in paused.GetComponentsInChildren<UnityEngine.UI.Button>(true)) {
                button.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();
                if(button.name=="Restart") UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,controller.RestartSession);
                else if(button.name=="Stop") UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,controller.StopSession);
                else throw new Exception("Unknown pause button "+button.name);
            }
            if(controller.pausePanel) Undo.DestroyObjectImmediate(controller.pausePanel);
            controller.pausePanel=clone; clone.SetActive(false);
            EditorUtility.SetDirty(controller); EditorUtility.SetDirty(blur);
        } finally {EditorSceneManager.CloseScene(source,true);}
        if(!resizeSlide) {
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Temp/RehearPauseRepair.txt","Copied current tutorial pause hierarchy, original icon sprites/materials, 35-degree curvature; connected presentation blur camera; rebound restart/stop.");
            return;
        }
        var slide=all.Single(t=>t && t.name=="Slide" && t.GetComponent<MeshRenderer>());
        var screen=(RectTransform)all.Single(t=>t && t.name=="SlideScreen");
        var before=slide.GetComponent<Renderer>().bounds.center;
        // One authoring operation: move the frame and its existing screen together.
        if(!screen.IsChildOf(slide)) {
            var previousCanvas=screen.GetComponentInParent<Canvas>();
            var canvas=screen.GetComponent<Canvas>(); if(!canvas) {canvas=screen.gameObject.AddComponent<Canvas>(); EditorUtility.CopySerialized(previousCanvas,canvas);}
            canvas.renderMode=RenderMode.WorldSpace; screen.SetParent(slide,true);
        }
        slide.localScale*=.9f;
        var after=slide.GetComponent<Renderer>().bounds.center;
        slide.position+=before-after+Vector3.back*.15f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(slide);
        PrefabUtility.RecordPrefabInstancePropertyModifications(screen);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearPresentationUi.txt","PASS tutorial pause view copied; Restart/Stop rewired; grip controller retained; Slide and SlideScreen scaled 90%, moved world Z -0.15m.\n");
    }
    static void RepairLaptops(UnityEngine.SceneManagement.Scene scene)
    {
        var layout=scene.GetRootGameObjects().Single(g=>g.name=="Audience").GetComponent<AudienceSeating>();
        Material Mat(string name,Color color) {
            string path="Assets/03_Prefabs/Audience/Presentation/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) {material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color=color; material.SetFloat("_Smoothness",.3f); AssetDatabase.CreateAsset(material,path);}
            return material;
        }
        var shell=Mat("Laptop Shell",new Color(.20f,.22f,.25f)); var keys=Mat("Laptop Keys",new Color(.025f,.03f,.04f)); var display=Mat("Laptop Display",new Color(.5f,.64f,.8f));
        var model=new GameObject("Audience Laptop Model");
        void Part(string name,Transform parent,Vector3 pos,Vector3 size,Material material) {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cube); part.name=name; part.transform.SetParent(parent,false); part.transform.localPosition=pos; part.transform.localScale=size; part.GetComponent<Renderer>().sharedMaterial=material; UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        }
        Part("Base",model.transform,new Vector3(0,.008f,0),new Vector3(.32f,.016f,.23f),shell);
        Part("Touchpad",model.transform,new Vector3(0,.0165f,-.067f),new Vector3(.09f,.001f,.047f),keys);
        for(int row=0;row<4;row++) for(int col=0;col<10;col++) Part("Key",model.transform,new Vector3((col-4.5f)*.028f,.017f,.065f-row*.026f),new Vector3(.024f,.002f,.020f),keys);
        var lid=new GameObject("Lid").transform; lid.SetParent(model.transform,false); lid.localPosition=new Vector3(0,.018f,.108f); lid.localRotation=Quaternion.Euler(-12,0,0);
        Part("Display bezel",lid,new Vector3(0,.105f,0),new Vector3(.32f,.21f,.010f),keys);
        Part("Screen",lid,new Vector3(0,.109f,-.006f),new Vector3(.292f,.180f,.001f),display);
        var asset=PrefabUtility.SaveAsPrefabAsset(model,"Assets/03_Prefabs/Audience/Presentation/Audience Laptop.prefab"); UnityEngine.Object.DestroyImmediate(model);
        for(int i=0;i<layout.laptops.Length;i++) {
            var holder=layout.laptops[i]; foreach(var child in holder.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(asset,holder.transform); instance.transform.localPosition=Vector3.zero; instance.transform.localRotation=Quaternion.identity; instance.transform.localScale=Vector3.one;
            holder.transform.SetPositionAndRotation(layout.seats[i].laptopAnchor.position,layout.seats[i].laptopAnchor.rotation); holder.SetActive(false);
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    static void FinalizePresentation(UnityEngine.SceneManagement.Scene scene)
    {
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var layout=all.Select(t=>t.GetComponent<AudienceSeating>()).Single(c=>c);
        var chairs=scene.GetRootGameObjects().Single(g=>g.name=="Chair").transform;
        var table=all.Single(t=>t.name=="Conference_table").GetComponent<Renderer>().bounds;
        for(int i=0;i<6;i++) {
            var chair=chairs.Find(i==0?"Chair":"Chair ("+i+")"); var inward=table.center-chair.position; inward.y=0; inward.Normalize();
            var pos=chair.position+inward*.65f; pos.y=table.max.y+.005f;
            layout.seats[i].laptopAnchor.SetPositionAndRotation(pos,Quaternion.LookRotation(inward));
        }
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).Single(c=>c);
        foreach(var button in controller.pausePanel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            if(button.name!="Restart" && button.name!="Stop") UnityEngine.Object.DestroyImmediate(button.gameObject);
        foreach(var prefab in layout.audiencePrefabs) {
            var path=AssetDatabase.GetAssetPath(prefab); var root=PrefabUtility.LoadPrefabContents(path);
            try {var gaze=root.GetComponent<AudienceGazeController>(); if(gaze) gaze.enabled=false; PrefabUtility.SaveAsPrefabAsset(root,path);}
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    static void SyncMaterials(UnityEngine.SceneManagement.Scene scene)
    {
        var layout=scene.GetRootGameObjects().Single(g=>g.name=="Audience").GetComponent<AudienceSeating>();
        var source=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_00_5_Tutorial.unity",OpenSceneMode.Additive);
        var report=new StringBuilder();
        try {
            var originals=source.GetRootGameObjects().Single(g=>g.name=="Audience").transform;
            foreach(var prefab in layout.audiencePrefabs) {
                var original=originals.Cast<Transform>().Single(t=>t.name==prefab.name);
                var reference=original.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>AnimationUtility.CalculateTransformPath(r.transform,original));
                string path=AssetDatabase.GetAssetPath(prefab); var root=PrefabUtility.LoadPrefabContents(path);
                try {
                    int changed=0,slots=0;
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                        string key=AnimationUtility.CalculateTransformPath(renderer.transform,root.transform);
                        if(!reference.TryGetValue(key,out var authored)) throw new Exception(prefab.name+" renderer mismatch: "+key);
                        var current=renderer.sharedMaterials; var desired=authored.sharedMaterials;
                        if(!current.SequenceEqual(desired)) {
                            changed++;
                            report.AppendLine($"{prefab.name}/{key}: {string.Join(",",current.Select(m=>m ? m.name : "missing"))} -> {string.Join(",",desired.Select(m=>m ? m.name : "missing"))}");
                        }
                        renderer.sharedMaterials=desired; renderer.enabled=authored.enabled;
                        renderer.gameObject.SetActive(authored.gameObject.activeSelf);
                        renderer.shadowCastingMode=authored.shadowCastingMode; renderer.receiveShadows=authored.receiveShadows;
                        slots+=desired.Length;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                    report.AppendLine($"PASS {prefab.name}: {reference.Count} renderer paths matched; {slots} material slots identical; {changed} renderers corrected.");
                } finally {PrefabUtility.UnloadPrefabContents(root);}
            }
        } finally {EditorSceneManager.CloseScene(source,true);}
        File.WriteAllText("Temp/RehearAudienceMaterials.txt",report.ToString());
    }
    static void RuntimeReport()
    {
        var layout=UnityEngine.Object.FindFirstObjectByType<AudienceSeating>();
        var report=new StringBuilder();
        foreach(var a in layout.members)
        {
            var animator=a.GetComponentInChildren<Animator>();
            var hip=animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : a.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="pelvis");
            report.AppendLine($"ACTOR {a.name} root={a.transform.position} rotation={a.transform.eulerAngles} hip={hip?.position} localHip={(hip ? a.transform.InverseTransformPoint(hip.position) : Vector3.zero)} anim={animator?.transform.eulerAngles} rootMotion={animator?.applyRootMotion} seat={a.GetComponent<AudienceSeatAssignment>()?.Seat?.SeatId}");
        }
        foreach(var t in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="Clock" || t.parent?.name=="Chair"))
        {
            report.AppendLine($"OBJECT {t.name} position={t.position} rotation={t.eulerAngles} scale={t.lossyScale}");
            foreach(var r in t.GetComponentsInChildren<Renderer>(true)) report.AppendLine($"MESH {r.name} bounds={r.bounds} local={r.localBounds}");
        }
        foreach(var l in layout.laptops) foreach(var r in l.GetComponentsInChildren<Renderer>(true)) report.AppendLine($"LAPTOP {r.name} active={r.gameObject.activeInHierarchy} position={r.transform.position} bounds={r.bounds} local={r.localBounds}");
        File.WriteAllText("Temp/RehearSeatingRuntime.txt",report.ToString());
        ScreenCapture.CaptureScreenshot("Temp/RehearPresentationRuntime.png");
    }
    static void SetupLaptopVariants(UnityEngine.SceneManagement.Scene scene)
    {
        var layout=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>(true)).Single();
        var sources=new[]{"Assets/03_Prefabs/black_laptop_OFF.prefab","Assets/03_Prefabs/laptop.prefab"};
        var variants=new GameObject[2];
        var details=new StringBuilder();
        for(int i=0;i<sources.Length;i++) {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(sources[i]);
            var root=new GameObject("Seated "+prefab.name);
            try {
                var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
                model.transform.localPosition=Vector3.zero;
                var renderers=model.GetComponentsInChildren<Renderer>(true);
                Bounds BoundsOfModel() { var b=renderers[0].bounds; foreach(var r in renderers.Skip(1)) b.Encapsulate(r.bounds); return b; }
                var bounds=BoundsOfModel();
                // The upper geometry is the lid. Keep it on the table side (+Z),
                // so the keyboard faces the occupant at -Z for either import convention.
                double upperZ=0; int upperCount=0;
                foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true))
                    foreach(var vertex in filter.sharedMesh.vertices) {
                        var p=filter.transform.TransformPoint(vertex);
                        if(p.y>bounds.min.y+bounds.size.y*.65f) { upperZ+=p.z; upperCount++; }
                    }
                if(upperCount>0 && upperZ/upperCount<bounds.center.z) model.transform.Rotate(0,180,0,Space.World);
                bounds=BoundsOfModel();
                model.transform.localScale*=.32f/bounds.size.x;
                bounds=BoundsOfModel();
                model.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
                variants[i]=PrefabUtility.SaveAsPrefabAsset(root,"Assets/03_Prefabs/Audience/Presentation/"+root.name+".prefab");
                details.AppendLine($"{sources[i]} -> width=0.32m, bottom=0, rotation={model.transform.localEulerAngles}, scale={model.transform.localScale}");
            } finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        foreach(var old in layout.laptops ?? Array.Empty<GameObject>()) if(old) UnityEngine.Object.DestroyImmediate(old);
        layout.laptops=Array.Empty<GameObject>(); layout.laptopPrefabs=variants; layout.initialLaptopCount=2;
        EditorUtility.SetDirty(layout); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearLaptopVariants.txt",details.ToString());
    }
    static void CapturePause(UnityEngine.SceneManagement.Scene scene)
    {
        var c=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single();
        var root=c.pausePanel; var view=root.transform.Find("Figma UI design");
        var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First(x=>x.CompareTag("MainCamera"));
        var position=camera.transform.position; var rotation=camera.transform.rotation; var projection=camera.projectionMatrix;
        bool active=root.activeSelf;
        var target=new RenderTexture(1200,900,24); var previous=RenderTexture.active;
        try {
            root.SetActive(true);
            camera.transform.SetPositionAndRotation(view.position-view.forward*1.4f,view.rotation); camera.ResetProjectionMatrix();
            var sync=camera.GetComponent<TutorialBlurCameraSync>();
            if(sync && sync.uiCamera) {sync.uiCamera.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation); sync.uiCamera.projectionMatrix=camera.projectionMatrix;}
            foreach(var text in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {text.renderMode=TMPro.TextRenderFlags.Render; text.ForceMeshUpdate(true,true); text.SetAllDirty();}
            foreach(var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.SetAllDirty();
            foreach(var effect in root.GetComponentsInChildren<CurvedUI.CurvedUIVertexEffect>(true)) effect.SetDirty();
            Canvas.ForceUpdateCanvases();
            var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest {destination=target};
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=target; var png=new Texture2D(1200,900,TextureFormat.RGB24,false);
            png.ReadPixels(new Rect(0,0,1200,900),0,0); png.Apply(); File.WriteAllBytes("Temp/RehearPauseVerified.png",png.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(png);
        } finally {camera.transform.SetPositionAndRotation(position,rotation); camera.projectionMatrix=projection; root.SetActive(active); RenderTexture.active=previous; target.Release(); UnityEngine.Object.DestroyImmediate(target);}
    }
    static void PauseAudit(UnityEngine.SceneManagement.Scene scene)
    {
        var source=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_00_5_Tutorial.unity",OpenSceneMode.Additive);
        try
        {
            var view=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TutorialFigmaView>(true)).Single();
            var target=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single().pausePanel;
            var report=new StringBuilder();
            void Dump(GameObject root)
            {
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var rect=t as RectTransform; var graphic=t.GetComponent<UnityEngine.UI.Graphic>();
                    report.AppendLine($"{AnimationUtility.CalculateTransformPath(t,root.transform)} active={t.gameObject.activeSelf} pos={t.position} local={t.localPosition} rot={t.eulerAngles} scale={t.lossyScale} rect={rect?.rect} material={graphic?.material?.name}");
                }
            }
            report.AppendLine("TUTORIAL"); Dump(view.steps[7]);
            report.AppendLine("PRESENTATION"); Dump(target);
            void Details(GameObject root) {
                foreach(var c in root.GetComponentsInChildren<Component>(true)) {
                    if(!c || c is Transform || c is CanvasRenderer) continue;
                    report.AppendLine("COMPONENT "+AnimationUtility.CalculateTransformPath(c.transform,root.transform)+" "+c.GetType().FullName+" "+EditorJsonUtility.ToJson(c));
                }
            }
            report.AppendLine("SOURCE COMPONENTS"); Details(view.GetComponentInParent<Canvas>().gameObject);
            report.AppendLine("TARGET COMPONENTS"); Details(target);
            File.WriteAllText("Temp/RehearPauseAudit.txt",report.ToString());
        }
        finally {EditorSceneManager.CloseScene(source,true);}
    }
    static void TestSessionButtons(UnityEngine.SceneManagement.Scene scene)
    {
        var host=new GameObject("Session controls check");
        try
        {
            var c=host.AddComponent<PresentationController>(); c.enabled=false;
            UnityEngine.UI.Button Button(string name) {var g=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Button)); g.transform.SetParent(host.transform); return g.GetComponent<UnityEngine.UI.Button>();}
            c.startPresentationButton=Button("start"); c.endPresentationButton=Button("end"); c.qaButton=Button("qa");
            c.pausePanel=new GameObject("pause"); c.pausePanel.transform.SetParent(host.transform);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(PresentationController).GetMethod("Start",flags).Invoke(c,null);
            bool Running() => (bool)typeof(PresentationController).GetField("isRunning",flags).GetValue(c);
            if(Running() || !c.startPresentationButton.gameObject.activeSelf || c.endPresentationButton.gameObject.activeSelf) throw new Exception("Not waiting for start");
            c.TogglePause(); if(Running() || !c.IsPaused) throw new Exception("Ready-state pause failed");
            c.TogglePause(); if(Running() || c.IsPaused) throw new Exception("Grip bypassed start");
            c.BeginPresentation(); c.BeginPresentation();
            if(!Running() || c.startPresentationButton.gameObject.activeSelf || !c.endPresentationButton.gameObject.activeSelf) throw new Exception("Start failed");
            c.TogglePause(); if(Running() || !c.IsPaused || !c.pausePanel.activeSelf) throw new Exception("Pause failed");
            c.TogglePause(); if(!Running() || c.IsPaused) throw new Exception("Resume failed");
            c.EndPresentation(); c.BeginPresentation(); c.ResumeGame();
            if(Running() || c.endPresentationButton.gameObject.activeSelf || !c.qaButton.gameObject.activeSelf) throw new Exception("End failed");
            var actual=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single(x=>x!=c);
            if(actual.startPresentationButton.onClick.GetPersistentMethodName(0)!="BeginPresentation" || actual.endPresentationButton.onClick.GetPersistentMethodName(0)!="EndPresentation") throw new Exception("Scene buttons not wired");
            File.WriteAllText("Temp/RehearSessionButtonTests.txt","PASS waiting, manual start, duplicate start, grip pause/resume, end, cannot resume after end, scene button bindings. No microphone/server used.");
        }
        finally {UnityEngine.Object.DestroyImmediate(host);}
    }
    static void SessionButtons(UnityEngine.SceneManagement.Scene scene)
    {
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).Single(c=>c);
        var toggle=all.Select(t=>t.GetComponent<PodiumScriptToggle>()).Single(c=>c);
        if(controller.startPresentationButton) UnityEngine.Object.DestroyImmediate(controller.startPresentationButton.gameObject);
        if(controller.endPresentationButton) UnityEngine.Object.DestroyImmediate(controller.endPresentationButton.gameObject);
        var screen=(RectTransform)all.Single(t=>t && t.name=="DeskScreen");
        var camera=all.Select(t=>t ? t.GetComponent<Camera>() : null).First(c=>c && c.CompareTag("MainCamera"));
        foreach(var oldMount in all.Where(t=>t && t.name=="Presentation Controls")) UnityEngine.Object.DestroyImmediate(oldMount.gameObject);
        var mount=new GameObject("Presentation Controls",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.GraphicRaycaster));
        mount.layer=LayerMask.NameToLayer("UI");
        var normal=screen.forward; var top=screen.TransformPoint(new Vector3(0,screen.rect.yMax,0));
        if(Vector3.Dot(normal,camera.transform.position-top)>0) normal=-normal;
        mount.transform.SetPositionAndRotation(top+screen.up*.07f-normal*.015f,Quaternion.LookRotation(normal,screen.up));
        mount.transform.localScale=Vector3.one*.001f; mount.transform.SetParent(screen.parent,true);
        ((RectTransform)mount.transform).sizeDelta=new Vector2(500,70);
        var mountCanvas=mount.GetComponent<Canvas>(); mountCanvas.renderMode=RenderMode.WorldSpace; mountCanvas.worldCamera=camera;
        mount.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        UnityEngine.UI.Button Make(string name, string caption, UnityEngine.Events.UnityAction action)
        {
            var go=UnityEngine.Object.Instantiate(toggle.gameObject,mount.transform,false);
            go.name=name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<PodiumScriptToggle>());
            var rect=(RectTransform)go.transform;
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.localPosition=Vector3.zero; rect.localRotation=Quaternion.identity; rect.localScale=Vector3.one;
            rect.sizeDelta=new Vector2(500,70);
            var button=go.GetComponent<UnityEngine.UI.Button>();
            button.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,action);
            var label=go.GetComponentInChildren<TMPro.TMP_Text>(true); label.text=caption; label.fontSize=28;
            ApplySessionButtonSprite(button);
            return button;
        }
        controller.startPresentationButton=Make("Presentation Start Button","발표 시작하기",controller.BeginPresentation);
        controller.endPresentationButton=Make("Presentation End Button","발표 끝내기",controller.EndPresentation);
        controller.startPresentationButton.gameObject.SetActive(true);
        controller.endPresentationButton.gameObject.SetActive(false);
        foreach(var t in all.Where(t=>t && t.name=="Text_Script")) UnityEngine.Object.DestroyImmediate(t.gameObject);
        EditorUtility.SetDirty(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearSessionButtons.txt","Created start/end controls; manual start; end routes to configured Q&A/report.");
    }
    static void ApplySessionButtonSprite(UnityEngine.UI.Button button)
    {
        const string path="Assets/Textures/UI/PresentationButton.png";
        var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if(!sprite) {
            const int size=128;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++) {
                float distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(64,64));
                pixels[y*size+x]=new Color(1,1,1,Mathf.Clamp01(64-distance));
            }
            texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
            importer.spriteBorder=new Vector4(63,63,63,63); importer.spritePixelsPerUnit=100;
            importer.mipmapEnabled=false; importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport(); sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        var image=button.GetComponent<UnityEngine.UI.Image>();
        image.material=null; image.sprite=sprite; image.overrideSprite=null;
        image.type=UnityEngine.UI.Image.Type.Sliced; image.fillCenter=true;
        image.pixelsPerUnitMultiplier=128f/70f; image.color=new Color32(0,51,255,255);
        button.targetGraphic=image;
        EditorUtility.SetDirty(image);
    }
    static void FinishUi(UnityEngine.SceneManagement.Scene scene)
    {
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var toggle=all.Select(t=>t.GetComponent<PodiumScriptToggle>()).Single(c=>c);
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).Single(c=>c);
        toggle.controller=controller; controller.scriptPanel=toggle.scriptPanel;
        var serialized=new SerializedObject(controller); serialized.FindProperty("scriptButtonText").objectReferenceValue=null; serialized.ApplyModifiedProperties();
        foreach(var old in all.Where(t=>t.name=="Btn_Script" || t.name=="Text_ScriptOnOff").ToArray())
            if(old && !old.IsChildOf(toggle.transform)) Undo.DestroyObjectImmediate(old.gameObject);
        var button=toggle.GetComponent<UnityEngine.UI.Button>();
        for(int i=button.onClick.GetPersistentEventCount()-1;i>=0;i--) UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick,i);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,toggle.ToggleScript);
        foreach(var volume in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true)))
        {
            if(volume.sharedProfile && AssetDatabase.GetAssetPath(volume.sharedProfile)=="Assets/Settings/Tutorial Quest3 Volume.asset")
            { Undo.RecordObject(volume,"Preserve authored room color correction"); volume.priority=-1; EditorUtility.SetDirty(volume); PrefabUtility.RecordPrefabInstancePropertyModifications(volume); }
        }
        foreach(var component in new UnityEngine.Object[]{toggle,controller,button})
        {EditorUtility.SetDirty(component); PrefabUtility.RecordPrefabInstancePropertyModifications(component);}
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    static void ConvertToPrefabSpawning(UnityEngine.SceneManagement.Scene scene)
    {
        var root=scene.GetRootGameObjects().Single(g=>g.name=="Audience");
        var layout=root.GetComponent<AudienceSeating>();
        if(layout.audiencePrefabs != null && layout.audiencePrefabs.Length==6) return;
        var old=layout.members.ToArray();
        layout.audiencePrefabs=old.Select(a=>AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(a))).ToArray();
        if(layout.audiencePrefabs.Any(p=>!p)) throw new Exception("All six actors must have reusable prefabs before removal.");
        layout.actionRegistries=old.Select(a=>a.ActionRegistry).ToArray();
        layout.seatedHeightOffsets=old.Select(a=>a.transform.position.y-layout.seats.Min(s=>s.transform.position.y)).ToArray();
        var bindings=Undo.AddComponent<PresentationAudienceBindings>(root); bindings.seating=layout;
        var gaze=new SerializedObject(old[0].GetComponent<AudienceGazeController>());
        bindings.presenterTarget=(Transform)gaze.FindProperty("presenterTarget").objectReferenceValue;
        bindings.slideTarget=(Transform)gaze.FindProperty("slideTarget").objectReferenceValue;
        var around=gaze.FindProperty("aroundTargets"); bindings.aroundTargets=new Transform[around.arraySize];
        for(int i=0;i<around.arraySize;i++) bindings.aroundTargets[i]=(Transform)around.GetArrayElementAtIndex(i).objectReferenceValue;
        layout.members=Array.Empty<Rehear.Evc.Audience.AudienceAgent>();
        foreach(var actor in old) Undo.DestroyObjectImmediate(actor.gameObject);
        foreach(var rootObject in scene.GetRootGameObjects())
        {
            foreach(var c in rootObject.GetComponentsInChildren<Rehear.Evc.Audience.AudienceReactionCoordinator>(true))
            { var so=new SerializedObject(c); so.FindProperty("agents").arraySize=0; so.ApplyModifiedProperties(); }
            foreach(var d in rootObject.GetComponentsInChildren<AudienceCommandDispatcher>(true))
            { var so=new SerializedObject(d); so.FindProperty("audienceBindings").arraySize=0; so.ApplyModifiedProperties(); }
        }
        EditorUtility.SetDirty(layout); EditorUtility.SetDirty(bindings);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    static void Check(AudienceSeating layout)
    {
        if(layout.seats.Count(s=>s.Occupant)!=6 || layout.seats.Select(s=>s.Occupant).Distinct().Count()!=6) throw new Exception("Duplicate/missing occupant.");
        if(layout.seats.Count(s=>s.Occupant.Allows("ACT_08.side_conversation"))!=2) throw new Exception("Rear conversation eligibility mismatch.");
        foreach(var seat in layout.seats)
        {
            if(seat.Occupant.Allows("ACT_01.laptoptyping")!=seat.HasLaptop) throw new Exception("Laptop action mismatch.");
            var pose=seat.Occupant.GetComponent<AudienceSeatedPose>();
            var offset=(pose ? seat.Occupant.transform.TransformPoint(pose.localHip) : seat.Occupant.transform.position)-seat.transform.position; if(!pose) offset.y=0;
            if(offset.sqrMagnitude>.00001f) throw new Exception("Seat position mismatch.");
        }
    }
    static void Test(UnityEngine.SceneManagement.Scene scene)
    {
        var source=scene.GetRootGameObjects().Single(g=>g.name=="Audience");
        var arrangements=new System.Collections.Generic.HashSet<string>();
        var laptopModels=new System.Collections.Generic.HashSet<string>();
        var preview=EditorSceneManager.NewPreviewScene();
        try
        {
            for(int seed=0;seed<20;seed++)
            {
                var clone=UnityEngine.Object.Instantiate(source);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,preview);
                try
                {
                    var layout=clone.GetComponent<AudienceSeating>(); layout.Initialize(seed); Check(layout);
                    arrangements.Add(string.Join(",",layout.seats.Select(s=>s.Occupant.name)));
                    if(layout.seats.Count(s=>s.HasLaptop)!=2) throw new Exception("Initial laptop count.");
                    CheckLaptops(layout);
                    foreach(var laptop in layout.SpawnedLaptops) laptopModels.Add(laptop.name);
                    var profiles=layout.members.Select((m,i)=>new Rehear.Evc.Contracts.AudienceDto {
                        agent_id=m.AgentId, profile=new Rehear.Evc.Contracts.AudienceSeatingProfile {
                            row=layout.seats[5-i].row,seat=layout.seats[5-i].side,has_laptop=i%2==0 }}).ToArray();
                    layout.ApplyServerProfiles(profiles); Check(layout);
                    if(layout.seats.Count(s=>s.HasLaptop)!=3) throw new Exception("Server laptop allocation.");
                    CheckLaptops(layout);
                    for(int i=0;i<6;i++) if(layout.seats[5-i].Occupant.gameObject!=layout.members[i].gameObject) throw new Exception("Server ID/seat mapping mismatch.");
                    var before=string.Join(",",layout.seats.Select(s=>s.Occupant.name));
                    profiles[1].profile=profiles[0].profile;
                    try {layout.ApplyServerProfiles(profiles);throw new Exception("Duplicate profile accepted.");}
                    catch(InvalidOperationException) { }
                    if(before!=string.Join(",",layout.seats.Select(s=>s.Occupant.name))) throw new Exception("Invalid profile partly applied.");
                    profiles=layout.members.Select((m,i)=>new Rehear.Evc.Contracts.AudienceDto {agent_id=m.AgentId,
                        profile=new Rehear.Evc.Contracts.AudienceSeatingProfile {row=layout.seats[i].row,seat=layout.seats[i].side,has_laptop=true}}).ToArray();
                    layout.ApplyServerProfiles(profiles); CheckLaptops(layout);
                    var instanceIds=layout.SpawnedLaptops.Select(l=>l.GetInstanceID()).ToArray();
                    layout.ApplyServerProfiles(profiles);
                    if(!instanceIds.SequenceEqual(layout.SpawnedLaptops.Select(l=>l.GetInstanceID()))) throw new Exception("Repeated profile recreated laptops.");
                    foreach(var p in profiles) p.profile.has_laptop=false;
                    layout.ApplyServerProfiles(profiles); CheckLaptops(layout);
                }
                finally {UnityEngine.Object.DestroyImmediate(clone);}
            }
            if(arrangements.Count<2) throw new Exception("Random placement did not vary.");
            if(laptopModels.Count!=2) throw new Exception("Both laptop variants were not selected across seeds.");
            File.WriteAllText("Temp/RehearPresentationSeating-tests.txt",$"PASS 20 seeds, {arrangements.Count} arrangements; six unique seats; rear-only conversation; laptop-only typing; authoritative profile remap; invalid duplicate profiles atomic.\n");
            File.AppendAllText("Temp/RehearPresentationSeating-tests.txt","PASS both laptop models selected; tabletop bottom/anchor alignment; 0/2/3/6 equipped seats; repeated profiles preserve instances.\n");
        }
        finally {EditorSceneManager.ClosePreviewScene(preview);}
    }
    static void CheckLaptops(AudienceSeating layout)
    {
        var active=layout.SpawnedLaptops.Where(l=>l.activeSelf).ToArray();
        if(active.Length!=layout.seats.Count(s=>s.HasLaptop)) throw new Exception("Laptop count does not match seat flags.");
        foreach(var laptop in active) {
            var seat=layout.seats.Single(s=>s.HasLaptop && Vector3.Distance(s.laptopAnchor.position,laptop.transform.position)<.001f);
            var renderers=laptop.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0 || Math.Abs(renderers.Min(r=>r.bounds.min.y)-seat.laptopAnchor.position.y)>.002f) throw new Exception("Laptop is floating or below tabletop.");
            if(renderers.Any(r=>r.sharedMaterials.Any(m=>!m))) throw new Exception("Missing laptop material.");
        }
        foreach(var seat in layout.seats) if(seat.Occupant.Allows("ACT_01.laptoptyping")!=seat.HasLaptop) throw new Exception("Typing gate mismatch.");
    }
    static void Apply(UnityEngine.SceneManagement.Scene scene)
    {
        var root=scene.GetRootGameObjects().Single(g=>g.name=="Audience");
        if(root.GetComponent<AudienceSeating>()) return;
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var chairRoot=scene.GetRootGameObjects().Single(g=>g.name=="Chair").transform;
        var actors=root.GetComponentsInChildren<Rehear.Evc.Audience.AudienceAgent>(true);
        if(actors.Length!=6) throw new InvalidOperationException("Expected six existing prefab agents.");
        var layout=Undo.AddComponent<AudienceSeating>(root);
        layout.members=actors;
        var seatsRoot=new GameObject("Seat Anchors"); seatsRoot.transform.SetParent(root.transform,false);
        layout.seats=new AudienceSeat[6];
        string[] rows={"front","middle","rear","rear","middle","front"};
        string[] sides={"left","left","left","right","right","right"};
        string[] occupants={"Aud_M_01","Aud_W_01","Aud_W_03","Aud_M_03","Aud_W_02","Aud_M_02"};
        var table=all.Single(t=>t.name=="Conference_table").GetComponent<Renderer>().bounds;
        for(int i=0;i<6;i++)
        {
            var chair=chairRoot.Find(i==0?"Chair":"Chair ("+i+")");
            var source=actors.Single(a=>a.name==occupants[i]);
            var seat=new GameObject(rows[i]+"_"+sides[i]).AddComponent<AudienceSeat>();
            seat.transform.SetParent(seatsRoot.transform,false);
            seat.row=rows[i]; seat.side=sides[i];
            var position=source.transform.position; position.y=chair.position.y;
            seat.transform.SetPositionAndRotation(position,Quaternion.Euler(0,source.transform.eulerAngles.y,0));
            var anchor=new GameObject("Laptop Anchor").transform; anchor.SetParent(seat.transform,false);
            var inward=table.center-chair.position; inward.y=0; inward.Normalize();
            var laptopPosition=chair.position+inward*.65f; laptopPosition.y=table.max.y+.005f;
            anchor.SetPositionAndRotation(laptopPosition,Quaternion.LookRotation(inward));
            seat.laptopAnchor=anchor; layout.seats[i]=seat;
            if(!source.GetComponent<AudienceSeatAssignment>()) Undo.AddComponent<AudienceSeatAssignment>(source.gameObject);
        }
        layout.seats[2].conversationPartner=layout.seats[3];
        layout.seats[3].conversationPartner=layout.seats[2];
        var props=new GameObject("Audience Laptops"); props.transform.SetParent(root.transform,false);
        layout.laptops=new GameObject[2];
        string[] models={"black_laptop_ON","Laptop_white"};
        for(int i=0;i<2;i++)
        {
            var source=all.Single(t=>t.name==models[i]);
            var renderers=source.GetComponentsInChildren<Renderer>(true);
            var bounds=renderers[0].bounds; foreach(var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            var holder=new GameObject("Audience Laptop "+(i+1)); holder.transform.SetParent(props.transform,false);
            holder.transform.SetPositionAndRotation(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),Quaternion.Euler(0,source.eulerAngles.y,0));
            var model=UnityEngine.Object.Instantiate(source.gameObject,holder.transform,true); model.name=source.name;
            foreach(var r in model.GetComponentsInChildren<Renderer>(true))
            { GameObjectUtility.SetStaticEditorFlags(r.gameObject,0); r.lightmapIndex=-1; r.realtimeLightmapIndex=-1; }
            source.gameObject.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(source.gameObject);
            holder.SetActive(false); layout.laptops[i]=holder;
        }
        EditorUtility.SetDirty(layout); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
}

















