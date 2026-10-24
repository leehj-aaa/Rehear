using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Localizes authored interface copy only. Uploaded scripts, titles and questions stay intact.
[DefaultExecutionOrder(32000)]
public sealed class RehearUiLocalization : MonoBehaviour
{
    const string Preference = "Rehear.UiLanguage";
    public static string Language { get; private set; } = "ko";
    static RehearUiLocalization instance;
    sealed class Entry { public TMP_Text text; public string source, rendered; }
    readonly Dictionary<int, Entry> labels = new();
    readonly List<int> expired = new();
    readonly HashSet<TMP_Text> userContent = new();
    static readonly Dictionary<string,string> English = new()
    {
        { "Q&A로 넘어갈까요?", "Continue to Q&A?" },
        { "Q&A 시작", "Start Q&A" },
        { "그립 버튼으로 계속 발표", "Press grip to continue" },
        { "서버 연결에 실패했습니다.", "Connection failed. Please retry." },
        { "존재하지 않는 PIN 번호입니다.", "PIN not found. Check the digits." },
        { "발표 시작 중…", "Starting presentation…" },
        { "다시 시작 중…", "Restarting…" },
        { "일시정지 중…", "Pausing…" },
        { "발표 재개 중…", "Resuming…" },
        { "발표 준비 중…", "Preparing presentation…" },
        { "발표 종료 중…", "Ending presentation…" },
        { "응답이 늦어요.", "The response is taking longer." },
        { "세션 인증을 확인해주세요.", "Check your session access." },
        { "잠시 후 다시 시도해주세요.", "Please try again shortly." },
        { "마이크 권한을 확인해주세요.", "Check microphone access." },
        { "발표 설정을 확인해주세요.", "Check your presentation settings." },
        { "발표 끝내기", "End presentation" },
        { "<line-height=135%>잠시만 기다려주세요.\n정교한 환경 조성을 위해 데이터를 불러오는 중입니다.", "<line-height=135%>Please wait.\nPreparing your environment." },
        { "VR 기기를 내려놓고\n모니터에서 웹 리포트를 확인하세요.", "Remove your headset and\nview the report on your monitor." },
        { "그립 버튼을 눌러 진행 중인 세션을 계속하세요", "Squeeze the grip to resume." },
        { "사전에 설정한 내용을 확인하고 발표를 시작하세요", "Check your settings, then start." },
        { "여기에서 세션을 다시 시작하거나 종료할 수 있어요\n그립 버튼을 다시 눌러 이어서 진행해볼까요?", "Restart or end your session here.\nSqueeze the grip again to resume." },
        { "연결 실패\n그립 버튼으로 다시 시도", "Connection failed.\nSqueeze the grip to retry." },
        { "질문을 불러오지 못했습니다. 다시 시도해주세요.", "Unable to load questions. Please retry." },
        { "AI 세션을 복구할 수 없습니다. 발표를 다시 시작해주세요.", "Unable to resume. Restart the presentation." },
        { "서버 연결 모듈을 초기화하지 못했습니다. Firebase 설치 상태를 확인해 주세요.", "Unable to connect. Please restart the app." },
        { "튜토리얼 씬을 불러올 수 없습니다. 빌드의 씬 설정을 확인해 주세요.", "Unable to open the tutorial. Restart the app." },
        { "PIN 번호 입력", "Enter PIN" },
        { "PIN번호 다시 입력하기", "Re-enter PIN" },
        { "Q&A 개수", "Questions" },
        { "Q&A 시간", "Q&A time" },
        { "UI 버튼 선택", "Select a button" },
        { "같은 세션을 다시 연습할까요?", "Practice this session again?" },
        { "거의 다 왔어요!", "Almost there!" },
        { "건너뛰기", "Skip" },
        { "결과 확인 필요", "Check your results" },
        { "결과를 불러오지 못했어요", "Results unavailable" },
        { "계속 발표", "Continue" },
        { "기본 조작을 연습해볼까요?", "Try the controls" },
        { "낮음", "Low" },
        { "중간", "Medium" },
        { "높음", "High" },
        { "눌러보기", "Try pressing" },
        { "다시 시작", "Restart" },
        { "다시 연습하기", "Practice again" },
        { "다시 연습", "Practice again" },
        { "대본", "Script" },
        { "대본 보기", "Show script" },
        { "대본 숨기기", "Hide script" },
        { "대본 페이지 이동", "Turn script pages" },
        { "명확도", "Clarity" },
        { "모든 준비가 완료됐어요!", "You're ready!" },
        { "몰입도", "Engagement" },
        { "발표 마치기", "Finish" },
        { "발표 모드", "Presentation" },
        { "발표 시간", "Presentation time" },
        { "발표 시작하기", "Start presentation" },
        { "발표 연습 시작하기", "Start practice" },
        { "발표 준비하기", "Prepare presentation" },
        { "발표 환경", "Room" },
        { "발표건너뛰기(체험용)", "Skip presentation (demo)" },
        { "발표를 마칠까요?", "End your presentation?" },
        { "보통", "Fair" },
        { "세미나실", "Seminar room" },
        { "세션 불러오기 완료", "Session loaded" },
        { "세션 불러오는 중", "Loading session" },
        { "세션 시작하기", "Start session" },
        { "세션 유형", "Session type" },
        { "세션 종료", "End session" },
        { "세션 종료하기", "End session" },
        { "VR 종료", "Finish VR" },
        { "세션 준비 완료", "Session ready" },
        { "세션을 종료할까요?", "End this session?" },
        { "세션이 종료되었습니다", "Session complete" },
        { "수고하셨습니다", "Well done" },
        { "슬라이드", "Slides" },
        { "슬라이드 이동", "Change slides" },
        { "시작 화면으로 돌아가기", "Back to start" },
        { "시작하기", "Start" },
        { "신뢰도", "Credibility" },
        { "아래 버튼을 선택해보세요", "Select the button below" },
        { "웹에서 리포트를 확인하세요", "View your report on the web" },
        { "웹으로 돌아가기", "Return to the web" },
        { "이번 세션 결과", "Your results" },
        { "이전으로", "Back" },
        { "일시정지", "Paused" },
        { "입력하기", "Load session" },
        { "잘했어요!", "Well done!" },
        { "잠시 뒤를 돌아봐 주세요!", "Look behind you" },
        { "점", "pts" },
        { "좋아요!", "Great!" },
        { "중지로 그립을 눌러요", "Squeeze the grip" },
        { "지우기", "Backspace" },
        { "직접 해보기", "Try it" },
        { "질문하는 중...", "Asking a question..." },
        { "질의응답 하기", "Start Q&A" },
        { "질의응답하기", "Start Q&A" },
        { "처음부터 다시 할까요?", "Start again?" },
        { "처음부터 다시하기", "Start again" },
        { "처음으로 돌아가기", "Back to start" },
        { "청중 관심도", "Audience interest" },
        { "청중 규모", "Audience size" },
        { "청중 전문성", "Audience expertise" },
        { "취소", "Cancel" },
        { "취소하기", "Cancel" },
        { "컨트롤러 조작 방법", "Controller guide" },
        { "튜토리얼 시작하기", "Start tutorial" },
        { "확인", "Confirm" },
        { "다시 시도", "Retry" },
        { "시작 실패", "Couldn't start" },
        { "연결을 확인해주세요.", "Check your connection." },
        { "연결 실패", "Connection failed" },
        { "발표 종료하기", "End presentation" },
        { "청중 질문 중…", "Audience speaking…" },
        { "질문 음성 준비 중…", "Preparing question audio…" },
        { "질문 음성 다시 시도", "Retry question audio" },
        { "청중 질문 준비 중…", "Preparing question…" },
        { "질문 다시 시도", "Retry question" },
        { "리포트 생성 중...", "Preparing report..." },
        { "발표 종료 다시 시도", "Retry ending presentation" },
        { "응답 시작하기", "Start answer" },
        { "마이크 준비 중…", "Preparing microphone…" },
        { "마이크 다시 시도", "Retry microphone" },
        { "응답 마치기", "Finish answer" },
        { "응답 저장 중…", "Saving answer…" },
        { "응답 다시 녹음하기", "Record answer again" },
        { "피드백 보기", "View results" },
        { "다음 질문 듣기", "Next question" },
        { "응답 저장 다시 시도", "Retry saving answer" },
        { "질문 생성 중...", "Preparing question..." },
        { "질문 생성 다시 시도", "Retry question" },
        { "한 번 더!", "Once more!" },
        { "마지막으로!", "One last time!" },
        { "검지로 트리거를 눌러요", "Press the trigger" },
        { "조이스틱을 좌우로 움직여요", "Move the stick left or right" },
        { "조이스틱을 위아래로 움직여요", "Move the stick up or down" },
        { "완료!", "Done!" },
        { "우수", "Excellent" },
        { "개선", "Needs practice" },
        { "다음 연습", "Next practice" },
        { "다음 연습 · 명확도", "Next practice · Clarity" },
        { "다음 연습 · 몰입도", "Next practice · Engagement" },
        { "다음 연습 · 신뢰도", "Next practice · Credibility" },
        { "자세한 분석은 웹 리포트에서 확인하세요.", "See the full report on the web." },
        { "자세한 결과는 웹에서 확인하세요", "See your full results on the web" },
        { "웹 리포트에서 세션 결과를 확인해주세요.", "Check your session report on the web." },
        { "미설정", "Not set" },
        { "발표 제목 없음", "Untitled presentation" },
        { "핵심 문장을 짧게 말하고, 잠깐 쉬어보세요.", "Use short key sentences, then pause." },
        { "핵심 메시지를 강조하고, 청중에게 질문해보세요.", "Emphasize key points and ask a question." },
        { "주장을 뒷받침하는 구체적인 근거를 덧붙여보세요.", "Support your claims with specific evidence." },
        { "PIN 번호 4자리를 모두 입력해주세요.", "Enter all four PIN digits." },
        { "서버에 연결 중입니다. 잠시 후 다시 시도해주세요.", "Connecting. Please try again shortly." },
        { "세션 정보를 불러오는 중입니다.", "Loading session details." },
        { "서버 응답이 늦어지고 있습니다. 연결을 확인하고 다시 시도해 주세요.", "Connection timed out. Check your connection and retry." },
        { "세션 데이터가 비어 있습니다.", "No session data found." },
        { "세션 데이터 형식이 올바르지 않습니다.", "Invalid session data." },
        { "필수 세션정보가 없습니다.", "Session details are missing." },
        { "세션 정보가 올바르지 않습니다. 웹에서 대본과 세션 설정을 확인해 주세요.", "Check your script and session settings on the web." },
        { "세션 불러오기가 취소되었습니다.", "Session loading cancelled." },
        { "청중의 관심을\n유지했어요", "You held the audience’s\nattention." },
        { "청중의 관심을 끌도록\n핵심을 강조해보세요", "Emphasize key points\nto engage your audience." },
        { "내용을 믿을 만하게\n전달했어요", "You delivered a\ncredible message." },
        { "구체적인 근거를 더해\n전달해보세요", "Add specific evidence\nto support your points." },
        { "핵심을 분명하게\n전달했어요", "Your key message\nwas clear." },
        { "핵심을 더 분명하게\n전달해보세요", "Make your key\nmessage clearer." },
        { "다시 연습해도 방금 완료한 결과는\n웹 리포트에서 확인할 수 있어요.", "Your completed results stay\navailable in the web report." },
        { "이제 VR 기기를 내려놓고\n모니터에서 웹 리포트를 확인하세요.", "Remove your headset and\nview your report on the web." },
        { "웹에서 생성된 PIN 번호 4자리를 입력하고 설정한 세션을 불러오세요", "Enter the four-digit PIN from the web." },
        { "잠시만 기다려 주세요.\n웹에서 설정한 세션 정보를 불러오고 있습니다.", "Loading your session from the web." },
        { "오른손 컨트롤러 앞쪽의 파란 버튼을 확인해 주세요.\n버튼을 가리킨 뒤 검지로 트리거를 눌러 선택해요.", "Point at a button, then press\nthe right trigger to select it." },
        { "이번에는 뒤쪽 화면을 보며 슬라이드를 넘겨요.\n오른손 조이스틱을 좌우로 기울인 뒤 가운데로 놓아 주세요.", "Look at the rear screen. Move the right stick\nleft or right, then release it." },
        { "오른손 조이스틱을 왼쪽·오른쪽으로 기울여요.\n한 번 넘긴 뒤 가운데로 놓으면 다시 넘길 수 있어요.", "Move the right stick left or right.\nRelease it before changing slides again." },
        { "오른손 조이스틱을 위·아래로 기울여 대본을 넘겨요.\n한 번 넘긴 뒤 가운데로 놓으면 다시 넘길 수 있어요.", "Move the right stick up or down to turn script pages.\nRelease it before turning another page." },
        { "오른손 컨트롤러 옆면의 파란 버튼을 확인해 주세요.\n그립을 쥐면 일시정지하고, 놓았다 다시 쥐면 재개해요.", "Squeeze the right grip to pause.\nRelease and squeeze again to resume." },
    };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; Language = "ko"; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (instance) return;
        var go = new GameObject("ReHear UI Language");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RehearUiLocalization>();
        Language = PlayerPrefs.GetString(Preference, "ko");
        SceneManager.sceneLoaded += instance.OnSceneLoaded;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(instance.TrackChanged);
    }
    public static void SetLanguage(string language)
    {
        Language = !string.IsNullOrWhiteSpace(language) &&
            (language.StartsWith("en", System.StringComparison.OrdinalIgnoreCase) || language == "영어") ? "en" : "ko";
        PlayerPrefs.SetString(Preference, Language);
        PlayerPrefs.Save();
        if (instance) { instance.Scan(); instance.Refresh(); }
        RefreshGeometry();
    }
    public static void RefreshGeometry()
    {
        foreach(var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if(!text||!text.gameObject.scene.IsValid())continue;
            text.SetAllDirty();text.ForceMeshUpdate(true,true);
        }
        foreach(var effect in Resources.FindObjectsOfTypeAll<CurvedUI.CurvedUIVertexEffect>())
            if(effect&&effect.gameObject.scene.IsValid())effect.SetDirty();
        foreach(var text in Resources.FindObjectsOfTypeAll<CurvedUI.Core.Integrations.CurvedUITMP>())
            if(text&&text.gameObject.scene.IsValid())text.Dirty=true;
        Canvas.ForceUpdateCanvases();
    }
    public static string Source(TMP_Text text)
    {
        if (!text) return "";
        if (instance && instance.labels.TryGetValue(text.GetInstanceID(), out var entry) && text.text == entry.rendered)
            return entry.source;
        return Original(text.text);
    }
    static string Original(string value)
    {
        foreach(var copy in English)if(copy.Value==value)return copy.Key;
        return value;
    }
    public static string Translate(string value)
    {
        if (Language != "en" || string.IsNullOrWhiteSpace(value)) return value;
        string key = value.Trim();
        if (English.TryGetValue(key, out var translated)) return translated;
        var count = Regex.Match(key, @"^(\d+)\s*(분|개|명|회 남음)$");
        if (count.Success) return count.Groups[1].Value + (count.Groups[2].Value == "분" ? " min" :
            count.Groups[2].Value == "개" ? " questions" : count.Groups[2].Value == "명" ? " people" : " remaining");
        var progress=Regex.Match(key,@"^(\d+) / (\d+)번째 질문 · 이후 (\d+)개 남음$");
        if(progress.Success)return "Question "+progress.Groups[1].Value+" of "+progress.Groups[2].Value+" · "+progress.Groups[3].Value+" remaining";
        return value;
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Scan();
    public static void RefreshScene()
    {
        if (!instance) return;
        instance.Scan(); instance.Refresh();
    }
    void Scan()
    {
        userContent.Clear();
        foreach(var scroller in FindObjectsByType<ScriptScroller>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(scroller.ScriptText)userContent.Add(scroller.ScriptText);
        foreach(var qa in FindObjectsByType<QuestionAnswerManager>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(qa.questionText)userContent.Add(qa.questionText);
        foreach(var ready in FindObjectsByType<PresentationSessionReady>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(ready.presentationTitle)userContent.Add(ready.presentationTitle);
        foreach(var text in userContent)
        {
            if(labels.TryGetValue(text.GetInstanceID(),out var old) && text.text==old.rendered)text.text=old.source;
            labels.Remove(text.GetInstanceID());
        }
        foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            if (text && text.gameObject.scene.IsValid()) Track(text);
    }
    void TrackChanged(Object target) { if (target is TMP_Text text && text.gameObject.scene.IsValid()) Track(text); }
    void Track(TMP_Text text)
    {
        // Uploaded content must never be translated or replaced by UI copy.
        if (userContent.Contains(text) || text.name == "Text_Script_New" || text.name == "Script Text" || text.name == "QuestionText") return;
        var ready = text.GetComponentInParent<PresentationSessionReady>(true);
        if (ready && ready.presentationTitle == text) return;
        int id = text.GetInstanceID();
        if (!labels.ContainsKey(id)) labels.Add(id, new Entry { text = text, source = Original(text.text), rendered = text.text });
    }
    void LateUpdate() => Refresh();
    void Refresh()
    {
        expired.Clear();
        foreach (var pair in labels)
        {
            var e = pair.Value;
            if (!e.text) { expired.Add(pair.Key); continue; }
            if (e.text.text != e.rendered) e.source = e.text.text;
            e.rendered = Translate(e.source);
            if (e.text.text != e.rendered) e.text.text = e.rendered;
        }
        foreach (var id in expired) labels.Remove(id);
    }
    void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(TrackChanged);
        instance = null;
    }
}
