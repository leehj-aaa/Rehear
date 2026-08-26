using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Audio;
using Rehear.Evc.Audience;
using Rehear.Evc.Config;
using Rehear.Evc.Contracts;
using Rehear.Evc.Questions;
using Rehear.Evc.Session;
using Rehear.Evc.Transport;
using Rehear.Evc.Update;
using Rehear.Evc.Report;
using UnityEngine;

namespace Rehear.Evc.Presentation
{
    public enum PresentationFlowState
    {
        Idle,
        Starting,
        Running,
        Paused,
        Finishing,
        QuestionsReady,
        Failed,
        Stopped
    }

    public sealed class PresentationFlowController : MonoBehaviour
    {
        [SerializeField] private EvcEnvironmentConfig environmentConfig;
        [SerializeField] private AudioSegmentCapture audioCapture;
        [SerializeField] private AudienceReactionCoordinator audienceCoordinator;
        [SerializeField] private bool autoStart;
        [SerializeField] private string language = "ko-KR";

        private readonly PresentationClock clock = new PresentationClock();
        private CancellationTokenSource lifetimeCancellation;
        private IEvcApiClient apiClient;
        private EvcSessionService sessionService;
        private EvcUpdateService updateService;
        private PresentationQuestionService questionService;
        private Task startTask;
        private int lastSlideIndex;
        private bool presentationFinalizedForQuestions;
        private PresentationReportService reportService;
        private bool questionFlowStarted;
        

        public ReportFeedback CurrentReport { get; private set; }
        public event Action<PresentationFlowState, string> StateChanged;

        public PresentationFlowState State { get; private set; } = PresentationFlowState.Idle;
        public IPresentationClock Clock => clock;
        public IReadOnlyList<GeneratedQuestion> Questions { get; private set; } = Array.Empty<GeneratedQuestion>();
        public bool CanRetryQuestions =>
                    questionFlowStarted &&
                    State == PresentationFlowState.Failed;

        private void Awake()
        {
            lifetimeCancellation = new CancellationTokenSource();
            if (audienceCoordinator != null)
                audienceCoordinator.SetClock(clock);
        }

        private void Start()
        {
            if (autoStart)
                _ = StartPresentationAsync();
        }

        public Task StartPresentationAsync()
        {
            if (startTask != null && !startTask.IsCompleted)
                return startTask;
            if (State == PresentationFlowState.Running || State == PresentationFlowState.Paused)
                return Task.CompletedTask;

            startTask = StartInternalAsync(lifetimeCancellation.Token);
            return startTask;
        }

        public async Task FlushSegmentAsync(
            string utterancePosition,
            int currentSlideIndex,
            CancellationToken cancellationToken)
        {
            if (State != PresentationFlowState.Running && State != PresentationFlowState.Paused)
                return;
            if (audioCapture == null || updateService == null)
                return;

            lastSlideIndex =
            NormalizeServerSlideIndex(
                currentSlideIndex
            );

            var audio = audioCapture.FlushSegment();
            if (audio == null)
                return;

            await updateService.EnqueueAsync(new AudioSegmentPayload
            {
                Audio = audio,
                ClientTimeSeconds = clock.ElapsedSeconds,
                SlideIndex = lastSlideIndex,
                UtterancePosition = utterancePosition,
                Language = language
            }, cancellationToken);
        }

        public async Task PauseAsync(int currentSlideIndex, CancellationToken cancellationToken)
        {
            if (State != PresentationFlowState.Running)
                return;

            lastSlideIndex =
            NormalizeServerSlideIndex(
                currentSlideIndex
            );
            var audio = audioCapture != null
                ? await audioCapture.StopAndFlushAsync(cancellationToken)
                : null;
            clock.Pause();
            SetState(PresentationFlowState.Paused, string.Empty);

            if (audio != null && updateService != null)
            {
                await updateService.EnqueueAsync(new AudioSegmentPayload
                {
                    Audio = audio,
                    ClientTimeSeconds = clock.ElapsedSeconds,
                    SlideIndex = lastSlideIndex,
                    UtterancePosition = "silence_or_pause",
                    Language = language
                }, cancellationToken);
            }
        }

