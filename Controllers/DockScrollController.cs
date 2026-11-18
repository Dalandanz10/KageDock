
using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KageDock.Controllers
{
    public sealed class DockScrollController : IDisposable
    {
        private readonly ScrollViewer _scrollViewer;

        private double _targetScroll;
        private bool _scrollRenderingAttached;
        private DateTime _lastScrollFrame = DateTime.MinValue;
        private bool _disposed;

        public DockScrollController(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;

            _scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private void OnPreviewMouseWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            if (_disposed)
                return;

            e.Handled = true;

            double baseOffset = _scrollRenderingAttached
                ? _targetScroll
                : _scrollViewer.VerticalOffset;

            double maxOffset = Math.Max(
                0,
                _scrollViewer.ExtentHeight - _scrollViewer.ViewportHeight);

            _targetScroll = Math.Clamp(
                baseOffset - e.Delta * 0.65,
                0,
                maxOffset);

            if (!_scrollRenderingAttached)
            {
                CompositionTarget.Rendering += SmoothScroll_Rendering;
                _scrollRenderingAttached = true;
            }
        }

        private void SmoothScroll_Rendering(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                StopSmoothScroll();
                return;
            }

            if (!_scrollViewer.IsVisible)
            {
                StopSmoothScroll();
                return;
            }

            var now = DateTime.UtcNow;

            if (_lastScrollFrame != DateTime.MinValue)
            {
                double elapsed = (now - _lastScrollFrame).TotalSeconds;

                // Ignore unusually large frame gaps.
                if (elapsed > 0.05)
                {
                    _lastScrollFrame = now;
                    return;
                }

                double current = _scrollViewer.VerticalOffset;
                double difference = _targetScroll - current;

                if (Math.Abs(difference) < 0.5)
                {
                    _scrollViewer.ScrollToVerticalOffset(_targetScroll);
                    StopSmoothScroll();
                    return;
                }

                // Frame-rate-independent smoothing.
                double blend = 1.0 - Math.Exp(-14.0 * elapsed);

                _scrollViewer.ScrollToVerticalOffset(
                    current + difference * blend);
            }

            _lastScrollFrame = now;
        }

        private void StopSmoothScroll()
        {
            if (_scrollRenderingAttached)
            {
                CompositionTarget.Rendering -= SmoothScroll_Rendering;
                _scrollRenderingAttached = false;
            }

            _lastScrollFrame = DateTime.MinValue;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;

            StopSmoothScroll();
        }
    }
}
