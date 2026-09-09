using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class RehearAudienceMotionPreview
{
    static bool reviewMode, reviewStarted, reviewReturned, reviewAuto=true;
    static AudienceAnimationCatalog.Entry[] reviewEntries;
    static int reviewIndex;
    static float reviewDuration;
    static string reviewStatus;
    static readonly string[] ReviewGroups={"BL_","AL_","EM_","CT_","ACT_","QS_","AP_"};
    static readonly string[] ReviewGroupNames={"기본 듣기","주의·긍정 반응","감정 반응","이해·혼란 반응","개별 행동","질문 손들기","박수"};

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
                else {reviewAuto=false;reviewStatus="전체 순서 재생 완료";}
            }
        }
    }

    static void DrawReviewOverlay(SceneView view)
    {
        if(!reviewMode || !root || reviewEntries==null)return;
        Handles.BeginGUI();
        var width=Mathf.Min(600f,view.position.width-32f);
        GUI.Box(new Rect(16,48,width,66),GUIContent.none);
        var style=new GUIStyle(EditorStyles.boldLabel){fontSize=18,wordWrap=true};
        GUI.Label(new Rect(26,54,width-20,48),$"동작 점검 {reviewIndex+1}/{reviewEntries.Length}\n{ReviewTitle(reviewEntries[reviewIndex])}",style);
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
