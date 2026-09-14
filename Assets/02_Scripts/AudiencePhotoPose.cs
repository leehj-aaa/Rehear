using System.Linq;
using UnityEngine;

// Keep the authored grip while aiming the rear camera at the presentation screen.
// Generic rigs need a two-segment arm solve rather than Humanoid Animator IK.
internal sealed class AudiencePhotoPose
{
    private sealed class Arm { public Transform upper,lower,hand,index; public Vector3 position; public Quaternion rotation; }
    private readonly Arm[] arms;
    private readonly Transform actor;
    private readonly Transform spine;
    private readonly Transform[] bones;
    private readonly Transform rightThigh;
    public AudiencePhotoPose(Transform root)
    {
        actor=root;
        bones=root.GetComponentsInChildren<Transform>();
        Transform Bone(string name)=>bones.FirstOrDefault(b=>b.name==name);
        spine=Bone("spine_01");
        rightThigh=Bone("thigh_r");
        arms=new[]{"l","r"}.Select(s=>new Arm{upper=Bone("upperarm_"+s),lower=Bone("lowerarm_"+s),hand=Bone("hand_"+s),index=Bone("index_02_"+s)}).ToArray();
    }
    public void Apply(Transform phone,Transform screen,float weight)
    {
        if(arms.Any(a=>!a.upper||!a.lower||!a.hand))return;
        var grip=phone.GetComponent<AudiencePhotoGrip>();
        var center=phone.position;
        var direction=screen.position-center;
        if(direction.sqrMagnitude<.001f)return;
        float raised=Vector3.Dot(center-(arms[0].upper.position+arms[1].upper.position)*.5f,actor.up);
        float aimWeight=Mathf.Clamp01(weight)*Mathf.InverseLerp(-.50f,-.25f,raised);
        // Turn the chest and both shoulders together before adjusting the wrists.
        // Holding the torso fixed makes an otherwise reachable arm solve cross the chest.
        if(spine){
            var cameraForward=Vector3.ProjectOnPlane(-phone.forward,actor.up);
            var screenForward=Vector3.ProjectOnPlane(direction,actor.up);
            float yaw=Vector3.SignedAngle(cameraForward,screenForward,actor.up);
            spine.rotation=Quaternion.AngleAxis(Mathf.Clamp(yaw,-60f,60f)*aimWeight,actor.up)*spine.rotation;
            center=phone.position;direction=screen.position-center;
        }
        // The supplied phone model's rear lens faces local -Z; its top is -Y.
        var desired=Quaternion.LookRotation(-direction.normalized,-Vector3.up);
        var turn=Quaternion.Slerp(Quaternion.identity,desired*Quaternion.Inverse(phone.rotation),aimWeight);
        arms[0].position=center+turn*(arms[0].hand.position-center);
        arms[0].rotation=turn*arms[0].hand.rotation;
        var rightPosition=center+turn*(arms[1].hand.position-center);
        var rightRotation=turn*arms[1].hand.rotation;
        Solve(arms[0]);
        if(grip && rightThigh){
            // Preserve the authored taps while low. Only lower the free hand
            // when lifting the phone to take the photo.
            float restWeight=Mathf.Clamp01(weight)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.20f,-.04f,raised));
            foreach(var pose in grip.restPose){
                var bone=System.Array.Find(bones,b=>b.name==pose.bone);
                if(bone)bone.localRotation=Quaternion.Slerp(bone.localRotation,pose.rotation,restWeight);
            }
            arms[1].position=Vector3.Lerp(rightPosition,rightThigh.TransformPoint(grip.wristInThigh),restWeight);
            arms[1].rotation=Quaternion.Slerp(rightRotation,rightThigh.rotation*grip.wristRotationInThigh,restWeight);
            Solve(arms[1]);
        }
    }
    private void Solve(Arm arm)
    {
        var shoulder=arm.upper.position;
        float a=Vector3.Distance(shoulder,arm.lower.position),b=Vector3.Distance(arm.lower.position,arm.hand.position);
        var delta=arm.position-shoulder;
        if(a<.001f||b<.001f||delta.sqrMagnitude<.000001f)return;
        var direction=delta.normalized;
        float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,(a+b)*.999f);
        var bend=Vector3.ProjectOnPlane(arm.lower.position-shoulder,direction);
        if(bend.sqrMagnitude<.000001f)bend=Vector3.ProjectOnPlane(-actor.up,direction);
        float along=(a*a-b*b+distance*distance)/(2*distance);
        var elbow=shoulder+direction*along+bend.normalized*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        arm.upper.rotation=Quaternion.FromToRotation(arm.lower.position-shoulder,elbow-shoulder)*arm.upper.rotation;
        arm.lower.rotation=Quaternion.FromToRotation(arm.hand.position-arm.lower.position,shoulder+direction*distance-arm.lower.position)*arm.lower.rotation;
        arm.hand.rotation=arm.rotation;
    }
}
