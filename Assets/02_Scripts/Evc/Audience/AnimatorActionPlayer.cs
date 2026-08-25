using System.Collections;
using Rehear.Evc.Contracts;
using UnityEngine;

namespace Rehear.Evc.Audience
{
    public sealed class AnimatorActionPlayer : MonoBehaviour, IActionPlayer
    {
        [SerializeField] private string layer = "Body";
        [SerializeField] private Animator animator;
        [SerializeField, Min(0)] private int animatorLayerIndex;
        [SerializeField, Min(0f)] private float crossFadeSeconds = 0.1f;
        [SerializeField] private bool supportsAdditive;

        private Coroutine restoreCoroutine;
        private string activeBaseline;

        public string Layer => layer;

        public bool CanPlay(AudienceActionDefinition action, string blendMode)
        {
            return animator != null &&
                   action != null &&
                   action.layer == layer &&
                   !string.IsNullOrWhiteSpace(action.animatorStateName) &&
                   (blendMode != "additive" || supportsAdditive && action.supportsAdditive);
        }

        public void Play(AudienceActionDefinition action, UnityCommandDto command)
        {
            if (!CanPlay(action, command.blend_mode))
                return;

            if (restoreCoroutine != null)
                StopCoroutine(restoreCoroutine);

            activeBaseline = action.baselineStateName;
            animator.SetLayerWeight(animatorLayerIndex, Mathf.Clamp01(command.intensity));
            animator.CrossFadeInFixedTime(
                action.animatorStateName,
                Mathf.Max(0f, crossFadeSeconds),
                animatorLayerIndex);
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
            if (animator == null)
                return;

            if (!string.IsNullOrWhiteSpace(activeBaseline))
            {
                animator.CrossFadeInFixedTime(
                    activeBaseline,
                    Mathf.Max(0f, crossFadeSeconds),
                    animatorLayerIndex);
            }
            else if (animatorLayerIndex > 0)
            {
                animator.SetLayerWeight(animatorLayerIndex, 0f);
            }
        }
    }
}
