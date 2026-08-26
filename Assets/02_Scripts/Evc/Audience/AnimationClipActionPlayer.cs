using System.Collections;
using Rehear.Evc.Contracts;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Rehear.Evc.Audience
{
    public sealed class AnimationClipActionPlayer : MonoBehaviour, IActionPlayer
    {
        [SerializeField] private string layer = "Body";
        [SerializeField] private Animator animator;

        private PlayableGraph graph;
        private Coroutine restoreCoroutine;
        private AnimationClip baselineClip;

        public string Layer => layer;

        public bool CanPlay(AudienceActionDefinition action, string blendMode)
        {
            return animator != null && action != null && action.layer == layer &&
                   action.animationClip != null && blendMode == "override";
        }

        public void Play(AudienceActionDefinition action, UnityCommandDto command)
        {
            if (!CanPlay(action, command.blend_mode))
                return;

            StopGraph();
            baselineClip = action.baselineClip;
            graph = PlayableGraph.Create("EVC " + layer + " " + command.action_id);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var playable = AnimationClipPlayable.Create(graph, action.animationClip);
            playable.SetSpeed(1d);
            var output = AnimationPlayableOutput.Create(graph, "EVC " + layer, animator);
            output.SetSourcePlayable(playable);
            output.SetWeight(Mathf.Clamp01(command.intensity));
            graph.Play();
            restoreCoroutine = StartCoroutine(RestoreAfter(Mathf.Clamp(command.duration, 0.01f, 60f)));
        }

        public void StopAndRestoreBaseline()
        {
            if (restoreCoroutine != null)
            {
                StopCoroutine(restoreCoroutine);
                restoreCoroutine = null;
            }
            RestoreBaseline();
        }

        private IEnumerator RestoreAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            restoreCoroutine = null;
            RestoreBaseline();
        }

        private void RestoreBaseline()
        {
            StopGraph();
            if (baselineClip == null || animator == null)
                return;

            graph = PlayableGraph.Create("EVC " + layer + " baseline");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var playable = AnimationClipPlayable.Create(graph, baselineClip);
            var output = AnimationPlayableOutput.Create(graph, "EVC " + layer + " baseline", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
        }

        private void StopGraph()
        {
            if (graph.IsValid())
                graph.Destroy();
        }

        private void OnDestroy()
        {
            StopGraph();
        }
    }
}
