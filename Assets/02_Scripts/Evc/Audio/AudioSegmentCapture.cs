using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Contracts;
using UnityEngine;

namespace Rehear.Evc.Audio
{
    public enum MicrophonePermissionState
    {
        Unknown,
        Requesting,
        Granted,
        Denied
    }

   

    public sealed class AudioSegmentCapture : MonoBehaviour
    {
        [SerializeField] private AudioCapturePolicy policy;
        [SerializeField] private string microphoneDevice = string.Empty;

        private AudioClip recording;
        private int lastPosition;
        private bool starting;

        public event Action<MicrophonePermissionState> PermissionStateChanged;

        public bool IsRecording => recording != null && Microphone.IsRecording(DeviceName);
        public MicrophonePermissionState PermissionState { get; private set; } = MicrophonePermissionState.Unknown;
        private string DeviceName => string.IsNullOrWhiteSpace(microphoneDevice) ? null : microphoneDevice;

        public async Task<bool> StartCaptureAsync(CancellationToken cancellationToken)
        {
            if (IsRecording)
                return true;
            if (starting)
                return false;
            if (policy == null)
                throw new InvalidOperationException("AudioCapturePolicy가 연결되지 않았습니다.");

            starting = true;
            try
            {
                SetPermissionState(MicrophonePermissionState.Requesting);
                if (!await RequestMicrophonePermissionAsync(cancellationToken))
                {
                    SetPermissionState(MicrophonePermissionState.Denied);
                    return false;
                }

                SetPermissionState(MicrophonePermissionState.Granted);

                recording = Microphone.Start(
                    DeviceName,
                    true,
                    policy.RingBufferSeconds,
                    policy.SampleRate);
                lastPosition = 0;
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return recording != null;
            }
            finally
            {
                starting = false;
            }
        }

        public BinaryFileDto FlushSegment()
        {
            if (!IsRecording || recording == null)
                return null;

            var currentPosition = Microphone.GetPosition(DeviceName);
            if (currentPosition < 0 || currentPosition == lastPosition)
                return null;

            var frameCount = currentPosition > lastPosition
                ? currentPosition - lastPosition
                : recording.samples - lastPosition + currentPosition;
            if (frameCount < Mathf.CeilToInt(recording.frequency * policy.MinimumSegmentSeconds))
                return null;

            var samples = ReadSamples(lastPosition, frameCount);
            lastPosition = currentPosition;
            if (samples.Length == 0 || CalculateRms(samples) < policy.SilenceRmsThreshold)
                return null;

            return new BinaryFileDto
            {
                bytes = WavEncoder.EncodePcm16(samples, recording.channels, recording.frequency),
                file_name = "segment-" + Guid.NewGuid().ToString("N") + ".wav",
                mime_type = "audio/wav"
            };
        }

        public Task<BinaryFileDto> StopAndFlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = FlushSegment();
            StopCapture();
            return Task.FromResult(segment);
        }

        public Task<bool> RetryStartCaptureAsync(CancellationToken cancellationToken)
        {
            RefreshPermissionState();
            return StartCaptureAsync(cancellationToken);
        }

        public void RefreshPermissionState()
        {
            SetPermissionState(HasMicrophonePermission()
                ? MicrophonePermissionState.Granted
                : PermissionState == MicrophonePermissionState.Unknown
                    ? MicrophonePermissionState.Unknown
                    : MicrophonePermissionState.Denied);
        }

        public void StopCapture()
        {
            if (Microphone.IsRecording(DeviceName))
                Microphone.End(DeviceName);
            recording = null;
            lastPosition = 0;
        }

        private float[] ReadSamples(int startFrame, int frameCount)
        {
            var channels = Math.Max(1, recording.channels);
            var all = new float[recording.samples * channels];
            if (!recording.GetData(all, 0))
                return Array.Empty<float>();

            var result = new float[frameCount * channels];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var sourceFrame = (startFrame + frame) % recording.samples;
                for (var channel = 0; channel < channels; channel++)
                    result[frame * channels + channel] = all[sourceFrame * channels + channel];
            }
            return result;
        }

        private static float CalculateRms(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return 0f;

            double squareSum = 0d;
            for (var index = 0; index < samples.Length; index++)
                squareSum += samples[index] * samples[index];
            return (float)Math.Sqrt(squareSum / samples.Length);
        }

        private static bool HasMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Microphone);
#else
            return Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
        }

        private static async Task<bool> RequestMicrophonePermissionAsync(CancellationToken cancellationToken)
        {
            if (HasMicrophonePermission())
                return true;

#if UNITY_ANDROID && !UNITY_EDITOR
            var completion = new TaskCompletionSource<bool>();
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => completion.TrySetResult(true);
            callbacks.PermissionDenied += _ => completion.TrySetResult(false);
            callbacks.PermissionDeniedAndDontAskAgain += _ => completion.TrySetResult(false);
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.Microphone,
                callbacks);

            using (cancellationToken.Register(() => completion.TrySetCanceled()))
                return await completion.Task;
#else
            var permission = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            while (!permission.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            return HasMicrophonePermission();
#endif
        }

        private void SetPermissionState(MicrophonePermissionState value)
        {
            if (PermissionState == value)
                return;
            PermissionState = value;
            PermissionStateChanged?.Invoke(value);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
                RefreshPermissionState();
        }

        private void OnDestroy()
        {
            StopCapture();
        }
    }

    public static class WavEncoder
    {
        public static byte[] EncodePcm16(float[] samples, int channels, int sampleRate)
        {
            if (samples == null || samples.Length == 0)
                return Array.Empty<byte>();
            channels = Math.Max(1, channels);
            sampleRate = Math.Max(8000, sampleRate);
            var dataLength = samples.Length * sizeof(short);

            using (var stream = new MemoryStream(44 + dataLength))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * sizeof(short));
                writer.Write((short)(channels * sizeof(short)));
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);

                for (var index = 0; index < samples.Length; index++)
                {
                    var clamped = Mathf.Clamp(samples[index], -1f, 1f);
                    writer.Write((short)Mathf.RoundToInt(clamped * short.MaxValue));
                }
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
