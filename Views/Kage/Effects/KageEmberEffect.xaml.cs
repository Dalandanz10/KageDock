using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KageDock.Views
{
    /// <summary>
    /// "Ember Awakening": the eyes thin, glow pale blue, an aura blooms and embers
    /// lift off, then everything cools back to neutral. About 4 seconds.
    ///
    /// Idle = Collapsed. Play() runs keyframe animations only; no timers, no per-frame
    /// callbacks, and no visual elements are created per play.
    ///
    /// The thinning uses its own ScaleTransform temporarily inserted into each eye's
    /// existing TransformGroup, so blink and gaze keep working on top of it.
    /// The caller should not Play() while Kage is sleeping.
    /// </summary>
    public partial class KageEmberEffect : System.Windows.Controls.UserControl
    {
        private const int EmberCount = 10;
        private const double ThinScaleY = 0.72;

        private static readonly IEasingFunction EaseOut = CreateEase(EasingMode.EaseOut);
        private static readonly IEasingFunction EaseInOut = CreateEase(EasingMode.EaseInOut);

        private static readonly System.Windows.Media.Brush[] EmberBrushes =
        {
            CreateEmberBrush(0xFF, 0xD0, 0x72), // Warm amber
            CreateEmberBrush(0xFF, 0x8A, 0x32), // Orange
            CreateEmberBrush(0xFF, 0x65, 0x24), // Deep orange
            CreateEmberBrush(0xFF, 0xE4, 0x9A), // Golden
            CreateEmberBrush(0xFF, 0xF2, 0xC0)  // Hot yellow
        };

        private readonly Ellipse[] _embers = new Ellipse[EmberCount];
        private readonly TranslateTransform[] _emberShift = new TranslateTransform[EmberCount];

        private readonly ScaleTransform _leftThin = new(1, 1);
        private readonly ScaleTransform _rightThin = new(1, 1);

        private readonly Random _random = new();

        // Reused every play to avoid allocating lists repeatedly.
        private readonly List<(IAnimatable Target, DependencyProperty Property, AnimationTimeline Animation)> _plan = new();

        private FrameworkElement? _leftEye;
        private FrameworkElement? _rightEye;

        private int _version;

        public bool IsPlaying => Visibility == Visibility.Visible;

        public KageEmberEffect()
        {
            InitializeComponent();

            double[] sizes = { 1.6, 2.0, 2.4 };

            for (int i = 0; i < EmberCount; i++)
            {
                double size = sizes[i % sizes.Length];

                _emberShift[i] = new TranslateTransform();

                _embers[i] = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = EmberBrushes[i % EmberBrushes.Length],
                    Opacity = 0,
                    IsHitTestVisible = false,
                    RenderTransform = _emberShift[i]
                };

                Layer.Children.Add(_embers[i]);
            }

            Unloaded += (_, _) => Cancel();
        }

        /// <summary>Gives the effect the two open-eye elements (the same ones the eye controller uses).</summary>
        public void Attach(FrameworkElement leftEye, FrameworkElement rightEye)
        {
            _leftEye = leftEye;
            _rightEye = rightEye;
        }

        /// <summary>Starts the sequence. Returns false if it could not start.</summary>
        public bool Play()
        {
            if (_leftEye == null || _rightEye == null || IsPlaying)
                return false;

            int version = ++_version;
            _plan.Clear();

            AttachThin(_leftEye, _leftThin);
            AttachThin(_rightEye, _rightThin);

            Visibility = Visibility.Visible;

            // --- Thin: narrow into a glare, hold, ease back. ---
            Queue(_leftThin, ScaleTransform.ScaleYProperty, 0,
                K(0, 1), K(450, ThinScaleY, EaseOut), K(2400, ThinScaleY), K(3100, 1, EaseInOut));
            Queue(_rightThin, ScaleTransform.ScaleYProperty, 24,
                K(0, 1), K(450, ThinScaleY, EaseOut), K(2400, ThinScaleY), K(3100, 1, EaseInOut));

            // --- Glow: builds after the eyes start thinning, cools after they recover. ---
            Queue(GlowLeft, OpacityProperty, 350,
                K(0, 0), K(600, 1, EaseOut), K(2000, 1), K(3000, 0, EaseInOut));
            Queue(GlowRight, OpacityProperty, 374,
                K(0, 0), K(600, 1, EaseOut), K(2000, 1), K(3000, 0, EaseInOut));

            // --- Aura ---
            Queue(Aura, OpacityProperty, 700,
                K(0, 0), K(700, 1, EaseOut), K(1700, 1), K(3200, 0, EaseInOut));

            // --- Embers: lift off the eyes, drift up and outward, fade. ---
            for (int i = 0; i < EmberCount; i++)
            {
                bool left = i % 2 == 0;
                double outward = left ? -1 : 1;

                double startX = left ? Range(26, 42) : Range(58, 74);
                double startY = Range(11, 17);

                Canvas.SetLeft(_embers[i], startX);
                Canvas.SetTop(_embers[i], startY);

                double begin = 800 + i * 110 + Range(0, 120);
                double life = Range(1400, 2000);

                Queue(_emberShift[i], TranslateTransform.YProperty, begin,
                    K(0, 0), K(life, -Range(9, 15), EaseOut));

                Queue(_emberShift[i], TranslateTransform.XProperty, begin,
                    K(0, 0),
                    K(life * 0.45, outward * Range(2, 6)),
                    K(life, outward * Range(8, 16), EaseOut));

                Queue(_embers[i], OpacityProperty, begin,
                    K(0, 0), K(life * 0.15, 0.95), K(life * 0.55, 0.7), K(life, 0));
            }

            // Attach the completion handler to whichever animation ends last,
            // then start everything.
            double longestEnd = -1;
            AnimationTimeline? longest = null;

            foreach (var item in _plan)
            {
                double end = item.Animation.BeginTime!.Value.TotalMilliseconds
                           + item.Animation.Duration.TimeSpan.TotalMilliseconds;

                if (end > longestEnd)
                {
                    longestEnd = end;
                    longest = item.Animation;
                }
            }

            if (longest != null)
            {
                longest.Completed += (_, _) =>
                {
                    if (version == _version)
                        Finish();
                };
            }

            foreach (var item in _plan)
                item.Target.BeginAnimation(item.Property, item.Animation);

            return true;
        }

        /// <summary>Immediately stops the effect and restores the eyes (dock expand, sleep, dispose).</summary>
        public void Cancel()
        {
            if (IsPlaying)
                Finish();
        }

        private void Finish()
        {
            _version++;

            foreach (var item in _plan)
                item.Target.BeginAnimation(item.Property, null);

            _plan.Clear();

            if (_leftEye != null) DetachThin(_leftEye, _leftThin);
            if (_rightEye != null) DetachThin(_rightEye, _rightThin);

            Visibility = Visibility.Collapsed;
        }

        // ---------------- helpers ----------------

        private static void AttachThin(FrameworkElement eye, ScaleTransform thin)
        {
            if (eye.RenderTransform is TransformGroup group && !group.Children.Contains(thin))
                group.Children.Insert(0, thin);
        }

        private static void DetachThin(FrameworkElement eye, ScaleTransform thin)
        {
            if (eye.RenderTransform is TransformGroup group)
                group.Children.Remove(thin);
        }

        private static (double Ms, double Value, IEasingFunction? Ease) K(
            double ms, double value, IEasingFunction? ease = null) => (ms, value, ease);

        private void Queue(
            IAnimatable target,
            DependencyProperty property,
            double beginMs,
            params (double Ms, double Value, IEasingFunction? Ease)[] keys)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromMilliseconds(beginMs),
                Duration = TimeSpan.FromMilliseconds(keys[^1].Ms),
                FillBehavior = FillBehavior.Stop
            };

            Timeline.SetDesiredFrameRate(animation, 60);

            foreach (var key in keys)
            {
                var time = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(key.Ms));

                if (key.Ease != null)
                {
                    animation.KeyFrames.Add(new EasingDoubleKeyFrame(key.Value, time)
                    {
                        EasingFunction = key.Ease
                    });
                }
                else
                {
                    animation.KeyFrames.Add(new LinearDoubleKeyFrame(key.Value, time));
                }
            }

            _plan.Add((target, property, animation));
        }

        private double Range(double min, double max) =>
            min + _random.NextDouble() * (max - min);

        private static IEasingFunction CreateEase(EasingMode mode)
        {
            var ease = new CubicEase { EasingMode = mode };
            ease.Freeze();
            return ease;
        }

        private static System.Windows.Media.Brush CreateEmberBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(r, g, b));

            brush.Freeze();
            return brush;
        }
    }
}
