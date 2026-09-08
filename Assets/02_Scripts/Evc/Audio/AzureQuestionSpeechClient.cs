using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Presentation;
using UnityEngine;
using UnityEngine.Networking;

namespace Rehear.Evc.Audio
{
    public sealed class AzureQuestionSpeechClient
    {
        private readonly AzureSpeechConfig config;
        public AzureQuestionSpeechClient(AzureSpeechConfig config) => this.config = config;

        private string Url(int questionIndex, string suffix)
        {
            if (!config) throw new InvalidOperationException("AzureSpeechConfig를 설정해주세요.");
            if (!PresentationSessionContext.Current.HasEvcSession)
                throw new InvalidOperationException("발표 세션 인증이 필요합니다.");
            if (questionIndex < 0) throw new ArgumentOutOfRangeException(nameof(questionIndex));
            return config.BaseUrl + "/sessions/" + UnityWebRequest.EscapeURL(PresentationSessionContext.Current.SessionId) +
                "/questions/" + questionIndex + suffix;
        }

        public void Validate() => _ = Url(0, "/speech");

        public async Task<AudioClip> SynthesizeAsync(int questionIndex, string voiceName, string speakerId, CancellationToken token)
        {
            // Return an AudioClip to the audience AudioSource/OVRLipSync, not a system speaker.
            using (var request = UnityWebRequestMultimedia.GetAudioClip(Url(questionIndex, "/speech"), AudioType.WAV))
            {
                request.method = UnityWebRequest.kHttpVerbPOST;
                request.SetRequestHeader("X-Speech-Voice", voiceName);
                request.SetRequestHeader("X-Audience-Id", speakerId);
                await SendAsync(request, token);
                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (!clip || clip.samples == 0) throw new InvalidOperationException("질문 음성이 비어 있습니다.");
                return clip;
            }
        }

        public async Task<AnswerResult> SubmitAnswerAsync(int questionIndex, byte[] wav, string requestId, string nextAudienceId, CancellationToken token)
        {
            if (wav == null || wav.Length <= 44) throw new InvalidOperationException("녹음된 응답이 없습니다.");
            using (var request = new UnityWebRequest(Url(questionIndex, "/answer"), UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(wav);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "audio/wav");
                request.SetRequestHeader("X-Request-Id", requestId);
                if (!string.IsNullOrEmpty(nextAudienceId)) request.SetRequestHeader("X-Next-Audience-Id", nextAudienceId);
                await SendAsync(request, token, 720);
                var result = JsonUtility.FromJson<AnswerResult>(request.downloadHandler.text);
                if (result != null && result.question_index == questionIndex && result.request_id == requestId &&
                    !result.saved && string.IsNullOrWhiteSpace(result.transcript)) return null;
                if (result == null || result.question_index != questionIndex || result.request_id != requestId ||
                    !result.saved || string.IsNullOrWhiteSpace(result.transcript))
                    throw new InvalidOperationException("응답 인식·저장 결과를 확인하지 못했습니다.");
                return result;
            }
        }

        private static async Task SendAsync(UnityWebRequest request, CancellationToken token, int timeoutSeconds = 180)
        {
            request.timeout = timeoutSeconds;
            request.redirectLimit = 0; // Never forward session credentials to a redirected host.
            request.SetRequestHeader("X-EVC-Session-Token", PresentationSessionContext.Current.SessionToken);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (token.IsCancellationRequested) { request.Abort(); token.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException($"음성 서버 요청 실패 (HTTP {request.responseCode}). 연결과 리소스 설정을 확인해주세요.");
        }

        [Serializable] public sealed class NextQuestion
        {
            public string id;
            public int order;
            public string question;
        }

        [Serializable] public sealed class AnswerResult
        {
            public int question_index;
            public string request_id;
            public string transcript;
            public bool saved;
            public int total;
            public NextQuestion next_question;
        }
    }
}
