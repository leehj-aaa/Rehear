using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class ConferenceSeatLayoutWindow : EditorWindow
{
    private int count = 10;
    private string message;
    [MenuItem("Rehear/Audience/좌석과 테이블 배치")]
    private static void Open() => GetWindow<ConferenceSeatLayoutWindow>("좌석과 테이블");
    private void OnGUI()
    {
        EditorGUILayout.LabelField("회의실 좌석", EditorStyles.boldLabel);
        count = EditorGUILayout.IntSlider("청중 수", count, 6, 10);
        EditorGUILayout.LabelField("6석: 기존 크기 / 7–8석: 중간 / 9–10석: 긴 테이블");
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("좌석과 테이블 배치 적용", GUILayout.Height(32)))
                try { var layout = ConferenceSeatLayoutEditor.ApplyToScene(count); message = count + "명 · " + layout.ChairCapacity + "석 · 테이블 " + layout.TableLength.ToString("0.00") + "m"; }
                catch (Exception e) { message = e.Message; Debug.LogException(e); }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
    }
}

[CustomEditor(typeof(ConferenceSeatLayout))]
public sealed class ConferenceSeatLayoutEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var layout = (ConferenceSeatLayout)target;
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            EditorGUILayout.BeginHorizontal();
            for (int n = 6; n <= 10; n++) { int count = n; if (GUILayout.Button(n + "명")) Apply(layout, count); }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("선택한 좌석 수 적용")) Apply(layout, layout.SeatCount);
        }
        EditorGUILayout.HelpBox("의자·좌석 앵커·테이블 크기를 함께 배치합니다.", MessageType.Info);
    }

    public static ConferenceSeatLayout ApplyToScene(int count)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("회의실 씬을 먼저 열어주세요.");
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var layout = all.Select(t => t.GetComponent<ConferenceSeatLayout>()).FirstOrDefault(c => c);
        if (!layout)
        {
            var seating = all.Select(t => t.GetComponent<AudienceSeating>()).FirstOrDefault(c => c);
            var chairRoot = scene.GetRootGameObjects().SingleOrDefault(g => g.name == "Chair");
            var table = all.SingleOrDefault(t => t.name == "Conference_table");
            if (!seating || !chairRoot || !table || seating.seats.Length != 6)
                throw new InvalidOperationException("기존 의자 6개와 좌석 앵커, 회의 테이블이 필요합니다.");
            var originals = Enumerable.Range(0, 6).Select(i => chairRoot.transform.Find(i == 0 ? "Chair" : "Chair (" + i + ")")).ToArray();
            if (originals.Any(t => !t)) throw new InvalidOperationException("기존 의자 참조가 없습니다.");
            var go = new GameObject("Conference Seat Layout"); Undo.RegisterCreatedObjectUndo(go, "Create conference seats");
            layout = Undo.AddComponent<ConferenceSeatLayout>(go);
            var chairs = new Transform[10]; var anchors = new AudienceSeat[10]; var actors = new Transform[6];
            Array.Copy(originals, chairs, 6); Array.Copy(seating.seats, anchors, 6);
            var candidates = seating.GetComponentsInChildren<Rehear.Evc.Audience.AudienceAgent>(true).Select(a => a.transform).ToList();
            for (int i = 0; i < 6 && candidates.Count > 0; i++)
            {
                var seat = anchors[i].transform.position;
                var actor = candidates.OrderBy(a => new Vector2(a.position.x - seat.x, a.position.z - seat.z).sqrMagnitude).First();
                actors[i] = actor; candidates.Remove(actor);
            }
            for (int i = 6; i < 10; i++)
            {
                int source = i % 2 == 0 ? 0 : 5;
                var chair = Instantiate(originals[source].gameObject, go.transform); chair.name = "Chair " + (i + 1);
                Undo.RegisterCreatedObjectUndo(chair, "Create conference chair"); chairs[i] = chair.transform;
                var anchor = Instantiate(anchors[source], go.transform); anchor.name = "Seat " + (i + 1);
                Undo.RegisterCreatedObjectUndo(anchor.gameObject, "Create seat anchor"); anchor.row = "extra_" + ((i - 6) / 2 + 1); anchor.side = i % 2 == 0 ? "left" : "right";
                anchor.conversationPartner = null; anchor.Assign(null, false); anchors[i] = anchor;
            }
            layout.Configure(table, chairs, anchors, actors);
        }
        Apply(layout, count); Selection.activeGameObject = layout.gameObject; return layout;
    }

    public static void Apply(ConferenceSeatLayout layout, int count)
    {
        if (Application.isPlaying) { layout.SetSeatCount(count); return; }
        int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Set conference seat count");
        var scene = layout.gameObject.scene;
        var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).SelectMany(t => new UnityEngine.Object[] { t, t.gameObject }).ToArray();
        Undo.RecordObjects(objects, "Arrange conference seats"); Undo.RecordObject(layout, "Choose seat count");
        var seating = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AudienceSeating>(true)).Single();
        var prefabs = seating.audiencePrefabs.Take(6).Concat(new[] {
            "Aud_M_04_ME2_Casual", "Aud_M_05_MG2_Business", "Aud_W_04_FM2_SmartCasual", "Aud_W_05_FH2_Business"
        }.Select(name => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/03_Prefabs/Audience/Presentation/" + name + ".prefab"))).ToArray();
        layout.ConfigureAudience(seating, prefabs);
        foreach (var seat in layout.SeatPool) Undo.RecordObject(seat, "Set rear conversation pair");
        layout.SetSeatCount(count);
        seating.RefreshEditorPreview(layout.ActiveSeats, prefabs, 24010);
        foreach (var chair in layout.Chairs) PrepareMovable(chair.gameObject);
        PrepareMovable(layout.Table.gameObject);
        foreach (var obj in objects) if (obj) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        EditorUtility.SetDirty(layout); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(group); SceneView.RepaintAll();
    }

    private static void PrepareMovable(GameObject root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            Undo.RecordObject(r, "Update movable furniture lighting");
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
            r.lightmapIndex = -1; r.realtimeLightmapIndex = -1; r.lightProbeUsage = LightProbeUsage.BlendProbes;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r); PrefabUtility.RecordPrefabInstancePropertyModifications(r.gameObject);
        }
    }
}

