
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfPath = System.Windows.Shapes.Path;
using WpfPoint = System.Windows.Point;

namespace KageDock.Views
{
    /// <summary>
    /// Kaze (風): an occasional, quiet breeze and soft sunlight
    /// across Kage's clock strip.
    /// </summary>
    public partial class KazeSunlitBreezeEffect :
    System.Windows.Controls.UserControl, IDisposable
    {   
        public bool IsPlaying => _isRunning;
        public event EventHandler? BurstStarted;
        // ------------------------------------------------------------
        // Breeze
        // ------------------------------------------------------------

        private const double FirstDelayMin = 20;
        private const double FirstDelayMax = 40;
        private bool _isOneShot;
        private const double GapMin = 20;
        private const double GapMax = 40;

        private const double DisabledAnimationRetryMin = 30;
        private const double DisabledAnimationRetryMax = 60;
        private const double Pad = 4;
        private bool _isBurstActive;
        private DateTime _burstEndTime;
        private const double BurstMinSeconds = 5;
        private const double BurstMaxSeconds = 6;
        private static readonly WpfBrush TrailBrush = CreateTrailBrush();

        private static readonly SineEase SineInOut =
            Frozen(new SineEase
            {
                EasingMode = EasingMode.EaseInOut
            });

        private static readonly QuadraticEase QuadIn =
            Frozen(new QuadraticEase
            {
                EasingMode = EasingMode.EaseIn
            });

        private readonly DispatcherTimer _timer = new();
        private readonly Random _random = new();

        private bool _isRunning;
        private bool _isDisposed;

        // ------------------------------------------------------------
        // Solar flare
        // ------------------------------------------------------------

        private const double FlareMinSeconds = 6.0;
        private const double FlareMaxSeconds = 8.0;

        private const double FlareStrength = 1.0;

        private const double FlareCornerBudget = 0.30;
        private const double FlareEyeBudget = 0.16;

        private const int FlareFadeInMs = 1100;

        private static readonly double[] StripeFractions =
        {
            1.0,
            0.70,
            0.45,
            0.22
        };

        private static readonly double[] StripeAlphaScales =
        {
            0.100,
            0.278,
            0.538,
            0.867
        };

        private static readonly WpfColor[] StripeColors =
        {
            WpfColor.FromRgb(255, 222, 155),
            WpfColor.FromRgb(255, 236, 185),
            WpfColor.FromRgb(255, 246, 215),
            WpfColor.FromRgb(255, 251, 235)
        };

        // Radial falloff used by the source light.
        private static readonly (double Radius, byte Alpha)[] SunGradientProfile =
        {
            (0.00, 55),
            (0.15, 51),
            (0.30, 42),
            (0.50, 25),
            (0.70, 12),
            (0.88, 5),
            (1.00, 0)
        };

        // Light intensity along each ray.
        private static readonly (double T, double A)[] AlongProfile =
        {
            (0.00, 1.00),
            (0.12, 0.97),
            (0.30, 0.80),
            (0.50, 0.52),
            (0.70, 0.24),
            (0.88, 0.06),
            (1.00, 0.00)
        };

        private static readonly WpfBrush SpillBrush =
            CreateSunBrush();

        private int _generation;

        private readonly record struct BeamSpec(
            WpfPoint Start,
            WpfPoint End,
            double Width,
            double Peak,
            double Begin,
            bool IsHaze);
        private readonly record struct BreezeFlight(
            double StartDelay,
            TimeSpan Duration,
            double StartY,
            double TravelY);
        private sealed class FlareVisual
        {
            public required Canvas Root { get; init; }
            public required int Version { get; init; }
        }

        private readonly record struct RibbonStrand(
            Geometry Geometry,
            double Opacity);

        private sealed class RibbonDrawingHost : FrameworkElement
        {
            private readonly DrawingVisual _visual = new();
            
            public RibbonDrawingHost(
                double width,
                IReadOnlyList<RibbonStrand> strands)
            {
                Width = width;
                Height = 12;

                IsHitTestVisible = false;
                ClipToBounds = false;

                AddVisualChild(_visual);

                using DrawingContext dc = _visual.RenderOpen();

                foreach (var strand in strands)
                {
                    var brush = TrailBrush.Clone();
                    brush.Opacity = strand.Opacity;
                    brush.Freeze();

                    dc.DrawGeometry(brush, null, strand.Geometry);
                }
            }

            protected override int VisualChildrenCount => 1;

            protected override Visual GetVisualChild(int index)
            {
                if (index != 0)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return _visual;
            }
        }

        private FlareVisual? _currentFlare;
        // ------------------------------------------------------------
        // Independent event scheduler
        // ------------------------------------------------------------

        private const int MaxActiveBundles = 2;
        private const int MaxActiveLeaves = 3;
        private const int MaxActiveRays = 1;

        private int _activeBundles;
        private int _activeLeaves;
        private int _activeRays;

        private DateTime _nextBundle;
        private DateTime _nextLeaf;
        private DateTime _nextRay;
        private DateTime _nextBurst;

        private static readonly WpfColor[] LeafColors =
        {
            WpfColor.FromRgb(218, 151, 55),
            WpfColor.FromRgb(190, 91, 39),
            WpfColor.FromRgb(224, 171, 76),
            WpfColor.FromRgb(153, 65, 42),
            WpfColor.FromRgb(202, 119, 48)
        };

        private static readonly Geometry LeafGeometry =
            CreateAutumnLeafGeometry();


       
        // ------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------

        public KazeSunlitBreezeEffect()
        {
            InitializeComponent();

            _timer.Tick += Timer_Tick;

            Unloaded += KazeSunlitBreezeEffect_Unloaded;
            IsVisibleChanged += KazeSunlitBreezeEffect_IsVisibleChanged;
        }
        public bool PlayOnce()
        {
            if (_isDisposed ||
                _isBurstActive ||
                !SystemParameters.ClientAreaAnimation)
                return false;

            _isOneShot = true;

            if (!_isRunning)
            {
                _isRunning = true;
                Visibility = Visibility.Visible;
            }

            if (!IsVisible)
            {
                Stop();
                return false;
            }

            _isBurstActive = false;
            _nextBurst = DateTime.UtcNow;

            _nextBundle = DateTime.MaxValue;
            _nextLeaf = DateTime.MaxValue;
            _nextRay = DateTime.MaxValue;

            ArmScheduler();

            return true;
        }
        public void Start()
        {
            if (_isDisposed || _isRunning)
                return;

            _isRunning = true;

            Visibility = Visibility.Visible;

            if (IsVisible)
                ScheduleInitialEvents();
        }

        public void Stop()
        {
            if (!_isRunning && !_timer.IsEnabled &&
                Layer.Children.Count == 0 &&
                SolarFlareLayer.Children.Count == 0)
                return;

            _isRunning = false;
            _isOneShot = false;
            _generation++;

            _isBurstActive = false;
            _burstEndTime = DateTime.MinValue;

            _nextBurst = DateTime.MaxValue;
            _nextBundle = DateTime.MaxValue;
            _nextLeaf = DateTime.MaxValue;
            _nextRay = DateTime.MaxValue;

            _timer.Stop();

            _activeBundles = 0;
            _activeLeaves = 0;
            _activeRays = 0;

            ClearTrails();
            SolarFlareLayer.Children.Clear();
            _currentFlare = null;

            Visibility = Visibility.Collapsed;
        }

        private void KazeSunlitBreezeEffect_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            Stop();
        }

