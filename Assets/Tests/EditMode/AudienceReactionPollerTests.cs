using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rehear.Evc.Audience;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;

namespace Rehear.Evc.Tests
{
    public sealed class AudienceReactionPollerTests
    {
        private sealed class Clock : IPresentationClock
        {
            public double ElapsedSeconds { get; set; } = 10;
            public bool IsRunning { get; set; } = true;
            public bool IsPaused { get; set; }
        }
        private sealed class Client : IAudienceReactionClient
        {
            public Func<AudienceReactionRequest, Task<AudienceReactionResponse>> Handler;
            public readonly List<AudienceReactionRequest> Requests = new List<AudienceReactionRequest>();
            public Task<AudienceReactionResponse> PollReactionsAsync(string session, string token,
                AudienceReactionRequest request, CancellationToken cancellation)
            {
                Requests.Add(request);
                return Handler(request);
            }
        }
        private static AudienceReactionResponse Response(AudienceReactionRequest request) =>
            new AudienceReactionResponse { session_id = "session", request_id = request.request_id, sequence = 1 };

        [Test]
        public async Task UncertainNetworkResponse_ReusesExactRequestAndDeliversOnce()
        {
            var clock = new Clock();
            var client = new Client();
            bool active = true;
            int delivered = 0;
            client.Handler = request => client.Requests.Count == 1
                ? Task.FromException<AudienceReactionResponse>(new IOException())
                : Task.FromResult(Response(request));
            var poller = new AudienceReactionPoller(client, (ms, ct) => {
                clock.ElapsedSeconds += ms / 1000d;
                return Task.CompletedTask;
            });
            await poller.RunAsync("session", "token", clock, () => active,
                response => { delivered++; active = false; }, null, CancellationToken.None);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].request_id, Is.EqualTo(client.Requests[0].request_id));
            Assert.That(client.Requests[1].client_time_s, Is.EqualTo(10));
            Assert.That(delivered, Is.EqualTo(1));
        }

        [Test]
        public async Task PauseDuringRequest_HoldsResponseUntilResumeWithoutRepolling()
        {
            var clock = new Clock();
            var client = new Client();
            bool active = true;
            int delivered = 0, pauseWaits = 0;
            client.Handler = request => {
                clock.IsPaused = true;
                return Task.FromResult(Response(request));
            };
            var poller = new AudienceReactionPoller(client, (ms, ct) => {
                if (clock.IsPaused) {
                    Assert.That(delivered, Is.Zero);
                    if (++pauseWaits == 3) clock.IsPaused = false;
                }
                return Task.CompletedTask;
            });
            await poller.RunAsync("session", "token", clock, () => active,
                response => { delivered++; active = false; }, null, CancellationToken.None);
            Assert.That(pauseWaits, Is.EqualTo(3));
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(delivered, Is.EqualTo(1));
        }

        [Test]
        public async Task FinishDuringRequest_DropsLateBackchannelBeforeQa()
        {
            var clock = new Clock();
            var client = new Client();
            bool active = true;
            client.Handler = request => { active = false; return Task.FromResult(Response(request)); };
            await new AudienceReactionPoller(client).RunAsync("session", "token", clock, () => active,
                response => Assert.Fail("Late reaction entered Q&A"), null, CancellationToken.None);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
        }
    }
}
