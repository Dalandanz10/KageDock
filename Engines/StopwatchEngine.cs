
using System;

namespace KageDock.Engines
{
    public sealed class StopwatchEngine
    {
        private DateTime _startedAt;
        private TimeSpan _elapsed;

        public bool IsRunning { get; private set; }

        public int LapNumber { get; private set; }

        public TimeSpan Elapsed
        {
            get
            {
                if (!IsRunning)
                    return _elapsed;

                return _elapsed + (DateTime.UtcNow - _startedAt);
            }
        }

        public void Start()
        {
            if (IsRunning)
                return;

            _startedAt = DateTime.UtcNow;
            IsRunning = true;
        }

        public void Pause()
        {
            if (!IsRunning)
                return;

            _elapsed += DateTime.UtcNow - _startedAt;
            IsRunning = false;
        }

        public void Reset()
        {
            IsRunning = false;
            _elapsed = TimeSpan.Zero;
            LapNumber = 0;
        }

        public TimeSpan RecordLap()
        {
            if (!IsRunning)
                return Elapsed;

            LapNumber++;
            return Elapsed;
        }
    }
}
