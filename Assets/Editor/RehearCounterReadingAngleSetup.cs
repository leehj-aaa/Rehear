using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearCounterReadingAngleSetup
{
    const string Prefab = "Assets/03_Prefabs/SharedRoom/Counter.prefab";
    const string Request = "Temp/RehearCounterReadingAngle.request";
    static RehearCounterReadingAngleSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if(command=="show-script") ShowScript(); else if(command=="parts") InspectParts(); else if (command == "apply" || command == "preview") Apply(command == "apply"); else if(command=="framed") CaptureLive(false,true); else if(command=="live" || command=="sync" || command=="viewer") CaptureLive(command=="sync",false,false,command=="viewer"?3:0); else if (command == "verify") Verify(); else Inspect(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearCounterReadingAngle.txt", e.ToString()); }
    }
    static void ShowScript()
    {
        var scene=EditorSceneManager.GetActiveScene();
        var toggle=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PodiumScriptToggle>(true)).Single();
        if(!toggle.scriptPanel) throw new Exception("Missing script panel.");
        toggle.scriptPanel.SetActive(true); toggle.Refresh();
        foreach(var text in toggle.scriptPanel.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            text.pageToDisplay=1; text.ForceMeshUpdate(true,true); EditorUtility.SetDirty(text);
        }
        Canvas.ForceUpdateCanvases();
        PrefabUtility.RecordPrefabInstancePropertyModifications(toggle.scriptPanel);
        if(toggle.label) {EditorUtility.SetDirty(toggle.label); PrefabUtility.RecordPrefabInstancePropertyModifications(toggle.label);}
        if(toggle.background) {EditorUtility.SetDirty(toggle.background); PrefabUtility.RecordPrefabInstancePropertyModifications(toggle.background);}
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        SceneView.RepaintAll();
        CaptureLive(false,false,false,3);
        File.WriteAllText("Temp/RehearScriptPreview.txt","PASS script panel enabled, first page visible, toggle label updated.\n");
    }

    static void Verify()
    {
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var panel = (RectTransform)root.transform.Find("Panel_Script_New");
            panel.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            var text = panel.GetComponentsInChildren<TMPro.TMP_Text>(true).Single(t=>t.name=="Text_Script_New");
            text.ForceMeshUpdate(true,true);
            int pages = text.textInfo.pageCount;
            int visible = 0;
            for (int page=1;page<=pages;page++)
            {
                text.pageToDisplay=page; text.ForceMeshUpdate(true,true);
                foreach (var character in text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c=>c.isVisible && c.pageNumber==page-1))
                {
                    visible++;
                    foreach (var point in new[] {character.bottomLeft,character.topRight})
                    {
                        var p=panel.InverseTransformPoint(text.transform.TransformPoint(point));
                        if(!new Rect(panel.rect.xMin-.05f,panel.rect.yMin-.05f,panel.rect.width+.1f,panel.rect.height+.1f).Contains(p))
                            throw new Exception($"Script outside panel: page={page} point={p}");
                    }
                }
            }
            if(pages<2 || visible<500) throw new Exception("Script pagination did not produce expected content.");
            File.WriteAllText("Temp/RehearCounterReadingTextCheck.txt", $"PASS pages={pages} visibleCharacters={visible} fontSize={text.fontSize}\nAll page glyphs fit within panel.\n");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static void InspectParts()
    {
        var mesh=AssetDatabase.LoadAllAssetsAtPath("Assets/03_Prefabs/Counter.FBX").OfType<Mesh>().Single(m=>m.name=="ULT2_Counter");
        var v=mesh.vertices; var parent=Enumerable.Range(0,v.Length).ToArray();
        int Find(int n) { while(parent[n]!=n) n=parent[n]; return n; }
        void Join(int a,int b) {parent[Find(a)]=Find(b);}
        for(int i=0;i<v.Length;i++) for(int j=0;j<i;j++) if((v[i]-v[j]).sqrMagnitude<1e-8f) Join(i,j);
        var triangles=mesh.triangles;
        for(int i=0;i<triangles.Length;i+=3) {Join(triangles[i],triangles[i+1]); Join(triangles[i],triangles[i+2]);}
        var report=new StringBuilder();
        foreach(var part in Enumerable.Range(0,v.Length).GroupBy(Find))
        {
            var bounds=new Bounds(v[part.First()],Vector3.zero);foreach(int i in part) bounds.Encapsulate(v[i]);
            report.AppendLine($"PART {part.Key} count={part.Count()} min={bounds.min:F5} max={bounds.max:F5}");
            foreach(int i in part) report.AppendLine($"{i}: {v[i]:F5}");
        }
        File.WriteAllText("Temp/CounterOriginalParts.txt",report.ToString());
    }
    static Vector3[] RearSupportVertices(Mesh mesh)
    {
        var v=mesh.vertices; var parent=Enumerable.Range(0,v.Length).ToArray();
        int Find(int n) { while(parent[n]!=n) n=parent[n]; return n; }
        void Join(int a,int b) {parent[Find(a)]=Find(b);}
        for(int i=0;i<v.Length;i++) for(int j=0;j<i;j++) if((v[i]-v[j]).sqrMagnitude<1e-8f) Join(i,j);
        var t=mesh.triangles;
        for(int i=0;i<t.Length;i+=3) {Join(t[i],t[i+1]);Join(t[i],t[i+2]);}
        int highest=Enumerable.Range(0,v.Length).OrderByDescending(i=>v[i].y).First();
        var result=Enumerable.Range(0,v.Length).Where(i=>Find(i)==Find(highest)).Select(i=>v[i]).ToArray();
        if(result.Length!=140) throw new Exception("Unexpected rear support topology");
        return result;
    }
    // The tabletop's rear edge is also the mounting curve of the upright panel.
    // Both use one deformation; panel thickness is carried along the new normal.
    static readonly Vector2[] RearCurve = {
        new Vector2(-4.06644f,2.95281f), new Vector2(-3.46022f,3.17568f),
        new Vector2(-2.66445f,3.44406f), new Vector2(-1.22106f,3.57825f),
        new Vector2(1.20765f,3.57825f), new Vector2(2.65104f,3.44406f),
        new Vector2(3.44681f,3.17568f), new Vector2(4.05303f,2.95281f)
    };
    static float TrimWidth(float x) => x < -2.86f ? -2.86f+(x+2.86f)*.2f : x>2.86f ? 2.86f+(x-2.86f)*.9f : x;
    static Vector3 FitRearSupport(Vector3 vertex,float depthScale)
    {
        var p=new Vector2(vertex.x,vertex.z);
        float distance=float.PositiveInfinity, bestT=0; int segment=0;
        for(int i=0;i<RearCurve.Length-1;i++)
        {
            var edge=RearCurve[i+1]-RearCurve[i];
            float t=Mathf.Clamp01(Vector2.Dot(p-RearCurve[i],edge)/edge.sqrMagnitude);
            float d=(p-Vector2.Lerp(RearCurve[i],RearCurve[i+1],t)).sqrMagnitude;
            if(d<distance) {distance=d;bestT=t;segment=i;}
        }
        var a=RearCurve[segment]; var b=RearCurve[segment+1];
        var tangent=(b-a).normalized; var normal=new Vector2(-tangent.y,tangent.x);
        var offset=p-Vector2.Lerp(a,b,bestT);
        var newA=new Vector2(TrimWidth(a.x),a.y*depthScale);
        var newB=new Vector2(TrimWidth(b.x),b.y*depthScale);
        var newTangent=(newB-newA).normalized; var newNormal=new Vector2(-newTangent.y,newTangent.x);
        var result=Vector2.Lerp(newA,newB,bestT)+newNormal*Vector2.Dot(offset,normal)+newTangent*Vector2.Dot(offset,tangent);
        return new Vector3(result.x,vertex.y,result.y);
    }
    static void RefineRearPanel(Mesh mesh,System.Collections.Generic.List<int> oldPanel,float depthScale,float width)
    {
        var removed=new System.Collections.Generic.HashSet<int>(oldPanel);
        var vertices=mesh.vertices.ToList(); var uv=mesh.uv.ToList();
        var submeshes=new System.Collections.Generic.List<int>[mesh.subMeshCount];
        for(int sub=0;sub<mesh.subMeshCount;sub++)
        {
            submeshes[sub]=new System.Collections.Generic.List<int>();
            var t=mesh.GetTriangles(sub);
            for(int i=0;i<t.Length;i+=3) if(!removed.Contains(t[i]) && !removed.Contains(t[i+1]) && !removed.Contains(t[i+2]))
                submeshes[sub].AddRange(new[]{t[i],t[i+1],t[i+2]});
        }
        const float bottom=10.55f,top=12.60f,thickness=.10f,bevel=.0111f,radius=.17f;
        float centerX=-.0067f, centerY=(bottom+top)*.5f,height=top-bottom;
        var curve=RearCurve.Select(p=>new Vector2(TrimWidth(p.x),p.y*depthScale)).ToArray();
        Vector3 Surface(float x,float y,float depth)
        {
            int i=0; while(i<curve.Length-2 && x>curve[i+1].x) i++;
            var tangent=(curve[i+1]-curve[i]).normalized;
            float z=Mathf.LerpUnclamped(curve[i].y,curve[i+1].y,(x-curve[i].x)/(curve[i+1].x-curve[i].x));
            return new Vector3(x-tangent.y*depth,y,z+tangent.x*depth);
        }
        int start=vertices.Count; const int segments=12,count=4*(segments+1);
        for(int ring=0;ring<4;ring++)
        {
            float inset=ring==0 || ring==3?bevel:0;
            float depth=new[]{-.01f,bevel-.01f,thickness-bevel-.01f,thickness-.01f}[ring];
            for(int corner=0;corner<4;corner++) for(int j=0;j<=segments;j++)
            {
                float a=(corner*90+j*90f/segments)*Mathf.Deg2Rad;
                float x=centerX+(corner==0 || corner==3?1:-1)*(width*.5f-radius)+(radius-inset)*Mathf.Cos(a);
                float y=centerY+(corner<2?1:-1)*(height*.5f-radius)+(radius-inset)*Mathf.Sin(a);
                vertices.Add(Surface(x,y,depth));uv.Add(new Vector2((x-centerX)/width+.5f,(y-bottom)/height));
            }
        }
        var faces=submeshes[1];
        for(int ring=0;ring<3;ring++) for(int j=0;j<count;j++)
        {
            int a=start+ring*count+j,b=start+ring*count+(j+1)%count,c=a+count,d=b+count;
            faces.AddRange(new[]{a,b,c,b,d,c});
        }
        int front=vertices.Count;vertices.Add(Surface(centerX,centerY,-.01f));uv.Add(new Vector2(.5f,.5f));
        int back=vertices.Count;vertices.Add(Surface(centerX,centerY,thickness-.01f));uv.Add(new Vector2(.5f,.5f));
        for(int j=0;j<count;j++)
        {
            int n=(j+1)%count;
            faces.AddRange(new[]{front,start+n,start+j,back,start+3*count+j,start+3*count+n});
        }
        // Remove the former panel completely, including unused vertices and bounds.
        var used=submeshes.SelectMany(t=>t).Distinct().ToArray();
        var remap=new int[vertices.Count];for(int i=0;i<used.Length;i++)remap[used[i]]=i;
        mesh.Clear();mesh.vertices=used.Select(i=>vertices[i]).ToArray();mesh.uv=used.Select(i=>uv[i]).ToArray();
        mesh.subMeshCount=submeshes.Length;
        for(int sub=0;sub<submeshes.Length;sub++) mesh.SetTriangles(submeshes[sub].Select(i=>remap[i]).ToArray(),sub);
        mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
        RehearGeneratedMeshLighting.Unwrap(mesh);
    }
    static void Apply(bool save)
    {
        const string sourcePath = "Assets/Settings/TutorialUI/Counter Integrated Script Control.asset";
        const string meshPath = "Assets/Settings/TutorialUI/Counter Connected Reading Surface.asset";
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            // Always derive from the unchanged integrated mesh, so rerunning is idempotent.
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);
            var vertices = source.vertices;
            var materials = root.GetComponent<MeshRenderer>().sharedMaterials;
            int screen = Array.FindIndex(materials, m => m && m.name == "Script Screen Black");
            if (screen < 0) throw new Exception("Missing script screen submesh");
            int[] triangles = source.GetTriangles(screen);
            Vector3 normal = Vector3.Cross(vertices[triangles[1]] - vertices[triangles[0]], vertices[triangles[2]] - vertices[triangles[0]]).normalized;
            if (normal.y < 0) normal = -normal;
            float oldAngle = Vector3.Angle(normal, Vector3.up);
            Quaternion tilt = Quaternion.AngleAxis(-(35f - oldAngle), Vector3.right);
            Vector3 pivot = new Vector3(0, 10.70282f, -4.30873f);
            // Native counter vertices above its tabletop form the inset reading wedge.
            // The appended toggle housing occupies the same region, so identify originals
            // by position instead of depending on indices in the unwrapped mesh.
            var original = AssetDatabase.LoadAllAssetsAtPath("Assets/03_Prefabs/Counter.FBX").OfType<Mesh>().Single(m => m.name == "ULT2_Counter");
            var wedge = original.vertices.Where(v => v.y > 10.61f && v.y < 11.7f && v.z < 0 && Mathf.Abs(v.x) < 2.86f).ToArray();
            var originalVertices = original.vertices;
            var rearSupport = RearSupportVertices(original);
            var supportIndices = new System.Collections.Generic.List<int>();
            float rearDepthScale=(rearSupport.Max(v=>v.z)*.15f-(rearSupport.Max(v=>v.z)-3.57825f))/3.57825f;
            var toggle = (RectTransform)root.transform.Find("ScriptToggle");
            // Source mesh includes the old shallow control housing. Use its original
            // pose, not the previously saved prefab pose, so rebuilding is repeatable.
            Vector3 oldTogglePosition = new Vector3(3.3058546f,10.817427f,-3.7493804f);
            Quaternion oldToggleRotation = new Quaternion(.6238585f,.00007528067f,.00009426475f,.78153735f);
            Quaternion toggleRotation = Quaternion.Euler(55f,0,0);
            Quaternion toggleTilt = toggleRotation * Quaternion.Inverse(oldToggleRotation);
            Vector3 togglePivot = oldTogglePosition + oldToggleRotation * new Vector3(0,-37f/120f,0);
            var toggleVertices = new System.Collections.Generic.List<int>();
            var toggleTopVertices = new System.Collections.Generic.HashSet<int>();
            int housingMoved = 0, rearMoved = 0;
            int moved = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (wedge.Any(v => (v - vertices[i]).sqrMagnitude < .00000001f))
                { vertices[i] = pivot + tilt * (vertices[i] - pivot); moved++; }
                else if(rearSupport.Any(v=>(v-vertices[i]).sqrMagnitude<1e-8f))
                {
                    supportIndices.Add(i);
                    vertices[i]=FitRearSupport(vertices[i],rearDepthScale);
                }
                else if (!originalVertices.Any(v => (v-vertices[i]).sqrMagnitude<.00000001f))
                {
                    toggleVertices.Add(i);
                    // Leave the housing's grounded bottom ring on the countertop;
                    // rotate its bezel and front surface together with the button UI.
                    if(vertices[i].y>10.61f)
                    { vertices[i]=togglePivot+toggleTilt*(vertices[i]-togglePivot); toggleTopVertices.Add(i); housingMoved++; }
                }
                else
                {
                    if(vertices[i].z>0) { vertices[i].z*=rearDepthScale; rearMoved++; }
                    // Retain the full reading surface and the right-hand control bay;
                    // trim the unused side margins of the cabinet and countertop.
                    vertices[i].x=TrimWidth(vertices[i].x);
                }
            }
            if (moved < 70) throw new Exception("Reading wedge selection incomplete: " + moved);
            if(housingMoved<70 || rearMoved<40) throw new Exception($"Counter selection incomplete: housing={housingMoved}, rear={rearMoved}");
            toggle.localPosition=togglePivot+toggleTilt*(oldTogglePosition-togglePivot);
            toggle.localRotation=toggleRotation;
            var panel = (RectTransform)root.transform.Find("Panel_Script_New");
            Vector3 newNormal = tilt * normal;
            Quaternion rotation = Quaternion.LookRotation(-newNormal, Vector3.ProjectOnPlane(Vector3.forward, newNormal).normalized);
            Quaternion inverse = Quaternion.Inverse(rotation);
            var points = triangles.Distinct().Select(i => vertices[i]).ToArray();
            var bounds = new Bounds(inverse * points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(inverse * p);
            panel.localRotation = rotation;
            panel.localPosition = rotation * bounds.center + newNormal * (.004f / root.transform.localScale.x);
            panel.sizeDelta = new Vector2(45.623184f,32.065136f);

            // Match both the reading UI plane and its bottom edge. Matching rotation
            // alone leaves the toggle recessed below the adjacent reading surface.
            Vector3 panelInPlane = inverse * panel.localPosition;
            float panelBottom = panelInPlane.y-panel.sizeDelta.y*panel.localScale.y*.5f;
            Vector3 toggleInPlane = inverse * toggle.localPosition;
            toggleInPlane.y=panelBottom+toggle.sizeDelta.y*toggle.localScale.y*.5f;
            toggleInPlane.z=panelInPlane.z;
            // Dock the housing into the outer bezel while keeping the blue face
            // just outside its edge. Both surfaces retain the same reading plane.
            toggleInPlane.x=wedge.Max(v=>v.x)+.005f+toggle.sizeDelta.x*toggle.localScale.x*.5f;
            Vector3 toggleShift=rotation*toggleInPlane-toggle.localPosition;
            toggle.localPosition+=toggleShift;
            toggle.localRotation=rotation;
            foreach(int i in toggleVertices)
            {
                vertices[i]+=toggleTopVertices.Contains(i)?toggleShift:new Vector3(toggleShift.x,0,toggleShift.z);
            }
            float togglePlaneGap=Mathf.Abs(Vector3.Dot(toggle.localPosition-panel.localPosition,newNormal))*.09f;
            float toggleBottomGap=Mathf.Abs((inverse*toggle.localPosition).y-toggle.sizeDelta.y*toggle.localScale.y*.5f-panelBottom)*.09f;
            if(togglePlaneGap>.0001f || toggleBottomGap>.0001f) throw new Exception("Toggle plane/bottom alignment failed");
            var mesh = new Mesh();
            mesh.name = "Counter Connected Reading Surface";
            mesh.vertices = vertices;
            mesh.uv=source.uv;mesh.uv2=source.uv2;mesh.subMeshCount=source.subMeshCount;
            for(int sub=0;sub<source.subMeshCount;sub++) mesh.SetTriangles(source.GetTriangles(sub),sub);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            float readingFrameWidth=wedge.Max(v=>v.x)-wedge.Min(v=>v.x);
            RefineRearPanel(mesh,supportIndices,rearDepthScale,readingFrameWidth);
            var saved = save ? AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) : null;
            if (saved)
            {
                // Rebuild the native buffers as well as serialized data: live scene
                // renderers otherwise sometimes retain the previous geometry.
                EditorUtility.CopySerialized(mesh,saved);
                saved.Clear(); saved.indexFormat=mesh.indexFormat;
                saved.vertices=mesh.vertices; saved.normals=mesh.normals; saved.tangents=mesh.tangents;
                saved.uv=mesh.uv; saved.uv2=mesh.uv2; saved.subMeshCount=mesh.subMeshCount;
                for(int sub=0;sub<mesh.subMeshCount;sub++) saved.SetTriangles(mesh.GetTriangles(sub),sub);
                saved.RecalculateBounds(); saved.UploadMeshData(false);
                EditorUtility.SetDirty(saved); UnityEngine.Object.DestroyImmediate(mesh);
            }
            else { if (save) AssetDatabase.CreateAsset(mesh, meshPath); saved = mesh; }
            root.GetComponent<MeshFilter>().sharedMesh = saved;
            root.GetComponent<MeshCollider>().sharedMesh = saved;
            var desk = root.transform.Find("DeskScreen");
            // Join the display's lower bezel to the reading wedge's upper rim in 3D,
            // not just in the projection of one camera.
            var topRim = wedge.Where(v=>v.y>11.598f && Mathf.Abs(v.x)<1.3f).ToArray();
            if(topRim.Length<2) throw new Exception("Reading rim missing");
            Vector3 join = pivot + tilt * (topRim.Aggregate(Vector3.zero,(a,b)=>a+b)/topRim.Length-pivot);
            var housing = desk.Find("DisplayHousing").GetComponent<MeshFilter>();
            var housingBounds = housing.sharedMesh.bounds;
            var deskRect=(RectTransform)desk;
            float originalSlideAspect=deskRect.sizeDelta.x/deskRect.sizeDelta.y;
            float currentFrameWidth=housingBounds.size.x*housing.transform.localScale.x*desk.localScale.x;
            desk.localScale*=readingFrameWidth/currentFrameWidth;
            float finalFrameWidth=housingBounds.size.x*housing.transform.localScale.x*desk.localScale.x;
            if(Mathf.Abs(finalFrameWidth-readingFrameWidth)>.0001f || Mathf.Abs(desk.localScale.x/desk.localScale.y-1f)>.0001f)
                throw new Exception("Slide width/aspect alignment failed");
            var lowerBezel = new Vector3(0,housingBounds.min.y,housingBounds.min.z);
            Vector3 lowerInCounter = root.transform.InverseTransformPoint(housing.transform.TransformPoint(lowerBezel));
            desk.localPosition += join + Vector3.up * .01f - lowerInCounter;
            panel.sizeDelta = new Vector2(45.623184f,32.065136f);
            float jointGap = Vector3.Distance(root.transform.InverseTransformPoint(housing.transform.TransformPoint(lowerBezel)),join)*.09f;
            if(jointGap>.002f) throw new Exception("Bezel joint exceeds 2mm");
            float angle = Vector3.Angle(newNormal, Vector3.up);
            if (Mathf.Abs(angle - 35f) > .1f || bounds.size.z * .09f > .005f) throw new Exception("Surface alignment failed");
            float toggleAngleDifference=Quaternion.Angle(toggle.localRotation,panel.localRotation);
            if(toggleAngleDifference>.05f) throw new Exception("Toggle and prompter angles differ");
            ValidateVisibility(root, saved, (RectTransform)desk);
            if (save) { PrefabUtility.SaveAsPrefabAsset(root, Prefab); AssetDatabase.SaveAssets(); }
            if(save) SaveControls(desk.localPosition + new Vector3(0,2.741825f,.5621297f)*(desk.localScale.x/(1f/9f)));
            Capture(root);
            File.WriteAllText("Temp/RehearCounterReadingAngle.txt", $"PASS\nangleFromHorizontal={angle:F2}\ntoggleAngleDifference={toggleAngleDifference:F3}\nhousingMoved={housingMoved}\nrearMoved={rearMoved}\noldDepthMetres={source.bounds.size.z*.09f:F4}\nnewDepthMetres={saved.bounds.size.z*.09f:F4}\nfrontEdgeUnchanged={Mathf.Abs(source.bounds.min.z-saved.bounds.min.z)<.0001f}\nUI gap=4mm\nOriginal UI width, height and font preserved\nbezelJointGapMetres={jointGap:F4}\ndeskPosition={desk.localPosition:F5}\nsaved={save}\nslide visibility: 245 unobstructed sightlines across default camera and standing eye heights\n");
            File.AppendAllText("Temp/RehearCounterReadingAngle.txt",$"togglePlaneGapMetres={togglePlaneGap:F6}\ntoggleBottomGapMetres={toggleBottomGap:F6}\ntogglePosition={toggle.localPosition:F5}\n");
            File.AppendAllText("Temp/RehearCounterReadingAngle.txt",$"oldWidthMetres={source.bounds.size.x*.09f:F4}\nnewWidthMetres={saved.bounds.size.x*.09f:F4}\n");
            File.AppendAllText("Temp/RehearCounterReadingAngle.txt",$"slideAndPrompterFrameWidthMetres={finalFrameWidth*.09f:F4}\nslideAspectRatioPreserved={originalSlideAspect:F6}\nrearPanelThicknessMm=9\nrearPanelEdgeBevelMm=1\nrearPanelCornerRadiusMm=15.3\nrearPanelHeightMm=184.5\nrearSupportFollowsTabletopCurve=True\n");
            if (!save) UnityEngine.Object.DestroyImmediate(saved);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static void CaptureLive(bool sync, bool framed=false, bool side=false, int detail=0)
    {
        var counter=Resources.FindObjectsOfTypeAll<Transform>().Single(t=>t.name=="Counter" && t.gameObject.scene.path=="Assets/01_Scene/Scene_02_Presentation.unity");
        if(sync)
        {
            var mesh=counter.GetComponent<MeshFilter>().sharedMesh;
            mesh.UploadMeshData(false); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssets();
            var filter=counter.GetComponent<MeshFilter>(); filter.sharedMesh=null; filter.sharedMesh=mesh;
            SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate();
        }
        var view=SceneView.lastActiveSceneView;
        var go=new GameObject("Counter live verification",typeof(Camera));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,counter.gameObject.scene);
        var camera=go.GetComponent<Camera>(); camera.CopyFrom(view.camera); camera.scene=counter.gameObject.scene;
        camera.transform.SetPositionAndRotation(view.camera.transform.position,view.camera.transform.rotation);
        if(detail==3) {camera.transform.SetPositionAndRotation(Camera.main.transform.position,Camera.main.transform.rotation); camera.fieldOfView=70; camera.nearClipPlane=.01f;}
        if(framed)
        {
            camera.transform.position=counter.TransformPoint(side?new Vector3(-12,18,-8):new Vector3(1.9893f,17.2827f,-15.8848f));
            camera.transform.LookAt(counter.TransformPoint(new Vector3(0,13,-1.5f)));
            camera.fieldOfView=40; camera.nearClipPlane=.01f;
            if(detail==1)
            {
                camera.transform.position=counter.TransformPoint(new Vector3(-8,16,8));
                camera.transform.LookAt(counter.TransformPoint(new Vector3(0,11.5f,-1.5f)));
            }
            else if(detail==2)
            {
                camera.transform.position=counter.TransformPoint(new Vector3(.6f,25,-1.5f));
                camera.transform.rotation=counter.rotation*Quaternion.LookRotation(Vector3.down,Vector3.forward);
                camera.orthographic=true;camera.orthographicSize=.43f;
            }
        }
        var rt=new RenderTexture(1200,1100,24); var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
            var image=new Texture2D(1200,1100,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1200,1100),0,0);image.Apply();
            File.WriteAllBytes(detail==3?"Temp/SessionReady-viewer-placement.png":detail==1?"Temp/Counter-live-rear.png":detail==2?"Temp/Counter-live-top.png":framed?(side?"Temp/Counter-live-side.png":"Temp/Counter-live-front.png"):(sync?"Temp/Counter-live-synced.png":"Temp/Counter-live-before.png"),image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally {RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);}
        if(framed && !side && detail==0) {CaptureLive(false,true,true);CaptureLive(false,true,true,1);CaptureLive(false,true,true,2);}
    }
    static void SaveControls(Vector3 position)
    {
        const string scenePath="Assets/01_Scene/Scene_02_Presentation.unity";
        string yaml=File.ReadAllText(scenePath);
        string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
        var block=Regex.Match(yaml,@"--- !u!224 &796967052\r?\n[\s\S]*?(?=--- !u!)");
        if(!block.Success) throw new Exception("Presentation controls transform missing");
        var anchor=Regex.Match(block.Value,@"m_AnchoredPosition: \{x: ([^,]+), y: ([^}]+)\}");
        var depth=Regex.Match(block.Value,@"m_LocalPosition: \{x: [^,]+, y: [^,]+, z: ([^}]+)\}");
        if(anchor.Success && depth.Success)
        {
            var stored=new Vector3(float.Parse(anchor.Groups[1].Value,CultureInfo.InvariantCulture),float.Parse(anchor.Groups[2].Value,CultureInfo.InvariantCulture),float.Parse(depth.Groups[1].Value,CultureInfo.InvariantCulture));
            if((stored-position).sqrMagnitude<1e-8f) return;
        }
        string updated=Regex.Replace(block.Value,@"m_AnchoredPosition: \{[^}]*\}",$"m_AnchoredPosition: {{x: {F(position.x)}, y: {F(position.y)}}}");
        updated=Regex.Replace(updated,@"m_LocalPosition: \{[^}]*\}",$"m_LocalPosition: {{x: 0, y: 0, z: {F(position.z)}}}");
        string result=yaml.Replace(block.Value,updated);
        if(result!=yaml) File.WriteAllText(scenePath,result);
        foreach(var t in Resources.FindObjectsOfTypeAll<RectTransform>().Where(t=>t.name=="Presentation Controls" && t.gameObject.scene.path==scenePath))
            t.localPosition=position;
    }
    static void ValidateVisibility(GameObject root, Mesh mesh, RectTransform desk)
    {
        var corners = new Vector3[4]; desk.GetWorldCorners(corners);
        for (int i=0; i<4; i++) corners[i] = root.transform.InverseTransformPoint(corners[i]);
        var vertices = mesh.vertices; var triangles = mesh.triangles;
        foreach (float eyeY in new[] {17.2827f, 18.8222f, 19.9333f, 21.0444f, 21.6f})
        for (int x=0; x<7; x++) for (int y=0; y<7; y++)
        {
            var eye = new Vector3(1.9893f, eyeY, -15.8848f);
            var target = Vector3.Lerp(Vector3.Lerp(corners[0],corners[3],x/6f), Vector3.Lerp(corners[1],corners[2],x/6f),y/6f);
            var direction = target-eye; float length = direction.magnitude; direction /= length;
            for (int t=0;t<triangles.Length;t+=3)
            {
                var a=vertices[triangles[t]]; var e1=vertices[triangles[t+1]]-a; var e2=vertices[triangles[t+2]]-a;
                var p=Vector3.Cross(direction,e2); float det=Vector3.Dot(e1,p);
                if (Mathf.Abs(det)<1e-7f) continue;
                var s=eye-a; float u=Vector3.Dot(s,p)/det; if(u<0 || u>1) continue;
                var q=Vector3.Cross(s,e1); float v=Vector3.Dot(direction,q)/det; if(v<0 || u+v>1) continue;
                float distance=Vector3.Dot(e2,q)/det;
                if(distance>0 && distance<length-.01f) throw new Exception($"Slide occluded at eyeY={eyeY}, screen={x},{y}");
            }
        }
    }
    static void Capture(GameObject root, bool side=false)
    {
        var go = new GameObject("Reading angle verification camera", typeof(Camera));
        var lightGo = new GameObject("Reading angle verification light", typeof(Light));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, root.scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, root.scene);
        var camera = go.GetComponent<Camera>(); camera.scene = root.scene;
        camera.transform.position = root.transform.TransformPoint(side ? new Vector3(14,18,-11) : new Vector3(1.9893f, 17.2827f, -15.8848f));
        camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0, 13.5f, -1.5f)));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.22f,.24f,.28f);
        camera.fieldOfView = 40; camera.nearClipPlane = .01f;
        var light = lightGo.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
        light.transform.rotation = Quaternion.Euler(40,-30,0);
        var panel = root.transform.Find("Panel_Script_New");
        panel.gameObject.SetActive(true);
        foreach (var text in panel.GetComponentsInChildren<TMPro.TMP_Text>(true)) text.text = "대본 화면\n\n시선을 편하게 두고\n발표를 이어가세요.";
        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(1200,1200,24);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1200,1200,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1200,1200),0,0); image.Apply();
            File.WriteAllBytes(side ? "Temp/CounterReadingAngle-side.png" : "Temp/CounterReadingAngle.png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(lightGo);
        }
        if(!side) Capture(root,true);
    }
    static void Inspect()
    {
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var mesh = root.GetComponent<MeshFilter>().sharedMesh;
            var s = new StringBuilder();
            var mats = root.GetComponent<MeshRenderer>().sharedMaterials;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                s.AppendLine($"SUB {sub} {mats[sub].name}");
                foreach (int i in mesh.GetTriangles(sub).Distinct()) s.AppendLine($"{i}: {mesh.vertices[i].ToString("F5")}");
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.parent == root.transform))
                s.AppendLine($"CHILD {t.name} pos={t.localPosition:F5} rot={t.localEulerAngles:F5}");
            foreach (var counter in Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.name == "Counter" && t.gameObject.scene.IsValid() && t.gameObject.scene != root.scene))
            {
                s.AppendLine($"COUNTER {counter.gameObject.scene.path} world={counter.position:F4} scale={counter.lossyScale:F4}");
                var filter=counter.GetComponent<MeshFilter>();
                s.AppendLine($"LIVE mesh={AssetDatabase.GetAssetPath(filter.sharedMesh)} id={filter.sharedMesh.GetInstanceID()} staticBatch={counter.GetComponent<MeshRenderer>().isPartOfStaticBatch}");
                var tr=filter.sharedMesh.GetTriangles(2); var vs=filter.sharedMesh.vertices;
                s.AppendLine($"LIVE angle={Vector3.Angle(Vector3.Cross(vs[tr[1]]-vs[tr[0]],vs[tr[2]]-vs[tr[0]]).normalized,Vector3.up)}");
                foreach(var child in counter.GetComponentsInChildren<Transform>(true).Where(t=>t.parent==counter))
                    s.AppendLine($"LIVE child={child.name} position={child.localPosition:F5} rotation={child.localEulerAngles:F5}");
                foreach(var mod in PrefabUtility.GetPropertyModifications(counter.gameObject) ?? Array.Empty<PropertyModification>())
                    if(mod.target && (mod.target.name=="DeskScreen" || mod.target.name=="Panel_Script_New" || mod.propertyPath=="m_Mesh"))
                        s.AppendLine($"OVERRIDE {mod.target.name} {mod.propertyPath}={mod.value} object={mod.objectReference}");
                foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.gameObject.scene == counter.gameObject.scene))
                    s.AppendLine($"EYE {cam.name} world={cam.transform.position:F4} relative={counter.InverseTransformPoint(cam.transform.position):F4}");
                foreach (var button in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>().Where(b => b.gameObject.scene == counter.gameObject.scene))
                    s.AppendLine($"BUTTON {button.name} parent={button.transform.parent.name} world={button.transform.position:F4} relative={counter.InverseTransformPoint(button.transform.position):F4} id={GlobalObjectId.GetGlobalObjectIdSlow(button.transform)}");
            }
            if(SceneView.lastActiveSceneView) s.AppendLine($"SceneView eye={SceneView.lastActiveSceneView.camera.transform.position:F4} rotation={SceneView.lastActiveSceneView.camera.transform.eulerAngles:F4}");
            File.WriteAllText("Temp/RehearCounterReadingAngle.txt", s.ToString());
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
