using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class AudienceCommandDispatcher :
    MonoBehaviour
{
    [Serializable]
    public class AudienceBinding
    {
        [Tooltip("audience_01 ~ audience_06")]
        public string agentId;

        [Tooltip(
            "해당 청중의 RandomAudienceAnimator"
        )]
        public RandomAudienceAnimator bodyAnimator;

        [Tooltip(
            "해당 청중의 AudienceGazeController"
        )]
        public AudienceGazeController gazeController;
    }

    [Header("청중 연결")]
    [SerializeField]
    private AudienceBinding[] audienceBindings;

    [Header("디버그")]
    [SerializeField]
    private bool printDispatchLog = true;

    private readonly Dictionary<
        string,
        AudienceBinding
    > bindingMap =
        new Dictionary<string, AudienceBinding>();

    private readonly HashSet<string>
        handledCommandKeys =
            new HashSet<string>();

    private void Awake()
    {
        BuildBindingMap();
    }

    private void BuildBindingMap()
    {
        bindingMap.Clear();

        if (audienceBindings == null)
            return;

        foreach (
            AudienceBinding binding
            in audienceBindings)
        {
            if (binding == null ||
                string.IsNullOrWhiteSpace(
                    binding.agentId))
            {
                continue;
            }

            string normalizedAgentId =
                binding.agentId.Trim();

            if (bindingMap.ContainsKey(
                    normalizedAgentId))
            {
                Debug.LogWarning(
                    "[AI 청중 연결] 중복 Agent ID: " +
                    normalizedAgentId,
                    this
                );

                continue;
            }

            bindingMap.Add(
                normalizedAgentId,
                binding
            );
        }

        Debug.Log(
            "[AI 청중 연결] 등록된 청중 수: " +
            bindingMap.Count
        );
    }

    public void DispatchCommands(
        string requestId,
        UnityAudienceCommand[] commands,
        float currentSessionTime)
    {
        if (commands == null ||
            commands.Length == 0)
        {
            return;
        }

        Dictionary<
            string,
            UnityAudienceCommand
        > highestPriorityCommands =
            SelectHighestPriorityCommands(
                commands
            );

        foreach (
            UnityAudienceCommand command
            in highestPriorityCommands.Values)
        {
            if (command == null)
                continue;

            string commandKey =
                CreateCommandKey(
                    requestId,
                    command
                );

            if (handledCommandKeys.Contains(
                    commandKey))
            {
                if (printDispatchLog)
                {
                    Debug.Log(
                        "[AI 청중 명령] 중복 명령 무시" +
                        "\n" + commandKey
                    );
                }

                continue;
            }

            handledCommandKeys.Add(
                commandKey
            );

            float delay =
                Mathf.Max(
                    0f,
                    command.start_time -
                    currentSessionTime
                );

            StartCoroutine(
                ExecuteCommandAfterDelay(
                    command,
                    delay
                )
            );
        }
    }

    private IEnumerator ExecuteCommandAfterDelay(
        UnityAudienceCommand command,
        float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(
                delay
            );
        }

        ExecuteCommand(command);
    }

    private void ExecuteCommand(
        UnityAudienceCommand command)
    {
        if (!bindingMap.TryGetValue(
                command.agent_id,
                out AudienceBinding binding))
        {
            Debug.LogWarning(
                "[AI 청중 명령] 연결되지 않은 Agent ID" +
                "\nAgent ID: " +
                command.agent_id,
                this
            );

            return;
        }

        string layer =
            command.layer != null
                ? command.layer.Trim()
                : "";

        bool executed = false;

        if (layer.Equals(
                "Body",
                StringComparison.OrdinalIgnoreCase))
        {
            if (binding.bodyAnimator != null)
            {
                executed =
                    binding.bodyAnimator
                        .PlayServerVariation(
                            command
                                .selected_variation_id,
                            command.duration,
                            command.intensity
                        );
            }
            else
            {
                Debug.LogWarning(
                    "[AI Body] Body Animator가 없습니다." +
                    "\nAgent ID: " +
                    command.agent_id
                );
            }
        }
        else if (layer.Equals(
                     "GazeHead",
                     StringComparison.OrdinalIgnoreCase))
        {
            if (binding.gazeController != null)
            {
                binding.gazeController
                    .ApplyServerGaze(
                        command.action_id,
                        command.duration
                    );

                executed = true;
            }
            else
            {
                Debug.LogWarning(
                    "[AI GazeHead] Gaze Controller가 없습니다." +
                    "\nAgent ID: " +
                    command.agent_id
                );
            }
        }
        else if (layer.Equals(
                     "Face",
                     StringComparison.OrdinalIgnoreCase))
        {
            // 현재 프로젝트에는 표정 BlendShape
            // 전용 컨트롤러가 없으므로 제출 버전에서는
            // Face 명령을 기록만 하고 건너뛴다.
            if (printDispatchLog)
            {
                Debug.Log(
                    "[AI Face] 현재 미구현으로 명령을 건너뜁니다." +
                    "\nAgent ID: " +
                    command.agent_id +
                    "\nAction ID: " +
                    command.action_id
                );
            }

            return;
        }
        else
        {
            Debug.LogWarning(
                "[AI 청중 명령] 알 수 없는 Layer" +
                "\nLayer: " + layer
            );

            return;
        }

        if (printDispatchLog)
        {
            Debug.Log(
                "[AI 청중 명령 실행]" +
                "\nAgent ID: " +
                command.agent_id +
                "\nLayer: " +
                command.layer +
                "\nAction ID: " +
                command.action_id +
                "\nVariation ID: " +
                command.selected_variation_id +
                "\n실행 결과: " +
                executed
            );
        }
    }

    private Dictionary<
        string,
        UnityAudienceCommand
    > SelectHighestPriorityCommands(
        UnityAudienceCommand[] commands)
    {
        Dictionary<
            string,
            UnityAudienceCommand
        > selected =
            new Dictionary<
                string,
                UnityAudienceCommand
            >();

        foreach (
            UnityAudienceCommand command
            in commands)
        {
            if (command == null)
                continue;

            string conflictKey =
                command.agent_id +
                "|" +
                command.layer +
                "|" +
                command.start_time.ToString(
                    "F3",
                    CultureInfo.InvariantCulture
                );

            if (!selected.TryGetValue(
                    conflictKey,
                    out UnityAudienceCommand existing))
            {
                selected.Add(
                    conflictKey,
                    command
                );

                continue;
            }

            if (command.priority >
                existing.priority)
            {
                selected[conflictKey] =
                    command;
            }
        }

        return selected;
    }

    private string CreateCommandKey(
        string requestId,
        UnityAudienceCommand command)
    {
        return (
            requestId +
            "|" +
            command.sync_group +
            "|" +
            command.agent_id +
            "|" +
            command.action_id
        );
    }

    public void ClearSessionHistory()
    {
        handledCommandKeys.Clear();
        StopAllCoroutines();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }
}