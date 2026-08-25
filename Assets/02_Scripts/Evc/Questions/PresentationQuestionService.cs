using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;
using Rehear.Evc.Transport;
using Rehear.Evc.Update;

namespace Rehear.Evc.Questions
{
    public interface IPresentationQuestionService
    {
        Task<IReadOnlyList<GeneratedQuestion>> GenerateAfterLastSegmentAsync(
            int count,
            CancellationToken cancellationToken);
        Task<IReadOnlyList<GeneratedQuestion>> RestoreAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<GeneratedQuestion>> RestoreOrGenerateAfterLastSegmentAsync(
            int count,
            bool presentationEndConfirmed,
            CancellationToken cancellationToken);
    }

    public sealed class PresentationQuestionService : IPresentationQuestionService
    {
        private readonly IEvcApiClient apiClient;
        private readonly PresentationSessionContext context;
        private readonly EvcUpdateService updateService;
        private readonly int maxTransientRetries;
        private readonly Func<int, CancellationToken, Task> retryDelay;
        private readonly SemaphoreSlim generationGate = new SemaphoreSlim(1, 1);
        private IReadOnlyList<GeneratedQuestion> cachedQuestions;
        private string generationRequestId;

        public PresentationQuestionService(
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

        public async Task<IReadOnlyList<GeneratedQuestion>> GenerateAfterLastSegmentAsync(
            int count,
            CancellationToken cancellationToken)
        {
            count = Math.Max(1, Math.Min(5, count));
            await generationGate.WaitAsync(cancellationToken);
            try
            {
                if (cachedQuestions != null)
                    return cachedQuestions;

                updateService.StopAccepting();
                await updateService.WhenIdleAsync(cancellationToken);
                EnsureSession();

                var requestId = generationRequestId ?? (generationRequestId = Guid.NewGuid().ToString("D"));
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        var response = await apiClient.GenerateQuestionsAsync(
                            context.SessionId,
                            context.SessionToken,
                            requestId,
                            count,
                            cancellationToken);
                        cachedQuestions = ValidateAndSort(response, count, context.SessionId);
                        return cachedQuestions;
                    }
                    catch (EvcApiException exception)
                    {
                        EvcSafeDiagnostics.RequestFailed("questions_generate", requestId, context.Step, exception);
                        if (!exception.IsTransient || attempt >= maxTransientRetries)
                            throw;

                        var restored = await TryRestoreGeneratedQuestionsAsync(cancellationToken);
                        if (restored != null)
                            return restored;

                        await retryDelay(attempt, cancellationToken);
                    }
                }
            }
            finally
            {
                generationGate.Release();
            }
        }

        public async Task<IReadOnlyList<GeneratedQuestion>> RestoreAsync(CancellationToken cancellationToken)
        {
            if (cachedQuestions != null)
                return cachedQuestions;

            EnsureSession();
            var response = await apiClient.GetQuestionsAsync(
                context.SessionId,
                context.SessionToken,
                cancellationToken);
            cachedQuestions = ValidateAndSort(response, null, context.SessionId);
            return cachedQuestions;
        }

        public async Task<IReadOnlyList<GeneratedQuestion>> RestoreOrGenerateAfterLastSegmentAsync(
            int count,
            bool presentationEndConfirmed,
            CancellationToken cancellationToken)
        {
            if (cachedQuestions != null)
                return cachedQuestions;

            updateService.StopAccepting();
            await updateService.WhenIdleAsync(cancellationToken);

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await RestoreAsync(cancellationToken);
                }
                catch (EvcApiException exception)
                {
                    var notGenerated = exception.Kind == EvcErrorKind.NotFound &&
                                       (string.IsNullOrWhiteSpace(exception.ErrorCode) ||
                                        exception.ErrorCode == "questions_not_generated");
                    if (notGenerated)
                    {
                        if (!presentationEndConfirmed)
                            throw;
                        return await GenerateAfterLastSegmentAsync(count, cancellationToken);
                    }

                    var generationInProgress = exception.Kind == EvcErrorKind.Conflict &&
                                               exception.ErrorCode == "questions_generating";
                    if ((!exception.IsTransient && !generationInProgress) || attempt >= maxTransientRetries)
                        throw;

                    await retryDelay(attempt, cancellationToken);
                }
            }
        }

        private async Task<IReadOnlyList<GeneratedQuestion>> TryRestoreGeneratedQuestionsAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await RestoreAsync(cancellationToken);
            }
            catch (EvcApiException exception)
            {
                if (exception.Kind == EvcErrorKind.NotFound &&
                    exception.ErrorCode == "questions_not_generated")
                    return null;
                if (exception.Kind == EvcErrorKind.Conflict &&
                    exception.ErrorCode == "questions_generating")
                    return null;
                if (exception.IsTransient)
                {
                    EvcSafeDiagnostics.RequestFailed(
                        "questions_restore",
                        generationRequestId,
                        context.Step,
                        exception);
                    return null;
                }
                throw;
            }
        }

        private void EnsureSession()
        {
            if (!context.HasEvcSession)
                throw new InvalidOperationException("EVC session has not started.");
        }

        private static IReadOnlyList<GeneratedQuestion> ValidateAndSort(
            QuestionListResponse response,
            int? expectedCount,
            string expectedSessionId)
        {
            if (response == null ||
                response.session_id != expectedSessionId ||
                response.status != "ready" ||
                response.questions == null)
            {
                throw new EvcApiException(
                    EvcErrorKind.InvalidResponse,
                    "invalid_questions",
                    200,
                    "질문 응답에 질문 목록이 없습니다.");
            }

            if (expectedCount.HasValue && response.questions.Length != expectedCount.Value)
            {
                throw new EvcApiException(
                    EvcErrorKind.InvalidResponse,
                    "question_count_mismatch",
                    200,
                    "서버 질문 개수가 요청과 다릅니다.");
            }

            var questions = new List<GeneratedQuestion>(response.questions);
            questions.Sort((left, right) => left.order.CompareTo(right.order));
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenTexts = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < questions.Count; index++)
            {
                var item = questions[index];
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.id) ||
                    string.IsNullOrWhiteSpace(item.question) ||
                    item.order != index + 1 ||
                    item.id != "q" + (index + 1) ||
                    item.source_steps == null ||
                    item.source_steps.Length == 0 ||
                    HasInvalidSourceStep(item.source_steps) ||
                    !seenIds.Add(item.id) ||
                    !seenTexts.Add(item.question))
                {
                    throw new EvcApiException(
                        EvcErrorKind.InvalidResponse,
                        "invalid_question_item",
                        200,
                        "서버 질문의 id/order/question이 올바르지 않습니다.");
                }
            }

            return questions;
        }

        private static bool HasInvalidSourceStep(int[] sourceSteps)
        {
            for (var index = 0; index < sourceSteps.Length; index++)
            {
                if (sourceSteps[index] <= 0)
                    return true;
            }
            return false;
        }

        private static Task DefaultRetryDelayAsync(int attempt, CancellationToken cancellationToken)
        {
            var milliseconds = Math.Min(2000, 250 * (1 << Math.Min(attempt, 3)));
            return Task.Delay(milliseconds, cancellationToken);
        }
    }
}
