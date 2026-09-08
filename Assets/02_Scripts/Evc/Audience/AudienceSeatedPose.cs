using UnityEngine;

// Calibration of the neutral sitting clip, kept separate from animated bones.
public sealed class AudienceSeatedPose : MonoBehaviour
{
    public Vector3 localHip;
    public Quaternion facingCorrection = Quaternion.identity;
}
