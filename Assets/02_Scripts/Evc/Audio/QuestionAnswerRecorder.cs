using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Rehear.Evc.Audio
{
    // A short mic ring is drained every frame so answers longer than a ring are not truncated.
    // Only this component owns the microphone during Q&A. TTS finishes before BeginAsync.
    public sealed class QuestionAnswerRecorder : MonoBehaviour
    {
        private const int Rate = 16000, RingSeconds = 5, MaxSeconds = 600;
        private AudioClip clip;
        private int lastFrame;
        private bool paused, applicationPaused;
        private readonly List<float> samples = new List<float>();
        public bool IsRecording => clip && Microphone.IsRecording(null);
        public bool ReachedLimit => samples.Count >= (clip ? clip.frequency : Rate) * MaxSeconds;

        public async Task BeginAsync(CancellationToken token)
        {
            if (clip) throw new InvalidOperationException("이미 응답을 녹음 중입니다.");
            if (Microphone.IsRecording(null)) throw new InvalidOperationException("다른 녹음이 아직 진행 중입니다.");
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                var done = new TaskCompletionSource<bool>();
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => done.TrySetResult(true);
                callbacks.PermissionDenied += _ => done.TrySetResult(false);
                callbacks.PermissionDeniedAndDontAskAgain += _ => done.TrySetResult(false);
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
                using (token.Register(() => done.TrySetCanceled()))
                    if (!await done.Task) throw new InvalidOperationException("마이크 권한을 허용해주세요.");
            }
#else
            var permission = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            while (!permission.isDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone)) throw new InvalidOperationException("마이크 권한을 허용해주세요.");
#endif
            token.ThrowIfCancellationRequested();
            samples.Clear(); lastFrame = 0; paused = false;
            clip = Microphone.Start(null, true, RingSeconds, Rate);
            try
            {
                var deadline = Time.realtimeSinceStartup + 5;
                while (clip && Microphone.GetPosition(null) <= 0 && Time.realtimeSinceStartup < deadline)
                { token.ThrowIfCancellationRequested(); await Task.Yield(); }
                token.ThrowIfCancellationRequested();
                if (!IsRecording || Microphone.GetPosition(null) <= 0)
                    throw new InvalidOperationException("마이크를 시작하지 못했습니다.");
            }
            catch { Cancel(); throw; }
        }

        private void Update() => Drain();
        private void Drain()
        {
            if (!clip) return;
            int position = Microphone.GetPosition(null);
            if (position < 0) return;
            int frames = (position - lastFrame + clip.samples) % clip.samples;
            if (frames > 0 && !paused && !applicationPaused && !ReachedLimit)
            {
                // AudioClip.GetData wraps at the ring boundary.
                var chunk = new float[frames * clip.channels];
                if (!clip.GetData(chunk, lastFrame)) throw new InvalidOperationException("마이크 데이터를 읽지 못했습니다.");
                for (int i = 0; i < frames && !ReachedLimit; i++)
                {
                    float mono = 0;
                    for (int c = 0; c < clip.channels; c++) mono += chunk[i * clip.channels + c];
                    samples.Add(mono / clip.channels);
                }
            }
            lastFrame = position;
        }
        public void SetPaused(bool value) { Drain(); paused = value; }
        private void OnApplicationPause(bool value)
        {
            // Discard the suspended interval, which may exceed the ring duration.
            if (value) Drain();
            else if (clip) lastFrame = Math.Max(0, Microphone.GetPosition(null));
            applicationPaused = value;
        }
        public byte[] Finish()
        {
            Drain();
            int frequency = clip ? clip.frequency : Rate;
            StopMicrophone();
            // Some Android microphones negotiate a different hardware sample rate.
            // Normalize the upload to mono 16 kHz so ten minutes stays below 20 MB.
            var pcm = samples.ToArray();
            if (frequency != Rate && pcm.Length > 0)
            {
                var resampled = new float[(int)((long)pcm.Length * Rate / frequency)];
                for (int i = 0; i < resampled.Length; i++)
                {
                    float source = (float)i * frequency / Rate;
                    int left = Mathf.Min((int)source, pcm.Length - 1);
                    resampled[i] = Mathf.Lerp(pcm[left], pcm[Mathf.Min(left + 1, pcm.Length - 1)], source - left);
                }
                pcm = resampled;
            }
            var wav = WavEncoder.EncodePcm16(pcm, 1, Rate);
            samples.Clear();
            return wav;
        }
        public void Cancel() { StopMicrophone(); samples.Clear(); }
        private void StopMicrophone()
        {
            if (!clip) return;
            Microphone.End(null);
            Destroy(clip); clip = null;
        }
        private void OnDisable() => Cancel();
    }
}
