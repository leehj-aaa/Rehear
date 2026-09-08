using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;
using Rehear.Evc.Session;
using Rehear.Evc.Transport;

namespace Rehear.Evc.Update
{
    public sealed class AudioSegmentPayload
    {
        public BinaryFileDto Audio;
        public double ClientTimeSeconds;
        public int SlideIndex;
        public string UtterancePosition;
        public string Language = "ko-KR";
    }

    public interface IAudienceCommandSink
    {
        void HandleCommands(string requestId, IReadOnlyList<UnityCommandDto> commands);
    }

    public sealed class EvcUpdateService : IDisposable
    {
        private readonly object gate = new object();
        private readonly IEvcApiClient apiClient;
        private readonly PresentationSessionContext context;
        private readonly EvcSessionService sessionService;
        private readonly IAudienceCommandSink commandSink;
        private readonly int maxTransientRetries;
        private readonly Func<int, CancellationToken, Task> retryDelay;
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private Task queueTail = Task.CompletedTask;
        private bool accepting = true;
        private bool disposed;
        private double lastClientTime;
        private Exception queueFailure;
        private int pendingCount;
        private int succeededCount;
        private int failedCount;
        private double lastProcessingMilliseconds;

        public EvcUpdateService(
            IEvcApiClient apiClient,
            PresentationSessionContext context,
            EvcSessionService sessionService,
            IAudienceCommandSink commandSink,
            int maxTransientRetries = 2,
            Func<int, CancellationToken, Task> retryDelay = null)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            this.commandSink = commandSink;
            this.maxTransientRetries = Math.Max(0, maxTransientRetries);
            this.retryDelay = retryDelay ?? DefaultRetryDelayAsync;
        }

        public bool IsAccepting
        {
            get { lock (gate) return accepting && !disposed; }
        }

        public int PendingCount
        {
            get { lock (gate) return pendingCount; }
        }

        public int SucceededCount
        {
            get { lock (gate) return succeededCount; }
        }

        public int FailedCount
        {
            get { lock (gate) return failedCount; }
        }

        public double LastProcessingMilliseconds
        {
            get { lock (gate) return lastProcessingMilliseconds; }
        }

        public Task<EvcUpdateResponse> EnqueueAsync(
            AudioSegmentPayload payload,
            CancellationToken cancellationToken)
        {
            ValidatePayload(payload);
            lock (gate)
            {
                ThrowIfDisposed();
                if (!accepting)
                    throw new InvalidOperationException("The EVC update queue no longer accepts segments.");

                var requestId = Guid.NewGuid().ToString("D");
                var previous = queueTail;
                pendingCount++;
                var operation = ProcessAfterAsync(previous, payload, requestId, cancellationToken);
                queueTail = IgnoreFailureAsync(operation);
                return operation;
            }
        }

        public void StopAccepting()
        {
            lock (gate)
                accepting = false;
        }

        public async Task WhenIdleAsync(CancellationToken cancellationToken)
        {
            Task tail;
            lock (gate)
                tail = queueTail;

            await AwaitWithCancellationAsync(tail, cancellationToken);
            Exception failure;
            lock (gate)
            {
                failure = queueFailure;
                // 한 음성 조각의 실패가 질문/리포트 재시도를 영구적으로
                // 막지 않도록 소비한 실패 상태를 비운다.
                queueFailure = null;
            }

            if (failure != null)
            {
                UnityEngine.Debug.LogWarning(
                    "[EVC] 실패한 음성 조각을 제외하고 " +
                    "이미 처리된 데이터로 계속 진행합니다." +
                    "\n원인: " + failure.Message
                );
            }
        }

        public void Dispose()
        {
            Task tail;
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                accepting = false;
                tail = queueTail;
            }

