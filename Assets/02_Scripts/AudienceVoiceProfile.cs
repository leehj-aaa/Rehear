using UnityEngine;

// Lives on the character prefab: randomized seats must not change a person's voice.
public sealed class AudienceVoiceProfile : MonoBehaviour
{
    // Keep serialized values stable for existing prefabs and scene instances.
    public enum KoreanVoice { InJoon = 0, BongJin = 1, GookMin = 2, SunHi = 3, JiMin = 4, SeoHyeon = 5, SoonBok = 6, YuJin = 7 }
    [SerializeField] private KoreanVoice voice;
    public string VoiceName => "ko-KR-" + voice + "Neural";
}
