using System;
using System.Collections.Generic;
using Rehear.Evc.Contracts;
using UnityEngine;

namespace Rehear.Evc.Audience
{
    [Serializable]
    public sealed class AudienceActionDefinition
    {
        public string actionId;
        public string layer;
        public AnimationClip animationClip;
        public string animatorStateName;
        public bool supportsAdditive;
        public string baselineStateName;
        public AnimationClip baselineClip;
    }

    [CreateAssetMenu(fileName = "AudienceActionRegistry", menuName = "Rehear/Audience Action Registry")]
    public sealed class AudienceActionRegistry : ScriptableObject
    {
        [SerializeField] private AudienceActionDefinition[] actions = Array.Empty<AudienceActionDefinition>();
        private Dictionary<string, AudienceActionDefinition> lookup;

        public IReadOnlyList<AudienceActionDefinition> Actions => actions;

        public bool TryGet(string actionId, out AudienceActionDefinition definition)
        {
            EnsureLookup();
            return lookup.TryGetValue(actionId ?? string.Empty, out definition);
        }

        public IReadOnlyList<string> Validate(IEnumerable<string> authoritativeActionIds = null)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                if (action == null || string.IsNullOrWhiteSpace(action.actionId))
                {
                    errors.Add("Registry entry " + index + " has no action id.");
                    continue;
                }

                if (!ids.Add(action.actionId))
                    errors.Add("Duplicate action id: " + action.actionId);
                if (!EvcContractRules.IsLayer(action.layer))
                    errors.Add("Unsupported layer for " + action.actionId + ": " + action.layer);
                if (action.animationClip == null && string.IsNullOrWhiteSpace(action.animatorStateName))
                    errors.Add("No Unity asset/state is mapped for " + action.actionId + ".");
            }

            if (authoritativeActionIds != null)
            {
                foreach (var actionId in authoritativeActionIds)
                {
                    if (!ids.Contains(actionId))
                        errors.Add("Missing authoritative action id: " + actionId);
                }
            }

            return errors;
        }

        private void OnValidate()
        {
            lookup = null;
        }

        private void EnsureLookup()
        {
            if (lookup != null)
                return;

            lookup = new Dictionary<string, AudienceActionDefinition>(StringComparer.Ordinal);
            for (var index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                if (action == null || string.IsNullOrWhiteSpace(action.actionId) || lookup.ContainsKey(action.actionId))
                    continue;
                lookup.Add(action.actionId, action);
            }
        }
    }
}