[InitializeOnLoad]
internal static class ConferenceSeatLayoutRequests
{
    static ConferenceSeatLayoutRequests() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        const string liveRequest = "Temp/ConferenceSeatLayout.live";
        if (EditorApplication.isPlaying && File.Exists(liveRequest))
        {
            File.Delete(liveRequest);
            try
            {
                var liveLayout = UnityEngine.Object.FindFirstObjectByType<ConferenceSeatLayout>();
                var liveSeating = UnityEngine.Object.FindFirstObjectByType<AudienceSeating>();
                if (liveSeating.members.Length != liveLayout.SeatCount || liveSeating.seats.Length != liveLayout.SeatCount ||
                    liveSeating.seats.Any(s => !s.Occupant || s.Occupant.Seat != s) ||
                    liveSeating.seats.Count(s => s.HasLaptop) != liveSeating.initialLaptopCount ||
                    liveSeating.seats.Count(s => s.Occupant.TryGetConversationVariation(out _)) != 2)
                    throw new Exception("Live audience initialization mismatch.");
                foreach (var coordinator in UnityEngine.Object.FindObjectsByType<Rehear.Evc.Audience.AudienceReactionCoordinator>(FindObjectsSortMode.None))
                    if (coordinator.ValidateSceneAgents().Count != 0) throw new Exception(string.Join(";", coordinator.ValidateSceneAgents()));
                File.WriteAllText("Temp/ConferenceSeatLayout-live.result", "PASS live Awake ordering; " + liveSeating.members.Length + " seated people; laptop assignment; rear pair only; coordinator membership.");
            }
            catch (Exception e) { File.WriteAllText("Temp/ConferenceSeatLayout-live.result", e.ToString()); }
            finally { EditorApplication.isPlaying = false; }
            return;
        }
        const string request = "Temp/ConferenceSeatLayout.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            var text = File.ReadAllText(request).Trim(); File.Delete(request);
            if (text == "room-audit")
            {
                var scene = SceneManager.GetActiveScene();
                var lines = new System.Text.StringBuilder();
                foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)))
                {
                    var t = r.transform; string path = t.name; while(t.parent) { t = t.parent; path = t.name + "/" + path; }
                    lines.AppendLine($"{path}|active={r.gameObject.activeInHierarchy}|bounds={r.bounds.ToString("F3")}|scale={r.transform.lossyScale}|rot={r.transform.eulerAngles}|mesh={AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>()?.sharedMesh)}|mats={string.Join(",",r.sharedMaterials.Where(m=>m).Select(m=>m.name))}");
                }
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None)) lines.AppendLine($"LIGHT {l.name}|{l.type}|{l.transform.position}|{l.lightmapBakeType}");
                File.WriteAllText("Temp/ConferenceRoom-audit.txt", lines.ToString()); return;
            }
            if (text == "playtest") { File.WriteAllText(liveRequest, ""); EditorApplication.isPlaying = true; return; }
            if (text == "view")
            {
                var previewLayout = UnityEngine.Object.FindFirstObjectByType<ConferenceSeatLayout>();
                var view = SceneView.lastActiveSceneView;
                var center = previewLayout.Table.GetComponent<Renderer>().bounds.center;
                var offset = new Vector3(0, 1.35f, -2.1f);
                view.LookAtDirect(center, Quaternion.LookRotation(-offset), offset.magnitude * Mathf.Sin(view.camera.fieldOfView * Mathf.Deg2Rad * .5f));
                view.Repaint();
                return;
            }
            if (text == "test") { TestLayouts(); return; }
            if (text == "assignments") { TestAssignments(); return; }
            var layout = ConferenceSeatLayoutEditor.ApplyToScene(int.Parse(text));
            var report = $"seats={layout.SeatCount}; activeChairs={layout.Chairs.Count(t => t.gameObject.activeSelf)}; tableLength={layout.TableLength:F3}\n";
            File.WriteAllText("Temp/ConferenceSeatLayout.result", report);
        }
        catch (Exception e) { File.WriteAllText("Temp/ConferenceSeatLayout.result", e.ToString()); Debug.LogException(e); }
    }
    private static void TestLayouts()
    {
        var layout = ConferenceSeatLayoutEditor.ApplyToScene(6);
        var center = layout.Table.GetComponent<Renderer>().bounds.center;
        float previousLength = layout.TableLength;
        var lines = new System.Text.StringBuilder();
        for (int n = 6; n <= 10; n++)
        {
            layout.SetSeatCount(n);
            var first = layout.Chairs.Select(t => t.position).ToArray();
            layout.SetSeatCount(n);
            if (layout.Chairs.Count(t => t.gameObject.activeSelf) != layout.ChairCapacity || layout.ActiveSeats.Length != n)
                throw new Exception("Seat activation mismatch at " + n);
            if (Vector3.Distance(layout.Table.GetComponent<Renderer>().bounds.center, center) > .001f)
                throw new Exception("Table center drift at " + n);
            if (layout.TableLength < previousLength - .001f) throw new Exception("Table shrank when adding seats.");
            previousLength = layout.TableLength;
            if (layout.Chairs.Where((t, i) => Vector3.Distance(t.position, first[i]) > .001f).Any())
                throw new Exception("Repeated layout drifts.");
            float distance = float.MaxValue;
            for (int a = 0; a < layout.ChairCapacity; a++) for (int b = a + 1; b < layout.ChairCapacity; b++) distance = Mathf.Min(distance, Vector3.Distance(layout.Chairs[a].position, layout.Chairs[b].position));
            if (distance < .6f) throw new Exception("Chairs overlap at " + n);
            if (ConferenceSeatLayout.SideOrder(n, true).Concat(ConferenceSeatLayout.SideOrder(n, false)).Distinct().Count() != n)
                throw new Exception("Duplicate chair slot.");
            lines.AppendLine($"PASS seats={n} length={layout.TableLength:F3} minChairDistance={distance:F3}");
        }
        foreach (int invalid in new[] { 5, 11 })
        {
            bool rejected = false; try { layout.SetSeatCount(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            if (!rejected || layout.SeatCount != 10) throw new Exception("Invalid seat count changed layout.");
        }
        var floors = layout.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Where(r => r.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        if (floors.Length > 0)
        {
            var floor = floors[0].bounds; foreach (var f in floors.Skip(1)) floor.Encapsulate(f.bounds);
            foreach (var chair in layout.Chairs)
                if (chair.position.x < floor.min.x + .35f || chair.position.x > floor.max.x - .35f || chair.position.z < floor.min.z + .35f || chair.position.z > floor.max.z - .35f)
                    throw new Exception("Room needs expansion near " + chair.name + " at " + chair.position + "; floor=" + floor);
            lines.AppendLine("PASS all ten chairs remain inside the floor footprint; floor=" + floor);
        }
        layout.SetSeatCount(6);layout.SetSeatCount(10);ConferenceSeatLayoutEditor.Apply(layout, 10);
        var view = SceneView.lastActiveSceneView;
        if (view) view.LookAt(center, Quaternion.Euler(55, 0, 0), 5);
        File.WriteAllText("Temp/ConferenceSeatLayout.result", lines.ToString());
    }

    private static void TestAssignments()
    {
        var layout = ConferenceSeatLayoutEditor.ApplyToScene(10);
        var source = UnityEngine.Object.FindFirstObjectByType<AudienceSeating>();
        var report = new System.Text.StringBuilder();
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        for (int count = 6; count <= 10; count++)
        {
            layout.SetSeatCount(count);
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Seating test");
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var seating = root.AddComponent<AudienceSeating>();
                seating.seats = layout.ActiveSeats.Select(s => UnityEngine.Object.Instantiate(s, root.transform)).ToArray();
                foreach (var seat in seating.seats) { seat.Assign(null, false); seat.conversationPartner = null; }
                seating.seats[2].conversationPartner = seating.seats[3];
                seating.seats[3].conversationPartner = seating.seats[2];
                seating.audiencePrefabs = layout.AudiencePrefabPool.Take(count).ToArray();
                seating.laptopPrefabs = source.laptopPrefabs;
                seating.initialLaptopCount = 2;
                seating.Initialize(4100 + count);
                Require(seating.members.Length == count && seating.members.Select(a => a.AgentId).Distinct().Count() == count, "Member count mismatch.");
                Require(seating.seats.All(s => s.Occupant && s.Occupant.Seat == s), "Unassigned seat.");
                Require(seating.seats.Select(s => s.Occupant).Distinct().Count() == count, "Duplicate occupant.");
                Require(seating.seats.Count(s => s.HasLaptop) == 2, "Laptop count mismatch.");
                foreach (var seat in seating.seats)
                {
                    Require(seat.Occupant.Allows("ACT_01.laptoptyping") == seat.HasLaptop, "Typing allowed without laptop.");
                    Require(seat.Occupant.TryGetConversationVariation(out _) == (seat.row == "rear"), "Conversation not restricted to rear pair.");
                    if (seat.HasLaptop) Require(seating.SpawnedLaptops.Any(l => l.activeSelf && Vector3.Distance(l.transform.position, seat.laptopAnchor.position) < .001f && Quaternion.Angle(l.transform.rotation, seat.laptopAnchor.rotation) < .01f), "Laptop missed its anchor.");
                }
                var pair = seating.seats.Where(s => s.row == "rear").ToArray();
                Require(pair.Length == 2, "Expected exactly two rear seats.");
                pair[0].Occupant.TryGetConversationVariation(out var leftVariation);
                pair[1].Occupant.TryGetConversationVariation(out var rightVariation);
                Require(leftVariation != rightVariation, "Rear conversation directions match.");
                var profiles = seating.seats.Select((s, i) => new Rehear.Evc.Contracts.AudienceDto {
                    agent_id = seating.members[count - i - 1].AgentId,
                    profile = new Rehear.Evc.Contracts.AudienceSeatingProfile { row = s.row, seat = s.side, has_laptop = i % 2 == 0 }
                }).ToArray();
                seating.ApplyServerProfiles(profiles);
                Require(seating.seats.Count(s => s.HasLaptop) == (count + 1) / 2, "Profile laptop remap failed.");
                Require(seating.seats.All(s => s.Occupant && s.Occupant.Seat == s), "Profile seat remap failed.");
                var before = seating.seats.Select(s => s.Occupant).ToArray();
                var invalid = profiles.ToArray(); invalid[1] = invalid[0];
                bool rejected = false;
                try { seating.ApplyServerProfiles(invalid); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected && seating.seats.Select(s => s.Occupant).SequenceEqual(before), "Invalid profiles modified assignments.");
                if (count > 6)
                {
                    seating.ApplyServerProfiles(seating.seats.Take(6).Select((s, i) => new Rehear.Evc.Contracts.AudienceDto {
                        agent_id = "audience_" + (i + 1).ToString("00"),
                        profile = new Rehear.Evc.Contracts.AudienceSeatingProfile { row = s.row, seat = s.side, has_laptop = false }
                    }).ToArray());
                    Require(seating.seats.Select(s => s.Occupant).Distinct().Count() == count, "Legacy profiles lost additional participants.");
                }
                report.AppendLine($"PASS {count} people: unique seating; laptop anchors and typing eligibility; profile remap; only rear pair conversations with opposite directions.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
        }
        ConferenceSeatLayoutEditor.Apply(layout, 10);
        File.WriteAllText("Temp/ConferenceSeatLayout.result", report.ToString());
    }
}

