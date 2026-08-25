using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rehear.Evc.Audience;
using Rehear.Evc.Audio;
using Rehear.Evc.Contracts;
using Rehear.Evc.Data;
using Rehear.Evc.Presentation;
using Rehear.Evc.Questions;
using Rehear.Evc.Report;
using Rehear.Evc.Session;
using Rehear.Evc.Transport;
using Rehear.Evc.Update;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rehear.Evc.Tests
{
    public sealed class EvcPipelineTests
    {
        private PresentationSessionContext context;

        [SetUp]
        public void SetUp()
        {
            context = PresentationSessionContext.Current;
            context.ClearAll();
            var validation = context.BeginPresentation(CreatePresentation());
            Assert.That(validation.IsValid, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            context.ClearAll();
        }

        [Test]
        public void PresentationValidation_RequiresExplicitTitleAndPages()
        {
            var data = CreatePresentation();
            data.presentation_title = string.Empty;
            Assert.That(PresentationDataValidator.Validate(data).IsValid, Is.False);
        }

        [TestCase("12A4")]
        [TestCase("１２３４")]
        [TestCase("123")]
        [TestCase("12345")]
        public void PresentationValidation_RequiresFourAsciiDigits(string pin)
        {
            var data = CreatePresentation();
            data.pin = pin;
            Assert.That(PresentationDataValidator.Validate(data).IsValid, Is.False);
        }

        [Test]
        public void FirebaseValueMapper_MapsCompleteInputAndDefaultsQuestionCount()
        {
            var input = CreateFirebaseValue();
            var page1 = (Dictionary<string, object>)input["page_1"];
            page1.Remove("qa_count");

            var data = FirebasePresentationValueMapper.Map(input, "1234", out var errors);

            Assert.That(errors, Is.Empty);
            Assert.That(PresentationDataValidator.Validate(data).IsValid, Is.True);
            Assert.That(data.page_1.qa_count, Is.EqualTo(3));
            Assert.That(data.page_2.slide_image.image_urls, Is.EqualTo(new[]
            {
                "https://example.test/slide-1.png",
                "https://example.test/slide-2.png"
            }));
        }

        [Test]
        public void FirebaseValueMapper_ReportsMissingPageAndWrongTypes()
        {
            var input = CreateFirebaseValue();
            input.Remove("page_3");
            input["presentation_title"] = 123L;
            ((Dictionary<string, object>)input["page_1"])["duration_minutes"] = "5";

            var data = FirebasePresentationValueMapper.Map(input, "1234", out var errors);
            var validation = PresentationDataValidator.Validate(data);

            Assert.That(errors, Has.Some.Contains("page_3"));
            Assert.That(errors, Has.Some.Contains("presentation_title"));
            Assert.That(errors, Has.Some.Contains("duration_minutes"));
            Assert.That(validation.IsValid, Is.False);
        }

        [Test]
        public void PresentationLoadResult_RepresentsMissingPinWithoutData()
        {
            var result = PresentationLoadResult.Error(
                PresentationLoadStatus.NotFound,
                "존재하지 않는 PIN 번호입니다.");
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Status, Is.EqualTo(PresentationLoadStatus.NotFound));
            Assert.That(result.Data, Is.Null);
        }

        [TestCase(401, EvcErrorKind.Unauthorized)]
        [TestCase(404, EvcErrorKind.NotFound)]
        [TestCase(409, EvcErrorKind.Conflict)]
        [TestCase(413, EvcErrorKind.PayloadTooLarge)]
        [TestCase(415, EvcErrorKind.UnsupportedMediaType)]
        [TestCase(422, EvcErrorKind.Validation)]
        [TestCase(429, EvcErrorKind.RateLimited)]
        [TestCase(502, EvcErrorKind.ProviderUnavailable)]
        [TestCase(500, EvcErrorKind.ServerError)]
        [TestCase(503, EvcErrorKind.ServerError)]
        public void HttpStatus_MapsToDomainError(long status, EvcErrorKind expected)
        {
            Assert.That(EvcErrorMapper.Map(status, false, false), Is.EqualTo(expected));
        }

        [Test]
        public void Timeout_IsDistinctFromOtherConnectionErrors()
        {
            Assert.That(EvcErrorMapper.Map(0, true, true), Is.EqualTo(EvcErrorKind.Timeout));
            Assert.That(EvcErrorMapper.Map(0, true, false), Is.EqualTo(EvcErrorKind.Network));
        }

        [Test]
        public void ServerErrors_AreTransient()
        {
            var exception = new EvcApiException(EvcErrorKind.ServerError, "server_error", 503, "safe");
            Assert.That(exception.IsTransient, Is.True);
        }

        [Test]
        public void EnvironmentConfig_RejectsNonHttpUrl()
        {
            var config = ScriptableObject.CreateInstance<Rehear.Evc.Config.EvcEnvironmentConfig>();
            var serialized = new SerializedObject(config);
            serialized.FindProperty("serverBaseUrl").stringValue = "file:///tmp/evc";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(config.TryValidate(out var error), Is.False);
            Assert.That(error, Does.Contain("HTTP"));
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void PresentationClock_ExcludesPausedTimeAndIsMonotonic()
        {
            var now = 10d;
            var clock = new PresentationClock(() => now);
            clock.Start();
            now = 12d;
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(2d));
            clock.Pause();
            now = 20d;
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(2d));
            clock.Resume();
            now = 21.5d;
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(3.5d));
        }

        [Test]
        public void WavEncoder_WritesPcm16RiffHeader()
        {
            var bytes = WavEncoder.EncodePcm16(new[] { -1f, 0f, 1f }, 1, 16000);
            Assert.That(bytes.Length, Is.EqualTo(50));
            Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("WAVE"));
        }

        [Test]
        public void CommandFixture_DeserializesAllRoutingFields()
        {
            const string json = "{\"session_id\":\"s\",\"request_id\":\"r\",\"step\":1,\"commands\":[{\"agent_id\":\"audience_01\",\"start_time\":2.5,\"layer\":\"Face\",\"action_id\":\"face.smile\",\"duration\":1.5,\"sync_group\":\"g\",\"selected_behavior_id\":\"b\",\"selected_variation_id\":\"v\",\"priority\":100,\"blend_mode\":\"additive\",\"intensity\":0.8}]}";
            var response = JsonUtility.FromJson<EvcUpdateResponse>(json);
            Assert.That(response.commands, Has.Length.EqualTo(1));
            Assert.That(response.commands[0].agent_id, Is.EqualTo("audience_01"));
            Assert.That(response.commands[0].priority, Is.EqualTo(100));
            Assert.That(response.commands[0].blend_mode, Is.EqualTo("additive"));
            Assert.That(response.commands[0].intensity, Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void SanitizedFixtures_DeserializeRequiredContracts()
        {
            var startJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Tests/Fixtures/smart-start-response.json");
            var updateJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Tests/Fixtures/update-response.json");
            var questionsJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Tests/Fixtures/questions-response.json");
            var reportJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Tests/Fixtures/report-finish-response.json");
            Assert.That(startJson, Is.Not.Null);
            Assert.That(updateJson, Is.Not.Null);
            Assert.That(questionsJson, Is.Not.Null);
            Assert.That(reportJson, Is.Not.Null);

            var start = JsonUtility.FromJson<SmartStartResponse>(startJson.text);
            var update = JsonUtility.FromJson<EvcUpdateResponse>(updateJson.text);
            var questions = JsonUtility.FromJson<QuestionListResponse>(questionsJson.text);
            var report = JsonUtility.FromJson<ReportFinishResponse>(reportJson.text);
            Assert.That(EvcContractRules.ValidateAudienceSet(start.audiences), Is.Empty);
            Assert.That(update.commands, Has.Length.EqualTo(1));
            Assert.That(questions.questions, Has.Length.EqualTo(1));
            Assert.That(report.status, Is.EqualTo("ready"));
            Assert.That(report.report.score.overall_score, Is.EqualTo(82));
            Assert.That(report.report.audience_analysis.graph, Has.Length.EqualTo(1));
            Assert.That(ClipPoolActionIdExtractor.Extract(updateJson.text), Is.EquivalentTo(new[] { "face.fixture_smile" }));
        }

        [Test]
        public void ReportOpenApiSnapshot_ContainsRequiredContractFields()
        {
            var snapshot = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Tests/Fixtures/evc-report-openapi-contract.json");
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.text, Does.Contain("/odi/xreal_rehear/evc/sessions/{session_id}/finish"));
            Assert.That(snapshot.text, Does.Contain("\"ReportFinishRequest\""));
            Assert.That(snapshot.text, Does.Contain("\"request_id\""));
            Assert.That(snapshot.text, Does.Contain("\"ReportStatusResponse\""));
            Assert.That(snapshot.text, Does.Contain("\"ReportFeedback\""));
            Assert.That(snapshot.text, Does.Not.Contain("session_token"));
        }

        [Test]
        public void Diagnostics_DoNotLogUntrustedErrorDetails()
        {
            string captured = null;
            Application.LogCallback callback = (condition, trace, type) =>
            {
                if (condition.StartsWith("EVC request failed", StringComparison.Ordinal))
                    captured = condition;
            };
            Application.logMessageReceived += callback;
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("^EVC request failed.*code=unrecognized$"));
                EvcSafeDiagnostics.RequestFailed(
                    "update",
                    "00000000-0000-0000-0000-000000000001",
                    2,
                    new EvcApiException(
                        EvcErrorKind.Validation,
                        "secret-token transcript contents",
                        422,
                        "safe"));
            }
            finally
            {
                Application.logMessageReceived -= callback;
            }

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured, Does.Not.Contain("secret-token"));
            Assert.That(captured, Does.Not.Contain("transcript contents"));
        }

        [Test]
        public async Task SessionStart_StoresSixAgentsAndStepOnce()
        {
            var fake = new FakeEvcApiClient { StartResponse = CreateStartResponse() };
            var service = new EvcSessionService(fake, context);
            await service.StartAsync(null, 42, CancellationToken.None);
            Assert.That(context.HasEvcSession, Is.True);
            Assert.That(context.Step, Is.Zero);
            Assert.That(context.Seed, Is.EqualTo(7));
            Assert.That(fake.LastStartRequest.presentation_title, Is.EqualTo("Test Presentation"));
        }

        [Test]
        public void SessionStart_RejectsDuplicateAgent()
        {
            var fake = new FakeEvcApiClient { StartResponse = CreateStartResponse() };
            fake.StartResponse.audiences[5].agent_id = "audience_01";
            var service = new EvcSessionService(fake, context);
            Assert.ThrowsAsync<EvcApiException>(() => service.StartAsync(null, null, CancellationToken.None));
            Assert.That(context.HasEvcSession, Is.False);
        }

        [Test]
        public void SessionStart_FailureDoesNotMutateContext()
        {
            var fake = new FakeEvcApiClient
            {
                StartException = new EvcApiException(EvcErrorKind.Unauthorized, "invalid_session_token", 401, "safe")
            };
            var service = new EvcSessionService(fake, context);
            Assert.ThrowsAsync<EvcApiException>(() => service.StartAsync(null, null, CancellationToken.None));
            Assert.That(context.HasEvcSession, Is.False);
            Assert.That(context.Step, Is.Zero);
        }

        [Test]
        public void BeginningNewPresentation_ClearsPreviousEvcSession()
        {
            context.ApplySmartStart(CreateStartResponse());
            var replacement = CreatePresentation();
            replacement.pin = "5678";

            Assert.That(context.BeginPresentation(replacement).IsValid, Is.True);
            Assert.That(context.HasEvcSession, Is.False);
            Assert.That(context.Presentation.pin, Is.EqualTo("5678"));
        }

        [Test]
        public async Task UpdateQueue_IsSerialAndAdvancesStepAfterSuccess()
        {
            context.ApplySmartStart(CreateStartResponse());
            var releaseFirst = new TaskCompletionSource<bool>();
            var fake = new FakeEvcApiClient();
            fake.SegmentHandler = async request =>
            {
                if (request.expected_step == 0) await releaseFirst.Task;
                return new EvcUpdateResponse
                {
                    session_id = request.session_id,
                    request_id = request.request_id,
                    step = request.expected_step + 1,
                    commands = Array.Empty<UnityCommandDto>()
                };
            };
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var first = updates.EnqueueAsync(CreateSegment(1d), CancellationToken.None);
                var second = updates.EnqueueAsync(CreateSegment(2d), CancellationToken.None);
                await Task.Yield();
                Assert.That(fake.MaxConcurrentSegments, Is.EqualTo(1));
                releaseFirst.SetResult(true);
                await Task.WhenAll(first, second);
                Assert.That(fake.SegmentRequests[0].expected_step, Is.Zero);
                Assert.That(fake.SegmentRequests[1].expected_step, Is.EqualTo(1));
                Assert.That(context.Step, Is.EqualTo(2));
                Assert.That(fake.MaxConcurrentSegments, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task UpdateRetry_PreservesRequestIdAndExpectedStep()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient();
            var calls = 0;
            fake.SegmentHandler = request =>
            {
                calls++;
                if (calls == 1)
                    throw new EvcApiException(EvcErrorKind.Timeout, "timeout", 0, "timeout");
                return Task.FromResult(new EvcUpdateResponse
                {
                    session_id = request.session_id,
                    request_id = request.request_id,
                    step = 1,
                    commands = Array.Empty<UnityCommandDto>()
                });
            };
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(
                       fake,
                       context,
                       session,
                       null,
                       1,
                       (attempt, token) => Task.CompletedTask))
            {
                await updates.EnqueueAsync(CreateSegment(1d), CancellationToken.None);
                Assert.That(fake.SegmentRequests, Has.Count.EqualTo(2));
                Assert.That(fake.SegmentRequests[1].request_id, Is.EqualTo(fake.SegmentRequests[0].request_id));
                Assert.That(fake.SegmentRequests[1].expected_step, Is.EqualTo(fake.SegmentRequests[0].expected_step));
                Assert.That(context.Step, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task UpdateQueue_TwentyConcurrentEnqueuesRemainSerialAndObservable()
        {
            context.ApplySmartStart(CreateStartResponse());
            var releaseFirst = new TaskCompletionSource<bool>();
            var fake = new FakeEvcApiClient();
            fake.SegmentHandler = async request =>
            {
                if (request.expected_step == 0)
                    await releaseFirst.Task;
                return SuccessfulUpdate(request);
            };
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var tasks = new List<Task<EvcUpdateResponse>>();
                for (var index = 0; index < 20; index++)
                    tasks.Add(updates.EnqueueAsync(CreateSegment(index + 1d), CancellationToken.None));

                await Task.Yield();
                Assert.That(updates.PendingCount, Is.EqualTo(20));
                Assert.That(fake.MaxConcurrentSegments, Is.EqualTo(1));

                releaseFirst.SetResult(true);
                await Task.WhenAll(tasks);
                await updates.WhenIdleAsync(CancellationToken.None);

                Assert.That(fake.MaxConcurrentSegments, Is.EqualTo(1));
                Assert.That(fake.SegmentRequests, Has.Count.EqualTo(20));
                Assert.That(updates.PendingCount, Is.Zero);
                Assert.That(updates.SucceededCount, Is.EqualTo(20));
                Assert.That(updates.FailedCount, Is.Zero);
                Assert.That(updates.LastProcessingMilliseconds, Is.GreaterThanOrEqualTo(0d));
                Assert.That(context.Step, Is.EqualTo(20));
            }
        }

        [Test]
        public void Update_InvalidResponseDoesNotAdvanceStep()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient
            {
                SegmentHandler = request => Task.FromResult(new EvcUpdateResponse
                {
                    session_id = request.session_id,
                    request_id = "different-request",
                    step = request.expected_step + 1,
                    commands = Array.Empty<UnityCommandDto>()
                })
            };
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                Assert.ThrowsAsync<EvcApiException>(() =>
                    updates.EnqueueAsync(CreateSegment(1d), CancellationToken.None));
                Assert.That(context.Step, Is.Zero);
                Assert.That(updates.FailedCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task Update_ClampsSlideIndexAndRejectsDecreasingClientTime()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient();
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var first = CreateSegment(2d);
                first.SlideIndex = 999;
                await updates.EnqueueAsync(first, CancellationToken.None);
                Assert.That(fake.SegmentRequests[0].current_slide_index, Is.EqualTo(2));

                Assert.ThrowsAsync<InvalidOperationException>(() =>
                    updates.EnqueueAsync(CreateSegment(1d), CancellationToken.None));
                Assert.That(context.Step, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task Update_EmptyCommandsIsSuccessfulNoOp()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient();
            var sink = new RecordingCommandSink();
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, sink, 0))
            {
                await updates.EnqueueAsync(CreateSegment(1d), CancellationToken.None);
                Assert.That(sink.CallCount, Is.EqualTo(1));
                Assert.That(sink.LastCommandCount, Is.Zero);
                Assert.That(context.Step, Is.EqualTo(1));
            }
        }

        [Test]
        public void Update_StepConflictRestoresServerStateWithoutAutomaticReplay()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient
            {
                SessionResponse = new SessionResponse
                {
                    session_id = "session",
                    seed = 7,
                    step = 5,
                    slide_count = 3,
                    status = "active",
                    audiences = CreateStartResponse().audiences
                },
                SegmentHandler = request => Task.FromException<EvcUpdateResponse>(
                    new EvcApiException(EvcErrorKind.Conflict, "step_conflict", 409, "safe"))
            };
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 2,
                       (attempt, token) => Task.CompletedTask))
            {
                Assert.ThrowsAsync<EvcApiException>(() =>
                    updates.EnqueueAsync(CreateSegment(1d), CancellationToken.None));
                Assert.That(fake.SegmentRequests, Has.Count.EqualTo(1));
                Assert.That(fake.GetSessionCalls, Is.EqualTo(1));
                Assert.That(context.Step, Is.EqualTo(5));
            }
        }

        [Test]
        public async Task Questions_WaitForLastUpdateAndSortByOrder()
        {
            context.ApplySmartStart(CreateStartResponse());
            var pending = new TaskCompletionSource<bool>();
            var fake = new FakeEvcApiClient
            {
                QuestionsResponse = new QuestionListResponse
                {
                    session_id = "session",
                    status = "ready",
                    questions = new[]
                    {
                        new GeneratedQuestion { id = "q2", order = 2, question = "Second", source_steps = new[] { 1 } },
                        new GeneratedQuestion { id = "q1", order = 1, question = "First", source_steps = new[] { 1 } }
                    }
                }
            };
            fake.SegmentHandler = async request =>
            {
                await pending.Task;
                return new EvcUpdateResponse
                {
                    session_id = request.session_id,
                    request_id = request.request_id,
                    step = 1,
                    commands = Array.Empty<UnityCommandDto>()
                };
            };
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var update = updates.EnqueueAsync(CreateSegment(1d), CancellationToken.None);
                var questions = new PresentationQuestionService(fake, context, updates, 0);
                var generate = questions.GenerateAfterLastSegmentAsync(2, CancellationToken.None);
                await Task.Yield();
                Assert.That(fake.GenerateQuestionCalls, Is.Zero);
                pending.SetResult(true);
                await update;
                var result = await generate;
                Assert.That(fake.GenerateQuestionCalls, Is.EqualTo(1));
                Assert.That(result[0].id, Is.EqualTo("q1"));
                Assert.That(result[1].id, Is.EqualTo("q2"));
            }
        }

        [Test]
        public async Task Questions_RestoreReadyResultDoesNotGenerateAgain()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient { QuestionsResponse = CreateQuestionResponse(2) };
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var service = new PresentationQuestionService(fake, context, updates, 1,
                    (attempt, token) => Task.CompletedTask);
                var restored = await service.RestoreOrGenerateAfterLastSegmentAsync(
                    2, true, CancellationToken.None);

                Assert.That(restored, Has.Count.EqualTo(2));
                Assert.That(fake.GetQuestionCalls, Is.EqualTo(1));
                Assert.That(fake.GenerateQuestionCalls, Is.Zero);
            }
        }

        [Test]
        public async Task Questions_NotGeneratedAfterConfirmedEndGeneratesOnce()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient { QuestionsResponse = CreateQuestionResponse(2) };
            fake.GetQuestionFailures.Enqueue(new EvcApiException(
                EvcErrorKind.NotFound, "questions_not_generated", 404, "safe"));
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var service = new PresentationQuestionService(fake, context, updates, 1,
                    (attempt, token) => Task.CompletedTask);
                var generated = await service.RestoreOrGenerateAfterLastSegmentAsync(
                    2, true, CancellationToken.None);
                var cached = await service.GenerateAfterLastSegmentAsync(2, CancellationToken.None);

                Assert.That(generated, Is.SameAs(cached));
                Assert.That(fake.GenerateQuestionCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public void Questions_NotGeneratedWithoutEndIntentDoesNotGenerate()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient { QuestionsResponse = CreateQuestionResponse(2) };
            fake.GetQuestionFailures.Enqueue(new EvcApiException(
                EvcErrorKind.NotFound, "questions_not_generated", 404, "safe"));
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var service = new PresentationQuestionService(fake, context, updates, 0);
                Assert.ThrowsAsync<EvcApiException>(() => service.RestoreOrGenerateAfterLastSegmentAsync(
                    2, false, CancellationToken.None));
                Assert.That(fake.GenerateQuestionCalls, Is.Zero);
            }
        }

        [Test]
        public async Task Questions_UserRetryReusesGenerationRequestId()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient { QuestionsResponse = CreateQuestionResponse(1) };
            fake.GenerateQuestionFailures.Enqueue(new EvcApiException(
                EvcErrorKind.ProviderUnavailable,
                "question_generation_provider_error",
                502,
                "safe"));
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var service = new PresentationQuestionService(fake, context, updates, 0);
                Assert.ThrowsAsync<EvcApiException>(() =>
                    service.GenerateAfterLastSegmentAsync(1, CancellationToken.None));
                var result = await service.GenerateAfterLastSegmentAsync(1, CancellationToken.None);

                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(fake.QuestionRequestIds, Has.Count.EqualTo(2));
                Assert.That(fake.QuestionRequestIds[1], Is.EqualTo(fake.QuestionRequestIds[0]));
            }
        }

        [Test]
        public async Task Report_TransientFailureRetriesWithSameRequestId()
        {
            context.ApplySmartStart(CreateStartResponse());
            var fake = new FakeEvcApiClient
            {
                FinishResponse = CreateFinishResponse(),
                ReportStatusResponse = new ReportStatusResponse
                {
                    session_id = "session",
                    status = "generating"
                }
            };
            fake.FinishFailures.Enqueue(new EvcApiException(
                EvcErrorKind.Timeout, "timeout", 0, "safe"));
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var service = new PresentationReportService(fake, context, updates, 1,
                    (attempt, token) => Task.CompletedTask);
                var report = await service.FinishAfterLastSegmentAsync(300, 60, CancellationToken.None);

                Assert.That(report.score.overall_score, Is.EqualTo(82));
                Assert.That(fake.FinishRequestIds, Has.Count.EqualTo(2));
                Assert.That(fake.FinishRequestIds[1], Is.EqualTo(fake.FinishRequestIds[0]));
                Assert.That(Guid.TryParseExact(fake.FinishRequestIds[0], "D", out _), Is.True);
                Assert.That(fake.GetReportCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task Report_TimeoutRecoversReadyReportWithoutSecondFinish()
        {
            context.ApplySmartStart(CreateStartResponse());
            var ready = CreateFinishResponse();
            var fake = new FakeEvcApiClient
            {
                FinishResponse = ready,
                ReportStatusResponse = new ReportStatusResponse
                {
                    session_id = "session",
                    status = "ready",
                    report = ready.report
                }
            };
            fake.FinishFailures.Enqueue(new EvcApiException(
                EvcErrorKind.Network, "network", 0, "safe"));
            var session = new EvcSessionService(fake, context);
            using (var updates = new EvcUpdateService(fake, context, session, null, 0))
            {
                var service = new PresentationReportService(fake, context, updates, 2,
                    (attempt, token) => Task.CompletedTask);
                var report = await service.FinishAfterLastSegmentAsync(300, 60, CancellationToken.None);

                Assert.That(report, Is.SameAs(ready.report));
                Assert.That(fake.FinishCalls, Is.EqualTo(1));
                Assert.That(fake.GetReportCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public void AudienceAgent_RoutesKnownActionAndRejectsUnknownAction()
        {
            var registry = ScriptableObject.CreateInstance<AudienceActionRegistry>();
            SetPrivateField(registry, "actions", new[]
            {
                new AudienceActionDefinition
                {
                    actionId = "body.test",
                    layer = "Body",
                    animatorStateName = "TestState"
                }
            });
            var gameObject = new GameObject("Agent");
            var player = gameObject.AddComponent<FakeActionPlayer>();
            var agent = gameObject.AddComponent<AudienceAgent>();
            agent.Configure("audience_01", registry);

            var command = new UnityCommandDto
            {
                agent_id = "audience_01",
                layer = "Body",
                action_id = "body.test",
                blend_mode = "override",
                duration = 1f,
                intensity = 1f
            };
            Assert.That(agent.TryExecute(command, out var reason), Is.True, reason);
            Assert.That(player.PlayCount, Is.EqualTo(1));

            command.action_id = "body.unknown";
            Assert.That(agent.TryExecute(command, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("unknown_action"));
            Assert.That(player.PlayCount, Is.EqualTo(1));

            UnityEngine.Object.DestroyImmediate(gameObject);
            UnityEngine.Object.DestroyImmediate(registry);
        }

        [Test]
        public void QuestionPresenter_StopsAtLastQuestion()
        {
            var gameObject = new GameObject("QuestionPresenter");
            var presenter = gameObject.AddComponent<Rehear.Evc.UI.QuestionPresenter>();
            presenter.ShowReady(CreateQuestionResponse(2).questions);

            Assert.That(presenter.CurrentIndex, Is.Zero);
            Assert.That(presenter.MoveNext(), Is.True);
            Assert.That(presenter.CurrentIndex, Is.EqualTo(1));
            Assert.That(presenter.MoveNext(), Is.False);
            Assert.That(presenter.CurrentIndex, Is.EqualTo(1));

            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        private static PresentationDataDto CreatePresentation()
        {
            return new PresentationDataDto
            {
                pin = "1234",
                presentation_title = "Test Presentation",
                page_1 = new PresentationPage1Dto { duration_minutes = 5, environment_type = "office", qa_count = 3 },
                page_2 = new PresentationPage2Dto
                {
                    presentation_script_content = "script",
                    slide_image = new SlideImageDto { image_urls = Array.Empty<string>() }
                },
                page_3 = new PresentationPage3Dto
                {
                    audience_expertise = "middle",
                    audience_interest = "high",
                    audience_scale = 6
                }
            };
        }

        private static Dictionary<string, object> CreateFirebaseValue()
        {
            return new Dictionary<string, object>
            {
                { "presentation_title", "Test Presentation" },
                { "page_1", new Dictionary<string, object>
                    {
                        { "duration_minutes", 5L },
                        { "environment_type", "office" },
                        { "qa_count", 3L }
                    }
                },
                { "page_2", new Dictionary<string, object>
                    {
                        { "presentation_script_content", "script" },
                        { "slide_image", new Dictionary<string, object>
                            {
                                { "image_urls", new Dictionary<string, object>
                                    {
                                        { "1", "https://example.test/slide-2.png" },
                                        { "0", "https://example.test/slide-1.png" }
                                    }
                                }
                            }
                        }
                    }
                },
                { "page_3", new Dictionary<string, object>
                    {
                        { "audience_expertise", "middle" },
                        { "audience_interest", "high" },
                        { "audience_scale", 6L }
                    }
                }
            };
        }

        private static SmartStartResponse CreateStartResponse()
        {
            var audiences = new AudienceDto[6];
            for (var index = 0; index < audiences.Length; index++)
                audiences[index] = new AudienceDto { agent_id = "audience_0" + (index + 1) };
            return new SmartStartResponse
            {
                session_id = "session",
                session_token = "secret-token",
                seed = 7,
                step = 0,
                slide_count = 3,
                status = "active",
                audiences = audiences
            };
        }

        private static AudioSegmentPayload CreateSegment(double clientTime)
        {
            return new AudioSegmentPayload
            {
                Audio = new BinaryFileDto { bytes = new byte[] { 1, 2 }, file_name = "audio.wav", mime_type = "audio/wav" },
                ClientTimeSeconds = clientTime,
                SlideIndex = 0,
                UtterancePosition = "during_speech"
            };
        }

        private static EvcUpdateResponse SuccessfulUpdate(EvcSegmentRequest request)
        {
            return new EvcUpdateResponse
            {
                session_id = request.session_id,
                request_id = request.request_id,
                step = request.expected_step + 1,
                commands = Array.Empty<UnityCommandDto>()
            };
        }

        private static QuestionListResponse CreateQuestionResponse(int count)
        {
            var questions = new GeneratedQuestion[count];
            for (var index = 0; index < count; index++)
            {
                questions[index] = new GeneratedQuestion
                {
                    id = "q" + (index + 1),
                    order = index + 1,
                    question = "Question " + (index + 1),
                    intent = "intent",
                    source_steps = new[] { 1 }
                };
            }

            return new QuestionListResponse
            {
                session_id = "session",
                status = "ready",
                questions = questions
            };
        }

        private static ReportFinishResponse CreateFinishResponse()
        {
            return new ReportFinishResponse
            {
                session_id = "session",
                status = "ready",
                generated_at = "2026-08-25T12:00:00Z",
                report = new ReportFeedback
                {
                    version = "presentation-report-v1",
                    generation = new ReportGenerationMetadata
                    {
                        generated_at = "2026-08-25T12:00:00Z",
                        generator = "test",
                        source_segment_count = 1,
                        transcript_word_count = 10
                    },
                    score = new ReportScore { overall_score = 82, grade = "B" },
                    duration = new ReportDuration { planned_seconds = 300, actual_seconds = 280, qa_seconds = 60 },
                    score_card = new ReportScoreCard
                    {
                        scores = new ReportScoreCardValues { engagement = 80, clarity = 82, credibility = 84 },
                        descriptions = new ReportScoreCardDescriptions
                        {
                            engagement = "good",
                            clarity = "good",
                            credibility = "good"
                        }
                    },
                    detail_analysis = new DetailAnalysis
                    {
                        content_analysis = new ReportIntegerMap(),
                        delivery_analysis = new ReportIntegerMap()
                    },
                    audience_analysis = new AudienceAnalysis(),
                    ai_insight = new AIInsight { title = "Insight", description = "Description" }
                }
            };
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing field " + name);
            field.SetValue(target, value);
        }

        private sealed class FakeActionPlayer : MonoBehaviour, IActionPlayer
        {
            public string Layer => "Body";
            public int PlayCount { get; private set; }

            public bool CanPlay(AudienceActionDefinition action, string blendMode)
            {
                return action != null && action.layer == Layer && blendMode == "override";
            }

            public void Play(AudienceActionDefinition action, UnityCommandDto command)
            {
                PlayCount++;
            }

            public void StopAndRestoreBaseline()
            {
            }
        }

        private sealed class RecordingCommandSink : IAudienceCommandSink
        {
            public int CallCount;
            public int LastCommandCount;

            public void HandleCommands(string requestId, IReadOnlyList<UnityCommandDto> commands)
            {
                CallCount++;
                LastCommandCount = commands?.Count ?? 0;
            }
        }

        private sealed class FakeEvcApiClient : IEvcApiClient
        {
            private int concurrentSegments;
            public SmartStartResponse StartResponse;
            public QuestionListResponse QuestionsResponse;
            public StartSessionRequest LastStartRequest;
            public readonly List<EvcSegmentRequest> SegmentRequests = new List<EvcSegmentRequest>();
            public Func<EvcSegmentRequest, Task<EvcUpdateResponse>> SegmentHandler;
            public int MaxConcurrentSegments;
            public int GenerateQuestionCalls;
            public int GetQuestionCalls;
            public int GetSessionCalls;
            public Exception StartException;
            public SessionResponse SessionResponse;
            public readonly Queue<Exception> GenerateQuestionFailures = new Queue<Exception>();
            public readonly Queue<Exception> GetQuestionFailures = new Queue<Exception>();
            public readonly List<string> QuestionRequestIds = new List<string>();
            public ReportFinishResponse FinishResponse;
            public ReportStatusResponse ReportStatusResponse;
            public int FinishCalls;
            public int GetReportCalls;
            public readonly Queue<Exception> FinishFailures = new Queue<Exception>();
            public readonly List<string> FinishRequestIds = new List<string>();

            public Task<SmartStartResponse> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken)
            {
                LastStartRequest = request;
                if (StartException != null)
                    return Task.FromException<SmartStartResponse>(StartException);
                return Task.FromResult(StartResponse);
            }

            public async Task<EvcUpdateResponse> SendSegmentAsync(EvcSegmentRequest request, CancellationToken cancellationToken)
            {
                SegmentRequests.Add(request);
                concurrentSegments++;
                MaxConcurrentSegments = Math.Max(MaxConcurrentSegments, concurrentSegments);
                try
                {
                    return SegmentHandler != null
                        ? await SegmentHandler(request)
                        : SuccessfulUpdate(request);
                }
                finally
                {
                    concurrentSegments--;
                }
            }

            public Task<SessionResponse> GetSessionAsync(string sessionId, string token, CancellationToken cancellationToken)
            {
                GetSessionCalls++;
                return Task.FromResult(SessionResponse ?? new SessionResponse
                {
                    session_id = sessionId,
                    step = 0,
                    slide_count = 3,
                    status = "active",
                    audiences = CreateStartResponse().audiences
                });
            }

            public Task<QuestionListResponse> GenerateQuestionsAsync(
                string sessionId,
                string token,
                string requestId,
                int count,
                CancellationToken cancellationToken)
            {
                GenerateQuestionCalls++;
                QuestionRequestIds.Add(requestId);
                if (GenerateQuestionFailures.Count > 0)
                    return Task.FromException<QuestionListResponse>(GenerateQuestionFailures.Dequeue());
                return Task.FromResult(QuestionsResponse);
            }

            public Task<QuestionListResponse> GetQuestionsAsync(string sessionId, string token, CancellationToken cancellationToken)
            {
                GetQuestionCalls++;
                if (GetQuestionFailures.Count > 0)
                    return Task.FromException<QuestionListResponse>(GetQuestionFailures.Dequeue());
                return Task.FromResult(QuestionsResponse);
            }

            public Task<ReportFinishResponse> FinishSessionAsync(
                string sessionId,
                string token,
                string requestId,
                int plannedSeconds,
                int qaSeconds,
                CancellationToken cancellationToken)
            {
                FinishCalls++;
                FinishRequestIds.Add(requestId);
                if (FinishFailures.Count > 0)
                    return Task.FromException<ReportFinishResponse>(FinishFailures.Dequeue());
                return Task.FromResult(FinishResponse);
            }

            public Task<ReportStatusResponse> GetReportAsync(
                string sessionId,
                string token,
                CancellationToken cancellationToken)
            {
                GetReportCalls++;
                return Task.FromResult(ReportStatusResponse);
            }
        }
    }
}
