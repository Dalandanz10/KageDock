
using System;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KageDock.Engines;

namespace KageDock.Views
{
    public partial class TimeSuiteView : System.Windows.Controls.UserControl
    {
        private static readonly TimeSpan InitialDuration =
            TimeSpan.FromMinutes(5);
        private readonly CountdownTimerEngine _countdown =
            new(InitialDuration);

        private readonly DispatcherTimer _timer;

        private bool _isShutdown;

        private bool _warningActive;

        private bool TimerIsRunning => _countdown.IsRunning;
        private bool TimerIsCompleted => _countdown.IsCompleted;
        private TimeSpan TimerRemaining => _countdown.Remaining;
        private TimeSpan TimerConfiguredDuration =>
            _countdown.ConfiguredDuration;
        private bool _ringRenderingActive;
        private readonly StopwatchEngine _stopwatch = new();

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
            if (_countdown.IsRunning)
            {
                _countdown.Pause();

                StopRingRendering();

                PrimaryButton.Content = "Resume";
                StatusText.Text = "PAUSED";

                UpdateTimerDisplay();
                UpdateProgressRing();
                UpdateScheduler();
                return;
            }

            if (TimerIsCompleted || TimerRemaining <= TimeSpan.Zero)
            {
                if (!TryReadDuration(out TimeSpan duration))
                {
                    TimerAlertText.Text = "Enter a valid duration.";
                    return;
                }

                _countdown.Reset(duration);
                TimerAlertText.Text = "";
            }
            else if (PrimaryButton.Content?.ToString() == "Start")
            {
                if (!TryReadDuration(out TimeSpan duration))
                {
                    TimerAlertText.Text = "Enter a valid duration.";
                    return;
                }

                _countdown.Reset(duration);
                TimerAlertText.Text = "";
            }

            if (TimerRemaining <= TimeSpan.Zero)
            {
                TimerAlertText.Text = "Set a duration greater than zero.";
                return;
            }

            if (!_countdown.Start())
                return;

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
            _countdown.Reset(InitialDuration);

            StopRingRendering();

            _warningActive = false;

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


        private void UpdateTimerDisplay()
        {
            TimeDisplay.Text = TimerRemaining.TotalHours >= 1
                ? $"{(int)TimerRemaining.TotalHours:00}:{TimerRemaining.Minutes:00}:{TimerRemaining.Seconds:00}"
                : $"{TimerRemaining.Minutes:00}:{TimerRemaining.Seconds:00}";

            UpdateWarningState();
        }
       private void Ring_Rendering(object? sender, EventArgs e)
        {
            if (_isShutdown || !TimerIsRunning)
            {
                StopRingRendering();
                return;
            }

            if (_countdown.Update())
                CompleteTimer();

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

            double progress = TimerConfiguredDuration.TotalSeconds > 0
            ? TimerRemaining.TotalSeconds / TimerConfiguredDuration.TotalSeconds
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
                TimerIsRunning &&
                TimerRemaining > TimeSpan.Zero &&
                TimerRemaining <= TimeSpan.FromSeconds(10);

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
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Pause();
                StopwatchPrimaryButton.Content = "Resume";
            }
            else
            {
                _stopwatch.Start();
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
            _stopwatch.Reset();

            StopwatchPrimaryButton.Content = "Start";
            LapList.Items.Clear();

            UpdateStopwatchDisplay();
            UpdateScheduler();
        }

        private void LapButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_stopwatch.IsRunning)
                return;

            TimeSpan elapsed = _stopwatch.RecordLap();

            LapList.Items.Insert(
                0,
                $"Lap {_stopwatch.LapNumber:00}     {FormatStopwatch(elapsed)}");
        }

        private void UpdateStopwatchDisplay()
        {
            StopwatchDisplay.Text = FormatStopwatch(_stopwatch.Elapsed);
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

            bool needsTicks = TimerIsRunning || _stopwatch.IsRunning;

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

            if (TimerIsRunning)
            {
                if (_countdown.Update())
                    CompleteTimer();

                UpdateTimerDisplay();
            }

            if (_stopwatch.IsRunning)
                UpdateStopwatchDisplay();

            UpdateScheduler();
        }

        private void CompleteTimer()
        {
            StopRingRendering();

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

            UpdateTimerDisplay();
            UpdateProgressRing();
            UpdateScheduler();
        }

        public void Shutdown()
        {
            if (_isShutdown)
                return;

            _isShutdown = true;

            _timer.Stop();

            _countdown.Pause();
            _stopwatch.Pause();

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

                if (TimerIsRunning)
                    StartRingRendering();

                if (_warningActive && TimerIsRunning)
                    StartWarningPulseAnimation();
            }
        }
    }
}
