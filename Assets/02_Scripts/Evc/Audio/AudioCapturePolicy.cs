using UnityEngine;

namespace Rehear.Evc.Audio
{
    [CreateAssetMenu(
        fileName = "AudioCapturePolicy",
        menuName = "Rehear/Audio Capture Policy"
    )]
    public sealed class AudioCapturePolicy : ScriptableObject
    {
        [SerializeField, Min(8000)]
        private int sampleRate = 16000;

        [SerializeField, Min(10)]
        private int microphoneRingBufferSeconds = 120;

        [SerializeField, Min(0.05f)]
        private float minimumSegmentSeconds = 0.25f;

        [SerializeField, Range(0f, 0.1f)]
        private float silenceRmsThreshold = 0.002f;

        public int SampleRate =>
            Mathf.Max(8000, sampleRate);

        public int RingBufferSeconds =>
            Mathf.Max(10, microphoneRingBufferSeconds);

        public float MinimumSegmentSeconds =>
            Mathf.Max(0.05f, minimumSegmentSeconds);

        public float SilenceRmsThreshold =>
            Mathf.Max(0f, silenceRmsThreshold);
    }
}