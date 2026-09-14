using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class RehearAudienceMotionPreview
{
    static bool reviewMode, reviewStarted, reviewReturned, reviewAuto=true;
    static bool focusedReview;
    static readonly System.Collections.Generic.Dictionary<AudienceAnimationPlayer,float> focusedNext=new System.Collections.Generic.Dictionary<AudienceAnimationPlayer,float>();
    static readonly System.Collections.Generic.List<GameObject> focusedSuspended=new System.Collections.Generic.List<GameObject>();
    static AudienceAnimationCatalog.Entry[] reviewEntries;
    static int reviewIndex;
    static float reviewDuration;
    static string reviewStatus;
    static readonly string[] ReviewGroups={"BL_","AL_","EM_","CT_","ACT_","QS_","AP_"};
    static readonly string[] ReviewGroupNames={"기본 듣기","주의·긍정 반응","감정 반응","이해·혼란 반응","개별 행동","질문 손들기","박수"};

    static void StartFocusedPropsReview()
    {
        typeof(RehearQuestionFlowPreview).GetMethod("Stop",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,null);
        typeof(RehearFeedbackPreview).GetMethod("Stop",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,null);
        StopPreview();focusedReview=true;
        try {
            RehearPhotoAudioPreview.Prepare();
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/01_Scene/Scene_02_Presentation.unity");
            if(!scene.isLoaded)scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/01_Scene/Scene_02_Presentation.unity",UnityEditor.SceneManagement.OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            StartReview();
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){
                var other=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if(other!=scene)foreach(var item in other.GetRootGameObjects())if(item.activeSelf){focusedSuspended.Add(item);item.SetActive(false);}
            }
            reviewEntries=new[]{"ACT_06.smallstretch","ACT_02.photoslide"}.Select(id=>reviewEntries.Single(e=>e.variationId==id)).ToArray();
            SelectReview(0);
            var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c=>c.CompareTag("MainCamera"));
            var view=SceneView.lastActiveSceneView;
            if(view&&camera){view.orthographic=false;view.LookAtDirect(camera.transform.position+camera.transform.forward*3,camera.transform.rotation,1.5f);}
        }catch{StopPreview();focusedReview=false;throw;}
    }

    static string ReviewTitle(AudienceAnimationCatalog.Entry entry)
    {
        int group=Array.FindIndex(ReviewGroups,p=>entry.variationId.StartsWith(p,StringComparison.Ordinal));
        return AudienceSeatAssignment.IsSideConversation(entry.variationId)?"옆 대화 · L/R 함께 보기":
            (group>=0?ReviewGroupNames[group]:"청중 동작")+" · "+entry.variationId;
    }

    [MenuItem("Rehear/Audience/Review All Animations")]
    static void StartReview()
    {
        if(!root)Open();
        Restart();
        var catalog=AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
        reviewEntries=catalog.Entries.Where(e=>e!=null && !string.IsNullOrEmpty(e.variationId) &&
                e.variationId!="ACT_08.side_conversation_r")
            .OrderBy(e=>Array.FindIndex(ReviewGroups,p=>e.variationId.StartsWith(p,StringComparison.Ordinal)))
            .ThenBy(e=>e.variationId,StringComparer.Ordinal).ToArray();
        if(reviewEntries.Length==0)throw new InvalidOperationException("No audience animations registered.");
        reviewMode=true;cursor=trace.frames.Length;reviewAuto=true;
        SelectReview(0);
    }

    static void SelectReview(int index)
    {
        if(!root || reviewEntries==null)return;
        foreach(var body in bodies){body.ReleaseQuestionTurn();body.StopAction();}
        reviewIndex=Mathf.Clamp(index,0,reviewEntries.Length-1);
        elapsed=0;lastTime=EditorApplication.timeSinceStartup;paused=false;
        reviewStarted=false;reviewReturned=false;latest.Clear();
        focusedNext.Clear();
        var entry=reviewEntries[reviewIndex];
        if(entry.variationId==AudienceAnimationPlayer.QuestionGesture)
            foreach(var body in bodies)body.ReserveQuestionTurn();
        reviewDuration=Mathf.Max(6f,entry.maleClip?entry.maleClip.length:0,entry.femaleClip?entry.femaleClip.length:0);
        if(AudienceSeatAssignment.IsSideConversation(entry.variationId)) {
            var catalog=AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
            foreach(var e in catalog.Entries.Where(e=>AudienceSeatAssignment.IsSideConversation(e.variationId)))
                reviewDuration=Mathf.Max(reviewDuration,e.maleClip?e.maleClip.length:0,e.femaleClip?e.femaleClip.length:0);
        }
        reviewStatus="기본 자세로 전환 중";
        for(int i=0;i<gazes.Length;i++)if(gazes[i])nextGaze[i]=gazes[i].ResetManualGaze();
        SceneView.RepaintAll();if(window)window.Repaint();
    }

    static void AdvanceReview()
    {
        if(focusedReview){AdvanceFocusedProps();return;}
        if(!reviewStarted && elapsed>=1f) {
            reviewStarted=true;
            var entry=reviewEntries[reviewIndex];
            bool paired=AudienceSeatAssignment.IsSideConversation(entry.variationId);
            bool pairSent=false;int count=0;
            foreach(var body in bodies) {
                var seat=body.GetComponent<AudienceSeatAssignment>();
                if(!seat || !seat.Allows(entry.variationId))continue;
                bool accepted;
                if(entry.variationId==AudienceAnimationPlayer.QuestionGesture) {
                    accepted=body.PlayQuestionGesture();
                }else if(paired && pairSent)accepted=true;
                else accepted=body.PlayServerVariation(entry.variationId,reviewDuration,1f);
                if(!accepted)throw new InvalidOperationException("Review rejected: "+body.name+" / "+entry.variationId);
                if(paired)pairSent=true;
                latest[body.GetComponent<Rehear.Evc.Audience.AudienceAgent>().AgentId]=ReviewTitle(entry);count++;
            }
            if(count==0)throw new InvalidOperationException("No eligible audience for "+entry.variationId);
            reviewStatus=$"{count}명 재생 중 · 시선·소품 포함";
            File.AppendAllText("Temp/RehearAnimationReview.txt",$"START {reviewIndex+1}/{reviewEntries.Length} {entry.variationId} actors={count} duration={reviewDuration:F2}\n");
        }
        // Allow the laptop turn to complete before judging the end of a full clip.
        float end=1f+reviewDuration+2f;
        if(reviewStarted && !reviewReturned && elapsed>=end) {
            foreach(var body in bodies){body.ReleaseQuestionTurn();body.StopAction();}
            reviewReturned=true;reviewStatus="기본 자세로 복귀 중";
        }
        if(reviewReturned && elapsed>=end+1.5f) {
            reviewStatus="다음 동작으로 이동하거나 다시 재생할 수 있습니다.";
            if(reviewAuto) {
                if(reviewIndex+1<reviewEntries.Length)SelectReview(reviewIndex+1);
                else if(focusedReview)SelectReview(0);
                else {reviewAuto=false;reviewStatus="전체 순서 재생 완료";}
            }
        }
    }

    static void AdvanceFocusedProps()
    {
        var ordered=bodies.OrderBy(b=>b.Gender).ThenBy(b=>b.name,StringComparer.Ordinal).ToArray();
        if(focusedNext.Count==0){
            for(int i=0;i<ordered.Length;i++)focusedNext[ordered[i]]=1f+i*.15f;
            File.AppendAllText("Temp/RehearAnimationReview.txt","SPLIT PREVIEW: 3 SmallStretch + 3 PhotoSlide, both genders in each group.\n");
        }
        for(int i=0;i<ordered.Length;i++){
            var body=ordered[i];if(elapsed<focusedNext[body])continue;
            var entry=reviewEntries[i%2];
            var clip=body.Gender==AudienceGender.Female?entry.femaleClip:entry.maleClip;
            if(!clip||!body.PlayServerVariation(entry.variationId,clip.length,1f))throw new InvalidOperationException("Split review rejected: "+body.name);
            latest[body.GetComponent<Rehear.Evc.Audience.AudienceAgent>().AgentId]=ReviewTitle(entry);
            focusedNext[body]=elapsed+clip.length+2f;
            File.AppendAllText("Temp/RehearAnimationReview.txt",$"SPLIT {body.name} {body.Gender} {entry.variationId} clip={AssetDatabase.GetAssetPath(clip)}\n");
        }
        reviewStarted=true;reviewStatus="SmallStretch 3명 · Photo Slide 3명 동시 재생";
    }

    static void CaptureFocusedPhoto()
    {
        if(!root||!focusedReview)StartFocusedPropsReview();
        RehearPhotoAudioPreview.Suppress=true;
        try {
        SelectReview(0);
        while(elapsed<3f){elapsed+=1f/60;EvaluatePreview(1f/60);}
        CapturePhotoCamera(bodies[0],null,false);
        File.Copy("Temp/PhotoAim-scene.png","Temp/PhotoAim-operating.png",true);
        while(elapsed<5.5f){elapsed+=1f/60;EvaluatePreview(1f/60);}
        CapturePhotoCamera(bodies[0],null,false);
        File.Copy("Temp/PhotoAim-scene.png","Temp/PhotoAim-raised.png",true);
        while(elapsed<8f){elapsed+=1f/60;EvaluatePreview(1f/60);}
        var report=new System.Text.StringBuilder();
        foreach(var body in bodies.Where(b=>b.IsTakingPhoto)){
            var phone=(GameObject)typeof(AudienceAnimationPlayer).GetField("photoPhone",Flags).GetValue(body);
            var screen=body.GetComponent<AudienceGazeController>().SlideTarget;
            float angle=Vector3.Angle(-phone.transform.forward,screen.position-phone.transform.position);
            report.AppendLine($"{body.name}: rear lens -> {screen.name}, error={angle:F2}deg, parent={phone.transform.parent.name}");
            var hand=body.GetComponentsInChildren<Transform>().Single(t=>t.name=="hand_r");
            var thigh=body.GetComponentsInChildren<Transform>().Single(t=>t.name=="thigh_r");
            var grip=phone.GetComponent<AudiencePhotoGrip>();
            report.AppendLine($"  free wrist distance from lap={(hand.position-thigh.TransformPoint(grip.wristInThigh)).magnitude:F4}m (authored manipulation restored below shooting height), rest bones={grip.restPose.Length}");
            CapturePhotoCamera(body,phone.transform,true);
        }
        CapturePhotoCamera(bodies[0],null,false);
        File.WriteAllText("Temp/RehearPhotoAim.txt",report.ToString());
        lastTime=EditorApplication.timeSinceStartup;
        } finally { RehearPhotoAudioPreview.Suppress=false; }
    }
    static void CapturePhotoCamera(AudienceAnimationPlayer body,Transform phone,bool close)
    {
        var go=new GameObject("Photo inspection camera"){hideFlags=HideFlags.HideAndDontSave};
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,body.gameObject.scene);
        var camera=go.AddComponent<Camera>();
        var view=SceneView.lastActiveSceneView;
        if(view)camera.CopyFrom(view.camera);
        camera.scene=body.gameObject.scene;
        camera.enabled=false;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.cullingMask&=~(1<<LayerMask.NameToLayer("UI"));
        if(close){
            camera.transform.position=phone.position-phone.forward*.48f+phone.right*.15f;
            camera.transform.LookAt(phone.position,Vector3.up);camera.orthographic=false;camera.fieldOfView=42;camera.nearClipPlane=.01f;
        }else {
            var center=Vector3.zero;var forward=Vector3.zero;
            foreach(var actor in bodies){center+=actor.transform.position;forward+=actor.transform.forward;}
            center/=bodies.Length;forward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
            camera.transform.position=center+forward*3.3f+Vector3.up*1.55f;
            camera.transform.LookAt(center+Vector3.up*1f);camera.orthographic=false;camera.fieldOfView=65;camera.nearClipPlane=.03f;
        }
        int w=close?900:1600,h=close?900:900;
        var rt=new RenderTexture(w,h,24);var old=RenderTexture.active;camera.targetTexture=rt;
        try{
            camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();
            File.WriteAllBytes(close?$"Temp/PhotoAim-{body.name}.png":"Temp/PhotoAim-scene.png",tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }finally{RenderTexture.active=old;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);}
    }

    static void DrawReviewOverlay(SceneView view)
    {
        if(!reviewMode || !root || reviewEntries==null)return;
        Handles.BeginGUI();
        var width=Mathf.Min(600f,view.position.width-32f);
        GUI.Box(new Rect(16,48,width,66),GUIContent.none);
        var style=new GUIStyle(EditorStyles.boldLabel){fontSize=18,wordWrap=true};
        GUI.Label(new Rect(26,54,width-20,48),focusedReview?"동시 비교 · 남녀 각각 다른 파일 적용\nSmallStretch 3명 / Photo Slide 3명":$"동작 점검 {reviewIndex+1}/{reviewEntries.Length}\n{ReviewTitle(reviewEntries[reviewIndex])}",style);
        if(focusedReview){
            if(GUI.Button(new Rect(16,120,170,28),"두 동작 함께 다시 보기"))SelectReview(0);
            if(GUI.Button(new Rect(194,120,100,28),"점검 종료")){StopPreview();focusedReview=false;}
        }
        Handles.EndGUI();
    }

    void DrawReview()
    {
        if(!root || reviewEntries==null){reviewMode=false;return;}
        EditorGUILayout.LabelField($"청중 동작 점검 · {reviewIndex+1} / {reviewEntries.Length}",EditorStyles.boldLabel);
        EditorGUILayout.LabelField(ReviewTitle(reviewEntries[reviewIndex]),EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField(reviewStatus,EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField($"현재 단계 {elapsed:F1}초");
        reviewAuto=EditorGUILayout.Toggle("자동으로 다음 동작",reviewAuto);
        GUILayout.BeginHorizontal();
        GUI.enabled=reviewIndex>0;if(GUILayout.Button("이전"))SelectReview(reviewIndex-1);
        GUI.enabled=true;if(GUILayout.Button("다시 보기"))SelectReview(reviewIndex);
        GUI.enabled=reviewIndex<reviewEntries.Length-1;if(GUILayout.Button("다음"))SelectReview(reviewIndex+1);
        GUI.enabled=true;GUILayout.EndHorizontal();
        if(GUILayout.Button(paused?"계속 재생":"일시정지")){paused=!paused;lastTime=EditorApplication.timeSinceStartup;}
        int selected=EditorGUILayout.Popup("동작 선택",reviewIndex,reviewEntries.Select(ReviewTitle).ToArray());
        if(selected!=reviewIndex)SelectReview(selected);
        EditorGUILayout.HelpBox("점검용으로 같은 동작을 함께 재생합니다. 타이핑은 노트북 보유자, 옆 대화는 맨 뒤 두 사람에게만 적용됩니다.",MessageType.Info);
        if(GUILayout.Button("점검 종료"))StopPreview();
    }

    static void CheckReview()
    {
        StartReview();reviewAuto=false;
        File.WriteAllText("Temp/RehearAnimationReview.txt","");
        for(int i=0;i<reviewEntries.Length;i++) {
            SelectReview(i);reviewAuto=false;
            for(int frame=0;frame<65;frame++){elapsed+=.02f;EvaluatePreview(.02f);}
            if(!reviewStarted)throw new Exception("Review did not start: "+i);
        }
        File.AppendAllText("Temp/RehearAnimationReview.txt",$"PASS {reviewEntries.Length} steps: every registered animation starts on eligible seats; L/R tested together. Visual review still required.\n");
        SelectReview(0);reviewAuto=false;
    }
}
