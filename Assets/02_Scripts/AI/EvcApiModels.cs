using System;

#region 공통 데이터

[Serializable]
public class EvcState
{
    public float E;
    public float V;
    public float C;
}

[Serializable]
public class ChannelPreference
{
    public float Face;
    public float Body;
    public float GazeHead;
}

#endregion

#region Smart Start 응답

[Serializable]
public class SmartStartResponse
{
    public string api_version;
    public string session_id;
    public string session_token;
    public int seed;
    public string presentation_title;

    public EvcState initial_evc_state;

    public float topic_interest;
    public float prior_knowledge;

    public SmartStartAudience[] audiences;

    public int step;
    public float expires_in_s;
    public int slide_count;
}

[Serializable]
public class SmartStartAudience
{
    public string agent_id;
    public AudienceProfile profile;
    public EvcState state;
}

[Serializable]
public class AudienceProfile
{
    public string row;
    public string seat;
    public bool has_laptop;
    public float topic_interest;
    public float prior_knowledge;

    public float responsiveness;
    public float expressivity;
    public float critical_bias;

    public ChannelPreference channel_preference;
}

#endregion

#region Update 응답

[Serializable]
public class EvcUpdateResponse
{
    public string api_version;
    public string request_id;
    public string session_id;

    public int step;
    public float accepted_client_time_s;

    public string latest_speech;
    public int current_slide_index;

    public SpeechMetrics speech_metrics;
    public EvaluationResult evaluation;
    public EvcDelta delta;
    public EvcState evc_state;

    public UpdateAudience[] audiences;
    public UnityAudienceCommand[] commands;

    public string[] warnings;
    public string no_op_reason;
}

[Serializable]
public class SpeechMetrics
{
    public float duration_s;
    public int word_count;
    public float speech_rate_wps;

    public int pause_count;
    public float pause_total_s;

    public int filler_count;
    public int repeated_word_count;

    public float avg_confidence;
    public float vocal_delivery_score;
}

[Serializable]
public class EvaluationResult
{
    public string move;

    public ContentEvaluation content;
    public DeliveryEvaluation delivery;

    public string segment_note;
    public string short_reason;

    public string[] missing_inputs;
    public float confidence;
}

[Serializable]
public class ContentEvaluation
{
    public float organization;
    public float supporting_material;
    public float central_message;
    public float cer_validity;
}

[Serializable]
public class DeliveryEvaluation
{
    public float language_clarity;
    public float vocal_delivery;
    public float gaze_delivery;
    public float slide_speech_alignment;
}

[Serializable]
public class EvcDelta
{
    public EvcState content;
    public EvcState delivery;
    public EvcState common;
}

[Serializable]
public class UpdateAudience
{
    public string agent_id;

    public EvcState previous_state;
    public EvcState sensitivity;
    public EvcState state;

    public string dominant_axis;
    public string direction;

    public AudienceBehavior core_behavior;
    public AudienceBehavior action_overlay;

    public string no_op_reason;
}

[Serializable]
public class AudienceBehavior
{
    public string behavior_id;
    public string variation_id;
    public float probability;
}

#endregion

#region Unity 청중 명령

[Serializable]
public class UnityAudienceCommand
{
    public string agent_id;
    public float start_time;

    public string layer;
    public string action_id;
    public float duration;

    public string sync_group;

    public string selected_behavior_id;
    public string selected_variation_id;

    public int priority;
    public string blend_mode;
    public float intensity;
}

#endregion

#region 서버 오류 응답

[Serializable]
public class EvcErrorResponse
{
    public EvcErrorDetail detail;
}

[Serializable]
public class EvcErrorDetail
{
    public string code;
    public string message;
}

#endregion
