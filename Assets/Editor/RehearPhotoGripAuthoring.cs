using System.Linq;
using UnityEngine;

internal static class RehearPhotoGripAuthoring
{
    public static AudiencePhotoGrip.FingerPose[] Fit(Transform phone,Transform[] bones)
    {
        var scale=phone.lossyScale;
        var bounds=phone.GetComponent<MeshFilter>().sharedMesh.bounds;
        float edge=bounds.max.x*scale.x;
        float back=bounds.min.z*scale.z;
        float front=bounds.max.z*scale.z;
        Vector3 Local(Vector3 world)=>Vector3.Scale(phone.InverseTransformPoint(world),scale);
        Vector3 World(Vector3 metric)=>phone.TransformPoint(new Vector3(metric.x/scale.x,metric.y/scale.y,metric.z/scale.z));
        foreach(var finger in new[]{"index","middle","ring","pinky"}){
            var a=bones.Single(t=>t&&t.name==finger+"_01_l");
            var b=bones.Single(t=>t&&t.name==finger+"_02_l");
            var c=bones.Single(t=>t&&t.name==finger+"_03_l");
            // Keep the proximal joint outside the side wall, then curl around
            // the corner onto the back. Moving the phone alone cannot form a grip.
            var p=Local(b.position);var q=Local(c.position);
            var directionInDistal=Quaternion.Inverse(c.rotation)*(c.position-b.position).normalized;
            var outside=World(new Vector3(edge+.009f,p.y,front+.012f));
            a.rotation=Quaternion.FromToRotation(b.position-a.position,outside-a.position)*a.rotation;
            var corner=World(new Vector3(edge+.007f,q.y,back-.009f));
            b.rotation=Quaternion.FromToRotation(c.position-b.position,corner-b.position)*b.rotation;
            var pad=World(new Vector3(edge-.014f,Local(c.position).y,back-.011f));
            c.rotation=Quaternion.FromToRotation(c.rotation*directionInDistal,pad-c.position)*c.rotation;
        }
        return bones.Where(t=>t&&t.name.EndsWith("_l")&&new[]{"index_","middle_","ring_","pinky_"}.Any(p=>t.name.StartsWith(p)))
            .Select(t=>new AudiencePhotoGrip.FingerPose{bone=t.name,rotation=t.localRotation}).ToArray();
    }
}
