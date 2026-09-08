using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class RehearGeneratedMeshLighting
{
    // These meshes receive small atlas allocations in a mobile bake. Default
    // 4/1024 chart padding becomes sub-texel after allocation; reserve 4% instead.
    internal static void Unwrap(Mesh mesh)
    {
        UnwrapParam.SetDefaults(out var parameters);
        parameters.packMargin = .04f;
        parameters.hardAngle = 60;
        if (!Unwrapping.GenerateSecondaryUVSet(mesh, parameters)) throw new Exception("UV2 unwrap failed: " + mesh.name);
        if (mesh.uv2.Length != mesh.vertexCount || mesh.uv2.Any(v => !float.IsFinite(v.x) || !float.IsFinite(v.y)))
            throw new Exception("Invalid lightmap UVs: " + mesh.name);
        int overlaps = Overlaps(mesh);
        if (overlaps != 0) throw new Exception($"Overlapping UV triangles: {mesh.name}: {overlaps}");
        mesh.UploadMeshData(false);
    }
    internal static void Configure(MeshRenderer renderer)
    {
        GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
            GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) | StaticEditorFlags.ContributeGI);
        renderer.receiveGI = ReceiveGI.Lightmaps;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.scaleInLightmap = 2;
        renderer.stitchLightmapSeams = true;
        EditorUtility.SetDirty(renderer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
    }
    internal static int Overlaps(Mesh mesh)
    {
        Vector2[] uv = mesh.uv2; int[] tris = mesh.triangles;
        int overlap = 0;
        for (int a = 0; a < tris.Length; a += 3)
        {
            var p = new[] { uv[tris[a]], uv[tris[a+1]], uv[tris[a+2]] };
            if (Mathf.Abs(Cross(p[1]-p[0], p[2]-p[0])) < 1e-9f) continue;
            for (int b = a + 3; b < tris.Length; b += 3)
            {
                var q = new[] { uv[tris[b]], uv[tris[b+1]], uv[tris[b+2]] };
                if (p.Max(v=>v.x) <= q.Min(v=>v.x) || q.Max(v=>v.x) <= p.Min(v=>v.x) ||
                    p.Max(v=>v.y) <= q.Min(v=>v.y) || q.Max(v=>v.y) <= p.Min(v=>v.y)) continue;
                float direction = Mathf.Sign(Cross(q[1]-q[0], q[2]-q[0]));
                var polygon = p.ToList();
                for (int edge = 0; edge < 3 && polygon.Count > 0; edge++)
                {
                    var input = polygon; polygon = new List<Vector2>();
                    Vector2 start = q[edge], delta = q[(edge+1)%3]-start;
                    for (int i=0; i<input.Count; i++)
                    {
                        Vector2 u=input[i], v=input[(i+1)%input.Count];
                        float du=direction*Cross(delta,u-start), dv=direction*Cross(delta,v-start);
                        if (du >= 0) polygon.Add(u);
                        if ((du>=0)!=(dv>=0)) polygon.Add(Vector2.LerpUnclamped(u,v,du/(du-dv)));
                    }
                }
                float area=0;
                for (int i=0;i<polygon.Count;i++) area+=Cross(polygon[i],polygon[(i+1)%polygon.Count]);
                if (Mathf.Abs(area) > 1e-7f) overlap++;
            }
        }
        return overlap;
    }
    static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;
}
