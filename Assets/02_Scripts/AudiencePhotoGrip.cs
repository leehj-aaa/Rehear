using System;
using UnityEngine;

// The free arm rests on the thigh while the left hand takes the photo.
public sealed class AudiencePhotoGrip : MonoBehaviour
{
    [Serializable] public struct FingerPose { public string bone; public Quaternion rotation; }
    public FingerPose[] restPose=Array.Empty<FingerPose>();
    public FingerPose[] leftPose=Array.Empty<FingerPose>();
    public Vector3 wristInThigh;
    public Quaternion wristRotationInThigh;
}
