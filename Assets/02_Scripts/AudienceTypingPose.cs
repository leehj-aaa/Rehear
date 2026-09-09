using System.Linq;
using UnityEngine;

// Generic FBX rigs cannot use Animator's Humanoid hand IK. Solve the two
// articulated arm segments after the clip, retaining its finger animation.
internal sealed class AudienceTypingPose
{
    private sealed class Arm
    {
        public Transform upper, lower, hand, index, pinky, middle, tip;
        public float side;
    }
    private readonly Arm[] arms;
    private readonly Transform spine;
    private readonly Transform actor;

    public AudienceTypingPose(Transform root)
    {
        actor=root;
        var bones=root.GetComponentsInChildren<Transform>();
        Transform Bone(string name) => bones.FirstOrDefault(b=>b.name==name);
        spine=Bone("spine_01");
        arms=new[]{"l","r"}.Select(s=>new Arm {
            upper=Bone("upperarm_"+s),lower=Bone("lowerarm_"+s),hand=Bone("hand_"+s),
            index=Bone("index_01_"+s),pinky=Bone("pinky_01_"+s),
            middle=Bone("middle_01_"+s),tip=Bone("middle_03_"+s),side=s=="l"?-1:1
        }).ToArray();
    }

    public void Apply(Transform keyboard, float weight)
    {
        weight=Mathf.Clamp01(weight);
        // Lean from the waist while keeping the seated hip and legs in place.
        var assignment=actor.GetComponent<AudienceSeatAssignment>();
        var seatPosition=assignment && assignment.Seat ? assignment.Seat.transform.position : actor.position;
        var distanceToKeyboard=Vector3.ProjectOnPlane(keyboard.position-seatPosition,keyboard.up).magnitude;
        float lean=Mathf.Lerp(6f,27f,Mathf.InverseLerp(.50f,.71f,distanceToKeyboard));
        if(spine) spine.rotation=Quaternion.AngleAxis(lean*weight,keyboard.right)*spine.rotation;
        foreach(var arm in arms)
        {
            if(!arm.upper || !arm.lower || !arm.hand || !arm.index || !arm.pinky || !arm.middle || !arm.tip) continue;
            var handRotation=arm.hand.rotation;
            var normal=Vector3.Cross(arm.index.position-arm.hand.position,arm.pinky.position-arm.hand.position).normalized;
            if(Vector3.Dot(normal,keyboard.up)<0)normal=-normal;
            var handFrame=Quaternion.LookRotation(arm.middle.position-arm.hand.position,normal);
            var desiredRotation=Quaternion.LookRotation(keyboard.forward,keyboard.up)*Quaternion.Inverse(handFrame)*handRotation;
            var correction=desiredRotation*Quaternion.Inverse(handRotation);
            // Both supported laptop models have a 9–11 mm high keyboard deck.
            var fingertip=keyboard.TransformPoint(new Vector3(arm.side*.065f,.020f,-.015f));
            var wrist=fingertip-correction*(arm.tip.position-arm.hand.position);
            var target=Vector3.Lerp(arm.hand.position,wrist,weight);
            var shoulder=arm.upper.position;
            float upperLength=Vector3.Distance(shoulder,arm.lower.position);
            float lowerLength=Vector3.Distance(arm.lower.position,arm.hand.position);
            var delta=target-shoulder;
            if(delta.sqrMagnitude<.000001f || upperLength<.001f || lowerLength<.001f)continue;
            var direction=delta.normalized;
            float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(upperLength-lowerLength)+.001f,(upperLength+lowerLength)*.995f);
            var bend=Vector3.ProjectOnPlane(arm.lower.position-shoulder,direction);
            if(bend.sqrMagnitude<.000001f)bend=Vector3.ProjectOnPlane(-keyboard.up+keyboard.right*arm.side,direction);
            float along=(upperLength*upperLength-lowerLength*lowerLength+distance*distance)/(2*distance);
            float away=Mathf.Sqrt(Mathf.Max(0,upperLength*upperLength-along*along));
            var elbow=shoulder+direction*along+bend.normalized*away;
            arm.upper.rotation=Quaternion.FromToRotation(arm.lower.position-shoulder,elbow-shoulder)*arm.upper.rotation;
            var reachableTarget=shoulder+direction*distance;
            arm.lower.rotation=Quaternion.FromToRotation(arm.hand.position-arm.lower.position,reachableTarget-arm.lower.position)*arm.lower.rotation;
            arm.hand.rotation=Quaternion.Slerp(handRotation,desiredRotation,weight);
        }
    }
}
