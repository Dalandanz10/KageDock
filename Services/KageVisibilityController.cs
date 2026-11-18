
using System;
using System.Windows;
using System.Windows.Threading;
using KageDock.Models;

namespace KageDock.Services
{
    public sealed class KageVisibilityController : IDisposable
    {
        private readonly FrameworkElement _kageContainer;
        private readonly FrameworkElement _bubbleContainer;
        private readonly FullscreenDetectionService _detectionService;
        private readonly Dispatcher _dispatcher;
        private readonly Func<DockConfig> _getConfig;

        private bool _disposed;
        private bool _isHidden;

        public KageVisibilityController(
            FrameworkElement kageContainer,
            FrameworkElement bubbleContainer,
            FullscreenDetectionService detectionService,
            Func<DockConfig> getConfig)
        {
            _kageContainer = kageContainer;
            _bubbleContainer = bubbleContainer;
            _detectionService = detectionService;
            _getConfig = getConfig;

            _dispatcher = kageContainer.Dispatcher;

            _detectionService.FullscreenStateChanged += OnDetectionChanged;

            UpdateVisibility();
        }

        private void OnDetectionChanged(bool isFullscreen)
        {
            UpdateVisibility();
        }

        public void UpdateVisibility()
        {
            if (_disposed)
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(
                    new Action(UpdateVisibility),
                    DispatcherPriority.Normal);

                return;
            }

            DockConfig config = _getConfig();

            bool shouldHide =
            config.HideKageInFullscreen &&
            _detectionService.IsFullscreen;

            if (_isHidden == shouldHide)
                return;

            _isHidden = shouldHide;

            Visibility visibility = shouldHide
                ? Visibility.Hidden
                : Visibility.Visible;

            _kageContainer.Visibility = visibility;
            _bubbleContainer.Visibility = visibility;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _detectionService.FullscreenStateChanged -= OnDetectionChanged;
        }
    }
}
