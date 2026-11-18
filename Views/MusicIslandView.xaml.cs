
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Collections.Generic;
using KageDock.Models;

namespace KageDock.Views
{
    public partial class MusicIslandView : System.Windows.Controls.UserControl
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private Task? _initializationTask;
        private bool _isShutdown;
        private GlobalSystemMediaTransportControlsSession? _spotifySession;
        private bool _hasPlaybackStatus;
        private bool _wasPlaying;
        private bool _isRenderingProgress;
        private readonly Stopwatch _progressClock = new();
        private string? _cachedArtworkKey;
        private BitmapImage? _cachedArtwork;
        private int _artworkRequestVersion;
        private double _durationSeconds;
        private double _positionAtSync;
        private bool _updatingProgress;
        private bool _seekSyncPending;
        private bool _isSeeking;
        private bool _trackChangePending;
        private bool _particleEmissionActive;
        public event Action<KageDialogueCategory>? ReactionRequested;
        public event Action<bool>? PlaybackStateChanged;
        private sealed class ProgressParticle
        {
            public required Ellipse Shape { get; init; }
            public double X { get; set; }
            public double Y { get; set; }
            public double VelocityX { get; set; }
            public double VelocityY { get; set; }
            public double Age { get; set; }
            public double Lifetime { get; set; }
        }

        private readonly List<ProgressParticle> _progressParticles = new();

        private readonly Random _particleRandom = new();

        private TimeSpan _lastParticleFrame;
        private double _particleSpawnAccumulator;

        public MusicIslandView()
        {
            InitializeComponent();

            PreviousButton.Click += PreviousButton_Click;
            PlayPauseButton.Click += PlayPauseButton_Click;
            NextButton.Click += NextButton_Click;

            ShuffleButton.Click += ShuffleButton_Click;
            RepeatButton.Click += RepeatButton_Click;

            Loaded += MusicIslandView_Loaded;
            Unloaded += MusicIslandView_Unloaded;

        }

        private async void MusicIslandView_Loaded(object sender, RoutedEventArgs e)
        {
            await Dispatcher.InvokeAsync(
                RestartTitleMarquee,
                DispatcherPriority.Loaded);

            // Wait for the shared initialization, including background prewarming.
            await InitializeMediaAsync();

            if (!IsLoaded || _isShutdown)
                return;

            // Refresh playback state now that the view is loaded.
            // This also attaches the progress renderer when Spotify is playing.
            await RefreshPlaybackAsync();
        }

        private void ProgressThumb_DragStarted(
            object sender,
            DragStartedEventArgs e)
        {
            _isSeeking = true;
        }

        private async void ProgressThumb_DragCompleted(
            object sender,
            DragCompletedEventArgs e)
        {
            _isSeeking = false;

            await SeekToPositionAsync();
        }

