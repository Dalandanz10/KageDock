
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfPath = System.Windows.Shapes.Path;
using WpfPoint = System.Windows.Point;

namespace KageDock.Views
{
    /// <summary>
    /// Yozakura: white cherry blossoms drifting through
    /// Kage's clock strip, accompanied by soft eye illumination.
    /// </summary>
    public sealed partial class KageYozakuraEffect :
        System.Windows.Controls.UserControl
    {
        // ---- Tuning ----
        private const int BlossomCount = 18;

        private static readonly IEasingFunction EaseOut =
            CreateEase(EasingMode.EaseOut);

        private static readonly IEasingFunction EaseInOut =
            CreateEase(EasingMode.EaseInOut);

        private static readonly Geometry BlossomShape =
            CreateBlossomShape();

        private static readonly WpfBrush BlossomBrush =
            CreateBlossomBrush();

        private readonly WpfPath[] _blossoms =
            new WpfPath[BlossomCount];

        private readonly TranslateTransform[] _blossomShift =
            new TranslateTransform[BlossomCount];

        private readonly RotateTransform[] _blossomRotation =
            new RotateTransform[BlossomCount];

        private readonly Random _random = new();

        private readonly List<(
            IAnimatable Target,
            DependencyProperty Property,
            AnimationTimeline Animation)> _plan = new();

        private int _version;

        public bool IsPlaying => Visibility == Visibility.Visible;

        public KageYozakuraEffect()
        {
            InitializeComponent();

            // Keep eye illumination above the falling petals.
            System.Windows.Controls.Panel.SetZIndex(LeftEyeGlow, 10);
            System.Windows.Controls.Panel.SetZIndex(RightEyeGlow, 10);

            // Create the blossom pool once.
            for (int i = 0; i < BlossomCount; i++)
            {
                var shift = new TranslateTransform();
                var rotation = new RotateTransform();

                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(rotation);
                transformGroup.Children.Add(shift);

                var blossom = new WpfPath
                {
                    Data = BlossomShape,
                    Fill = BlossomBrush,
                    Width = 6,
                    Height = 6,
                    Opacity = 0,
                    IsHitTestVisible = false,
                    RenderTransformOrigin =
                        new WpfPoint(0.5, 0.5),
                    RenderTransform = transformGroup
                };

                _blossomShift[i] = shift;
                _blossomRotation[i] = rotation;
                _blossoms[i] = blossom;

                Layer.Children.Add(blossom);
            }

            Unloaded += (_, _) => Cancel();
        }

        public bool Play()
        {
            if (IsPlaying)
                return false;

            int version = ++_version;
            _plan.Clear();

            Visibility = Visibility.Visible;

            AmbientGlow.Opacity = 0;
            LeftEyeGlow.Opacity = 0;
            RightEyeGlow.Opacity = 0;

            for (int i = 0; i < BlossomCount; i++)
            {
                _blossomShift[i].X = 0;
                _blossomShift[i].Y = 0;
                _blossomRotation[i].Angle = 0;
                _blossoms[i].Opacity = 0;
            }

            // ---- Ambient moonlight ----
            Queue(
                AmbientGlow,
                UIElement.OpacityProperty,
                0,
                K(0, 0),
                K(900, 0.65, EaseOut),
                K(2600, 0.45),
                K(4300, 0, EaseInOut));

            // ---- Brief eye illumination ----
            // Both eyes glow softly for approximately one second.

            Queue(
                LeftEyeGlow,
                UIElement.OpacityProperty,
                0,
                K(0, 0),
                K(400, 0.65, EaseOut),
                K(1400, 0.65),
                K(2000, 0, EaseInOut));

            Queue(
                RightEyeGlow,
                UIElement.OpacityProperty,
                0,
                K(0, 0),
                K(400, 0.65, EaseOut),
                K(1400, 0.65),
                K(2000, 0, EaseInOut));

            // ---- Independently falling blossoms ----
            for (int i = 0; i < BlossomCount; i++)
            {
                var blossom = _blossoms[i];
                var shift = _blossomShift[i];
                var rotation = _blossomRotation[i];

                double startX = Range(-4, 95);
                double startY = Range(-12, -6);

                Canvas.SetLeft(blossom, startX);
                Canvas.SetTop(blossom, startY);

                // Independent starting times and lifetimes.
                double begin =
                    i * Range(450, 850) + Range(0, 250);

                double life = Range(3500, 5200);

                double destinationX = Range(-4, 95);
                double horizontalTravel = destinationX - startX;

                double driftOne =
                    horizontalTravel * 0.30 + Range(-3, 3);

                double driftTwo =
                    horizontalTravel * 0.72 + Range(-3, 3);

                double rotationStart = Range(-35, 35);
                double rotationEnd =
                    rotationStart + Range(-100, 100);

                double opacity = Range(0.55, 0.95);
                double size = Range(0.75, 1.10);

                blossom.Width = 6 * size;
                blossom.Height = 6 * size;

                rotation.Angle = rotationStart;

                // Downward movement.
                Queue(
                    shift,
                    TranslateTransform.YProperty,
                    begin,
                    K(0, 0),
                    K(life, 47));

                // Gentle breeze movement.
                Queue(
                    shift,
                    TranslateTransform.XProperty,
                    begin,
                    K(0, 0),
                    K(life * 0.35, driftOne),
                    K(life * 0.72, driftTwo),
                    K(life, horizontalTravel));

                // Petal rotation.
                Queue(
                    rotation,
                    RotateTransform.AngleProperty,
                    begin,
                    K(0, rotationStart),
                    K(life, rotationEnd));

                // Fade in and out.
                Queue(
                    blossom,
                    UIElement.OpacityProperty,
                    begin,
                    K(0, 0),
                    K(life * 0.12, opacity, EaseOut),
                    K(life * 0.72, opacity),
                    K(life, 0, EaseInOut));
            }

            // Find the animation that finishes last.
            double longestEnd = -1;
            AnimationTimeline? longest = null;

            foreach (var item in _plan)
            {
                double end =
                    item.Animation.BeginTime!.Value.TotalMilliseconds +
                    item.Animation.Duration.TimeSpan.TotalMilliseconds;

                if (end > longestEnd)
                {
                    longestEnd = end;
                    longest = item.Animation;
                }
            }

            // Finish the complete effect only once every animation ends.
            if (longest != null)
            {
                longest.Completed += (_, _) =>
                {
                    if (version == _version)
                        Finish();
                };
            }

            foreach (var item in _plan)
                item.Target.BeginAnimation(
                    item.Property,
                    item.Animation);

            return true;
        }

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

            AmbientGlow.Opacity = 0;
            LeftEyeGlow.Opacity = 0;
            RightEyeGlow.Opacity = 0;

            for (int i = 0; i < BlossomCount; i++)
            {
                _blossomShift[i].X = 0;
                _blossomShift[i].Y = 0;
                _blossomRotation[i].Angle = 0;
                _blossoms[i].Opacity = 0;
            }

            Visibility = Visibility.Collapsed;
        }

