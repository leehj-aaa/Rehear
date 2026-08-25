using System;
using Rehear.Evc.Contracts;
using Rehear.Evc.Data;

namespace Rehear.Evc.Presentation
{
    public sealed class PresentationSessionContext
    {
        private static readonly Lazy<PresentationSessionContext> InstanceFactory =
            new Lazy<PresentationSessionContext>(() => new PresentationSessionContext());

        private readonly object gate = new object();
        private PresentationDataDto presentation;
        private string sessionId;
        private string sessionToken;
        private int seed;
        private int step;
        private int slideCount;
        private string sessionStatus;

        private PresentationSessionContext()
        {
        }

        public static PresentationSessionContext Current => InstanceFactory.Value;

        public PresentationDataDto Presentation
        {
            get { lock (gate) return presentation; }
        }

        public bool HasPresentation
        {
            get { lock (gate) return presentation != null; }
        }

        public bool HasEvcSession
        {
            get { lock (gate) return !string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(sessionToken); }
        }

        public string SessionId
        {
            get { lock (gate) return sessionId; }
        }

        internal string SessionToken
        {
            get { lock (gate) return sessionToken; }
        }

        public int Seed
        {
            get { lock (gate) return seed; }
        }

        public int Step
        {
            get { lock (gate) return step; }
        }

        public int SlideCount
        {
            get { lock (gate) return slideCount; }
        }

        public string SessionStatus
        {
            get { lock (gate) return sessionStatus; }
        }

        public PresentationValidationResult BeginPresentation(PresentationDataDto data)
        {
            var validation = PresentationDataValidator.Validate(data);
            if (!validation.IsValid)
                return validation;

            lock (gate)
            {
                presentation = data;
                ClearEvcSessionLocked();
            }

            return validation;
        }

        public void ApplySmartStart(SmartStartResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            if (string.IsNullOrWhiteSpace(response.session_id) || string.IsNullOrWhiteSpace(response.session_token))
                throw new ArgumentException("Smart-start response is missing session credentials.", nameof(response));

            lock (gate)
            {
                sessionId = response.session_id;
                sessionToken = response.session_token;
                seed = response.seed;
                step = response.step;
                slideCount = Math.Max(0, response.slide_count);
                sessionStatus = response.status ?? string.Empty;
            }
        }

        public void ApplyServerState(SessionResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            lock (gate)
            {
                if (!string.IsNullOrWhiteSpace(sessionId) && sessionId != response.session_id)
                    throw new InvalidOperationException("Server response belongs to a different EVC session.");

                sessionId = response.session_id;
                seed = response.seed;
                step = response.step;
                slideCount = Math.Max(0, response.slide_count);
                sessionStatus = response.status ?? string.Empty;
            }
        }

        public void ConfirmUpdate(EvcUpdateResponse response, int expectedStep)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            lock (gate)
            {
                if (response.session_id != sessionId)
                    throw new InvalidOperationException("Update response belongs to a different EVC session.");
                if (step != expectedStep)
                    throw new InvalidOperationException("Local EVC step changed while an update was in flight.");
                if (response.step <= expectedStep)
                    throw new InvalidOperationException("Update response did not advance the EVC step.");

                step = response.step;
                if (!string.IsNullOrWhiteSpace(response.status))
                    sessionStatus = response.status;
            }
        }

        public void ClearEvcSession()
        {
            lock (gate)
                ClearEvcSessionLocked();
        }

        public void ClearAll()
        {
            lock (gate)
            {
                presentation = null;
                ClearEvcSessionLocked();
            }
        }

        private void ClearEvcSessionLocked()
        {
            sessionId = null;
            sessionToken = null;
            seed = 0;
            step = 0;
            slideCount = 0;
            sessionStatus = string.Empty;
        }
    }
}
