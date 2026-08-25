using System;
using System.Diagnostics;

namespace Rehear.Evc.Presentation
{
    public interface IPresentationClock
    {
        double ElapsedSeconds { get; }
        bool IsRunning { get; }
        bool IsPaused { get; }
    }

    public sealed class PresentationClock : IPresentationClock
    {
        private readonly Func<double> nowSeconds;
        private double startedAt;
        private double pausedAt;
        private double accumulatedPause;

        public PresentationClock()
            : this(() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency)
        {
        }

        public PresentationClock(Func<double> nowSeconds)
        {
            this.nowSeconds = nowSeconds ?? throw new ArgumentNullException(nameof(nowSeconds));
        }

        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }

        public double ElapsedSeconds
        {
            get
            {
                if (!IsRunning)
                    return 0d;

                var end = IsPaused ? pausedAt : nowSeconds();
                return Math.Max(0d, end - startedAt - accumulatedPause);
            }
        }

        public void Start()
        {
            startedAt = nowSeconds();
            pausedAt = 0d;
            accumulatedPause = 0d;
            IsPaused = false;
            IsRunning = true;
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused)
                return;

            pausedAt = nowSeconds();
            IsPaused = true;
        }

        public void Resume()
        {
            if (!IsRunning || !IsPaused)
                return;

            accumulatedPause += Math.Max(0d, nowSeconds() - pausedAt);
            IsPaused = false;
        }

        public void Stop()
        {
            IsRunning = false;
            IsPaused = false;
        }
    }
}