        public async Task ResumeAsync(CancellationToken cancellationToken)
        {
            if (State != PresentationFlowState.Paused)
                return;
            if (audioCapture != null && !await audioCapture.StartCaptureAsync(cancellationToken))
            {
                SetState(PresentationFlowState.Failed, "마이크 권한이 필요합니다.");
                return;
            }
            clock.Resume();
            SetState(PresentationFlowState.Running, string.Empty);
        }

        public async Task<IReadOnlyList<GeneratedQuestion>> EndPresentationAndLoadQuestionsAsync(
            int currentSlideIndex,
            CancellationToken cancellationToken)
        {
            if (State == PresentationFlowState.QuestionsReady)
                return Questions;
           var isActivePresentation =
                State == PresentationFlowState.Running ||
                State == PresentationFlowState.Paused;

            if (!isActivePresentation && !CanRetryQuestions)
            {
                throw new InvalidOperationException(
                    "Presentation is not active."
                );
            }

            questionFlowStarted = true;
            SetState(
                PresentationFlowState.Finishing,
                string.Empty
            );
            try
            {
                if (isActivePresentation)
                {
                    var finalAudio = audioCapture != null
                        ? await audioCapture.StopAndFlushAsync(cancellationToken)
                        : null;
                    if (finalAudio != null)
                    {
                        await updateService.EnqueueAsync(new AudioSegmentPayload
                        {
                            Audio = finalAudio,
                            ClientTimeSeconds = clock.ElapsedSeconds,
                            SlideIndex =
                                NormalizeServerSlideIndex(
                                    currentSlideIndex
                                ),
                            UtterancePosition = "utterance_boundary",
                            Language = language
                        }, cancellationToken);
                    }
                    presentationFinalizedForQuestions = true;
                }

                var count = PresentationSessionContext.Current.Presentation?.page_1?.qa_count ?? 3;
                Questions = await questionService.RestoreOrGenerateAfterLastSegmentAsync(
                    count,
                    true,
                    cancellationToken);
                clock.Stop();
                SetState(PresentationFlowState.QuestionsReady, string.Empty);
                return Questions;
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                SetState(PresentationFlowState.Failed, ToUserMessage(exception));
                throw;
            }
        }

        public void StopFlow()
        {
            lifetimeCancellation?.Cancel();
            audioCapture?.StopCapture();
            audienceCoordinator?.CancelAll();
            audienceCoordinator?.SetServerMode(false);
            updateService?.StopAccepting();
            clock.Stop();
            SetState(PresentationFlowState.Stopped, string.Empty);
        }

        private async Task StartInternalAsync(CancellationToken cancellationToken)
        {
            questionFlowStarted = false;
            presentationFinalizedForQuestions = false;

            SetState(PresentationFlowState.Starting, string.Empty);
            try
            {
                if (environmentConfig == null)
                    throw new InvalidOperationException("EVC 환경 설정이 없습니다.");
                if (!environmentConfig.TryValidate(out var configError))
                    throw new InvalidOperationException(configError);
                if (!PresentationSessionContext.Current.HasPresentation)
                    throw new InvalidOperationException("PIN 세션 데이터가 준비되지 않았습니다.");

                updateService?.Dispose();
                updateService = null;
                questionService = null;
                Questions = Array.Empty<GeneratedQuestion>();
                presentationFinalizedForQuestions = false;

                apiClient = new UnityWebRequestEvcApiClient(environmentConfig);
                sessionService = new EvcSessionService(apiClient, PresentationSessionContext.Current);
                updateService = new EvcUpdateService(
                    apiClient,
                    PresentationSessionContext.Current,
                    sessionService,
                    audienceCoordinator,
                    environmentConfig.TransientRetryCount);
                questionService = new PresentationQuestionService(
                    apiClient,
                    PresentationSessionContext.Current,
                    updateService,
                    environmentConfig.TransientRetryCount);
                reportService = new PresentationReportService(
                        apiClient,
                        PresentationSessionContext.Current,
                        updateService,
                        environmentConfig.TransientRetryCount
                    );

CurrentReport = null;

                if (audioCapture == null)
                    throw new InvalidOperationException("오디오 캡처가 연결되지 않았습니다.");
                if (!await audioCapture.StartCaptureAsync(cancellationToken))
                    throw new InvalidOperationException("마이크 권한이 거부되었습니다.");

                // slide_file is intentionally omitted until its source contract is supplied.
                await sessionService.StartAsync(null, null, cancellationToken);

                audienceCoordinator?.SetServerMode(true);
                clock.Start();
                SetState(PresentationFlowState.Running, string.Empty);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                audioCapture?.StopCapture();
                audienceCoordinator?.SetServerMode(false);
                clock.Stop();
                SetState(PresentationFlowState.Failed, ToUserMessage(exception));
                throw;
            }
        }

