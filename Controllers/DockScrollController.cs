using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;

namespace KageDock.Controllers
{
    public sealed class DockScrollController : IDisposable
    {
        private readonly ScrollViewer _scrollViewer;
        private TaskCompletionSource<bool>? _scrollToTopCompletion;
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
        public Task ScrollToTopAsync()
        {
            return ScrollToOffsetAsync(0);
        }

        public Task ScrollToOffsetAsync(double targetOffset)
        {
            if (_disposed || !_scrollViewer.IsVisible)
                return Task.CompletedTask;

            double maxOffset = Math.Max(
                0,
                _scrollViewer.ExtentHeight - _scrollViewer.ViewportHeight);

            _targetScroll = Math.Clamp(targetOffset, 0, maxOffset);

            if (Math.Abs(_scrollViewer.VerticalOffset - _targetScroll) < 0.5)
            {
                if (_scrollRenderingAttached)
                    StopSmoothScroll(true);

                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _scrollToTopCompletion?.TrySetResult(false);
            _scrollToTopCompletion = completion;

            if (!_scrollRenderingAttached)
            {
                CompositionTarget.Rendering += SmoothScroll_Rendering;
                _scrollRenderingAttached = true;
            }

            return completion.Task;
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

                double maxOffset = Math.Max(
                    0,
                    _scrollViewer.ExtentHeight - _scrollViewer.ViewportHeight);

                // Keep the target within the current scroll range.
                _targetScroll = Math.Clamp(_targetScroll, 0, maxOffset);

                double current = _scrollViewer.VerticalOffset;
                double difference = _targetScroll - current;

                if (Math.Abs(difference) < 0.5)
                {
                    _scrollViewer.ScrollToVerticalOffset(_targetScroll);
                    StopSmoothScroll(true);
                    return;
                }

                // Frame-rate-independent smoothing.
                double blend = 1.0 - Math.Exp(-14.0 * elapsed);

                _scrollViewer.ScrollToVerticalOffset(
                    current + difference * blend);
            }

            _lastScrollFrame = now;
        }

        private void StopSmoothScroll(bool completed = false)
        {
            if (_scrollRenderingAttached)
            {
                CompositionTarget.Rendering -= SmoothScroll_Rendering;
                _scrollRenderingAttached = false;
            }

            _lastScrollFrame = DateTime.MinValue;

            _scrollToTopCompletion?.TrySetResult(completed);
            _scrollToTopCompletion = null;
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
