
using System;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KageDock.Views
{
    public partial class TimeSuiteView : System.Windows.Controls.UserControl
    {
        private static readonly TimeSpan InitialDuration =
            TimeSpan.FromMinutes(5);

        private readonly DispatcherTimer _timer;

        private TimeSpan _configuredDuration = InitialDuration;
        private TimeSpan _remaining = InitialDuration;
        private DateTime _endTime;
        private bool _isShutdown;

        private bool _timerRunning;
        private bool _warningActive;
        private bool _completed;
        private bool _ringRenderingActive;
        private bool _stopwatchRunning;
        private DateTime _stopwatchStartedAt;
        private TimeSpan _stopwatchElapsed;
        private int _lapNumber;

        public TimeSuiteView()
        {
            InitializeComponent();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };

            _timer.Tick += Timer_Tick;

            TimerTab.Click += (_, _) => SwitchMode("Timer");
            StopwatchTab.Click += (_, _) => SwitchMode("Stopwatch");

            PrimaryButton.Click += PrimaryButton_Click;
            ResetButton.Click += ResetButton_Click;

            StopwatchPrimaryButton.Click += StopwatchPrimaryButton_Click;
            StopwatchResetButton.Click += StopwatchResetButton_Click;
            LapButton.Click += LapButton_Click;

            UpdateTimerDisplay();
            UpdateStopwatchDisplay();

            SwitchMode("Timer");
        }

        // ---------------- MODE SWITCHING ----------------

        private void SwitchMode(string mode)
        {
            bool showTimer = mode.Equals(
                "Timer",
                StringComparison.OrdinalIgnoreCase);

            TimerPanel.Visibility = showTimer
                ? Visibility.Visible
                : Visibility.Collapsed;

            StopwatchPanel.Visibility = showTimer
                ? Visibility.Collapsed
                : Visibility.Visible;

            SetTabStyle(TimerTab, showTimer);
            SetTabStyle(StopwatchTab, !showTimer);
        }

        private static void SetTabStyle(
            System.Windows.Controls.Button button,
            bool selected)
        {
            button.Background = selected
                ? new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(
                        50, 255, 255, 255))
                : System.Windows.Media.Brushes.Transparent;

            button.Foreground = selected
                ? System.Windows.Media.Brushes.White
                : new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        154, 168, 184));
        }

        // ---------------- TIMER ----------------

        private void PrimaryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_timerRunning)
            {
                UpdateRemaining();

                _timerRunning = false;
                StopRingRendering();

                PrimaryButton.Content = "Resume";
                StatusText.Text = "PAUSED";

                UpdateTimerDisplay();
                UpdateProgressRing();
                UpdateScheduler();
                return;
            }

            if (_completed || _remaining <= TimeSpan.Zero)
            {
                if (!TryReadDuration(out TimeSpan duration))
                {
                    TimerAlertText.Text = "Enter a valid duration.";
                    return;
                }

                _configuredDuration = duration;
                _remaining = duration;
                _completed = false;
                TimerAlertText.Text = "";
            }
            else if (PrimaryButton.Content?.ToString() == "Start")
            {
                if (!TryReadDuration(out TimeSpan duration))
                {
                    TimerAlertText.Text = "Enter a valid duration.";
                    return;
                }

                _configuredDuration = duration;
                _remaining = duration;
                TimerAlertText.Text = "";
            }

            if (_remaining <= TimeSpan.Zero)
            {
                TimerAlertText.Text = "Set a duration greater than zero.";
                return;
            }

            _endTime = DateTime.UtcNow + _remaining;
            _timerRunning = true;

            PrimaryButton.Content = "Pause";
            StatusText.Text = "COUNTDOWN IN PROGRESS";

            UpdateTimerDisplay();
            UpdateProgressRing();
            StartRingRendering();
            UpdateScheduler();
        }

        private bool TryReadDuration(out TimeSpan duration)
        {
            duration = TimeSpan.Zero;

            if (!int.TryParse(HoursInput.Text, out int hours) ||
                !int.TryParse(MinutesInput.Text, out int minutes) ||
                !int.TryParse(SecondsInput.Text, out int seconds))
            {
                return false;
            }

            if (hours < 0 ||
                minutes < 0 || minutes > 59 ||
                seconds < 0 || seconds > 59)
            {
                return false;
            }

            try
            {
                duration = new TimeSpan(hours, minutes, seconds);
                return duration > TimeSpan.Zero;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private void ResetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _timerRunning = false;
            StopRingRendering();

            _completed = false;
            _warningActive = false;

            _configuredDuration = InitialDuration;
            _remaining = InitialDuration;

            HoursInput.Text = "00";
            MinutesInput.Text = "05";
            SecondsInput.Text = "00";

            PrimaryButton.Content = "Start";
            StatusText.Text = "READY";
            TimerAlertText.Text = "";

            StopWarningAnimation();

            UpdateTimerDisplay();
            UpdateProgressRing();
            UpdateScheduler();
        }

        private void UpdateRemaining()
        {
            _remaining = _endTime - DateTime.UtcNow;

            if (_remaining < TimeSpan.Zero)
                _remaining = TimeSpan.Zero;
        }

        private void UpdateTimerDisplay()
        {
            TimeDisplay.Text = _remaining.TotalHours >= 1
                ? $"{(int)_remaining.TotalHours:00}:{_remaining.Minutes:00}:{_remaining.Seconds:00}"
                : $"{_remaining.Minutes:00}:{_remaining.Seconds:00}";

            UpdateWarningState();
        }
       private void Ring_Rendering(object? sender, EventArgs e)
        {
            if (_isShutdown || !_timerRunning)
            {
                StopRingRendering();
                return;
            }

            UpdateRemaining();
            UpdateProgressRing();
        }
        private void StartRingRendering()
        {
            if (_isShutdown || _ringRenderingActive)
                return;

            CompositionTarget.Rendering += Ring_Rendering;
            _ringRenderingActive = true;
        }
        private void StopRingRendering()
        {
            if (!_ringRenderingActive)
                return;

            CompositionTarget.Rendering -= Ring_Rendering;
            _ringRenderingActive = false;
        }

        private void UpdateProgressRing()
        {
            const double canvasSize = 200;
            const double ringSize = 176;
            const double stroke = 5;

            double center = canvasSize / 2;
            double radius = (ringSize - stroke) / 2;

            double progress = _configuredDuration.TotalSeconds > 0
                ? _remaining.TotalSeconds / _configuredDuration.TotalSeconds
                : 0;

            progress = Math.Clamp(progress, 0, 1);

            double angle = progress * 360;
            double radians = (angle - 90) * Math.PI / 180;

            var start = new System.Windows.Point(
                center, center - radius);

            var end = new System.Windows.Point(
                center + radius * Math.Cos(radians),
                center + radius * Math.Sin(radians));

            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                if (progress > 0)
                {
                    context.BeginFigure(start, false, false);

                    if (progress >= 0.9999)
                    {
                        var opposite = new System.Windows.Point(center, center + radius);

                        context.ArcTo(
                            opposite,
                            new System.Windows.Size(radius, radius),
                            0,
                            false,
                            SweepDirection.Clockwise,
                            true,
                            false);

                        context.ArcTo(
                            start,
                            new System.Windows.Size(radius, radius),
                            0,
                            false,
                            SweepDirection.Clockwise,
                            true,
                            false);
                    }
                    else
                    {
                        context.ArcTo(
                            end,
                            new System.Windows.Size(radius, radius),
                            0,
                            progress > 0.5,
                            SweepDirection.Clockwise,
                            true,
                            false);
                    }
                }
            }

            geometry.Freeze();

            ProgressArc.Data = geometry;
            GlowArc.Data = geometry;
        }

        private void UpdateWarningState()
        {
            bool shouldWarn =
                _timerRunning &&
                _remaining > TimeSpan.Zero &&
                _remaining <= TimeSpan.FromSeconds(10);

            if (shouldWarn == _warningActive)
                return;

            _warningActive = shouldWarn;
            StopWarningAnimation();

            if (shouldWarn)
            {
                TimeDisplay.Foreground =
                    new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 91, 91));

                ProgressArc.Stroke =
                    new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 75, 85));

                GlowArc.Stroke =
                    new SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(166, 255, 75, 85));

                StartWarningPulseAnimation();
            }
        }

        private void StartWarningPulseAnimation()
        {
            if (_isShutdown || !_warningActive)
                return;

            TimeDisplay.BeginAnimation(
                UIElement.OpacityProperty, null);

            TimeDisplay.Opacity = 1;

            var pulse = new DoubleAnimation
            {
                From = 1.0,
                To = 0.45,
                Duration = TimeSpan.FromMilliseconds(500),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            TimeDisplay.BeginAnimation(
                UIElement.OpacityProperty, pulse);
        }

        private void StopWarningAnimation()
        {
            TimeDisplay.BeginAnimation(
                UIElement.OpacityProperty, null);

            TimeDisplay.Opacity = 1;

            TimeDisplay.Foreground =
                new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        244, 248, 255));

            ProgressArc.Stroke =
                new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        244, 247, 252));

            GlowArc.Stroke =
                new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(
                        166, 255, 255, 255));
        }
        // ---------------- STOPWATCH ----------------

        private void StopwatchPrimaryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_stopwatchRunning)
            {
                _stopwatchElapsed +=
                    DateTime.UtcNow - _stopwatchStartedAt;

                _stopwatchRunning = false;
                StopwatchPrimaryButton.Content = "Resume";
            }
            else
            {
                _stopwatchStartedAt = DateTime.UtcNow;
                _stopwatchRunning = true;
                StopwatchPrimaryButton.Content = "Pause";
            }

            UpdateStopwatchDisplay();
            UpdateScheduler();
            UpdateProgressRing();
        }

        private void StopwatchResetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _stopwatchRunning = false;
            _stopwatchElapsed = TimeSpan.Zero;
            _lapNumber = 0;

            StopwatchPrimaryButton.Content = "Start";
            LapList.Items.Clear();

            UpdateStopwatchDisplay();
            UpdateScheduler();
        }

        private void LapButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_stopwatchRunning)
                return;

            TimeSpan elapsed = _stopwatchElapsed +
                (DateTime.UtcNow - _stopwatchStartedAt);

            _lapNumber++;

            LapList.Items.Insert(
                0,
                $"Lap {_lapNumber:00}     {FormatStopwatch(elapsed)}");
        }

        private void UpdateStopwatchDisplay()
        {
            TimeSpan elapsed = _stopwatchElapsed;

            if (_stopwatchRunning)
            {
                elapsed += DateTime.UtcNow - _stopwatchStartedAt;
            }

            StopwatchDisplay.Text = FormatStopwatch(elapsed);
        }

        private static string FormatStopwatch(TimeSpan elapsed)
        {
            int centiseconds = elapsed.Milliseconds / 10;

            return $"{(int)elapsed.TotalHours:00}:" +
                   $"{elapsed.Minutes:00}:" +
                   $"{elapsed.Seconds:00}." +
                   $"{centiseconds:00}";
        }

        // ---------------- SHARED SCHEDULER ----------------

        private void UpdateScheduler()
        {
            if (_isShutdown)
            {
                _timer.Stop();
                return;
            }

            bool needsTicks = _timerRunning || _stopwatchRunning;

            if (needsTicks)
            {
                if (!_timer.IsEnabled)
                    _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_isShutdown)
            {
                _timer.Stop();
                return;
            }

            if (_timerRunning)
            {
                UpdateRemaining();

                if (_remaining <= TimeSpan.Zero)
                {
                    _remaining = TimeSpan.Zero;
                    _timerRunning = false;
                    StopRingRendering();

                    _completed = true;

                    PrimaryButton.Content = "Start";
                    StatusText.Text = "TIME'S UP!";
                    TimerAlertText.Text = "Timer complete!";

                    StopWarningAnimation();

                    try
                    {
                        SystemSounds.Exclamation.Play();
                    }
                    catch
                    {
                        // Keep the visual completion state if sound fails.
                    }

                    UpdateProgressRing();
                }

                UpdateTimerDisplay();
            }

            if (_stopwatchRunning)
                UpdateStopwatchDisplay();

            UpdateScheduler();
        }
        public void Shutdown()
        {
            if (_isShutdown)
                return;

            _isShutdown = true;

            _timer.Stop();

            _timerRunning = false;
            _stopwatchRunning = false;

            StopRingRendering();
            StopWarningAnimation();
        }
        protected override void OnVisualParentChanged(
            DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);

            if (_isShutdown)
                return;

            if (VisualParent == null)
            {
                // Keep the timer and stopwatch running,
                // but detach unnecessary visual work.
                StopRingRendering();

                TimeDisplay.BeginAnimation(
                    UIElement.OpacityProperty, null);

                TimeDisplay.Opacity = 1;
            }
            else
            {
                UpdateScheduler();

                if (_timerRunning)
                    StartRingRendering();

                if (_warningActive && _timerRunning)
                    StartWarningPulseAnimation();
            }
        }
    }
}