        private void KazeSunlitBreezeEffect_IsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (!_isRunning || _isDisposed)
                return;

            if (!IsVisible)
            {
                _generation++;
                _timer.Stop();

                _activeBundles = 0;
                _activeLeaves = 0;
                _activeRays = 0;

                ClearTrails();
                SolarFlareLayer.Children.Clear();
                _currentFlare = null;

                return;
            }

            ScheduleInitialEvents();
        }

        private void ScheduleInitialEvents()
        {
            _isBurstActive = false;
            _burstEndTime = DateTime.MinValue;

            _nextBurst = DateTime.UtcNow.AddSeconds(Between(20, 40));

            _nextBundle = DateTime.MaxValue;
            _nextLeaf = DateTime.MaxValue;
            _nextRay = DateTime.MaxValue;

            ArmScheduler();
        }

        private void ArmScheduler()
        {
            if (!_isRunning || !IsVisible || _isDisposed)
            {
                _timer.Stop();
                return;
            }

            if (_isOneShot &&
                !_isBurstActive &&
                _nextBurst == DateTime.MaxValue)
            {
                _timer.Stop();
                return;
            }

            DateTime next;

            if (_isBurstActive)
            {
                // Always wake at burst end, even if every effect is at its active limit.
                next = _burstEndTime;

                if (_activeBundles < MaxActiveBundles)
                    next = Min(next, _nextBundle);

                if (_activeLeaves < MaxActiveLeaves)
                    next = Min(next, _nextLeaf);

                if (_activeRays < MaxActiveRays)
                    next = Min(next, _nextRay);
            }
            else
            {
                next = _nextBurst;
            }

            double milliseconds = Math.Max(
                1,
                (next - DateTime.UtcNow).TotalMilliseconds);

            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            _timer.Start();
        }

        private void TryCompleteOneShot()
        {
            if (!_isOneShot || _isBurstActive)
                return;

            if (_activeBundles > 0 ||
                _activeLeaves > 0 ||
                _activeRays > 0 ||
                _currentFlare != null)
                return;

            Stop();
        }

        private static DateTime Min(DateTime a, DateTime b) =>
            a <= b ? a : b;

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop();

            if (!_isRunning || _isDisposed || !IsVisible)
                return;

            if (!SystemParameters.ClientAreaAnimation)
            {
                _isBurstActive = false;
                _burstEndTime = DateTime.MinValue;

                _nextBundle = DateTime.MaxValue;
                _nextLeaf = DateTime.MaxValue;
                _nextRay = DateTime.MaxValue;

                _nextBurst = DateTime.UtcNow.AddSeconds(
                    Between(DisabledAnimationRetryMin, DisabledAnimationRetryMax));

                ArmScheduler();
                return;
            }

            DateTime now = DateTime.UtcNow;

            // Start a new burst after the quiet period.
            if (!_isBurstActive)
            {
                if (now < _nextBurst)
                {
                    ArmScheduler();
                    return;
                }

                _isBurstActive = true;
                BurstStarted?.Invoke(this, EventArgs.Empty);
                _burstEndTime = now.AddSeconds(
                    Between(BurstMinSeconds, BurstMaxSeconds));

                // All three effects join the same burst, with slight random staggering.
                _nextBundle = now;
                _nextLeaf = now;
                _nextRay = now;
            }

            // End the burst without interrupting visuals already on screen.
            if (now >= _burstEndTime)
            {
                _isBurstActive = false;

                _nextBundle = DateTime.MaxValue;
                _nextLeaf = DateTime.MaxValue;
                _nextRay = DateTime.MaxValue;

                // Quiet period starts after the burst ends.
                if (_isOneShot)
                {
                    _nextBurst = DateTime.MaxValue;
                    _timer.Stop();

                    TryCompleteOneShot();
                    return;
                }

                // Preserve the original recurring behavior for Start().
                _nextBurst = _burstEndTime.AddSeconds(Between(20, 40));

                ArmScheduler();
                return;
            }

            if (_activeBundles < MaxActiveBundles && now >= _nextBundle)
                SpawnBundle();

            if (_activeLeaves < MaxActiveLeaves && now >= _nextLeaf)
                SpawnLeaf();

            if (_activeRays < MaxActiveRays && now >= _nextRay)
                SpawnRay();

            ArmScheduler();
        }

        private void SetNextEvent(
            ref DateTime next,
            double minGap,
            double maxGap,
            double lullChance,
            double lullMin,
            double lullMax)
        {
            double gap = Between(minGap, maxGap);

            if (_random.NextDouble() < lullChance)
                gap += Between(lullMin, lullMax);

            next = DateTime.UtcNow.AddSeconds(gap);
        }


        // ------------------------------------------------------------
        // Solar flare generation
        // ------------------------------------------------------------

        private double PlaySolarFlare()
        {
            ClearSolarFlare();

            if (!IsVisible || !SystemParameters.ClientAreaAnimation)
                return 0;

            int version = _generation;

            var flare = new Canvas
            {
                Width = 100,
                Height = 30,
                Opacity = 0,
                IsHitTestVisible = false,
                ClipToBounds = true
            };

            var origin = new WpfPoint(0, -1);
            var beams = new List<BeamSpec>();

            // Main shafts: different endpoints create an irregular spread.
            int mainCount = _random.Next(9, 13);

            for (int i = 0; i < mainCount; i++)
            {
                var endpoint = new WpfPoint(
                    Between(15, 125),
                    Between(8, 58));

                beams.Add(new BeamSpec(
                    origin,
                    endpoint,
                    Between(3.0, 7.0),
                    Between(0.30, 0.58) * FlareStrength,
                    Between(0.0, 0.65),
                    false));
            }

            // Fine rays: fill the spaces between the main shafts.
            int fineRayCount = _random.Next(10, 16);

            for (int i = 0; i < fineRayCount; i++)
            {
                var endpoint = new WpfPoint(
                    Between(28, 125),
                    Between(12, 55));

                beams.Add(new BeamSpec(
                    origin,
                    endpoint,
                    Between(1.2, 3.0),
                    Between(0.10, 0.22),
                    Between(0.0, 0.9),
                    false));
            }

            // Wider, faint shafts soften the gaps between the main rays.
            int hazeCount = _random.Next(4, 7);

            for (int i = 0; i < hazeCount; i++)
            {
                var endpoint = new WpfPoint(
                    Between(30, 118),
                    Between(10, 46));

                beams.Add(new BeamSpec(
                    origin,
                    endpoint,
                    Between(15.0, 24.0),
                    Between(0.035, 0.065) * FlareStrength,
                    Between(0.0, 0.8),
                    true));
            }

            double brightnessScale = NormalizeFlareBrightness(beams);

            foreach (var beam in beams)
                AddBeam(flare, beam, brightnessScale, version);

            // Soft light source at the upper-left entrance.
            var sourceGlow = new System.Windows.Shapes.Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = SpillBrush,
                Opacity = 1.0,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(sourceGlow, -9);
            Canvas.SetTop(sourceGlow, -9);

            flare.Children.Add(sourceGlow);

            SolarFlareLayer.Children.Add(flare);

            _currentFlare = new FlareVisual
            {
                Root = flare,
                Version = version
            };

            var duration = TimeSpan.FromSeconds(
                Between(FlareMinSeconds, FlareMaxSeconds));

            var envelope = CreateFlareEnvelope(duration);

            envelope.Completed += (_, _) =>
            {
                if (version != _generation)
                    return;

                flare.BeginAnimation(UIElement.OpacityProperty, null);
                SolarFlareLayer.Children.Remove(flare);

                if (_currentFlare?.Version == version)
                    _currentFlare = null;

                    TryCompleteOneShot();
            };

            flare.BeginAnimation(UIElement.OpacityProperty, envelope);
            return duration.TotalSeconds;
        }

        private void AddBeam(
            Canvas target,
            BeamSpec beam,
            double brightnessScale,
            int version)
        {
            double dx = beam.End.X - beam.Start.X;
            double dy = beam.End.Y - beam.Start.Y;

            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 0.001)
                return;

            double perpendicularX = -dy / length;
            double perpendicularY = dx / length;

            for (int i = 0; i < StripeFractions.Length; i++)
            {
                double halfWidth =
                    beam.Width * StripeFractions[i] * 0.5;

                var geometry = CreateBeamGeometry(
                    beam.Start,
                    beam.End,
                    perpendicularX,
                    perpendicularY,
                    halfWidth);

                var path = new WpfPath
                {
                    Data = geometry,
                    Fill = CreateAlongBrush(
                        beam.Start,
                        beam.End,
                        StripeColors[i]),
                    Opacity = 0,
                    IsHitTestVisible = false
                };

                target.Children.Add(path);

                double opacity =
                    beam.Peak *
                    brightnessScale *
                    StripeAlphaScales[i];

                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = opacity,
                    Duration = TimeSpan.FromMilliseconds(
                        beam.IsHaze ? 1500 : FlareFadeInMs),
                    BeginTime = TimeSpan.FromSeconds(beam.Begin),
                    EasingFunction = SineInOut,
                    FillBehavior = FillBehavior.HoldEnd
                };

                path.BeginAnimation(
                    UIElement.OpacityProperty,
                    fadeIn);
            }
        }

        private static Geometry CreateBeamGeometry(
            WpfPoint start,
            WpfPoint end,
            double perpendicularX,
            double perpendicularY,
            double halfWidth)
        {
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(
                    new WpfPoint(
                        start.X + perpendicularX * halfWidth,
                        start.Y + perpendicularY * halfWidth),
                    true,
                    true);

                context.LineTo(
                    new WpfPoint(
                        end.X + perpendicularX * halfWidth,
                        end.Y + perpendicularY * halfWidth),
                    true,
                    false);

                context.LineTo(
                    new WpfPoint(
                        end.X - perpendicularX * halfWidth,
                        end.Y - perpendicularY * halfWidth),
                    true,
                    false);

                context.LineTo(
                    new WpfPoint(
                        start.X - perpendicularX * halfWidth,
                        start.Y - perpendicularY * halfWidth),
                    true,
                    false);
            }

            geometry.Freeze();
            return geometry;
        }

        private static LinearGradientBrush CreateAlongBrush(
            WpfPoint start,
            WpfPoint end,
            WpfColor color)
        {
            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = start,
                EndPoint = end
            };

            foreach (var stop in AlongProfile)
            {
                byte alpha = (byte)Math.Clamp(
                    stop.A * 255.0,
                    0,
                    255);

                brush.GradientStops.Add(
                    new GradientStop(
                        WpfColor.FromArgb(
                            alpha,
                            color.R,
                            color.G,
                            color.B),
                        stop.T));
            }

            brush.Freeze();
            return brush;
        }

        // ------------------------------------------------------------
        // Brightness normalization
        // ------------------------------------------------------------

        private double NormalizeFlareBrightness(
            IReadOnlyList<BeamSpec> beams)
        {
            double maxCorner = 0;
            double maxEyes = 0;

            // Sample the rendered strip at 2-pixel intervals.
            for (double y = 0; y <= 30; y += 2)
            {
                for (double x = 0; x <= 100; x += 2)
                {
                    double coverage = 0;

                    foreach (var beam in beams)
                    {
                        double contribution = BeamCoverage(
                            beam,
                            new WpfPoint(x, y));

                        coverage = 1.0 -
                            (1.0 - coverage) *
                            (1.0 - contribution);
                    }

                    if (x <= 20 && y <= 15)
                        maxCorner = Math.Max(maxCorner, coverage);

                    if (x >= 15 && x <= 85 && y >= 2 && y <= 28)
                        maxEyes = Math.Max(maxEyes, coverage);
                }
            }

            double scale = 1.0;

            if (maxCorner > 0.001)
            {
                scale = Math.Min(
                    scale,
                    FlareCornerBudget / maxCorner);
            }

            if (maxEyes > 0.001)
            {
                scale = Math.Min(
                    scale,
                    FlareEyeBudget / maxEyes);
            }

            return Math.Clamp(scale, 0, 1);
        }

        private static double BeamCoverage(
            BeamSpec beam,
            WpfPoint point)
        {
            double dx = beam.End.X - beam.Start.X;
            double dy = beam.End.Y - beam.Start.Y;

            double lengthSquared = dx * dx + dy * dy;

            if (lengthSquared < 0.001)
                return 0;

            double length = Math.Sqrt(lengthSquared);

            double projection =
                ((point.X - beam.Start.X) * dx +
                 (point.Y - beam.Start.Y) * dy) /
                lengthSquared;

            if (projection < 0 || projection > 1)
                return 0;

            double closestX = beam.Start.X + projection * dx;
            double closestY = beam.Start.Y + projection * dy;

            double distance = Math.Sqrt(
                Math.Pow(point.X - closestX, 2) +
                Math.Pow(point.Y - closestY, 2));

            double along = AlongAlpha(projection);

            if (along <= 0)
                return 0;

            if (beam.IsHaze)
            {
                double sigma = Math.Max(beam.Width * 0.42, 0.1);

                double radial = Math.Exp(
                    -(distance * distance) /
                    (2.0 * sigma * sigma));

                return Math.Clamp(
                    beam.Peak * along * radial,
                    0,
                    0.95);
            }

            double combined = 0;

            for (int i = 0; i < StripeFractions.Length; i++)
            {
                double halfWidth =
                    beam.Width * StripeFractions[i] * 0.5;

                if (distance > halfWidth)
                    continue;

                double alpha =
                    beam.Peak *
                    StripeAlphaScales[i] *
                    along;

                combined = 1.0 -
                    (1.0 - combined) *
                    (1.0 - alpha);
            }

            return Math.Clamp(combined, 0, 0.95);
        }

        private static double SunGradientAlpha(double radius)
        {
            radius = Math.Clamp(radius, 0, 1);

            for (int i = 1; i < SunGradientProfile.Length; i++)
            {
                if (radius > SunGradientProfile[i].Radius)
                    continue;

                var previous = SunGradientProfile[i - 1];
                var next = SunGradientProfile[i];

                double amount =
                    (radius - previous.Radius) /
                    (next.Radius - previous.Radius);

                return (
                    previous.Alpha +
                    (next.Alpha - previous.Alpha) * amount
                ) / 255.0;
            }

            return 0;
        }

        private static double AlongAlpha(double t)
        {
            t = Math.Clamp(t, 0, 1);

            for (int i = 1; i < AlongProfile.Length; i++)
            {
                if (t > AlongProfile[i].T)
                    continue;

                var previous = AlongProfile[i - 1];
                var next = AlongProfile[i];

                double amount =
                    (t - previous.T) /
                    (next.T - previous.T);

                return previous.A +
                    (next.A - previous.A) * amount;
            }

            return 0;
        }

        // ------------------------------------------------------------
        // Solar flare animation and cleanup
        // ------------------------------------------------------------

        private static DoubleAnimationUsingKeyFrames CreateFlareEnvelope(
            TimeSpan duration)
        {
            var envelope = new DoubleAnimationUsingKeyFrames
            {
                Duration = duration
            };

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0,
                    KeyTime.FromPercent(0)));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1,
                    KeyTime.FromPercent(0.18),
                    SineInOut));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0.88,
                    KeyTime.FromPercent(0.55),
                    SineInOut));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0,
                    KeyTime.FromPercent(1),
                    SineInOut));

            return envelope;
        }

        private void ClearSolarFlare()
        {
            // Invalidate completion callbacks from previous flares.
            _generation++;

            if (_currentFlare != null)
            {
                _currentFlare.Root.BeginAnimation(
                    UIElement.OpacityProperty,
                    null);

                foreach (UIElement child in _currentFlare.Root.Children)
                {
                    child.BeginAnimation(
                        UIElement.OpacityProperty,
                        null);
                }

                SolarFlareLayer.Children.Remove(
                    _currentFlare.Root);

                _currentFlare = null;
            }

            // Also remove any leftover visuals.
            SolarFlareLayer.Children.Clear();
        }

        private static WpfBrush CreateSunBrush()
        {
            var brush = new RadialGradientBrush
            {
                Center = new WpfPoint(0.5, 0.5),
                GradientOrigin = new WpfPoint(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };

            foreach (var stop in SunGradientProfile)
            {
                brush.GradientStops.Add(
                    new GradientStop(
                        WpfColor.FromArgb(
                            stop.Alpha,
                            255,
                            231,
                            175),
                        stop.Radius));
            }

            brush.Freeze();
            return brush;
        }

        // ------------------------------------------------------------
        // Breeze implementation
        // ------------------------------------------------------------

        
       private void SpawnBundle()
        {
            double width = ActualWidth > 0 ? ActualWidth : 100;

            double length = Between(46, 78);
            double direction = _random.Next(2) == 0 ? 1 : -1;

            double distance = Between(24, 48);

            double startX = Between(0, width);
            double startY = Between(5, 22);

            double travelY = Between(-5, 5);

            double durationSeconds = Between(2.0, 3.0);

            var duration = TimeSpan.FromSeconds(durationSeconds);

            double peak = Between(0.40, 0.68);

            var shift = new TranslateTransform();
            var scale = new ScaleTransform(
                direction,
                1,
                length / 2,
                Pad);

            var tilt = new RotateTransform(
                0,
                length / 2,
                Pad);

            var bundle = CreateBundle(length);

            bundle.Opacity = 0;
            bundle.RenderTransform = new TransformGroup
            {
                Children = { scale, tilt, shift }
            };

            Canvas.SetLeft(bundle, startX);
            Canvas.SetTop(bundle, startY - Pad);

            Layer.Children.Add(bundle);

            _activeBundles++;

            int generation = _generation;

            shift.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = 0,
                    To = direction * distance,
                    Duration = duration,
                    EasingFunction = SineInOut
                });

            shift.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    From = 0,
                    To = travelY,
                    Duration = duration,
                    EasingFunction = SineInOut
                });

            tilt.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation
                {
                    From = 0,
                    To = Math.Atan2(travelY, distance)
                        * 180.0 / Math.PI,
                    Duration = duration
                });

            var envelope = CreateEnvelope(
                peak,
                duration,
                TimeSpan.Zero);

            envelope.Completed += (_, _) =>
            {
                if (generation != _generation)
                    return;

                Layer.Children.Remove(bundle);

                _activeBundles = Math.Max(0, _activeBundles - 1);

                TryCompleteOneShot();
                ArmScheduler();
            };

            bundle.BeginAnimation(
                UIElement.OpacityProperty,
                envelope);

            _nextBundle = DateTime.UtcNow.AddSeconds(Between(0.5, 1.5));
        }


        private void SpawnLeaf()
        {
            double width = ActualWidth > 0 ? ActualWidth : 100;

            double startX = Between(0, width);
            double startY = Between(5, 24);

            double direction = _random.Next(2) == 0 ? 1 : -1;

            double distance = Between(18, 48);
            double durationSeconds = Between(2.5, 4.0);

            var duration = TimeSpan.FromSeconds(durationSeconds);

            double sway = Between(1.0, 2.5);
            double size = Between(0.65, 0.95);

            var scale = new ScaleTransform(size, size, 3, 3);

            var rotation = new RotateTransform(
                Between(-30, 30),
                3,
                3);

            var translation = new TranslateTransform();

            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(rotation);
            group.Children.Add(translation);

            var leaf = new WpfPath
            {
                Data = LeafGeometry,
                Fill = CreateLeafBrush(),
                Width = 6,
                Height = 6,
                Opacity = 0,
                RenderTransform = group,
                IsHitTestVisible = false,
                SnapsToDevicePixels = false
            };

            Canvas.SetLeft(leaf, startX);
            Canvas.SetTop(leaf, startY);

            Layer.Children.Add(leaf);

            _activeLeaves++;

            int generation = _generation;

            translation.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = 0,
                    To = direction * distance,
                    Duration = duration,
                    EasingFunction = SineInOut
                });

            var vertical = new DoubleAnimationUsingKeyFrames
            {
                Duration = duration
            };

            vertical.KeyFrames.Add(
                new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));

            vertical.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    -sway,
                    KeyTime.FromPercent(0.25),
                    SineInOut));

            vertical.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    sway,
                    KeyTime.FromPercent(0.52),
                    SineInOut));

            vertical.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    -sway * 0.5,
                    KeyTime.FromPercent(0.78),
                    SineInOut));

            vertical.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    Between(-3, 3),
                    KeyTime.FromPercent(1),
                    SineInOut));

            translation.BeginAnimation(
                TranslateTransform.YProperty,
                vertical);

            rotation.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation
                {
                    From = rotation.Angle,
                    To = rotation.Angle + Between(110, 260),
                    Duration = duration,
                    EasingFunction = SineInOut
                });

            var envelope = CreateEnvelope(
                Between(0.48, 0.72),
                duration,
                TimeSpan.Zero);

            envelope.Completed += (_, _) =>
            {
                if (generation != _generation)
                    return;

                Layer.Children.Remove(leaf);

                _activeLeaves = Math.Max(0, _activeLeaves - 1);

                TryCompleteOneShot();
                ArmScheduler();
            };

            leaf.BeginAnimation(
                UIElement.OpacityProperty,
                envelope);

            _nextLeaf = DateTime.UtcNow.AddSeconds(Between(0.8, 2.0));
        }

        
        private void SpawnRay()
        {
            double width = ActualWidth > 0 ? ActualWidth : 100;
            double x = Between(3, width - 3);

            // Rays originate at the top and descend almost to the bottom.
            var start = new WpfPoint(x, 0);

            var end = new WpfPoint(
                Math.Clamp(x + Between(-11, 11), 0, width),
                Between(26, 29));

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;

            double lineLength = Math.Sqrt(dx * dx + dy * dy);

            double perpendicularX = -dy / lineLength;
            double perpendicularY = dx / lineLength;

            var ray = new Canvas
            {
                Width = 100,
                Height = 30,
                Opacity = 0,
                IsHitTestVisible = false
            };

            WpfColor[] colors =
            {
                WpfColor.FromRgb(190, 145, 90),
                WpfColor.FromRgb(210, 170, 115),
                WpfColor.FromRgb(225, 195, 150),
                WpfColor.FromRgb(240, 220, 185)
            };

            double[] widths = { 21.0, 16.0, 10.0, 4.5 };

            byte[] centerAlphas = { 18, 30, 48, 68 };
            byte[] sideAlphas = { 2, 5, 9, 14 };

            for (int i = 0; i < widths.Length; i++)
            {
                var geometry = CreateBeamGeometry(
                    start,
                    end,
                    perpendicularX,
                    perpendicularY,
                    widths[i] / 2);

                // Fade both sides of the ray, keeping its center brightest.
                var brush = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    StartPoint = new WpfPoint(0, 0.5),
                    EndPoint = new WpfPoint(1, 0.5)
                };

                brush.GradientStops.Add(
                    new GradientStop(
                        WpfColor.FromArgb(0, colors[i].R, colors[i].G, colors[i].B),
                        0.0));

                brush.GradientStops.Add(
                    new GradientStop(
                        WpfColor.FromArgb(sideAlphas[i], colors[i].R, colors[i].G, colors[i].B),
                        0.18));

                brush.GradientStops.Add(
                    new GradientStop(
                        WpfColor.FromArgb(centerAlphas[i], colors[i].R, colors[i].G, colors[i].B),
                        0.42));

                brush.GradientStops.Add(
                    new GradientStop(
                        WpfColor.FromArgb(
                            (byte)(centerAlphas[i] * 0.55),
                            colors[i].R, colors[i].G, colors[i].B),
                        0.68));

                brush.GradientStops.Add(
                    new GradientStop(
                        WpfColor.FromArgb(0, colors[i].R, colors[i].G, colors[i].B),
        1.0));

                brush.Freeze();

                ray.Children.Add(new WpfPath
                {
                    Data = geometry,
                    Fill = brush,
                    Opacity = 1,
                    IsHitTestVisible = false
                });
            }

            SolarFlareLayer.Children.Add(ray);

            _activeRays++;

            int generation = _generation;

            var duration = TimeSpan.FromSeconds(Between(3, 4));

            var envelope = new DoubleAnimationUsingKeyFrames
            {
                Duration = duration
            };

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0,
                    KeyTime.FromPercent(0)));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    Between(0.55, 0.68),
                    KeyTime.FromPercent(0.28),
                    SineInOut));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    Between(0.40, 0.50),
                    KeyTime.FromPercent(0.68),
                    SineInOut));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0,
                    KeyTime.FromPercent(1),
                    SineInOut));

            envelope.Completed += (_, _) =>
            {
                if (generation != _generation)
                    return;

                SolarFlareLayer.Children.Remove(ray);

                _activeRays = Math.Max(0, _activeRays - 1);

                _nextRay = DateTime.UtcNow.AddSeconds(Between(0.6, 1.2));

                TryCompleteOneShot();
                ArmScheduler();
            };

            ray.BeginAnimation(
                UIElement.OpacityProperty,
                envelope);

            _nextRay = DateTime.UtcNow.AddSeconds(Between(1.0, 2.5));
        }
        
        


        private WpfBrush CreateLeafBrush()
        {
            var brush = new System.Windows.Media.SolidColorBrush(
                LeafColors[_random.Next(LeafColors.Length)]);

            brush.Freeze();
            return brush;
        }

        private static Geometry CreateAutumnLeafGeometry()
        {
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(
                    new WpfPoint(3.0, 0.0),
                    true,
                    true);

                context.LineTo(new WpfPoint(3.8, 1.1), true, false);
                context.LineTo(new WpfPoint(5.4, 0.8), true, false);
                context.LineTo(new WpfPoint(4.7, 2.2), true, false);
                context.LineTo(new WpfPoint(6.0, 3.0), true, false);
                context.LineTo(new WpfPoint(4.2, 3.6), true, false);
                context.LineTo(new WpfPoint(3.6, 5.5), true, false);
                context.LineTo(new WpfPoint(3.0, 4.5), true, false);
                context.LineTo(new WpfPoint(2.4, 5.5), true, false);
                context.LineTo(new WpfPoint(1.8, 3.6), true, false);
                context.LineTo(new WpfPoint(0.0, 3.0), true, false);
                context.LineTo(new WpfPoint(1.3, 2.2), true, false);
                context.LineTo(new WpfPoint(0.6, 0.8), true, false);
                context.LineTo(new WpfPoint(2.2, 1.1), true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        private static DoubleAnimationUsingKeyFrames CreateEnvelope(
            double peak,
            TimeSpan duration,
            TimeSpan begin)
        {
            var envelope = new DoubleAnimationUsingKeyFrames
            {
                Duration = duration,
                BeginTime = begin
            };

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0,
                    KeyTime.FromPercent(0)));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    peak,
                    KeyTime.FromPercent(0.30),
                    SineInOut));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    peak * 0.9,
                    KeyTime.FromPercent(0.55),
                    SineInOut));

            envelope.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0,
                    KeyTime.FromPercent(1.0),
                    SineInOut));

            return envelope;
        }

        /// <summary>
        /// A soft haze with 4-6 parallel strands.
        /// </summary>
        private RibbonDrawingHost CreateBundle(double length)
        {
            var strands = new List<RibbonStrand>(7);

            double sign = _random.Next(2) == 0 ? -1 : 1;

            double a1 = sign * Between(1.0, 2.0);
            double a2 = -sign * Between(0.8, 1.6);

            double end = Between(-1.2, 1.2);
            double fan = Between(1.4, 2.0);

            double f1 = Between(0.24, 0.34);
            double f2 = Between(0.64, 0.74);

            // Soft haze behind the strands.
            strands.Add(new RibbonStrand(
                CreateRibbon(
                    length,
                    0,
                    a1,
                    a2,
                    end,
                    2.2,
                    f1,
                    f2),
                0.20));

            int strandCount = _random.Next(4, 7);

            for (int k = 0; k < strandCount; k++)
            {
                double t = strandCount == 1
                    ? 0
                    : (2.0 * k / (strandCount - 1)) - 1.0;

                double j = Between(-0.25, 0.25);

                double opacity =
                    (1.0 - 0.4 * Math.Abs(t))
                    * Between(0.70, 1.0);

                Geometry geometry = CreateRibbon(
                    length * Between(0.80, 1.0),
                    t * fan * 0.15,
                    a1 + t * fan * 1.0 + j,
                    a2 + t * fan * 1.6 + j,
                    end + t * fan * 0.6,
                    Between(0.35, 0.70),
                    f1 + Between(-0.03, 0.03),
                    f2 + Between(-0.03, 0.03));

                strands.Add(new RibbonStrand(
                    geometry,
                    opacity));
            }

            return new RibbonDrawingHost(length, strands);
        }

        private static Geometry CreateRibbon(
            double length,
            double y0,
            double y1,
            double y2,
            double y3,
            double h,
            double f1,
            double f2)
        {
            var p0 = new WpfPoint(0, Pad + y0);

            var c1 = new WpfPoint(
                length * f1,
                Pad + y1);

            var c2 = new WpfPoint(
                length * f2,
                Pad + y2);

            var p3 = new WpfPoint(
                length,
                Pad + y3);

            var offset = new Vector(0, h);

            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(p0, true, true);

                ctx.BezierTo(
                    c1 - offset,
                    c2 - offset,
                    p3,
                    true,
                    false);

                ctx.BezierTo(
                    c2 + offset,
                    c1 + offset,
                    p0,
                    true,
                    false);
            }

            geometry.Freeze();

            return geometry;
        }
        private void ClearTrails()
        {
            foreach (UIElement element in Layer.Children)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);

                if (element is FrameworkElement frameworkElement)
                {
                    frameworkElement.BeginAnimation(
                        FrameworkElement.WidthProperty, null);

                    frameworkElement.BeginAnimation(
                        FrameworkElement.HeightProperty, null);

                    if (frameworkElement.RenderTransform is TransformGroup group)
                    {
                        foreach (var transform in group.Children)
                        {
                            if (transform is TranslateTransform translate)
                            {
                                translate.BeginAnimation(
                                    TranslateTransform.XProperty, null);

                                translate.BeginAnimation(
                                    TranslateTransform.YProperty, null);
                            }

                            if (transform is RotateTransform rotate)
                            {
                                rotate.BeginAnimation(
                                    RotateTransform.AngleProperty, null);
                            }
                        }
                    }
                }
            }

            Layer.Children.Clear();
        }
        private static WpfBrush CreateTrailBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new WpfPoint(0, 0.5),
                EndPoint = new WpfPoint(1, 0.5)
            };

            brush.GradientStops.Add(
                new GradientStop(
                    WpfColor.FromArgb(0, 232, 238, 247),
                    0.00));

            brush.GradientStops.Add(
                new GradientStop(
                    WpfColor.FromArgb(110, 232, 238, 247),
                    0.35));

            brush.GradientStops.Add(
                new GradientStop(
                    WpfColor.FromArgb(215, 240, 245, 252),
                    0.68));

            brush.GradientStops.Add(
                new GradientStop(
                    WpfColor.FromArgb(0, 240, 245, 252),
                    1.00));

            brush.Freeze();

            return brush;
        }

        // ------------------------------------------------------------
        // Utilities
        // ------------------------------------------------------------

        private double Between(double min, double max) =>
            min + _random.NextDouble() * (max - min);

        private static T Frozen<T>(T freezable)
            where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Stop();

            _timer.Tick -= Timer_Tick;

            Unloaded -= KazeSunlitBreezeEffect_Unloaded;
            IsVisibleChanged -= KazeSunlitBreezeEffect_IsVisibleChanged;

            _isDisposed = true;
        }
    }
}
