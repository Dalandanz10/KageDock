using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KageDock.Views
{
    /// <summary>
    /// Crimson Awakening: an irregular, one-shot lightning sequence confined
    /// to Kage's 100x30 clock strip. All motion is driven by WPF animations;
    /// there is no frame callback or per-frame timer.
    /// </summary>
    public partial class KageCrimsonEffect : System.Windows.Controls.UserControl
    {
        private const double TotalDurationMs = 6200;
        private const int LightningSlotCount = 6;
        private const int SparkCount = 12;

        private static readonly IEasingFunction EaseOut = CreateEase(EasingMode.EaseOut);
        private static readonly IEasingFunction EaseInOut = CreateEase(EasingMode.EaseInOut);

        private readonly Random _random = new();

        private readonly LightningSlot[] _lightningSlots =
            new LightningSlot[LightningSlotCount];

        private readonly Ellipse[] _sparks = new Ellipse[SparkCount];
        private readonly TranslateTransform[] _sparkTransforms =
            new TranslateTransform[SparkCount];

        private readonly List<(IAnimatable Target, DependencyProperty Property)> _animatedProperties = new();

        private SharpVectors.Converters.SvgViewbox? _leftEyeSvg;
        private SharpVectors.Converters.SvgViewbox? _rightEyeSvg;

        private int _version;

        public bool IsPlaying => Visibility == Visibility.Visible;

        
        public KageCrimsonEffect()
        {
            InitializeComponent();

            for (int i = 0; i < LightningSlotCount; i++)
            {
                // Wide, subtle crimson bloom.
                var outerGlow = new Path
                {
                    Stroke = CreateBrush(System.Windows.Media.Color.FromArgb(
                        0x66, 0x7A, 0x0A, 0x1A)),
                    StrokeThickness = 7.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Miter,
                    Opacity = 0,
                    IsHitTestVisible = false
                };

                // Brighter glow immediately surrounding the lightning core.
                var innerGlow = new Path
                {
                    Stroke = CreateBrush(System.Windows.Media.Color.FromArgb(
                        0xE0, 0xE0, 0x10, 0x2A)),
                    StrokeThickness = 3.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Miter,
                    Opacity = 0,
                    IsHitTestVisible = false
                };

                // Sharp, bright electrical core.
                var core = new Path
                {
                    Stroke = CreateBrush(System.Windows.Media.Color.FromArgb(
                        0xFF, 0xFF, 0x5A, 0x63)),
                    StrokeThickness = 1.05,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Miter,
                    Opacity = 0,
                    IsHitTestVisible = false
                };

                _lightningSlots[i] = new LightningSlot(
                    outerGlow,
                    innerGlow,
                    core);

                // Insert bolts above the eye art but below the environmental flash.
                int insertIndex = Layer.Children.IndexOf(EnvironmentalFlash);

                Layer.Children.Insert(insertIndex, outerGlow);
                Layer.Children.Insert(
                    Layer.Children.IndexOf(EnvironmentalFlash),
                    innerGlow);
                Layer.Children.Insert(
                    Layer.Children.IndexOf(EnvironmentalFlash),
                    core);
            }

            for (int i = 0; i < SparkCount; i++)
            {
                var shift = new TranslateTransform();
                _sparkTransforms[i] = shift;

                var spark = new Ellipse
                {
                    Width = i % 3 == 0 ? 1.8 : 1.25,
                    Height = i % 3 == 0 ? 1.8 : 1.25,
                    Fill = CreateBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0x65, 0x70)),
                    Opacity = 0,
                    RenderTransform = shift,
                    IsHitTestVisible = false
                };

                _sparks[i] = spark;

                Layer.Children.Insert(
                    Layer.Children.IndexOf(EnvironmentalFlash),
                    spark);
            }

            Unloaded += (_, _) => Cancel();
        }


        /// <summary>
        /// Shares the existing eye transforms. The crimson SVG copies therefore
        /// follow blink, gaze, expression, and any temporary eye transform.
        /// </summary>
        public void Attach(FrameworkElement leftEye, FrameworkElement rightEye)
        {
            _leftEyeSvg = leftEye as SharpVectors.Converters.SvgViewbox
                ?? throw new ArgumentException("Left eye must be an SvgViewbox.");

            _rightEyeSvg = rightEye as SharpVectors.Converters.SvgViewbox
                ?? throw new ArgumentException("Right eye must be an SvgViewbox.");

            Follow(CrimsonEyeLeft, _leftEyeSvg);
            Follow(CrimsonEyeRight, _rightEyeSvg);
        }

        private static void Follow(
            SharpVectors.Converters.SvgViewbox overlay,
            SharpVectors.Converters.SvgViewbox source)
        {
            BindingOperations.SetBinding(
                overlay,
                UIElement.RenderTransformProperty,
                new System.Windows.Data.Binding(nameof(UIElement.RenderTransform))
                {
                    Source = source
                });

            BindingOperations.SetBinding(
                overlay,
                UIElement.RenderTransformOriginProperty,
                new System.Windows.Data.Binding(nameof(UIElement.RenderTransformOrigin))
                {
                    Source = source
                });

            overlay.Stretch = source.Stretch;
            overlay.StretchDirection = source.StretchDirection;
            overlay.SnapsToDevicePixels = source.SnapsToDevicePixels;
            overlay.UseLayoutRounding = source.UseLayoutRounding;

            RenderOptions.SetEdgeMode(
                overlay,
                RenderOptions.GetEdgeMode(source));

            RenderOptions.SetBitmapScalingMode(
                overlay,
                RenderOptions.GetBitmapScalingMode(source));
        }

        /// <summary>Starts a fresh randomized sequence. Returns false if unavailable/already active.</summary>
        public bool Play()
        {
            if (_leftEyeSvg == null || _rightEyeSvg == null || IsPlaying)
                return false;

            int version = ++_version;
            ClearAnimations();

            Visibility = Visibility.Visible;

            // Start with a quiet atmospheric rise. The eye artwork remains the
            // exact original shape; only its SVG fill is crimson.
            var ambientAnimation = FindAmbientAnimation(version);
            AmbientGlow.BeginAnimation(UIElement.OpacityProperty, ambientAnimation);
            Track(AmbientGlow, UIElement.OpacityProperty);

            Queue(LeftEyeAura, UIElement.OpacityProperty, 0,
                K(0, 0), K(1050, 0.92, EaseOut), K(4300, 0.72),
                K(6100, 0, EaseInOut));

            Queue(RightEyeAura, UIElement.OpacityProperty, 80,
                K(0, 0), K(1050, 0.92, EaseOut), K(4300, 0.72),
                K(6100, 0, EaseInOut));

            // Crossfade crimson artwork over the continuously visible white eyes.
            Queue(CrimsonEyeLeft, UIElement.OpacityProperty, 0,
                K(0, 0),
                K(1250, 1, EaseOut),
                K(4450, 1),
                K(6100, 0, EaseInOut));

            Queue(CrimsonEyeRight, UIElement.OpacityProperty, 35,
                K(0, 0),
                K(1250, 1, EaseOut),
                K(4450, 1),
                K(6100, 0, EaseInOut));

                // Hand off to crimson after it becomes fully visible.
            Queue(_leftEyeSvg!, UIElement.OpacityProperty, 0,
                K(0, 1),
                K(1250, 1),
                K(1500, 0, EaseInOut),
                K(4250, 0),
                K(4450, 1, EaseOut),
                K(6100, 1));

            Queue(_rightEyeSvg!, UIElement.OpacityProperty, 35,
                K(0, 1),
                K(1250, 1),
                K(1500, 0, EaseInOut),
                K(4250, 0),
                K(4450, 1, EaseOut),
                K(6100, 1));

            var strikes = CreateStrikePlan();
            ApplyLightningPlan(strikes);
            ApplySparkPlan(strikes);

            var flashAnimation = BuildFlashAnimation(strikes);
            EnvironmentalFlash.BeginAnimation(UIElement.OpacityProperty, flashAnimation);
            Track(EnvironmentalFlash, UIElement.OpacityProperty);

            // The ambient animation is the longest-lived sequence element.
            // Its completion owns final cleanup.
            return true;
        }

        /// <summary>Immediately stops the sequence and clears all transient visuals.</summary>
        public void Cancel()
        {
            if (IsPlaying)
                Finish();
        }

        private void Finish()
        {
            _version++;

            ClearAnimations();

            foreach (var slot in _lightningSlots)
            {
                slot.OuterGlow.Data = null;
                slot.InnerGlow.Data = null;
                slot.Core.Data = null;

                slot.OuterGlow.Opacity = 0;
                slot.InnerGlow.Opacity = 0;
                slot.Core.Opacity = 0;
            }

            for (int i = 0; i < SparkCount; i++)
            {
                _sparks[i].Opacity = 0;
                _sparkTransforms[i].X = 0;
                _sparkTransforms[i].Y = 0;
            }

            AmbientGlow.Opacity = 0;
            LeftEyeAura.Opacity = 0;
            RightEyeAura.Opacity = 0;

            CrimsonEyeLeft.Opacity = 0;
            CrimsonEyeRight.Opacity = 0;

            EnvironmentalFlash.Opacity = 0;

            Visibility = Visibility.Collapsed;
        }

        private List<StrikePlan> CreateStrikePlan()
        {
            var result = new List<StrikePlan>();

            int desiredCount = _random.Next(15, 19);
            double time = 620;

            for (int i = 0; i < desiredCount && time < 4850; i++)
            {
                double progress = time / 4850.0;

                double intensity = 0.48 + _random.NextDouble() * 0.38;

                // One or two stronger surges, separated from one another.
                bool surge = (i == desiredCount / 3 || i == desiredCount * 2 / 3)
                             && _random.NextDouble() < 0.72;

                if (surge)
                    intensity = 0.9 + _random.NextDouble() * 0.1;

                double life = surge
                    ? 190 + _random.NextDouble() * 100
                    : 125 + _random.NextDouble() * 105;

                Geometry geometry = CreateBoltGeometry(surge);
                System.Windows.Point endpoint = _lastBoltEndpoint;

                result.Add(new StrikePlan(
                    time,
                    life,
                    intensity,
                    geometry,
                    endpoint,
                    surge));

                // Occasional quick double/triple strikes, followed by irregular quiet gaps.
                if (_random.NextDouble() < 0.28)
                    time += 55 + _random.NextDouble() * 115;
                else
                    time += 190 + _random.NextDouble() * 390;

                // Activity gradually becomes less frequent toward the end.
                if (progress > 0.72)
                    time += 70 + _random.NextDouble() * 170;
            }

            return result;
        }

        private System.Windows.Point _lastBoltEndpoint;

        private Geometry CreateBoltGeometry(bool surge)
        {
            int archetype = _random.Next(4);

            System.Windows.Point start;
            System.Windows.Point end;

            switch (archetype)
            {
                // A strike enters from one of the strip edges.
                case 0:
                    start = new System.Windows.Point(_random.Next(2) == 0 ? 0 : 100, Range(1, 29));
                    end = new System.Windows.Point(Range(18, 82), Range(2, 28));
                    break;

                // A short strike crawls around one eye.
                case 1:
                    double eyeX = _random.Next(2) == 0 ? 34 : 66;
                    start = new System.Windows.Point(eyeX + Range(-15, 15), _random.Next(2) == 0 ? 0 : 30);
                    end = new System.Windows.Point(eyeX + Range(-8, 8), Range(5, 25));
                    break;

                // A bridge crosses the center area.
                case 2:
                    start = new System.Windows.Point(Range(22, 42), _random.Next(2) == 0 ? 0 : 30);
                    end = new System.Windows.Point(Range(58, 78), Range(3, 27));
                    break;

                default:
                    start = new System.Windows.Point(Range(3, 97), Range(1, 29));
                    end = new System.Windows.Point(Range(3, 97), Range(1, 29));
                    break;
            }

            int segments = surge ? _random.Next(7, 10) : _random.Next(5, 9);
            var points = new List<System.Windows.Point> { start };

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double perpX = -dy / length;
            double perpY = dx / length;
            double amplitude = surge ? Range(3.0, 5.5) : Range(2.0, 4.2);

            for (int i = 1; i < segments; i++)
            {
                double t = i / (double)segments;
                double displacement = ( _random.NextDouble() * 2 - 1) * amplitude;

                // A few larger kinks keep the bolts irregular rather than sawtoothed.
                if (_random.NextDouble() < 0.22)
                    displacement *= 1.65;

                points.Add(new System.Windows.Point(
                    Clamp(start.X + dx * t + perpX * displacement, 0, 100),
                    Clamp(start.Y + dy * t + perpY * displacement, 0, 30)));
            }

            points.Add(end);

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);

                for (int i = 1; i < points.Count; i++)
                    context.LineTo(points[i], true, false);

                int branchCount = surge
                    ? _random.Next(1, 4)
                    : (_random.NextDouble() < 0.58 ? _random.Next(1, 3) : 0);

                for (int b = 0; b < branchCount; b++)
                {
                    int originIndex = _random.Next(1, points.Count - 1);
                    System.Windows.Point origin = points[originIndex];
                    System.Windows.Point previous = points[originIndex - 1];
                    System.Windows.Point next = points[originIndex + 1];

                    double vx = next.X - previous.X;
                    double vy = next.Y - previous.Y;
                    double angle = Math.Atan2(vy, vx);
                    angle += (_random.NextDouble() < 0.5 ? -1 : 1)
                             * (Math.PI / 180.0) * Range(25, 65);

                    double branchLength = Range(5, 12) * (surge ? 1.15 : 1.0);
                    System.Windows.Point branchEnd = new System.Windows.Point(
                        Clamp(origin.X + Math.Cos(angle) * branchLength, 0, 100),
                        Clamp(origin.Y + Math.Sin(angle) * branchLength, 0, 30));

                    System.Windows.Point branchMid = new System.Windows.Point(
                        Clamp(origin.X + (branchEnd.X - origin.X) * 0.55
                              + Range(-1.8, 1.8), 0, 100),
                        Clamp(origin.Y + (branchEnd.Y - origin.Y) * 0.55
                              + Range(-1.8, 1.8), 0, 30));

                    context.BeginFigure(origin, false, false);
                    context.LineTo(branchMid, true, false);
                    context.LineTo(branchEnd, true, false);
                }
            }

            geometry.Freeze();
            _lastBoltEndpoint = end;
            return geometry;
        }

        private void ApplyLightningPlan(List<StrikePlan> strikes)
        {
            var slotEvents = new List<StrikePlan>[LightningSlotCount];
            var slotAvailableAt = new double[LightningSlotCount];

            for (int i = 0; i < LightningSlotCount; i++)
                slotEvents[i] = new List<StrikePlan>();

            foreach (StrikePlan strike in strikes)
            {
                int slotIndex = -1;

                for (int i = 0; i < LightningSlotCount; i++)
                {
                    if (slotAvailableAt[i] <= strike.StartMs)
                    {
                        slotIndex = i;
                        break;
                    }
                }

                if (slotIndex < 0)
                    continue;

                slotEvents[slotIndex].Add(strike);
                slotAvailableAt[slotIndex] = strike.StartMs + strike.LifeMs + 20;
            }

            for (int i = 0; i < LightningSlotCount; i++)
            {
                if (slotEvents[i].Count == 0)
                    continue;

                ApplySlotAnimation(_lightningSlots[i].OuterGlow, slotEvents[i], 0.28);
                ApplySlotAnimation(_lightningSlots[i].InnerGlow, slotEvents[i], 0.72);
                ApplySlotAnimation(_lightningSlots[i].Core, slotEvents[i], 1.0);
            }
        }

        private void ApplySlotAnimation(
                Path path,
                List<StrikePlan> strikes,
                double peakOpacityScale)
        {
            var data = new ObjectAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(TotalDurationMs),
                FillBehavior = FillBehavior.Stop
            };

            var opacity = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(TotalDurationMs),
                FillBehavior = FillBehavior.Stop
            };

            data.KeyFrames.Add(new DiscreteObjectKeyFrame(
                null, KeyTime.FromTimeSpan(TimeSpan.Zero)));

            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
                0, KeyTime.FromTimeSpan(TimeSpan.Zero)));

            foreach (StrikePlan strike in strikes)
            {
                double start = strike.StartMs;
                double end = start + strike.LifeMs;

                data.KeyFrames.Add(new DiscreteObjectKeyFrame(
                    strike.Geometry, KeyTime.FromTimeSpan(Ms(start))));

                data.KeyFrames.Add(new DiscreteObjectKeyFrame(
                    null, KeyTime.FromTimeSpan(Ms(end))));

                double peak = strike.Intensity * peakOpacityScale;

                opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
                    0, KeyTime.FromTimeSpan(Ms(start))));

                opacity.KeyFrames.Add(new EasingDoubleKeyFrame(
                    peak, KeyTime.FromTimeSpan(Ms(start + 12)))
                {
                    EasingFunction = EaseOut
                });

                opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
                    peak * 0.68, KeyTime.FromTimeSpan(Ms(start + strike.LifeMs * 0.58))));

                opacity.KeyFrames.Add(new EasingDoubleKeyFrame(
                    0, KeyTime.FromTimeSpan(Ms(end)))
                {
                    EasingFunction = EaseInOut
                });
            }

            path.BeginAnimation(Path.DataProperty, data);
            path.BeginAnimation(UIElement.OpacityProperty, opacity);

            Track(path, Path.DataProperty);
            Track(path, UIElement.OpacityProperty);
        }

        private void ApplySparkPlan(List<StrikePlan> strikes)
        {
            int sparkIndex = 0;

            foreach (StrikePlan strike in strikes)
            {
                if (sparkIndex >= SparkCount)
                    break;

                if (_random.NextDouble() > (strike.IsSurge ? 0.9 : 0.48))
                    continue;

                Ellipse spark = _sparks[sparkIndex];
                TranslateTransform shift = _sparkTransforms[sparkIndex];

                System.Windows.Point origin = strike.Endpoint;

                Canvas.SetLeft(spark, origin.X);
                Canvas.SetTop(spark, origin.Y);

                double life = 170 + _random.NextDouble() * 150;
                double dx = Range(-5, 5);
                double dy = Range(-4, 4);

                var xAnimation = new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = Ms(strike.StartMs),
                    Duration = Ms(life),
                    FillBehavior = FillBehavior.Stop
                };
                xAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                xAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(dx, KeyTime.FromTimeSpan(Ms(life)), EaseOut));

                var yAnimation = new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = Ms(strike.StartMs),
                    Duration = Ms(life),
                    FillBehavior = FillBehavior.Stop
                };
                yAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                yAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(dy, KeyTime.FromTimeSpan(Ms(life)), EaseOut));

                var opacityAnimation = new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = Ms(strike.StartMs),
                    Duration = Ms(life),
                    FillBehavior = FillBehavior.Stop
                };
                opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(Ms(25)), EaseOut));
                opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Ms(life)), EaseInOut));

                shift.BeginAnimation(TranslateTransform.XProperty, xAnimation);
                shift.BeginAnimation(TranslateTransform.YProperty, yAnimation);
                spark.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);

                Track(shift, TranslateTransform.XProperty);
                Track(shift, TranslateTransform.YProperty);
                Track(spark, UIElement.OpacityProperty);

                sparkIndex++;
            }
        }

        private DoubleAnimationUsingKeyFrames BuildFlashAnimation(List<StrikePlan> strikes)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = Ms(TotalDurationMs),
                FillBehavior = FillBehavior.Stop
            };

            animation.KeyFrames.Add(new LinearDoubleKeyFrame(
                0, KeyTime.FromTimeSpan(TimeSpan.Zero)));

            foreach (StrikePlan strike in strikes.Where(s => s.IsSurge))
            {
                double peak = 0.12 + strike.Intensity * 0.09;

                animation.KeyFrames.Add(new LinearDoubleKeyFrame(
                    0, KeyTime.FromTimeSpan(Ms(strike.StartMs))));

                animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                    peak, KeyTime.FromTimeSpan(Ms(strike.StartMs + 12)), EaseOut));

                animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                    0, KeyTime.FromTimeSpan(Ms(strike.StartMs + 95)), EaseInOut));
            }

            return animation;
        }

        private DoubleAnimationUsingKeyFrames FindAmbientAnimation(int version)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = Ms(TotalDurationMs),
                FillBehavior = FillBehavior.Stop
            };

            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.72, KeyTime.FromTimeSpan(Ms(900)), EaseOut));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.58, KeyTime.FromTimeSpan(Ms(3900))));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Ms(6200)), EaseInOut));

            animation.Completed += (_, _) =>
            {
                if (version == _version)
                    Finish();
            };
            return animation;
        }

        private void Queue(
            IAnimatable target,
            DependencyProperty property,
            double beginMs,
            params (double Ms, double Value, IEasingFunction? Ease)[] keys)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = Ms(beginMs),
                Duration = Ms(keys[^1].Ms),
                FillBehavior = FillBehavior.Stop
            };

            foreach (var key in keys)
            {
                KeyTime time = KeyTime.FromTimeSpan(Ms(key.Ms));

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

            target.BeginAnimation(property, animation);
            Track(target, property);
        }

        private void Track(IAnimatable target, DependencyProperty property)
        {
            if (!_animatedProperties.Contains((target, property)))
                _animatedProperties.Add((target, property));
        }

        private void ClearAnimations()
        {
            foreach (var item in _animatedProperties)
                item.Target.BeginAnimation(item.Property, null);

            _animatedProperties.Clear();

            foreach (var slot in _lightningSlots)
            {
                slot.OuterGlow.BeginAnimation(UIElement.OpacityProperty, null);
                slot.InnerGlow.BeginAnimation(UIElement.OpacityProperty, null);
                slot.Core.BeginAnimation(UIElement.OpacityProperty, null);

                slot.OuterGlow.BeginAnimation(Path.DataProperty, null);
                slot.InnerGlow.BeginAnimation(Path.DataProperty, null);
                slot.Core.BeginAnimation(Path.DataProperty, null);
            }

            foreach (var spark in _sparks)
            {
                spark.BeginAnimation(UIElement.OpacityProperty, null);
            }

            foreach (var transform in _sparkTransforms)
            {
                transform?.BeginAnimation(TranslateTransform.XProperty, null);
                transform?.BeginAnimation(TranslateTransform.YProperty, null);
            }
        }

        private static IEasingFunction CreateEase(EasingMode mode)
        {
            var ease = new CubicEase { EasingMode = mode };
            ease.Freeze();
            return ease;
        }

        private static SolidColorBrush CreateBrush(System.Windows.Media.Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private double Range(double min, double max) =>
            min + _random.NextDouble() * (max - min);

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));

        private static TimeSpan Ms(double milliseconds) =>
            TimeSpan.FromMilliseconds(milliseconds);

        private static (double Ms, double Value, IEasingFunction? Ease) K(
            double ms, double value, IEasingFunction? ease = null) => (ms, value, ease);

        private sealed class LightningSlot
        {
            public Path OuterGlow { get; }
            public Path InnerGlow { get; }
            public Path Core { get; }

            public LightningSlot(Path outerGlow, Path innerGlow, Path core)
            {
                OuterGlow = outerGlow;
                InnerGlow = innerGlow;
                Core = core;
            }
        }

        private sealed record StrikePlan(
            double StartMs,
            double LifeMs,
            double Intensity,
            Geometry Geometry,
            System.Windows.Point Endpoint,
            bool IsSurge);
    }
}
