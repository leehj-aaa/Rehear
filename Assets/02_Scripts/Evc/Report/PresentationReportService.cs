using System;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;
using Rehear.Evc.Transport;
using Rehear.Evc.Update;

namespace Rehear.Evc.Report
{
    public sealed class PresentationReportService
    {
        private readonly IEvcApiClient apiClient;
        private readonly PresentationSessionContext context;
        private readonly EvcUpdateService updateService;
        private readonly int maxTransientRetries;
        private readonly Func<int, CancellationToken, Task> retryDelay;
        private readonly SemaphoreSlim finishGate = new SemaphoreSlim(1, 1);
        private string finishRequestId;
        private int finishPlannedSeconds;
        private int finishQaSeconds;
        private ReportFeedback cachedReport;

        public PresentationReportService(
            IEvcApiClient apiClient,
            PresentationSessionContext context,
            EvcUpdateService updateService,
            int maxTransientRetries = 2,
            Func<int, CancellationToken, Task> retryDelay = null)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            this.maxTransientRetries = Math.Max(0, maxTransientRetries);
            this.retryDelay = retryDelay ?? DefaultRetryDelayAsync;
        }

        public async Task<ReportFeedback> FinishAfterLastSegmentAsync(
            int plannedSeconds,
            int qaSeconds,
            CancellationToken cancellationToken)
        {
            ValidateSeconds(plannedSeconds, nameof(plannedSeconds));
            ValidateSeconds(qaSeconds, nameof(qaSeconds));
            await finishGate.WaitAsync(cancellationToken);
            try
            {
                if (cachedReport != null)
                    return cachedReport;

                updateService.StopAccepting();
                await updateService.WhenIdleAsync(cancellationToken);
                EnsureSession();
                if (finishRequestId == null)
                {
                    finishRequestId = Guid.NewGuid().ToString("D");
                    finishPlannedSeconds = plannedSeconds;
                    finishQaSeconds = qaSeconds;
                }
                else if (finishPlannedSeconds != plannedSeconds || finishQaSeconds != qaSeconds)
                {
                    throw new InvalidOperationException(
                        "A finish retry must use the original planned and Q&A durations.");
                }

                var requestId = finishRequestId;

                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        var response = await apiClient.FinishSessionAsync(
                            context.SessionId,
                            context.SessionToken,
                            requestId,
                            plannedSeconds,
                            qaSeconds,
                            cancellationToken);
                        cachedReport = response.report;
                        return cachedReport;
                    }
                    catch (EvcApiException exception)
                    {
                        EvcSafeDiagnostics.RequestFailed("finish", requestId, context.Step, exception);
                        if (!exception.IsTransient || attempt >= maxTransientRetries)
                            throw;

                        var restored = await TryRestoreReadyReportAsync(cancellationToken);
                        if (restored != null)
                            return restored;

                        await retryDelay(attempt, cancellationToken);
                    }
                }
            }
            finally
            {
                finishGate.Release();
            }
        }

        public async Task<ReportFeedback> RestoreAsync(CancellationToken cancellationToken)
        {
            if (cachedReport != null)
                return cachedReport;
            EnsureSession();
            var response = await apiClient.GetReportAsync(
                context.SessionId,
                context.SessionToken,
                cancellationToken);
            if (response.status != "ready" || response.report == null)
            {
                throw new EvcApiException(
                    EvcErrorKind.Conflict,
                    "report_" + response.status,
                    409,
                    "EVC 보고서가 아직 준비되지 않았습니다.");
            }

            cachedReport = response.report;
            return cachedReport;
        }

        private async Task<ReportFeedback> TryRestoreReadyReportAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await apiClient.GetReportAsync(
                    context.SessionId,
                    context.SessionToken,
                    cancellationToken);
                if (response.status != "ready" || response.report == null)
                    return null;
                cachedReport = response.report;
                return cachedReport;
            }
            catch (EvcApiException exception)
            {
                if (exception.Kind == EvcErrorKind.NotFound || exception.Kind == EvcErrorKind.Conflict ||
                    exception.IsTransient)
                    return null;
                throw;
            }
        }

        private void EnsureSession()
        {
            if (!context.HasEvcSession)
                throw new InvalidOperationException("EVC session has not started.");
        }

        private static void ValidateSeconds(int value, string parameterName)
        {
            if (value < 0 || value > 86400)
                throw new ArgumentOutOfRangeException(parameterName, "Report seconds must be between 0 and 86400.");
        }

        private static Task DefaultRetryDelayAsync(int attempt, CancellationToken cancellationToken)
        {
            var milliseconds = Math.Min(2000, 250 * (1 << Math.Min(attempt, 3)));
            return Task.Delay(milliseconds, cancellationToken);
        }
    }
}
