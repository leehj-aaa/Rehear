using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Rehear.Evc.Audio;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearAzureSpeechSetup
{
    static RehearAzureSpeechSetup() => EditorApplication.update += Poll;
    private static void Poll()
    {
        const string request = "Temp/RehearAzureSpeech.request";
        if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        string command;
        try { command = File.ReadAllText(request).Trim(); File.Delete(request); } catch (IOException) { return; }
        try { if (command == "hand") ShowRaisedHand(); else if (command == "answer") ShowAnswering(); else ConfigureAndCheck(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearAzureSpeech.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Rehear/Preview User Answering")]
    public static void ShowAnswering()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit mode for preview.");
        typeof(RehearSessionReadySetup).GetMethod("ShowQA", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        var controller = Object.FindFirstObjectByType<PresentationController>();
        var qa = controller.qaManager;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(QuestionAnswerManager).GetMethod("FinishQuestionAudio", flags).Invoke(qa, null);
        typeof(QuestionAnswerManager).GetMethod("BeginAnswer", flags).Invoke(qa, null);
        // Show the stable state after the brief anti-double-click lock, without recording.
        typeof(QuestionAnswerManager).GetField("answerUnlockTime", flags).SetValue(qa, Time.unscaledTime - 1);
        typeof(QuestionAnswerManager).GetMethod("Update", flags).Invoke(qa, null);
        var button = controller.endPresentationButton;
        if (!button.interactable || button.GetComponentInChildren<TMPro.TMP_Text>().text != "응답 마치기")
            throw new Exception("Answer preview did not use the shared podium control.");
        if (controller.scriptPanel.activeSelf) throw new Exception("Q&A script must be hidden.");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Selection.activeObject = null;
        SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText("Temp/RehearAnswerPreview.txt", "PASS answering preview: audience baseline/hands down, shared blue finish-answer button, question progress, clock Q&A, slides visible, script hidden. No recording or server requests.\n");
    }

    [MenuItem("Rehear/Preview Audience Raising Hand")]
    public static void ShowRaisedHand()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit mode for a frozen preview.");
        EditorApplication.ExecuteMenuItem("Rehear/Preview Seated Presentation Audience");
        var root = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
            .Single(g => g.name == "Audience Preview (Editor Only)");
        var agents = root.GetComponentsInChildren<Rehear.Evc.Audience.AudienceAgent>(true);
        int count = Mathf.Clamp(RuntimeSessionData.QaCount, 1, 5);
        var assigned = QuestionSpeakerAssignment.Create(agents.Select(a => a.AgentId).ToArray(), count,
            Rehear.Evc.Presentation.PresentationSessionContext.Current.Seed);
        var actor = agents.Single(a => a.AgentId == assigned[0]);
        var body = actor.GetComponent<AudienceAnimationPlayer>();
        var catalog = AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
        if (!catalog.TryGetClip(AudienceAnimationPlayer.QuestionGesture, body.Gender, out var clip)) throw new Exception("QS clip not configured.");
        var hands = actor.GetComponentsInChildren<Transform>(true).Where(t => t.name == "hand_l" || t.name == "hand_r").ToArray();
        if (hands.Length != 2) throw new Exception("Missing hands.");
        var position = actor.transform.position; var rotation = actor.transform.rotation;
        float peak = float.NegativeInfinity, peakTime = 0;
        for (float time = 0; time < clip.length; time += .1f)
        {
            clip.SampleAnimation(actor.gameObject, time);
            float height = hands.Max(t => actor.transform.InverseTransformPoint(t.position).y);
            if (height > peak) { peak = height; peakTime = time; }
        }
        clip.SampleAnimation(actor.gameObject, peakTime);
        actor.transform.SetPositionAndRotation(position, rotation);
        var controller = Object.FindFirstObjectByType<PresentationController>();
        if (controller && controller.endPresentationButton)
        {
            var label = controller.endPresentationButton.GetComponentInChildren<TMPro.TMP_Text>();
            if (label) label.text = "청중 질문 준비 중…";
        }
        Selection.activeObject = null;
        SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText("Temp/RehearRaisedHand.txt", $"PASS frozen editor-only QS pose: {actor.name}, {actor.AgentId}, {body.Gender}, t={peakTime:F2}s/{clip.length:F2}s, peak hand y={peak:F3}. No Play, TTS or microphone.\n");
    }

    [MenuItem("Rehear/Azure Speech/Configure Quest and Check QS")]
    public static void ConfigureAndCheck()
    {
        const string assetPath = "Assets/Resources/AzureSpeechConfig.asset";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var config = AssetDatabase.LoadAssetAtPath<AzureSpeechConfig>(assetPath);
        if (!config)
        {
            config = ScriptableObject.CreateInstance<AzureSpeechConfig>();
            AssetDatabase.CreateAsset(config, assetPath);
        }
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission = true;
        foreach (var suffix in new[]{"Male/QS_01_raise_hand_question_M.fbx", "Female/QS_01_raise_hand_question_F.fbx"})
        {
            var path = "Assets/06_Animation/Clips/" + suffix;
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (!importer) throw new Exception("Missing QS model: " + path);
            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips.Any(c => c.loopTime || c.loop))
            {
                foreach (var clip in clips) { clip.loopTime = false; clip.loop = false; }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }
        AudienceAnimationCatalogBuilder.RebuildCatalog();
        foreach (var entry in new[]{("Aud_M_01",0),("Aud_M_02",1),("Aud_M_03",2),("Aud_W_01",3),("Aud_W_02",4),("Aud_W_03",5)})
        {
            var path = "Assets/03_Prefabs/Audience/Presentation/" + entry.Item1 + ".prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // Only initialize missing profiles; preserve later Inspector voice choices.
                var profile = prefab.GetComponent<AudienceVoiceProfile>();
                if (!profile)
                {
                    profile = prefab.AddComponent<AudienceVoiceProfile>();
                    var serialized = new SerializedObject(profile);
                    serialized.FindProperty("voice").enumValueIndex = entry.Item2;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(prefab, path);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
        var catalog = AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
        string report = "PASS Quest settings: ARM64, IL2CPP, Internet permission. No Azure native binaries in APK.\n";
        foreach (AudienceGender gender in Enum.GetValues(typeof(AudienceGender)))
        {
            if (!catalog.TryGetClip(AudienceAnimationPlayer.QuestionGesture, gender, out var clip) || clip.isLooping || clip.length <= 0)
                throw new Exception("Invalid non-looping QS clip: " + gender);
            var root = new GameObject("QS transition test");
            root.SetActive(false);
            try
            {
                root.AddComponent<Animator>();
                var body = root.AddComponent<AudienceAnimationPlayer>();
                var serialized = new SerializedObject(body);
                serialized.FindProperty("catalog").objectReferenceValue = catalog;
                serialized.FindProperty("gender").enumValueIndex = (int)gender;
                serialized.FindProperty("printAnimationLog").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph", flags).Invoke(body, null);
                var advance = typeof(AudienceAnimationPlayer).GetMethod("Advance", flags);
                body.ReserveQuestionTurn();
                advance.Invoke(body, new object[]{1f});
                if (!body.IsAtBaseline || !body.PlayQuestionGesture() || body.IsAtBaseline) throw new Exception("QS did not begin from baseline.");
                if (body.PlayServerVariation("BL_03.quiet_stable_posture", 1, 1)) throw new Exception("Evaluation interrupted question turn.");
                float elapsed = 0;
                while (elapsed < clip.length - .05f) { advance.Invoke(body, new object[]{.02f}); elapsed += .02f; }
                if (body.IsAtBaseline) throw new Exception("QS ended before the hand lowering clip completed.");
                advance.Invoke(body, new object[]{.1f});
                advance.Invoke(body, new object[]{1f});
                if (!body.IsAtBaseline) throw new Exception("QS failed to blend back to baseline.");
                body.ReleaseQuestionTurn();
                report += $"PASS {gender}: QS {clip.length:F2}s non-looping -> baseline, scripted turn protected.\n";
            }
            finally { Object.DestroyImmediate(root); }
        }
        for (int count = 1; count <= 5; count++)
        {
            var assignment = QuestionSpeakerAssignment.Create(new[]{"A","B","C","D","E","F"}, count, 1234);
            if (assignment.Distinct().Count() != count || !assignment.SequenceEqual(QuestionSpeakerAssignment.Create(new[]{"F","E","D","C","B","A"}, count, 1234)))
                throw new Exception("Question assignment is not distinct and stable.");
        }
        report += "PASS question counts 1..5: distinct speakers, deterministic per session.\n";
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/03_Prefabs/Audience/Presentation"}))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (!prefab.GetComponent<AudienceAnimationPlayer>()) continue;
            var voice = prefab.GetComponent<AudienceVoiceProfile>();
            if (!voice) throw new Exception("Missing voice profile: " + prefab.name);
            report += $"PASS prefab voice {prefab.name}: {voice.VoiceName}\n";
            var copy = Object.Instantiate(prefab);
            copy.SetActive(false);
            try
            {
                var body = copy.GetComponent<AudienceAnimationPlayer>();
                if (!catalog.TryGetClip(AudienceAnimationPlayer.QuestionGesture, body.Gender, out var clip)) throw new Exception("Missing QS clip");
                var hands = copy.GetComponentsInChildren<Transform>(true).Where(t => t.name == "hand_l" || t.name == "hand_r").ToArray();
                if (hands.Length != 2) throw new Exception("Missing hand bones: " + prefab.name);
                clip.SampleAnimation(copy, 0);
                float start = hands.Max(t => copy.transform.InverseTransformPoint(t.position).y);
                float peak = start;
                for (float time = 0; time < clip.length; time += .25f)
                {
                    clip.SampleAnimation(copy, time);
                    peak = Mathf.Max(peak, hands.Max(t => copy.transform.InverseTransformPoint(t.position).y));
                }
                clip.SampleAnimation(copy, clip.length - .001f);
                float end = hands.Max(t => copy.transform.InverseTransformPoint(t.position).y);
                report += $"QS pose {prefab.name}: start={start:F3}, peak={peak:F3}, end={end:F3}, humanoid={clip.isHumanMotion}\n";
                if (peak - start < .15f || peak - end < .15f) throw new Exception("QS hand does not rise/lower on " + prefab.name + "\n" + report);
            }
            finally { Object.DestroyImmediate(copy); }
        }
        report += string.IsNullOrWhiteSpace(config.bridgeBaseUrl) ? "PENDING: deploy speech server and enter HTTPS URL; Azure credentials remain server-only.\n" : "Speech bridge URL configured.\n";
        AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/RehearAzureSpeech.txt", report);
    }
}

internal sealed class RehearAzureSpeechBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP ||
            PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            throw new BuildFailedException("Quest 빌드는 ARM64 + IL2CPP 설정이 필요합니다.");
        var config = Resources.Load<AzureSpeechConfig>("AzureSpeechConfig");
        if (!config || string.IsNullOrWhiteSpace(config.bridgeBaseUrl))
            Debug.LogWarning("[Azure Speech] 음성 서버 URL이 비어 있습니다. APK는 빌드되지만 STT/TTS 실서비스는 아직 연결되지 않습니다.");
        else _ = config.BaseUrl;
    }
}
