
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media.Control;

namespace KageDock.Services
{
    public sealed class SpotifyMediaSessionService : IDisposable
    {
        private readonly Dispatcher _dispatcher;

        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private Task? _initializationTask;
        private bool _disposed;

        public GlobalSystemMediaTransportControlsSession? CurrentSession
        {
            get;
            private set;
        }

        public event Action<GlobalSystemMediaTransportControlsSession?>? SessionChanged;

        public event Action<
            GlobalSystemMediaTransportControlsSession,
            MediaPropertiesChangedEventArgs>? MediaPropertiesChanged;

        public event Action<
            GlobalSystemMediaTransportControlsSession,
            PlaybackInfoChangedEventArgs>? PlaybackInfoChanged;

        public event Action<
            GlobalSystemMediaTransportControlsSession,
            TimelinePropertiesChangedEventArgs>? TimelinePropertiesChanged;

        public SpotifyMediaSessionService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ??
                throw new ArgumentNullException(nameof(dispatcher));
        }

        public Task InitializeAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_initializationTask?.IsFaulted == true ||
                _initializationTask?.IsCanceled == true)
            {
                _initializationTask = null;
            }

            return _initializationTask ??= InitializeCoreAsync();
        }

        private async Task InitializeCoreAsync()
        {
            try
            {
                _manager = await Task.Run(async () =>
                    await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

                if (_disposed)
                {
                    _manager = null;
                    return;
                }

                _manager.SessionsChanged += Manager_SessionsChanged;
                _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;

                await RefreshSessionAsync();
            }
            catch
            {
                // Clean up any partially initialized manager.
                if (_manager != null)
                {
                    _manager.SessionsChanged -= Manager_SessionsChanged;
                    _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
                    _manager = null;
                }

                DetachSessionEvents();
                CurrentSession = null;

                // Initialization errors are reported by the consuming view.
                throw;
            }
        }

        private void Manager_SessionsChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            SessionsChangedEventArgs args)
        {
            _ = _dispatcher.InvokeAsync(RefreshSessionAsync)
                .Task.Unwrap();
        }

        private void Manager_CurrentSessionChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            CurrentSessionChangedEventArgs args)
        {
            _ = _dispatcher.InvokeAsync(RefreshSessionAsync)
                .Task.Unwrap();
        }

        private Task RefreshSessionAsync()
        {
            if (_disposed || _manager == null)
                return Task.CompletedTask;

            var sessions = _manager.GetSessions();

            var session = sessions.FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains(
                    "Spotify",
                    StringComparison.OrdinalIgnoreCase));

            if (ReferenceEquals(CurrentSession, session))
                return Task.CompletedTask;

            DetachSessionEvents();

            CurrentSession = session;

            if (CurrentSession != null)
            {
                CurrentSession.MediaPropertiesChanged +=
                    Session_MediaPropertiesChanged;

                CurrentSession.PlaybackInfoChanged +=
                    Session_PlaybackInfoChanged;

                CurrentSession.TimelinePropertiesChanged +=
                    Session_TimelinePropertiesChanged;
            }

            SessionChanged?.Invoke(CurrentSession);

            return Task.CompletedTask;
        }

        private void Session_MediaPropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args)
        {
            Dispatch(() =>
            {
                if (!_disposed && ReferenceEquals(CurrentSession, sender))
                    MediaPropertiesChanged?.Invoke(sender, args);
            });
        }

        private void Session_PlaybackInfoChanged(
            GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args)
        {
            Dispatch(() =>
            {
                if (!_disposed && ReferenceEquals(CurrentSession, sender))
                    PlaybackInfoChanged?.Invoke(sender, args);
            });
        }

        private void Session_TimelinePropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            TimelinePropertiesChangedEventArgs args)
        {
            Dispatch(() =>
            {
                if (!_disposed && ReferenceEquals(CurrentSession, sender))
                    TimelinePropertiesChanged?.Invoke(sender, args);
            });
        }

        private void Dispatch(Action action)
        {
            if (_disposed)
                return;

            if (_dispatcher.CheckAccess())
                action();
            else
                _dispatcher.BeginInvoke(action);
        }

        private void DetachSessionEvents()
        {
            if (CurrentSession == null)
                return;

            CurrentSession.MediaPropertiesChanged -=
                Session_MediaPropertiesChanged;

            CurrentSession.PlaybackInfoChanged -=
                Session_PlaybackInfoChanged;

            CurrentSession.TimelinePropertiesChanged -=
                Session_TimelinePropertiesChanged;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_manager != null)
            {
                _manager.SessionsChanged -= Manager_SessionsChanged;
                _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
                _manager = null;
            }

            DetachSessionEvents();
            CurrentSession = null;

            SessionChanged = null;
            MediaPropertiesChanged = null;
            PlaybackInfoChanged = null;
            TimelinePropertiesChanged = null;
        }
    }
}
