using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Random = UnityEngine.Random;

public class AudienceCommandDispatcher :
    MonoBehaviour
{
    [Serializable]
    public class AudienceBinding
    {
        [Tooltip("audience_01 ~ audience_06")]
        public string agentId;

        [Tooltip(
            "해당 청중의 AudienceAnimationPlayer"
        )]
        public AudienceAnimationPlayer bodyAnimator;

        [Tooltip(
            "해당 청중의 AudienceGazeController"
        )]
        public AudienceGazeController gazeController;
        [Tooltip("다른 청중과 동시에 움직이지 않도록 하는 추가 지연")]
        [Range(0f, 2f)]
        public float startOffset;

        [Tooltip("청중별 행동 유지시간 배율")]
        [Range(0.8f, 1.5f)]
        public float durationMultiplier = 1f;
    }

    [Header("청중 연결")]
    [SerializeField]
    private AudienceBinding[] audienceBindings;

    [Header("디버그")]
    [SerializeField]
    private bool printDispatchLog = true;

    [Header("통신 실패 Fallback")]

    [SerializeField]
    private string[] fallbackVariationIds =
    {
        "BL_01.neutral_listening",
        "BL_03.quiet_stable_posture",
        "AL_01.stable_attention",
        "AL_01.active_following",
        "AL_03.passive_acceptance"
    };

    [SerializeField]
    private Vector2 fallbackInitialDelayRange =
        new Vector2(0.5f, 4f);

    [SerializeField]
    private Vector2 fallbackIntervalRange =
        new Vector2(5f, 10f);

    [SerializeField]
    private Vector2 fallbackDurationRange =
        new Vector2(2f, 3.5f);

    private readonly List<Coroutine>
        fallbackRoutines =
            new List<Coroutine>();

    public bool IsFallbackActive =>
        fallbackRoutines.Count > 0;

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
        var seating=FindFirstObjectByType<AudienceSeating>();
        if(seating && seating.gameObject.scene==gameObject.scene && seating.members != null)
        {
            audienceBindings=new AudienceBinding[seating.members.Length];
            for(int i=0;i<seating.members.Length;i++)
            {
                var member=seating.members[i];
                audienceBindings[i]=new AudienceBinding { agentId=member.AgentId,
                    bodyAnimator=member.GetComponent<AudienceAnimationPlayer>(),
                    gazeController=member.GetComponent<AudienceGazeController>(),
                    startOffset=i*.12f, durationMultiplier=1f };
            }
        }

        if (audienceBindings == null)
            return;

        foreach (
            AudienceBinding binding
            in audienceBindings)
        {
            if (binding?.bodyAnimator && binding.bodyAnimator.GetComponent<AudienceSeatAssignment>())
            {
                var agent=binding.bodyAnimator.GetComponent<Rehear.Evc.Audience.AudienceAgent>();
                if(agent) binding.agentId=agent.AgentId;
            }
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

            float agentOffset =
                GetAgentStartOffset(
                    command.agent_id
                );

            float delay =
                Mathf.Max(
                    0f,
                    command.start_time -
                    currentSessionTime
                ) +
                agentOffset;

            StartCoroutine(
                ExecuteCommandAfterDelay(
                    command,
                    delay
                )
            );
        }
    }
    private float GetAgentStartOffset(
        string agentId)
    {
        if (bindingMap.TryGetValue(
                agentId,
                out AudienceBinding binding))
        {
            return Mathf.Max(
                0f,
                binding.startOffset
            );
        }

        return 0f;
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

        var seatAssignment = binding.bodyAnimator ? binding.bodyAnimator.GetComponent<AudienceSeatAssignment>() : null;
        if (seatAssignment && (!seatAssignment.Allows(command.action_id) || !seatAssignment.Allows(command.selected_variation_id)))
            return;

        float adjustedDuration =
            command.duration *
            Mathf.Max(
                0.1f,
                binding.durationMultiplier
            );

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
                            adjustedDuration,
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
                        adjustedDuration
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
        StopFallbackMode();
        StopAllCoroutines();
    }
    public void StartFallbackMode()
    {
        if (IsFallbackActive)
            return;

        if (audienceBindings == null ||
            audienceBindings.Length == 0)
        {
            Debug.LogWarning(
                "[AI Fallback] 연결된 청중이 없습니다."
            );

            return;
        }

        Debug.LogWarning(
            "[AI Fallback] 서버 반응을 사용할 수 없어 " +
            "로컬 청중 애니메이션을 시작합니다."
        );

        for (int index = 0;
            index < audienceBindings.Length;
            index++)
        {
            AudienceBinding binding =
                audienceBindings[index];

            if (binding == null ||
                binding.bodyAnimator == null)
            {
                continue;
            }

            Coroutine routine =
                StartCoroutine(
                    FallbackAgentRoutine(
                        binding,
                        index
                    )
                );

            fallbackRoutines.Add(routine);
        }
    }

    public void StopFallbackMode()
    {
        if (!IsFallbackActive)
            return;

        foreach (Coroutine routine
                in fallbackRoutines)
        {
            if (routine != null)
                StopCoroutine(routine);
        }

        fallbackRoutines.Clear();

        Debug.Log(
            "[AI Fallback] 서버 연결이 복구되어 " +
            "로컬 애니메이션을 중지합니다."
        );
    }

    private IEnumerator FallbackAgentRoutine(
        AudienceBinding binding,
        int agentIndex)
    {
        float initialDelay =
            Random.Range(
                fallbackInitialDelayRange.x,
                fallbackInitialDelayRange.y
            );

        // 배열 순서에 따른 작은 추가 시차
        initialDelay += agentIndex * 0.15f;

        yield return new WaitForSecondsRealtime(
            initialDelay
        );

        int previousIndex = -1;

        while (true)
        {
            if (fallbackVariationIds != null &&
                fallbackVariationIds.Length > 0)
            {
                int selectedIndex =
                    GetDifferentFallbackIndex(
                        previousIndex
                    );

                previousIndex = selectedIndex;

                string variationId =
                    fallbackVariationIds[
                        selectedIndex
                    ];

                float duration =
                    Random.Range(
                        fallbackDurationRange.x,
                        fallbackDurationRange.y
                    );

                float intensity =
                    Random.Range(
                        0.4f,
                        0.7f
                    );

                binding.bodyAnimator
                    .PlayServerVariation(
                        variationId,
                        duration,
                        intensity
                    );
            }

            float interval =
                Random.Range(
                    fallbackIntervalRange.x,
                    fallbackIntervalRange.y
                );

            yield return new WaitForSecondsRealtime(
                interval
            );
        }
    }

    private int GetDifferentFallbackIndex(
        int previousIndex)
    {
        if (fallbackVariationIds.Length <= 1)
            return 0;

        int selectedIndex =
            previousIndex;

        while (selectedIndex ==
            previousIndex)
        {
            selectedIndex =
                Random.Range(
                    0,
                    fallbackVariationIds.Length
                );
        }

        return selectedIndex;
    }
}