        private void TrackTitleViewport_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(
                new Action(RestartTitleMarquee),
                DispatcherPriority.Loaded);
        }

        private void RestartTitleMarquee()
        {
            // Stop any previous animation.
            TrackTitleTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);

            TrackTitleTransform.X = 0;

            if (_isShutdown ||
                !IsLoaded ||
                TrackTitleViewport.ActualWidth <= 0)
                return;

            // Measure the title at its natural width.
            var formattedText = new FormattedText(
                TrackTitle.Text,
                CultureInfo.CurrentUICulture,
                TrackTitle.FlowDirection,
                new Typeface(
                    TrackTitle.FontFamily,
                    TrackTitle.FontStyle,
                    TrackTitle.FontWeight,
                    TrackTitle.FontStretch),
                TrackTitle.FontSize,
                TrackTitle.Foreground,
                VisualTreeHelper.GetDpi(TrackTitle).PixelsPerDip);

            double textWidth = formattedText.WidthIncludingTrailingWhitespace;
            double viewportWidth = TrackTitleViewport.ActualWidth;

            // No animation needed when the title fits.
            double requiredWidth = textWidth + TrackTitle.Margin.Left + 3;

            if (requiredWidth <= viewportWidth)
            {
                TrackTitle.Width = double.NaN;
                return;
            }

            TrackTitle.Width = textWidth;

            // Small extra travel distance so the final character clears the clipped edge.
            double distance =
            textWidth + TrackTitle.Margin.Left - viewportWidth + 3;

            // Slow movement: approximately 25 pixels per second.
            double travelSeconds = distance / 25.0;

            // Pause at both ends.
            double startPause = 1.2;
            double endPause = 1.2;

            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(
                    startPause + travelSeconds + endPause + travelSeconds),
                RepeatBehavior = RepeatBehavior.Forever
            };

            double leftPosition = -distance;

            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));

            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(startPause))));

            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    leftPosition,
                    KeyTime.FromTimeSpan(
                        TimeSpan.FromSeconds(startPause + travelSeconds))));

            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    leftPosition,
                    KeyTime.FromTimeSpan(
                        TimeSpan.FromSeconds(startPause + travelSeconds + endPause))));

            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(
                        TimeSpan.FromSeconds(
                            startPause + travelSeconds + endPause + travelSeconds))));

            TrackTitleTransform.BeginAnimation(
                TranslateTransform.XProperty,
                animation);
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_spotifySession == null)
            {
                StopParticleRendering();

                _progressParticles.Clear();
                ProgressParticleCanvas.Children.Clear();

                CompositionTarget.Rendering -= CompositionTarget_Rendering;
                _isRenderingProgress = false;

                return;
            }

            if (!_isSeeking && !_seekSyncPending && _progressClock.IsRunning)
                _ = RefreshProgressAsync();

            UpdateProgressParticles(e);
        }
        private void SetIconGlow(
            FrameworkElement icon,
            bool isActive)
        {
            icon.Opacity = isActive ? 1.0 : 0.45;

            icon.Effect = isActive
                ? new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.White,
                    BlurRadius = 7,
                    ShadowDepth = 0,
                    Opacity = 0.55
                }
                : null;
        }

        private async void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotifySession == null)
                return;

            try
            {
                var playback = _spotifySession.GetPlaybackInfo();

                if (!playback.Controls.IsRepeatEnabled)
                    return;

                var nextMode = playback.AutoRepeatMode switch
                {
                    Windows.Media.MediaPlaybackAutoRepeatMode.None =>
                        Windows.Media.MediaPlaybackAutoRepeatMode.List,

                    Windows.Media.MediaPlaybackAutoRepeatMode.List =>
                        Windows.Media.MediaPlaybackAutoRepeatMode.Track,

                    _ =>
                        Windows.Media.MediaPlaybackAutoRepeatMode.None
                };

                bool success = await _spotifySession.TryChangeAutoRepeatModeAsync(nextMode);

                if (success)
                    await RefreshPlaybackAsync();
            }
            catch
            {
                // Spotify may not support repeat control in the current session.
            }
        }

        private async void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotifySession == null)
                return;

            try
            {
                var playback = _spotifySession.GetPlaybackInfo();

                bool currentState = playback.IsShuffleActive == true;

                await _spotifySession.TryChangeShuffleActiveAsync(!currentState);

                await RefreshPlaybackAsync();
            }
            catch
            {
                // Spotify may not support shuffle control in the current session.
            }
        }

        private async void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotifySession == null)
                return;

            _trackChangePending = true;



            try
            {
                bool skipped = await _spotifySession.TrySkipPreviousAsync();

               if (!skipped)
                _trackChangePending = false;
            }
            catch
            {
                _trackChangePending = false;
                // Spotify may no longer have an active media session.
            }
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotifySession == null)
                return;

            try
            {
                await _spotifySession.TryTogglePlayPauseAsync();
                await RefreshPlaybackAsync();
            }
            catch
            {
                // The media session may have disappeared.
            }
        }

            private async void NextButton_Click(object sender, RoutedEventArgs e)
            {
                if (_spotifySession == null)
                    return;

                _trackChangePending = true;


                try
                {
                    bool skipped = await _spotifySession.TrySkipNextAsync();

                    if (!skipped)
                     _trackChangePending = false;
                }
                catch
                {
                    _trackChangePending = false;
                    // Spotify may no longer have an active media session.
                }
            }

            private async void Spotify_TimelinePropertiesChanged(
                GlobalSystemMediaTransportControlsSession sender,
                TimelinePropertiesChangedEventArgs args)
            {

                await Dispatcher.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_spotifySession, sender))
                        return;

                    SyncTimelineFromWindows(timelineChanged: true);
                    _ = RefreshProgressAsync();
                });
            }
            
            
            private void SyncTimelineFromWindows(bool timelineChanged = false)                {
                    if (_spotifySession == null || _isSeeking)
                        return;

                    try
                    {
                        var timeline = _spotifySession.GetTimelineProperties();

                        double duration =
                            (timeline.EndTime - timeline.StartTime).TotalSeconds;

                        double position =
                            (timeline.Position - timeline.StartTime).TotalSeconds;

                        if (duration <= 0)
                            return;

                        _durationSeconds = duration;
                        position = Math.Clamp(position, 0, duration);

                        // Compare Windows' position with our smoothly predicted position.
                        double predictedPosition = _positionAtSync;

                        if (_progressClock.IsRunning)
                            predictedPosition += _progressClock.Elapsed.TotalSeconds;

                        // Only resynchronize when the difference is significant.
                        bool acceptTrackChange =
                            _trackChangePending && timelineChanged;

                        bool acceptSeek =
                            _seekSyncPending && timelineChanged;

                        if (acceptTrackChange || acceptSeek ||
                            (!_seekSyncPending && (!_progressClock.IsRunning ||
                            position - predictedPosition > 0.75)))
                        {
                            _positionAtSync = position;

                            if (_progressClock.IsRunning)
                                _progressClock.Restart();
                            else
                                _progressClock.Reset();

                            if (acceptTrackChange)
                                _trackChangePending = false;

                            if (acceptSeek)
                                _seekSyncPending = false;
                        }
                    }
                    catch
                    {
                        // Timeline may temporarily be unavailable.
                    }
                }  
            
                private Task RefreshProgressAsync()
                {
                    if (_spotifySession == null || _isSeeking || _durationSeconds <= 0)
                        return Task.CompletedTask;

                    bool playing = _progressClock.IsRunning;

                    double displayPosition = _positionAtSync;

                    if (playing)
                        displayPosition += _progressClock.Elapsed.TotalSeconds;

                    displayPosition = Math.Clamp(displayPosition, 0, _durationSeconds);

                    _updatingProgress = true;

                    try
                    {
                        ProgressSlider.Minimum = 0;
                        ProgressSlider.Maximum = _durationSeconds;
                        ProgressSlider.Value = displayPosition;
                        UpdateProgressFill();
                        ProgressSlider.IsEnabled = true;

                        ElapsedText.Text = FormatTime(displayPosition);
                        RemainingText.Text =
                            "-" + FormatTime(_durationSeconds - displayPosition);
                    }
                    finally
                    {
                        _updatingProgress = false;
                    }

                    return Task.CompletedTask;
                }

                private void StartParticleRendering()
                {
                    _particleEmissionActive = true;
                    _lastParticleFrame = TimeSpan.Zero;
                }

                private void StopParticleRendering()
                {
                    _particleEmissionActive = false;
                    _particleSpawnAccumulator = 0;
                }

                private void EmitProgressParticle(double x)
                {
                    if (ProgressParticleCanvas.ActualWidth <= 0)
                        return;

                    var shape = new Ellipse
                    {
                        Width = 1.4 + _particleRandom.NextDouble() * 1.2,
                        Height = 1.4 + _particleRandom.NextDouble() * 1.2,
                        Fill = System.Windows.Media.Brushes.White,
                        Opacity = 1.0,
                        IsHitTestVisible = false
                    };

                    ProgressParticleCanvas.Children.Add(shape);

                    _progressParticles.Add(new ProgressParticle
                    {
                        Shape = shape,
                        X = x,
                        Y = 7,
                        VelocityX = -(35 + _particleRandom.NextDouble() * 65),
                        VelocityY = (_particleRandom.NextDouble() - 0.5) * 55,
                        Lifetime = 0.45 + _particleRandom.NextDouble() * 0.4
                    });
                }

                private void UpdateProgressParticles(EventArgs e)
                {
                    if (ProgressParticleCanvas.ActualWidth <= 0)
                        return;

                    if (e is not RenderingEventArgs renderingArgs)
                        return;

                    double delta = 0;

                    if (_lastParticleFrame != TimeSpan.Zero)
                    {
                        delta = (renderingArgs.RenderingTime - _lastParticleFrame).TotalSeconds;
                        delta = Math.Clamp(delta, 0, 0.05);
                    }

                    _lastParticleFrame = renderingArgs.RenderingTime;

                    bool playing = _particleEmissionActive && _progressClock.IsRunning;

                    if (playing)
                    {
                        double range = ProgressSlider.Maximum - ProgressSlider.Minimum;

                        if (range > 0)
                        {
                            double progress =
                                (ProgressSlider.Value - ProgressSlider.Minimum) / range;

                            double thumbX = Math.Clamp(progress, 0, 1) *
                                            ProgressParticleCanvas.ActualWidth;

                            _particleSpawnAccumulator += delta;

                            while (_particleSpawnAccumulator >= 0.045)
                            {
                                _particleSpawnAccumulator -= 0.045;

                                if (_progressParticles.Count < 30)
                                    EmitProgressParticle(thumbX);
                            }
                        }
                    }
                    else
                    {
                        _particleSpawnAccumulator = 0;
                    }

                    for (int i = _progressParticles.Count - 1; i >= 0; i--)
                    {
                        var particle = _progressParticles[i];

                        particle.Age += delta;

                        if (particle.Age >= particle.Lifetime)
                        {
                            ProgressParticleCanvas.Children.Remove(particle.Shape);
                            _progressParticles.RemoveAt(i);
                            continue;
                        }

                        particle.X += particle.VelocityX * delta;
                        particle.Y += particle.VelocityY * delta;

                        double fade = 1 - particle.Age / particle.Lifetime;

                        particle.Shape.Opacity = fade;

                        Canvas.SetLeft(
                            particle.Shape,
                            particle.X - particle.Shape.Width / 2);

                        Canvas.SetTop(
                            particle.Shape,
                            particle.Y - particle.Shape.Height / 2);
                    }

                    if (!playing && _progressParticles.Count == 0)
                    {
                        _particleSpawnAccumulator = 0;

                        if (_isRenderingProgress)
                        {
                            CompositionTarget.Rendering -= CompositionTarget_Rendering;
                            _isRenderingProgress = false;
                        }
                    }
                }

           private void UpdateProgressFill()
            {
                ProgressSlider.ApplyTemplate();

                var progressFill =
                    ProgressSlider.Template.FindName(
                        "ProgressFill", ProgressSlider) as Border;

                var track =
                    ProgressSlider.Template.FindName(
                        "PART_Track", ProgressSlider) as Track;

                if (progressFill == null || track == null)
                    return;

                double trackWidth = track.ActualWidth;

                if (trackWidth <= 0)
                    return;

                double range = ProgressSlider.Maximum - ProgressSlider.Minimum;

                if (range <= 0)
                    return;

                double progress =
                    (ProgressSlider.Value - ProgressSlider.Minimum) / range;

                progressFill.Width =
                    trackWidth * Math.Clamp(progress, 0, 1);
            }

            private async Task SeekToPositionAsync()
            {
                if (_updatingProgress || _spotifySession == null)
                    return;

                double targetPosition = Math.Clamp(
                    ProgressSlider.Value,
                    0,
                    _durationSeconds);

                bool wasPlaying = _progressClock.IsRunning;

                try
                {
                    var timeline = _spotifySession.GetTimelineProperties();

                    long targetTicks =
                        timeline.StartTime.Ticks +
                        TimeSpan.FromSeconds(targetPosition).Ticks;

                    targetTicks = Math.Clamp(
                        targetTicks,
                        timeline.StartTime.Ticks,
                        timeline.EndTime.Ticks);

                    _isSeeking = true;
                    _seekSyncPending = true;

                    // Immediately update our local progress prediction.
                    _positionAtSync = targetPosition;

                    if (wasPlaying)
                        _progressClock.Restart();
                    else
                        _progressClock.Reset();

                    await _spotifySession.TryChangePlaybackPositionAsync(targetTicks);

                }
                catch
                {
                    _seekSyncPending = false;

                    // Seeking may not be supported by the current session.
                }
                finally
                {
                    _isSeeking = false;
                }

                await RefreshProgressAsync();
            }

            private static string FormatTime(double seconds)
            {
                var time = TimeSpan.FromSeconds(Math.Max(0, seconds));

                return time.TotalHours >= 1
                    ? time.ToString(@"h\:mm\:ss")
                    : time.ToString(@"m\:ss");
            }

        public Task InitializeMediaAsync()
        {
            if (_isShutdown)
                return Task.CompletedTask;

            return _initializationTask ??= InitializeMediaCoreAsync();
        }

        private async Task InitializeMediaCoreAsync()
        {
            try
            {
                ConnectionText.Text = "CONNECTING";

                await Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.ContextIdle);

                await Task.Yield();

                if (_isShutdown)
                    return;

                _manager = await Task.Run(async () =>
                    await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

                if (_isShutdown)
                {
                    _manager = null;
                    return;
                }

                _manager.SessionsChanged += Manager_SessionsChanged;
                _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;

                await FindSpotifySessionAsync();
            }
            catch (Exception ex)
            {
                if (!_isShutdown)
                {
                    ConnectionText.Text = "UNAVAILABLE";
                    TrackTitle.Text = "Media access unavailable";
                    ArtistName.Text = ex.Message;
                }
            }
        }

        private void MusicIslandView_Unloaded(object sender, RoutedEventArgs e)
            {
                if (_isRenderingProgress)
                {
                    CompositionTarget.Rendering -= CompositionTarget_Rendering;
                    _isRenderingProgress = false;
                }

                StopParticleRendering();

                _progressParticles.Clear();
                ProgressParticleCanvas.Children.Clear();

                // Stop the title marquee.
                TrackTitleTransform.BeginAnimation(
                    TranslateTransform.XProperty,
                    null);

                TrackTitleTransform.X = 0;

                // Keep Spotify session monitoring active while the view is unloaded.
            }

        public void Shutdown()
            {
                if (_isShutdown)
                    return;

                _isShutdown = true;

                // Stop title marquee animation.
                TrackTitleTransform.BeginAnimation(
                    TranslateTransform.XProperty,
                    null);

                TrackTitleTransform.X = 0;

                if (_isRenderingProgress)
                {
                    CompositionTarget.Rendering -= CompositionTarget_Rendering;
                    _isRenderingProgress = false;
                }

                StopParticleRendering();

            _progressParticles.Clear();
            ProgressParticleCanvas.Children.Clear();

            if (_manager != null)
            {
                _manager.SessionsChanged -= Manager_SessionsChanged;
                _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
                _manager = null;
            }

            if (_spotifySession != null)
            {
                _spotifySession.MediaPropertiesChanged -= Spotify_MediaPropertiesChanged;
                _spotifySession.PlaybackInfoChanged -= Spotify_PlaybackInfoChanged;
                _spotifySession.TimelinePropertiesChanged -= Spotify_TimelinePropertiesChanged;

                _spotifySession = null;

                _artworkRequestVersion++;
                _cachedArtworkKey = null;
                _cachedArtwork = null;
            }

            _hasPlaybackStatus = false;
            _wasPlaying = false;

            PlaybackStateChanged?.Invoke(false);
        }

        private async void Manager_SessionsChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            SessionsChangedEventArgs args)
        {


            await Dispatcher.InvokeAsync(FindSpotifySessionAsync)
                .Task.Unwrap();
        }

        private async void Manager_CurrentSessionChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            CurrentSessionChangedEventArgs args)
        {

            await Dispatcher.InvokeAsync(FindSpotifySessionAsync)
            .Task.Unwrap();
        }

        private async Task FindSpotifySessionAsync()
        {
            if (_manager == null)
                return;

            var sessions = _manager.GetSessions();

            foreach (var item in sessions)
            {

            }

            var session = sessions.FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains(
                    "Spotify",
                    StringComparison.OrdinalIgnoreCase));


            if (session == null)
            {
                if (_spotifySession != null)
                {
                    _spotifySession.MediaPropertiesChanged -= Spotify_MediaPropertiesChanged;
                    _spotifySession.PlaybackInfoChanged -= Spotify_PlaybackInfoChanged;
                    _spotifySession.TimelinePropertiesChanged -= Spotify_TimelinePropertiesChanged;

                    _artworkRequestVersion++;
                    _cachedArtworkKey = null;
                    _cachedArtwork = null;

                    _hasPlaybackStatus = false;
                    _wasPlaying = false;

                    PlaybackStateChanged?.Invoke(false);

                    _spotifySession = null;
                }

                // Stop progress tracking for the missing session.
                _progressClock.Reset();
                _positionAtSync = 0;
                _durationSeconds = 0;
                _seekSyncPending = false;
                _trackChangePending = false;

                // Stop and clear particle rendering.
                StopParticleRendering();
                _progressParticles.Clear();
                ProgressParticleCanvas.Children.Clear();

                if (_isRenderingProgress)
                {
                    CompositionTarget.Rendering -= CompositionTarget_Rendering;
                    _isRenderingProgress = false;
                }

                ConnectionText.Text = "SPOTIFY NOT FOUND";

                ConnectionText.Text = "SPOTIFY NOT FOUND";
                TrackTitle.Text = "Open Spotify Desktop";
                ArtistName.Text = "Start playing a song";
                AlbumName.Text = "";
                PlayIcon.Visibility = Visibility.Visible;
                PauseIcon.Visibility = Visibility.Collapsed;
                PreviousButton.IsEnabled = false;
                PlayPauseButton.IsEnabled = false;
                NextButton.IsEnabled = false;
                ProgressSlider.IsEnabled = false;
                AlbumArt.Source = null;
                AlbumPlaceholder.Visibility = Visibility.Visible;

                return;
            }

            if (!ReferenceEquals(_spotifySession, session))
            {
                if (_spotifySession != null)
                {
                    _spotifySession.MediaPropertiesChanged -= Spotify_MediaPropertiesChanged;
                    _spotifySession.PlaybackInfoChanged -= Spotify_PlaybackInfoChanged;
                    _spotifySession.TimelinePropertiesChanged -= Spotify_TimelinePropertiesChanged;

                }

                _hasPlaybackStatus = false;
                _wasPlaying = false;

                PlaybackStateChanged?.Invoke(false);

                _spotifySession = session;
                _spotifySession.MediaPropertiesChanged += Spotify_MediaPropertiesChanged;
                _spotifySession.PlaybackInfoChanged += Spotify_PlaybackInfoChanged;
                _spotifySession.TimelinePropertiesChanged += Spotify_TimelinePropertiesChanged;

                ReactionRequested?.Invoke(KageDialogueCategory.Curious);
            }

            ConnectionText.Text = "SPOTIFY CONNECTED";

            await RefreshTrackAsync();
            await RefreshPlaybackAsync();

            SyncTimelineFromWindows();
            await RefreshProgressAsync();
        }

        private async void Spotify_MediaPropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args)
        {

            await Dispatcher.InvokeAsync(async () =>
            {
                if (!ReferenceEquals(_spotifySession, sender))
                    return;
                
                ReactionRequested?.Invoke(KageDialogueCategory.MusicSkip);

                // A new track may legitimately start at an earlier position.
                _trackChangePending = true;

                _progressClock.Reset();
                _positionAtSync = 0;
                _durationSeconds = 0;

                _updatingProgress = true;

                try
                {
                    ProgressSlider.Value = 0;
                    ElapsedText.Text = "0:00";
                    RemainingText.Text = "-0:00";

                    UpdateProgressFill();
                }
                finally
                {
                    _updatingProgress = false;
                }

                await RefreshTrackAsync();
                await RefreshPlaybackAsync();

                SyncTimelineFromWindows();
                await RefreshProgressAsync();
            }).Task.Unwrap();
        }

        private async void Spotify_PlaybackInfoChanged(
            GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args)
        {

            await Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_spotifySession, sender))
                    return Task.CompletedTask;

                return RefreshPlaybackAsync();
            }).Task.Unwrap();
        }

        
        
        private async Task RefreshTrackAsync()
        {
            var session = _spotifySession;

            if (session == null)
                return;

            // Identify this refresh so an older request cannot overwrite newer artwork.
            int requestVersion = ++_artworkRequestVersion;

            try
            {
                var info = await session.TryGetMediaPropertiesAsync();

                // Ignore results from an outdated request or replaced session.
                if (!ReferenceEquals(_spotifySession, session) ||
                    requestVersion != _artworkRequestVersion)
                {
                    return;
                }

                TrackTitle.Text = string.IsNullOrWhiteSpace(info.Title)
                    ? "Unknown track"
                    : info.Title;

                TrackTitle.Opacity = 0;
                Canvas.SetTop(TrackTitle, 4);

                var titleEntrance = new System.Windows.Media.Animation.Storyboard();

                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(150)
                };

                var rise = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 4,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(150)
                };

                System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, TrackTitle);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(
                    fadeIn, new PropertyPath(UIElement.OpacityProperty));

                System.Windows.Media.Animation.Storyboard.SetTarget(rise, TrackTitle);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(
                    rise, new PropertyPath("(Canvas.Top)"));

                titleEntrance.Children.Add(fadeIn);
                titleEntrance.Children.Add(rise);
                titleEntrance.Begin();

                await Dispatcher.InvokeAsync(
                    RestartTitleMarquee,
                    DispatcherPriority.Loaded);

                ArtistName.Text = string.IsNullOrWhiteSpace(info.Artist)
                    ? "Unknown artist"
                    : info.Artist;

                AlbumName.Text = info.AlbumTitle ?? "";

                // Build a key from the current track metadata.
                string artworkKey = string.Join(
                    "\u001F",
                    info.Title ?? "",
                    info.Artist ?? "",
                    info.AlbumTitle ?? "");

                // No thumbnail available.
                if (info.Thumbnail == null)
                {
                    _cachedArtworkKey = null;
                    _cachedArtwork = null;

                    AlbumArt.Source = null;
                    AlbumPlaceholder.Visibility = Visibility.Visible;

                    return;
                }

                // Reuse the already-decoded image when the track hasn't changed.
                if (_cachedArtwork != null &&
                    _cachedArtworkKey == artworkKey)
                {
                    AlbumArt.Source = _cachedArtwork;
                    AlbumPlaceholder.Visibility = Visibility.Collapsed;

                    return;
                }

                byte[] imageBytes;

                using (IRandomAccessStreamWithContentType stream =
                    await info.Thumbnail.OpenReadAsync())
                {
                    using var memoryStream = new System.IO.MemoryStream();

                    var buffer = new Windows.Storage.Streams.Buffer(81920);
                    uint bytesRead;

                    do
                    {
                        buffer = new Windows.Storage.Streams.Buffer(81920);

                        var result = await stream.ReadAsync(
                            buffer,
                            buffer.Capacity,
                            InputStreamOptions.None);

                        bytesRead = result.Length;

                        if (bytesRead > 0)
                        {
                            byte[] bytes = new byte[bytesRead];

                            using var reader =
                                Windows.Storage.Streams.DataReader.FromBuffer(result);

                            reader.ReadBytes(bytes);

                            await memoryStream.WriteAsync(bytes, 0, bytes.Length);
                        }
                    }
                    while (bytesRead > 0);

                    imageBytes = memoryStream.ToArray();
                }

                // Decode away from the UI thread.
                BitmapImage bitmap = await Task.Run(() =>
                {
                    using var imageStream =
                        new System.IO.MemoryStream(imageBytes);

                    var image = new BitmapImage();

                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 300;
                    image.StreamSource = imageStream;
                    image.EndInit();
                    image.Freeze();

                    return image;
                });

                // The user may have changed tracks while the image was loading.
                if (!ReferenceEquals(_spotifySession, session) ||
                    requestVersion != _artworkRequestVersion)
                {
                    return;
                }

                // Cache and display the decoded image.
                _cachedArtworkKey = artworkKey;
                _cachedArtwork = bitmap;

                AlbumArt.Source = bitmap;
                AlbumPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // Metadata or artwork may temporarily be unavailable.
            }
        }

        private async Task RefreshPlaybackAsync()
        {
            if (_spotifySession == null)
                return;

            try
            {
                var playback = _spotifySession.GetPlaybackInfo();

               bool playing = playback.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                bool hadPreviousStatus = _hasPlaybackStatus;
                bool wasPlaying = _wasPlaying;

                if (hadPreviousStatus && playing != wasPlaying)
                {
                    ReactionRequested?.Invoke(
                        playing
                            ? KageDialogueCategory.MusicGeneral
                            : KageDialogueCategory.MusicPause);
                }

                bool playbackStateChanged =
                    !hadPreviousStatus || playing != wasPlaying;

                _wasPlaying = playing;
                _hasPlaybackStatus = true;

                if (playbackStateChanged)
                    PlaybackStateChanged?.Invoke(playing);

                if (!playing)
                {
                    _positionAtSync = ProgressSlider.Value;
                    _progressClock.Reset();
                }
                else if (!_progressClock.IsRunning)
                {
                    _positionAtSync = ProgressSlider.Value;
                    _progressClock.Restart();
                }
                if (playing)
                    StartParticleRendering();
                else
                    StopParticleRendering();

                PlayIcon.Visibility = playing
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                PauseIcon.Visibility = playing
                    ? Visibility.Visible
                    : Visibility.Collapsed;


                // Shuffle state.
                bool shuffleActive = playback.IsShuffleActive == true;

                SetIconGlow(ShuffleIcon, shuffleActive);

                ShuffleButton.IsEnabled = playback.Controls.IsShuffleEnabled;

               // Repeat state.
                var repeatMode = playback.AutoRepeatMode;

                bool repeatActive =
                    repeatMode != Windows.Media.MediaPlaybackAutoRepeatMode.None;

                bool repeatOne =
                    repeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.Track;

                RepeatIcon.Visibility = repeatOne
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                RepeatOnceIcon.Visibility = repeatOne
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                SetIconGlow(RepeatIcon, repeatActive);
                SetIconGlow(RepeatOnceIcon, repeatActive);

                RepeatButton.IsEnabled = playback.Controls.IsRepeatEnabled;
                // Shared progress and particle rendering loop.
                if (IsLoaded && (playing || _progressParticles.Count > 0) &&
                    !_isRenderingProgress)
                {
                    CompositionTarget.Rendering += CompositionTarget_Rendering;
                    _isRenderingProgress = true;
                }
                else if ((!IsLoaded || (!playing && _progressParticles.Count == 0)) &&
                        _isRenderingProgress)
                {
                    CompositionTarget.Rendering -= CompositionTarget_Rendering;
                    _isRenderingProgress = false;
                }

                await RefreshProgressAsync();

                PreviousButton.IsEnabled = true;
                PlayPauseButton.IsEnabled = true;
                NextButton.IsEnabled = true;
            }
            catch
            {
                // The session may disappear while playback state is being read.
            }
        }
    }
}