            lifetimeCancellation.Cancel();
            _ = DisposeCancellationAfterQueueAsync(tail, lifetimeCancellation);
        }

        private async Task<EvcUpdateResponse> ProcessAfterAsync(
            Task previous,
            AudioSegmentPayload payload,
            string requestId,
            CancellationToken callerCancellation)
        {
            var stopwatch = Stopwatch.StartNew();
            var succeeded = false;
            try
            {
                await previous;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           callerCancellation,
                           lifetimeCancellation.Token))
                {
                    var cancellationToken = linked.Token;
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!context.HasEvcSession)
                        throw new InvalidOperationException("EVC session has not started.");
                    if (payload.ClientTimeSeconds + 0.0001d < lastClientTime)
                        throw new InvalidOperationException("client_time_s must be monotonic.");

                    lastClientTime = Math.Max(lastClientTime, payload.ClientTimeSeconds);
                    var expectedStep = context.Step;
                    var slideIndex = Math.Max(0, payload.SlideIndex);
                    if (context.SlideCount > 0)
                        slideIndex = Math.Min(slideIndex, context.SlideCount - 1);

                    var request = new EvcSegmentRequest
                    {
                        session_id = context.SessionId,
                        session_token = context.SessionToken,
                        request_id = requestId,
                        expected_step = expectedStep,
                        client_time_s = lastClientTime,
                        current_slide_index = slideIndex,
                        utterance_position = payload.UtterancePosition,
                        language = string.IsNullOrWhiteSpace(payload.Language) ? "ko-KR" : payload.Language,
                        audio = payload.Audio
                    };

                    EvcUpdateResponse response = null;
                    for (var attempt = 0; ; attempt++)
                    {
                        try
                        {
                            response = await apiClient.SendSegmentAsync(request, cancellationToken);
                            break;
                        }
                        catch (EvcApiException exception)
                        {
                            EvcSafeDiagnostics.RequestFailed("update", requestId, expectedStep, exception);
                            if (exception.Kind == EvcErrorKind.Conflict && exception.ErrorCode == "step_conflict")
                            {
                                await sessionService.RestoreAsync(cancellationToken);
                                throw;
                            }

                            if (!exception.IsTransient || attempt >= maxTransientRetries)
                                throw;

                            await retryDelay(attempt, cancellationToken);
                        }
                    }

                    ValidateResponse(response, request);
                    context.ConfirmUpdate(response, expectedStep);
                    int commandCount =
                        response.commands != null
                            ? response.commands.Length
                            : 0;

                    UnityEngine.Debug.Log(
                        "[EVC] 청중 명령 수신" +
                        "\nStep: " + response.step +
                        "\n명령 개수: " + commandCount
                    );
                    commandSink?.HandleCommands(requestId, response.commands ?? Array.Empty<UnityCommandDto>());
                    succeeded = true;
                    return response;
                }
            }
            finally
            {
                stopwatch.Stop();
                int remaining;
                lock (gate)
                {
                    pendingCount = Math.Max(0, pendingCount - 1);
                    if (succeeded)
                        succeededCount++;
                    else
                        failedCount++;
                    lastProcessingMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                    remaining = pendingCount;
                }

                if (succeeded)
                    EvcSafeDiagnostics.SegmentProcessed(requestId, context.Step, remaining, stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private static void ValidatePayload(AudioSegmentPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (payload.Audio == null || !payload.Audio.IsValid)
                throw new ArgumentException("Segment audio is missing or empty.", nameof(payload));
            if (payload.ClientTimeSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(payload), "Client time cannot be negative.");
            if (!string.IsNullOrWhiteSpace(payload.UtterancePosition) &&
                !EvcContractRules.IsUtterancePosition(payload.UtterancePosition))
            {
                throw new ArgumentException("Unsupported utterance position.", nameof(payload));
            }
        }

        private static void ValidateResponse(EvcUpdateResponse response, EvcSegmentRequest request)
        {
            if (response == null ||
                response.session_id != request.session_id ||
                response.request_id != request.request_id ||
                response.step != request.expected_step + 1)
            {
                throw new EvcApiException(
                    EvcErrorKind.InvalidResponse,
                    "invalid_update",
                    200,
                    "EVC update 응답의 세션 또는 step이 올바르지 않습니다.");
            }

            if (response.commands == null)
                response.commands = Array.Empty<UnityCommandDto>();
        }

        private async Task IgnoreFailureAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    if (queueFailure == null)
                        queueFailure = exception;
                }
            }
        }

        private static async Task AwaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
        {
            if (task.IsCompleted)
            {
                await task;
                return;
            }

            var cancellationSignal = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancellationSignal.TrySetCanceled()))
            {
                var completed = await Task.WhenAny(task, cancellationSignal.Task);
                if (completed != task)
                    cancellationToken.ThrowIfCancellationRequested();
                await task;
            }
        }

        private static Task DefaultRetryDelayAsync(int attempt, CancellationToken cancellationToken)
        {
            var milliseconds = Math.Min(2000, 250 * (1 << Math.Min(attempt, 3)));
            return Task.Delay(milliseconds, cancellationToken);
        }

        private static async Task DisposeCancellationAfterQueueAsync(
            Task tail,
            CancellationTokenSource cancellation)
        {
            try
            {
                await tail;
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(EvcUpdateService));
        }
    }
}
