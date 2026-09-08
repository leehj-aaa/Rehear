using UnityEngine;

[DefaultExecutionOrder(-150)]
public sealed class PresentationAudienceBindings : MonoBehaviour
{
    public AudienceSeating seating;
    public Transform presenterTarget;
    public Transform slideTarget;
    public Transform[] aroundTargets;
    private void Awake()
    {
        foreach(var agent in seating.members)
        {
            var body=agent.GetComponent<AudienceAnimationPlayer>();
            var random=agent.GetComponent<RandomAudienceAnimator>();
            if(random) random.enabled=false;
            if(body) body.enabled=true;
            var player=agent.GetComponent<LegacyAudienceEvcPlayer>();
            if(!player) player=agent.gameObject.AddComponent<LegacyAudienceEvcPlayer>();
            player.Configure(body);
            agent.Configure(agent.AgentId,agent.ActionRegistry);
            var gaze=agent.GetComponent<AudienceGazeController>();
            if(gaze) {gaze.ConfigureTargets(presenterTarget,slideTarget,aroundTargets); gaze.enabled=true;}
        }
    }
}

