
using System;

namespace KageDock.Engines
{
    public sealed class CountdownTimerEngine
    {
        private DateTime _endTime;

        public TimeSpan ConfiguredDuration { get; private set; }

        public TimeSpan Remaining { get; private set; }

        public bool IsRunning { get; private set; }

        public bool IsCompleted { get; private set; }

        public CountdownTimerEngine(TimeSpan initialDuration)
        {
            if (initialDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialDuration));
            }

            ConfiguredDuration = initialDuration;
            Remaining = initialDuration;
        }

        public void Reset(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration));
            }

            IsRunning = false;
            IsCompleted = false;

            ConfiguredDuration = duration;
            Remaining = duration;
        }

        public bool Start()
        {
            if (IsRunning || Remaining <= TimeSpan.Zero)
                return false;

            _endTime = DateTime.UtcNow + Remaining;
            IsRunning = true;

            return true;
        }

        public void Pause()
        {
            if (!IsRunning)
                return;

            UpdateRemaining();
            IsRunning = false;
        }

        public bool Update()
        {
            if (!IsRunning)
                return false;

            UpdateRemaining();

            if (Remaining > TimeSpan.Zero)
                return false;

            Remaining = TimeSpan.Zero;
            IsRunning = false;
            IsCompleted = true;

            return true;
        }

        private void UpdateRemaining()
        {
            Remaining = _endTime - DateTime.UtcNow;

            if (Remaining < TimeSpan.Zero)
                Remaining = TimeSpan.Zero;
        }
    }
}
