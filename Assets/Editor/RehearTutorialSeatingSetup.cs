using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit one-shot editor operation. Never moves seats automatically at runtime.
[InitializeOnLoad]
internal static class RehearTutorialSeatingSetup
{
    const string Request = "Temp/RehearTutorialSeating.request";
    static double next;
    static RehearTutorialSeatingSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearTutorialSeating.txt", "FAILED\n" + e); }
    }
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new InvalidOperationException("Open tutorial scene.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var table = all.Single(t => t.name == "Conference_table");
        var chairRoot = scene.GetRootGameObjects().Single(g => g.name == "Chair").transform;
        string[] chairs = { "Chair", "Chair (1)", "Chair (2)", "Chair (3)", "Chair (4)", "Chair (5)" };
        string[] people = { "Aud_M_01", "Aud_W_01", "Aud_W_03", "Aud_M_03", "Aud_W_02", "Aud_M_02" };
        // Two evenly spaced chairs on either long side; two aligned at the rear.
        Vector2[] offsets = {
            new Vector2(.60f, -1.16f), new Vector2(-.45f, -1.16f),
            new Vector2(-1.48f, -.415f), new Vector2(-1.48f, .415f),
            new Vector2(-.45f, 1.16f), new Vector2(.60f, 1.16f)
        };
        float[] yaws = { 315f, 315f, 0f, 0f, 25f, 25f };
        var targets = chairs.Select(n => chairRoot.Cast<Transform>().Single(t => t.name == n)).ToArray();
        var occupants = people.Select(n => all.Single(t => t.name == n && t.parent && t.parent.name == "Audience")).ToArray();
        var counter = all.Single(t => t.name == "Counter" && t.parent && t.parent.name == "_Props");
        Vector3 podiumPosition = counter.position, podiumScale = counter.localScale;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Align six audience seats");
        var report = new StringBuilder();
        try
        {
            for (int i = 0; i < targets.Length; i++)
            {
                var chair = targets[i]; var person = occupants[i];
                Vector3 originalChair = chair.position;
                Quaternion delta = Quaternion.Euler(0, Mathf.DeltaAngle(chair.eulerAngles.y, yaws[i]), 0);
                Vector3 newChair = new Vector3(table.position.x + offsets[i].x, originalChair.y, table.position.z + offsets[i].y);
                if (Vector3.Distance(newChair, originalChair) > .4f) throw new InvalidOperationException("Unexpected layout; seat shift exceeds 40 cm.");
                Vector3 relativeBefore = Quaternion.Inverse(chair.rotation) * (person.position - originalChair);
                float oldPersonHeight = person.position.y;
                Undo.RecordObjects(new UnityEngine.Object[] { chair, person }, "Align chair and seated person together");
                person.SetPositionAndRotation(newChair + delta * (person.position - originalChair), delta * person.rotation);
                chair.SetPositionAndRotation(newChair, delta * chair.rotation);
                Vector3 relativeAfter = Quaternion.Inverse(chair.rotation) * (person.position - chair.position);
                if (Vector3.Distance(relativeBefore, relativeAfter) > .0001f || Mathf.Abs(person.position.y - oldPersonHeight) > .0001f)
                    throw new InvalidOperationException("Seated pose or height changed.");
                PrefabUtility.RecordPrefabInstancePropertyModifications(chair);
                PrefabUtility.RecordPrefabInstancePropertyModifications(person);
                report.AppendLine($"{chair.name} + {person.name}: position={chair.position.ToString("F3")}, yaw={chair.eulerAngles.y:F1}, seatedOffsetPreserved=true");
            }
            if (counter.position != podiumPosition || counter.localScale != podiumScale) throw new InvalidOperationException("Podium changed unexpectedly.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = chairRoot.gameObject;
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(table.position + Vector3.up * .4f, Quaternion.Euler(60, 90, 0), 3.6f);
            report.AppendLine("saved=true\npodiumUnchanged=true\nlightingRebaked=false");
            File.WriteAllText("Temp/RehearTutorialSeating.txt", report.ToString());
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }
}
