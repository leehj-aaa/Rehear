using Rehear.Evc.Audience;
using Rehear.Evc.Contracts;
using UnityEngine;

public class LegacyAudienceEvcPlayer :
    MonoBehaviour,
    IRawCommandPlayer
{
    [SerializeField]
    private AudienceAnimationPlayer
        animationPlayer;

    public string Layer => "Body";

    private void Reset()
    {
        animationPlayer =
            GetComponent<
                AudienceAnimationPlayer>();
    }

    public bool TryPlayCommand(
        UnityCommandDto command,
        out string reason)
    {
        reason = string.Empty;

        if (command == null)
        {
            reason = "null_command";
            return false;
        }

        if (command.layer != Layer)
        {
            reason = "layer_mismatch";
            return false;
        }

        if (animationPlayer == null)
        {
            reason =
                "legacy_animation_player_missing";

            return false;
        }

        string variationId =
            command.selected_variation_id;

        if (string.IsNullOrWhiteSpace(
                variationId))
        {
            reason =
                "missing_selected_variation";

            return false;
        }

        bool played =
            animationPlayer
                .PlayServerVariation(
                    variationId,
                    command.duration,
                    command.intensity
                );

        if (!played)
        {
            reason =
                "unknown_variation:" +
                variationId;

            return false;
        }

        Debug.Log(
            "[EVC 청중 애니메이션 재생]" +
            "\nAgent ID: " +
            command.agent_id +
            "\nAction ID: " +
            command.action_id +
            "\nVariation ID: " +
            variationId
        );

        return true;
    }

    public void StopCommand()
    {
        // AudienceAnimationPlayer가 명령 시간 종료 후
        // 자체적으로 기본 자세로 복귀합니다.
    }
}