        private static string ToUserMessage(Exception exception)
        {
            if (exception is EvcApiException apiException)
            {
                switch (apiException.Kind)
                {
                    case EvcErrorKind.Unauthorized: return "AI 세션이 만료되었습니다. 다시 시작해주세요.";
                    case EvcErrorKind.NotFound: return "AI 세션 또는 질문 결과를 찾을 수 없습니다.";
                    case EvcErrorKind.Conflict: return "AI 세션 상태가 변경되었습니다. 잠시 후 다시 시도해주세요.";
                    case EvcErrorKind.Timeout: return "AI 서버 응답이 지연되고 있습니다. 다시 시도해주세요.";
                    case EvcErrorKind.Network: return "네트워크 연결을 확인해주세요.";
                    case EvcErrorKind.Validation: return "발표 데이터 또는 오디오 형식을 확인해주세요.";
                    case EvcErrorKind.PayloadTooLarge: return "녹음 구간이 너무 큽니다.";
                    case EvcErrorKind.UnsupportedMediaType: return "지원하지 않는 오디오 형식입니다.";
                    case EvcErrorKind.RateLimited: return "요청이 많습니다. 잠시 후 다시 시도해주세요.";
                    case EvcErrorKind.ProviderUnavailable: return "질문 생성 서버를 사용할 수 없습니다. 다시 시도해주세요.";
                    case EvcErrorKind.ServerError: return "AI 서버에 일시적인 문제가 있습니다. 다시 시도해주세요.";
                    default: return "AI 발표 세션을 처리하지 못했습니다.";
                }
            }

            return exception.Message;
        }

        private void SetState(PresentationFlowState state, string message)
        {
            State = state;
            StateChanged?.Invoke(state, message ?? string.Empty);
        }

        private void OnDestroy()
        {
            StopFlow();
            updateService?.Dispose();
            lifetimeCancellation?.Dispose();
        }

        private async void OnApplicationPause(bool paused)
        {
            if (lifetimeCancellation == null || lifetimeCancellation.IsCancellationRequested)
                return;
            try
            {
                if (paused && State == PresentationFlowState.Running)
                    await PauseAsync(lastSlideIndex, lifetimeCancellation.Token);
                else if (!paused && State == PresentationFlowState.Paused)
                    await ResumeAsync(lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                SetState(PresentationFlowState.Failed, ToUserMessage(exception));
            }
        }

        private static int NormalizeServerSlideIndex(
            int requestedIndex)
        {
            int serverSlideCount =
                PresentationSessionContext.Current
                    .SlideCount;

            // Smart Start에 PDF/PPT를 보내지 않은 경우
            // 서버에는 슬라이드가 등록되지 않았으므로 0만 전송한다.
            if (serverSlideCount <= 0)
                return 0;

            return Math.Max(
                0,
                Math.Min(
                    requestedIndex,
                    serverSlideCount - 1
                )
                    );
        }
        public async Task<ReportFeedback> FinishReportAsync(
            int plannedSeconds,
            int qaSeconds,
            CancellationToken cancellationToken)
        {
            if (reportService == null)
            {
                throw new InvalidOperationException(
                    "EVC 리포트 서비스가 준비되지 않았습니다."
                );
            }

            Debug.Log(
                "[EVC] AI 리포트 생성 요청" +
                "\n발표 예정 시간: " + plannedSeconds + "초" +
                "\n질의응답 시간: " + qaSeconds + "초"
            );

            CurrentReport =
                await reportService.FinishAfterLastSegmentAsync(
                    plannedSeconds,
                    qaSeconds,
                    cancellationToken
                );

            Debug.Log(
                "[EVC] AI 리포트 생성 완료" +
                "\n종합 점수: " +
                CurrentReport.score.overall_score +
                "\n참여도: " +
                CurrentReport.score_card.scores.engagement +
                "\n명확도: " +
                CurrentReport.score_card.scores.clarity +
                "\n신뢰도: " +
                CurrentReport.score_card.scores.credibility
            );

            return CurrentReport;
        }
    }
}
