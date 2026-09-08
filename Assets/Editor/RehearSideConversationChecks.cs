using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearSideConversationChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static RehearSideConversationChecks() => EditorApplication.update += Poll;
    static void Poll()
    {
        const string request = "Temp/RehearSideConversation.request";
        if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        try { File.Delete(request); Run(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearSideConversation.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Rehear/Check Rear Conversation Directions")]
    static void Run()
    {
        var output = new StringBuilder();
        var catalog = AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
        var createId = typeof(AudienceAnimationCatalogBuilder).GetMethod("CreateVariationId", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var suffix in new[] { "L", "R" })
        {
            string id = "ACT_08.side_conversation_" + suffix.ToLowerInvariant();
            if ((string)createId.Invoke(null, new object[] { "ACT_08_Side Conversation_" + suffix }) != id)
                throw new Exception("Builder merged L/R again");
            foreach (AudienceGender gender in Enum.GetValues(typeof(AudienceGender)))
                if (!catalog.TryGetClip(id, gender, out var clip) || !AssetDatabase.GetAssetPath(clip).EndsWith("_" + suffix + ".fbx"))
                    throw new Exception("Wrong directional clip: " + id + " / " + gender);
        }
        var preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Conversation test");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
        try
        {
            var seats = Enumerable.Range(0, 6).Select(i =>
            {
                var seat = new GameObject("Seat " + i).AddComponent<AudienceSeat>();
                seat.transform.SetParent(root.transform);
                seat.row = i < 2 ? "rear" : "front";
                seat.side = i % 2 == 0 ? "left" : "right";
                seat.transform.position = new Vector3(i % 2 == 0 ? -1 : 1, 0, i < 2 ? 0 : 3);
                seat.transform.rotation = Quaternion.Euler(0, 180, 0);
                return seat;
            }).ToArray();
            seats[0].conversationPartner = seats[1]; seats[1].conversationPartner = seats[0];
            var bodies = Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation", "Aud_*.prefab")
                .OrderBy(p => p).Select(path =>
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/')), preview);
                    instance.transform.SetParent(root.transform);
                    var body = instance.GetComponent<AudienceAnimationPlayer>();
                    if (!(bool)typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph", Flags).Invoke(body, null))
                        throw new Exception("Could not initialize preview graph: " + path);
                    return body;
                }).ToArray();
            if (bodies.Length != 6) throw new Exception("Expected six audience prefabs");
            var advance = typeof(AudienceAnimationPlayer).GetMethod("Advance", Flags);
            var target = typeof(AudienceAnimationPlayer).GetField("target", Flags);
            for (int seed = 0; seed < 20; seed++)
            {
                var random = new System.Random(seed);
                var order = bodies.OrderBy(_ => random.Next()).ToArray();
                for (int i = 0; i < 6; i++)
                {
                    var assignment = order[i].GetComponent<AudienceSeatAssignment>() ?? order[i].gameObject.AddComponent<AudienceSeatAssignment>();
                    assignment.Assign(seats[i]); seats[i].Assign(assignment, false);
                    order[i].transform.SetPositionAndRotation(seats[i].transform.position, seats[i].transform.rotation);
                }
                for (int i = 0; i < 6; i++)
                {
                    var body = order[i];
                    bool played = body.PlayServerVariation("ACT_08.side_conversation", 3, 1);
                    if (played != (i < 2)) throw new Exception($"Invalid eligibility: seed {seed}, seat {i}, actor {body.name}, played {played}");
                    if (!played) continue;
                    var voice = target.GetValue(body);
                    var clip = (AnimationClip)voice.GetType().GetField("clip").GetValue(voice);
                    string expected = i == 0 ? "L" : "R"; // Both face -Z: world-left's partner is to its own left.
                    if (!AssetDatabase.GetAssetPath(clip).EndsWith("_" + expected + ".fbx")) throw new Exception("Wrong seat direction");
                    // A bad server suffix cannot override the local seat direction.
                    if (!body.PlayServerVariation("ACT_08.side_conversation_" + (i == 0 ? "r" : "l"), 3, 1)) throw new Exception("Directional remap failed");
                    voice = target.GetValue(body);
                    clip = (AnimationClip)voice.GetType().GetField("clip").GetValue(voice);
                    if (!AssetDatabase.GetAssetPath(clip).EndsWith("_" + expected + ".fbx")) throw new Exception("Incoming suffix overrode seat direction");
                    advance.Invoke(body, new object[] { .7f });
                    if (body.ConversationWeight < .99f) throw new Exception("Conversation failed to blend in");
                    if (!body.PlayServerVariation("BL_03.quiet_stable_posture", 2, 1)) throw new Exception("New reaction did not interrupt");
                    advance.Invoke(body, new object[] { .7f });
                    if (body.ConversationWeight > .01f) throw new Exception("Conversation failed to blend out");
                    body.StopAction();
                }
            }
            var rear = seats[0].Occupant.GetComponent<AudienceAnimationPlayer>();
            seats[1].Assign(null, false);
            if (rear.PlayServerVariation("ACT_08.side_conversation", 3, 1)) throw new Exception("Missing partner allowed");
            output.AppendLine("PASS 20 random arrangements: rear two only; opposite L/R on all six prefabs; wrong incoming side corrected; missing partner rejected; blend in/out and interruption.");
            // Inspect actual imported motion relative to the neutral pose, independently of file naming.
            foreach (var body in bodies.Where(b => b.name.Contains("_01")))
            {
                typeof(AudienceAnimationPlayer).GetMethod("DestroyAnimationGraph", Flags).Invoke(body, null);
                body.enabled = false;
                var animator = (Animator)typeof(AudienceAnimationPlayer).GetField("targetAnimator", Flags).GetValue(body);
                var head = animator.GetComponentsInChildren<Transform>(true).Single(t => t.name == "head");
                catalog.TryGetClip("BL_03.quiet_stable_posture", body.Gender, out var idle);
                idle.SampleAnimation(animator.gameObject, .2f);
                var baseline = head.rotation;
                var forward = body.transform.forward;
                var right = body.transform.right;
                foreach (var suffix in new[] { "l", "r" })
                {
                    catalog.TryGetClip("ACT_08.side_conversation_" + suffix, body.Gender, out var clip);
                    float total = 0;
                    for (int sample = 1; sample <= 8; sample++)
                    {
                        clip.SampleAnimation(animator.gameObject, clip.length * sample / 10);
                        total += Vector3.Dot(head.rotation * Quaternion.Inverse(baseline) * forward, right);
                    }
                    output.AppendLine($"MOTION {body.Gender} {suffix}: mean rightward head turn {total/8:F3}");
                }
            }
        }
        finally
        {
            foreach (var body in root.GetComponentsInChildren<AudienceAnimationPlayer>(true))
                typeof(AudienceAnimationPlayer).GetMethod("DestroyAnimationGraph", Flags).Invoke(body, null);
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        var layout = Object.FindObjectsByType<AudienceSeating>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(l => l.gameObject.scene == EditorSceneManager.GetActiveScene());
        if (layout)
        {
            foreach (var seat in layout.seats.Where(s => s.row == "rear"))
            {
                if (!seat.conversationPartner || seat.conversationPartner.row != "rear") throw new Exception("Scene rear partner missing");
                var delta = seat.conversationPartner.transform.position - seat.transform.position;
                output.AppendLine($"SCENE {seat.SeatId}: {(Vector3.Dot(delta,seat.transform.right)>0 ? "R" : "L")} -> {seat.conversationPartner.SeatId}");
            }
        }
        File.WriteAllText("Temp/RehearSideConversation.txt", output.ToString());
    }
}
