using System;
using System.Collections;
using System.Collections.Generic;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;
using Rehear.Evc.Transport;
using Rehear.Evc.Update;
using UnityEngine;

namespace Rehear.Evc.Audience
{
    public sealed class AudienceReactionCoordinator : MonoBehaviour, IAudienceCommandSink
    {
        [SerializeField] private AudienceAgent[] agents = Array.Empty<AudienceAgent>();
        [SerializeField, Min(1f)] private float dedupeTtlSeconds = 120f;
        [SerializeField, Min(16)] private int maxDedupeEntries = 512;
        [SerializeField, Min(0f)] private float lateCommandExpirySeconds = 2f;

        private readonly Dictionary<string, AudienceAgent> agentLookup =
            new Dictionary<string, AudienceAgent>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> recentlyExecuted =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, LayerReservation> activeLayers =
            new Dictionary<string, LayerReservation>(StringComparer.Ordinal);
        private IPresentationClock clock;
        private bool serverMode;

        private void Awake()
        {
            RebuildAgentLookup();
        }

        private void OnDisable()
        {
            CancelAll();
        }

        public void SetClock(IPresentationClock presentationClock)
        {
            clock = presentationClock;
        }

        public void SetServerMode(bool enabled)
        {
            serverMode = enabled;
            for (var index = 0; index < agents.Length; index++)
                agents[index]?.SetServerMode(enabled);
        }

        public void HandleCommands(string requestId, IReadOnlyList<UnityCommandDto> commands)
        {
            if (!serverMode || commands == null || commands.Count == 0)
                return;
            if (clock == null || !clock.IsRunning)
            {
                EvcSafeDiagnostics.CommandDropped(requestId, null, null, "clock_not_running");
                return;
            }

            PurgeDedupeCache();
            var groups = new Dictionary<string, List<UnityCommandDto>>(StringComparer.Ordinal);
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                if (!TryValidate(command, out var reason))
                {
                    EvcSafeDiagnostics.CommandDropped(requestId, command?.agent_id, command?.layer, reason);
                    continue;
                }

                var sync = string.IsNullOrWhiteSpace(command.sync_group)
                    ? command.action_id + "@" + command.start_time.ToString("R")
                    : command.sync_group;
                var key = (requestId ?? string.Empty) + "|" + command.agent_id + "|" + sync;
                if (recentlyExecuted.ContainsKey(key))
                    continue;

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<UnityCommandDto>();
                    groups.Add(key, group);
                }
                group.Add(command);
            }

            foreach (var pair in groups)
            {
                recentlyExecuted[pair.Key] = Time.realtimeSinceStartup;
                StartCoroutine(ExecuteGroupAtAbsoluteTime(requestId, pair.Value));
            }
        }

        public void CancelAll()
        {
            StopAllCoroutines();
            activeLayers.Clear();
            for (var index = 0; index < agents.Length; index++)
                agents[index]?.StopAll();
        }

        public IReadOnlyList<string> ValidateSceneAgents()
        {
            RebuildAgentLookup();
            var errors = new List<string>();
            for (var index = 0; index < EvcContractRules.RequiredAudienceIds.Length; index++)
            {
                var id = EvcContractRules.RequiredAudienceIds[index];
                if (!agentLookup.ContainsKey(id))
                    errors.Add("Missing audience agent: " + id);
            }
            if (agentLookup.Count != EvcContractRules.AudienceCount)
                errors.Add("The scene must contain exactly six unique audience agents.");
            return errors;
        }

        private IEnumerator ExecuteGroupAtAbsoluteTime(string requestId, List<UnityCommandDto> commands)
        {
            var earliestStart = float.MaxValue;
            for (var index = 0; index < commands.Count; index++)
                earliestStart = Mathf.Min(earliestStart, commands[index].start_time);

            var lateness = clock.ElapsedSeconds - earliestStart;
            if (lateness > lateCommandExpirySeconds)
                yield break;

            // Wait against presentation time so an app/presentation pause also pauses
            // scheduled audience reactions. Realtime waits would execute while the clock
            // is intentionally frozen.
            while (clock != null &&
                   clock.IsRunning &&
                   (clock.IsPaused || clock.ElapsedSeconds < earliestStart))
                yield return null;
            if (clock == null || !clock.IsRunning)
                yield break;

            // All commands in this sync group execute in this same frame.
            commands.Sort((left, right) => right.priority.CompareTo(left.priority));
            var touchedLayers = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                var layerKey = command.agent_id + "|" + command.layer;
                if (!touchedLayers.Add(layerKey))
                {
                    EvcSafeDiagnostics.CommandDropped(requestId, command.agent_id, command.layer, "lower_priority_collision");
                    continue;
                }

                var now = clock.ElapsedSeconds;
                if (activeLayers.TryGetValue(layerKey, out var reservation) &&
                    reservation.EndTime > now &&
                    reservation.Priority > command.priority)
                {
                    EvcSafeDiagnostics.CommandDropped(requestId, command.agent_id, command.layer, "active_higher_priority");
                    continue;
                }

                if (!agentLookup[command.agent_id].TryExecute(command, out var reason))
                {
                    EvcSafeDiagnostics.CommandDropped(requestId, command.agent_id, command.layer, reason);
                    continue;
                }

                activeLayers[layerKey] = new LayerReservation
                {
                    Priority = command.priority,
                    EndTime = now + Math.Max(0.01f, command.duration)
                };
            }
        }

        private bool TryValidate(UnityCommandDto command, out string reason)
        {
            reason = string.Empty;
            if (command == null || string.IsNullOrWhiteSpace(command.agent_id) ||
                !agentLookup.ContainsKey(command.agent_id))
            {
                reason = "unknown_agent";
                return false;
            }
            if (!EvcContractRules.IsLayer(command.layer))
            {
                reason = "unknown_layer";
                return false;
            }
            if (!EvcContractRules.IsBlendMode(command.blend_mode))
            {
                reason = "unknown_blend_mode";
                return false;
            }
            if (string.IsNullOrWhiteSpace(command.action_id))
            {
                reason = "missing_action";
                return false;
            }
            if (command.duration <= 0f || command.duration > 60f || command.intensity < 0f || command.intensity > 1f)
            {
                reason = "invalid_timing_or_intensity";
                return false;
            }
            return true;
        }

        private void RebuildAgentLookup()
        {
            agentLookup.Clear();
            if (agents == null || agents.Length == 0)
                agents = FindObjectsByType<AudienceAgent>(FindObjectsSortMode.None);

            for (var index = 0; index < agents.Length; index++)
            {
                var agent = agents[index];
                if (agent == null || string.IsNullOrWhiteSpace(agent.AgentId) || agentLookup.ContainsKey(agent.AgentId))
                    continue;
                agentLookup.Add(agent.AgentId, agent);
            }
        }

        private void PurgeDedupeCache()
        {
            var cutoff = Time.realtimeSinceStartup - Mathf.Max(1f, dedupeTtlSeconds);
            var expired = new List<string>();
            foreach (var pair in recentlyExecuted)
            {
                if (pair.Value < cutoff)
                    expired.Add(pair.Key);
            }
            for (var index = 0; index < expired.Count; index++)
                recentlyExecuted.Remove(expired[index]);

            if (recentlyExecuted.Count <= maxDedupeEntries)
                return;

            recentlyExecuted.Clear();
        }

        private struct LayerReservation
        {
            public int Priority;
            public double EndTime;
        }
    }
}
