using UnityEngine;

// Lives on the character prefab: randomized seats must not change a person's voice.
public sealed class AudienceVoiceProfile : MonoBehaviour
{
    public enum KoreanVoice { InJoon, BongJin, GookMin, SunHi, JiMin, SeoHyeon }
    [SerializeField] private KoreanVoice voice;
    public string VoiceName => "ko-KR-" + voice + "Neural";
}
