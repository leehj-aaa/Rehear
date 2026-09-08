using System;
using System.Collections.Generic;
using Rehear.Evc.Contracts;
using UnityEngine;

namespace Rehear.Evc.Audience
{
    public interface IActionPlayer
    {
        string Layer { get; }
        bool CanPlay(AudienceActionDefinition action, string blendMode);
        void Play(AudienceActionDefinition action, UnityCommandDto command);
        void StopAndRestoreBaseline();
    }

    public interface IRawCommandPlayer
    {
        string Layer { get; }

        bool TryPlayCommand(
            UnityCommandDto command,
            out string reason
        );

        void StopCommand();
    }

    public interface IAudienceStateReceiver
    {
        void ApplyAudienceState(float engagement, float clarity);
    }

    public sealed class AudienceAgent : MonoBehaviour
    {
        [SerializeField] private string agentId;
        [SerializeField] private AudienceActionRegistry actionRegistry;
        [SerializeField] private MonoBehaviour[] actionPlayerBehaviours = Array.Empty<MonoBehaviour>();


        private readonly List<IRawCommandPlayer>
            rawCommandPlayers =
                new List<IRawCommandPlayer>();
        
        private readonly Dictionary<string, List<IActionPlayer>> players =
            new Dictionary<string, List<IActionPlayer>>(StringComparer.Ordinal);

        public string AgentId => agentId;
        public AudienceActionRegistry ActionRegistry => actionRegistry;

        private void Awake()
        {
            RebuildPlayers();
        }

        public void Configure(string id, AudienceActionRegistry registry)
        {
            agentId = id;
            actionRegistry = registry;
            RebuildPlayers();
        }

        public bool TryExecute(UnityCommandDto command, out string reason)
        {
            reason = string.Empty;
            if (command == null || command.agent_id != agentId)
            {
                reason = "agent_mismatch";
                return false;
            }

            if (!EvcContractRules.IsLayer(command.layer))
            {
                reason = "unknown_layer";
                return false;
            }

            var seating = GetComponent<AudienceSeatAssignment>();
            if (seating && (!seating.Allows(command.action_id) || !seating.Allows(command.selected_variation_id)))
            {
                reason = "seat_action_not_available";
                return false;
            }

            if (!EvcContractRules.IsBlendMode(command.blend_mode))
            {
                reason = "unknown_blend_mode";
                return false;
            }

            for (int index = 0;
                index < rawCommandPlayers.Count;
                index++)
            {
                IRawCommandPlayer rawPlayer =
                    rawCommandPlayers[index];

                if (rawPlayer == null ||
                    rawPlayer.Layer != command.layer)
                {
                    continue;
                }

                if (rawPlayer.TryPlayCommand(
                        command,
                        out reason))
                {
                    return true;
                }
            }

            if (actionRegistry == null || !actionRegistry.TryGet(command.action_id, out var action))
            {
                reason = "unknown_action";
                return false;
            }

            if (action.layer != command.layer)
            {
                reason = "action_layer_mismatch";
                return false;
            }

            if (!players.TryGetValue(command.layer, out var layerPlayers))
            {
                reason = command.blend_mode == "additive" ? "additive_not_supported" : "player_unavailable";
                return false;
            }

            for (var index = 0; index < layerPlayers.Count; index++)
            {
                if (!layerPlayers[index].CanPlay(action, command.blend_mode))
                    continue;
                layerPlayers[index].Play(action, command);
                return true;
            }

            reason = command.blend_mode == "additive" ? "additive_not_supported" : "player_unavailable";
            return false;
        }

        public void StopAll()
        {
            foreach (var layerPlayers in players.Values)
            {
                for (var index = 0; index < layerPlayers.Count; index++)
                    layerPlayers[index].StopAndRestoreBaseline();
            }

            for (int index = 0;
                index < rawCommandPlayers.Count;
                index++)
            {
                rawCommandPlayers[index]
                    ?.StopCommand();
            }
        }

        public void SetServerMode(bool enabled)
        {
            var behaviours = GetComponents<MonoBehaviour>();
            for (var index = 0; index < behaviours.Length; index++)
            {
                var behaviour = behaviours[index];
                if (behaviour != null && behaviour.GetType().Name == "RandomAudienceAnimator")
                    behaviour.enabled = !enabled;
            }
        }

        public void ApplyAudienceState(float engagement, float clarity)
        {
            var behaviours = GetComponents<MonoBehaviour>();
            for (var index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IAudienceStateReceiver receiver)
                    receiver.ApplyAudienceState(engagement, clarity);
            }
        }

        private void RebuildPlayers()
        {
            players.Clear();
            rawCommandPlayers.Clear();

            if (actionPlayerBehaviours == null || actionPlayerBehaviours.Length == 0)
                actionPlayerBehaviours = GetComponents<MonoBehaviour>();

            for (var index = 0; index < actionPlayerBehaviours.Length; index++)
            {   if (actionPlayerBehaviours[index]
                    is IRawCommandPlayer rawPlayer)
                {
                    rawCommandPlayers.Add(rawPlayer);
                }

                if (!(actionPlayerBehaviours[index] is IActionPlayer player) || string.IsNullOrWhiteSpace(player.Layer))
                    continue;
                if (!players.TryGetValue(player.Layer, out var layerPlayers))
                {
                    layerPlayers = new List<IActionPlayer>();
                    players.Add(player.Layer, layerPlayers);
                }
                layerPlayers.Add(player);
            }
        }
    }
}
