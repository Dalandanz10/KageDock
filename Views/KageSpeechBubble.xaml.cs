using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KageDock.Views
{
    /// <summary>
    /// Kage's speech bubble. Idle = Collapsed (no rendering, no timers).
    /// Show() plays one open / hold / close animation, then collapses itself.
    /// </summary>
    public partial class KageSpeechBubble : System.Windows.Controls.UserControl
    {
        private const double OpenMs = 300;
        private const double TextDelayMs = 80;
        private const double TextFadeInMs = 180;
        private const double TextFadeOutMs = 110;
        private const double CloseMs = 240;

        private static readonly IEasingFunction OpenEase = CreateEase(EasingMode.EaseOut);
        private static readonly IEasingFunction CloseEase = CreateEase(EasingMode.EaseIn);

        // Incremented whenever animations are cancelled or replaced, so a stale
        // Completed handler can never collapse a newer message.
        private int _version;

        public bool IsShowing => Visibility == Visibility.Visible;
        public event EventHandler? Closed;
        public KageSpeechBubble()
        {
            InitializeComponent();
            Unloaded += (_, _) => Dismiss();
        }

        /// <summary>
        /// Slides the bubble out from Kage, holds, then retracts.
        /// Calling this while a message is showing restarts from hidden.
        /// </summary>
        public void Show(string primary, string? secondary = null, double holdSeconds = 3.5)
        {
            if (string.IsNullOrWhiteSpace(primary) || IsShowing)
                return;

            CancelAnimations();;

            PrimaryRun.Text = primary;
            SecondaryRun.Text = string.IsNullOrWhiteSpace(secondary)
                ? string.Empty
                : " " + secondary;

            // Measure the body so we know exactly how far to slide it.
            Body.Measure(new System.Windows.Size(
                double.PositiveInfinity,
                double.PositiveInfinity));
            double hidden = -(Math.Ceiling(Body.DesiredSize.Width) + 1);

            // Base values = hidden state, so nothing flashes before the animation starts.
            Slide.X = hidden;
            MessageText.Opacity = 0;
            Visibility = Visibility.Visible;

            int version = _version;

            double closeStart = OpenMs + Math.Max(500, holdSeconds * 1000);
            double total = closeStart + CloseMs;
            var duration = TimeSpan.FromMilliseconds(total);

            var slide = new DoubleAnimationUsingKeyFrames
            {
                Duration = duration,
                FillBehavior = FillBehavior.Stop
            };
            slide.KeyFrames.Add(new LinearDoubleKeyFrame(hidden, Ms(0)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, Ms(OpenMs)) { EasingFunction = OpenEase });
            slide.KeyFrames.Add(new LinearDoubleKeyFrame(0, Ms(closeStart)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(hidden, Ms(total)) { EasingFunction = CloseEase });
            slide.Completed += (_, _) =>
            {
                if (version == _version)
                    HideNow();
            };

            var text = new DoubleAnimationUsingKeyFrames
            {
                Duration = duration,
                FillBehavior = FillBehavior.Stop
            };
            text.KeyFrames.Add(new LinearDoubleKeyFrame(0, Ms(0)));
            text.KeyFrames.Add(new LinearDoubleKeyFrame(0, Ms(TextDelayMs)));
            text.KeyFrames.Add(new EasingDoubleKeyFrame(1, Ms(TextDelayMs + TextFadeInMs)) { EasingFunction = OpenEase });
            text.KeyFrames.Add(new LinearDoubleKeyFrame(1, Ms(closeStart)));
            text.KeyFrames.Add(new EasingDoubleKeyFrame(0, Ms(closeStart + TextFadeOutMs)) { EasingFunction = CloseEase });
            text.KeyFrames.Add(new LinearDoubleKeyFrame(0, Ms(total)));

            Slide.BeginAnimation(TranslateTransform.XProperty, slide);
            MessageText.BeginAnimation(UIElement.OpacityProperty, text);
        }

        /// <summary>Instantly hides the bubble (e.g. when the dock expands).</summary>
        public void Dismiss()
        {
            if (Visibility == Visibility.Visible)
                HideNow();
        }

        private void HideNow()
        {
            bool wasShowing = IsShowing;

            CancelAnimations();
            MessageText.Opacity = 0;
            Visibility = Visibility.Collapsed;

            if (wasShowing)
                Closed?.Invoke(this, EventArgs.Empty);
        }

        private void CancelAnimations()
        {
            _version++;
            Slide.BeginAnimation(TranslateTransform.XProperty, null);
            MessageText.BeginAnimation(UIElement.OpacityProperty, null);
        }

        private static KeyTime Ms(double milliseconds) =>
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds));

        private static IEasingFunction CreateEase(EasingMode mode)
        {
            var ease = new CubicEase { EasingMode = mode };
            ease.Freeze();
            return ease;
        }
    }
}
