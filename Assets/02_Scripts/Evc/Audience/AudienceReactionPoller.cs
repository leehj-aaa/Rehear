using System;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;
using Rehear.Evc.Transport;

namespace Rehear.Evc.Audience
{
    /// <summary>One outstanding request; independent of the slower audio upload queue.</summary>
    public sealed class AudienceReactionPoller
    {
        private readonly IAudienceReactionClient client;
        private readonly Func<int, CancellationToken, Task> delay;

        public AudienceReactionPoller(IAudienceReactionClient client,
            Func<int, CancellationToken, Task> delay = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.delay = delay ?? Task.Delay;
        }

        public async Task RunAsync(string sessionId, string token, IPresentationClock clock,
            Func<bool> active, Action<AudienceReactionResponse> deliver, Action<string> error,
            CancellationToken cancellationToken)
        {
            AudienceReactionRequest request = null;
            int lastSequence = 0;
            bool reportedError = false;
            try
            {
                while (active() && !cancellationToken.IsCancellationRequested)
                {
                    if (clock.IsPaused || !clock.IsRunning)
                    {
                        await delay(250, cancellationToken);
                        continue;
                    }
                    if (request == null)
                        request = new AudienceReactionRequest {
                            request_id = Guid.NewGuid().ToString("D"), client_time_s = clock.ElapsedSeconds };
                    AudienceReactionResponse response;
                    try
                    {
                        response = await client.PollReactionsAsync(sessionId, token, request, cancellationToken);
                        if (response == null || response.session_id != sessionId ||
                            response.request_id != request.request_id || response.sequence <= 0)
                            throw new InvalidOperationException("Invalid audience reaction response.");
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        if (!reportedError) error?.Invoke("청중 반응 연결을 다시 시도합니다. " + ex.GetType().Name);
                        reportedError = true;
                        if (ex is EvcApiException api && !api.IsTransient) return;
                        // Reuse ID AND captured time after uncertain delivery: the
                        // server returns the cached evaluation without applying it twice.
                        await delay(2000, cancellationToken);
                        continue;
                    }
                    while (active() && clock.IsPaused)
                        await delay(250, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!active() || !clock.IsRunning) return;
                    if (response.sequence > lastSequence)
                    {
                        deliver?.Invoke(response);
                        lastSequence = response.sequence;
                    }
                    request = null;
                    reportedError = false;
                    await delay(250, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }
}