        // ---- Animation helpers ----

        private void Queue(
            IAnimatable target,
            DependencyProperty property,
            double beginMs,
            params (
                double Ms,
                double Value,
                IEasingFunction? Ease)[] keys)
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
                var time = KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(key.Ms));

                if (key.Ease != null)
                {
                    animation.KeyFrames.Add(
                        new EasingDoubleKeyFrame(key.Value, time)
                        {
                            EasingFunction = key.Ease
                        });
                }
                else
                {
                    animation.KeyFrames.Add(
                        new LinearDoubleKeyFrame(key.Value, time));
                }
            }

            _plan.Add((target, property, animation));
        }

        private static (
            double Ms,
            double Value,
            IEasingFunction? Ease) K(
                double ms,
                double value,
                IEasingFunction? ease = null)
            => (ms, value, ease);

        private double Range(double min, double max) =>
            min + _random.NextDouble() * (max - min);

        private static IEasingFunction CreateEase(EasingMode mode)
        {
            var ease = new CubicEase
            {
                EasingMode = mode
            };

            ease.Freeze();
            return ease;
        }

        // ---- Blossom geometry ----

        private static Geometry CreateBlossomShape()
        {
            var figure = new PathFigure
            {
                StartPoint = new WpfPoint(4.5, 8.5),
                IsClosed = true
            };

            figure.Segments.Add(new BezierSegment(
                new WpfPoint(0.4, 6.2),
                new WpfPoint(0.2, 2.5),
                new WpfPoint(1.5, 1.0),
                true));

            figure.Segments.Add(new BezierSegment(
                new WpfPoint(2.8, -0.2),
                new WpfPoint(4.0, 1.0),
                new WpfPoint(4.5, 2.0),
                true));

            figure.Segments.Add(new BezierSegment(
                new WpfPoint(5.0, 1.0),
                new WpfPoint(6.2, -0.2),
                new WpfPoint(7.5, 1.0),
                true));

            figure.Segments.Add(new BezierSegment(
                new WpfPoint(8.8, 2.5),
                new WpfPoint(8.6, 6.2),
                new WpfPoint(4.5, 8.5),
                true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            return geometry;
        }

        private static WpfBrush CreateBlossomBrush()
        {
            var brush = new SolidColorBrush(
                WpfColor.FromRgb(255, 255, 255));

            brush.Freeze();
            return brush;
        }
    }
}
