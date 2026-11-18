using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KageDock.Views
{
    public partial class DockWindow
    {
        private void ExpandDock()
        {
            if (_expanded)
                return;

            CancelCollapseTimer();

            _kageCoordinator?.OnDockExpanding();

            _expanded = true;

            ExpandedDockContent.Visibility = Visibility.Visible;
            KageStrip.Visibility = Visibility.Collapsed;

            _isDockOpening = true;

            AnimateDockReveal(true);
        }

            
        private void CollapseDock()
        {
            if (!_expanded)
                return;

            // Preserve the destination if a view morph is interrupted.
            if (_targetView != _currentView)
                _pendingMorphView = _targetView;

            _expanded = false;

            AnimateDockReveal(false);
        }

             
              
            private void AnimateDockReveal(bool opening)
            {
                const double collapsedWidth = 100;
                const double collapsedHeight = 28;

                var openingView = _pendingMorphView ?? _currentView ?? DockView.MyApps;
                var openSize = GetDockViewSize(openingView);

                double fullWidth = opening ? openSize.Width : MaxDockWidth;
                double fullHeight = opening ? openSize.Height : _maxDockHeight;

                if (fullWidth <= collapsedWidth || fullHeight <= collapsedHeight)
                    return;

                double startWidth = Math.Clamp(
                    DockRevealHost.ActualWidth > 0 ? DockRevealHost.ActualWidth : DockRevealHost.Width,
                    collapsedWidth, fullWidth);

                double startHeight = Math.Clamp(
                    DockRevealHost.ActualHeight > 0 ? DockRevealHost.ActualHeight : DockRevealHost.Height,
                    collapsedHeight, fullHeight);

                var oldClip = DockRevealHost.Clip as RectangleGeometry;

                Rect startRect = oldClip?.Rect ?? new Rect(0, 0, startWidth, startHeight);
                startRect = new Rect(
                    0, 0,
                    Math.Clamp(startRect.Width, collapsedWidth, fullWidth),
                    Math.Clamp(startRect.Height, collapsedHeight, fullHeight));

                DockRevealHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                DockPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
                DockRevealHost.BeginAnimation(FrameworkElement.HeightProperty, null);
                DockPanel.BeginAnimation(FrameworkElement.HeightProperty, null);

                if (oldClip != null)
                    oldClip.BeginAnimation(RectangleGeometry.RectProperty, null);

                DockRevealHost.Width = startWidth;
                DockRevealHost.Height = startHeight;

                var clip = new RectangleGeometry(startRect);
                DockRevealHost.Clip = clip;
                DockRevealHost.ClipToBounds = true;

                double targetWidth = opening ? fullWidth : collapsedWidth;
                double targetHeight = opening ? fullHeight : collapsedHeight;

                var duration = TimeSpan.FromSeconds(opening ? 0.42 : 0.30);

                IEasingFunction easing = opening
                    ? new SpringEase { Damping = 7.0, Frequency = 10.0 }
                    : new CubicEase { EasingMode = EasingMode.EaseInOut };

                int animationVersion = ++_dockAnimationVersion;

                _morphCompletion?.TrySetResult(true);

                _morphCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                
                if (opening)
                {
                    ExpandedDockContent.Visibility = Visibility.Visible;
                    ExpandedDockContent.IsHitTestVisible = true;
                    KageStrip.Visibility = Visibility.Collapsed;

                    // Hide only the active content before the dock expands.
                    if (AddAppsContent.Visibility == Visibility.Visible)
                    {
                        AddAppsContent.BeginAnimation(UIElement.OpacityProperty, null);
                        AddAppsContent.Opacity = 0;
                    }
                    else if (AppScrollViewer.Visibility == Visibility.Visible)
                    {
                        AppScrollViewer.BeginAnimation(UIElement.OpacityProperty, null);
                        AppScrollViewer.Opacity = 0;
                    }

                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Loaded,
                        new Action(async () =>
                        {
                            await Task.Delay(100);

                            if (animationVersion != _dockAnimationVersion ||
                                !_expanded ||
                                !_isDockOpening)
                                return;

                            _dockContentAnimationController?.RevealMyAppsContent();
                        }));
                }


                AnimationTimeline widthAnimation;
                AnimationTimeline heightAnimation;
                AnimationTimeline clipAnimation;
                DoubleAnimationUsingKeyFrames? panelWidthAnimation = null;

                if (opening)
                {
                    var overshootEase = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    };

                    var settleEase = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    };

                    double overshootWidth = Math.Min(targetWidth * 1.035, fullWidth * 1.035);
                    double overshootHeight = Math.Min(targetHeight * 1.025, fullHeight * 1.025);

                    panelWidthAnimation = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = duration,
                        FillBehavior = FillBehavior.Stop
                    };

                    panelWidthAnimation.KeyFrames.Add(
                        new EasingDoubleKeyFrame(targetWidth, KeyTime.FromPercent(0)));

                    panelWidthAnimation.KeyFrames.Add(
                        new EasingDoubleKeyFrame(
                            overshootWidth, KeyTime.FromPercent(0.82), overshootEase));

                    panelWidthAnimation.KeyFrames.Add(
                        new EasingDoubleKeyFrame(
                            targetWidth, KeyTime.FromPercent(1.0), settleEase));

                    var widthFrames = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = duration,
                        FillBehavior = FillBehavior.Stop
                    };

                    widthFrames.KeyFrames.Add(
                        new EasingDoubleKeyFrame(startWidth, KeyTime.FromPercent(0)));

                    widthFrames.KeyFrames.Add(
                        new EasingDoubleKeyFrame(
                            overshootWidth, KeyTime.FromPercent(0.82), overshootEase));

                    widthFrames.KeyFrames.Add(
                        new EasingDoubleKeyFrame(
                            targetWidth, KeyTime.FromPercent(1.0), settleEase));

                    var heightFrames = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = duration,
                        FillBehavior = FillBehavior.Stop
                    };

                    heightFrames.KeyFrames.Add(
                        new EasingDoubleKeyFrame(startHeight, KeyTime.FromPercent(0)));

                    heightFrames.KeyFrames.Add(
                        new EasingDoubleKeyFrame(
                            overshootHeight, KeyTime.FromPercent(0.82), overshootEase));

                    heightFrames.KeyFrames.Add(
                        new EasingDoubleKeyFrame(
                            targetHeight, KeyTime.FromPercent(1.0), settleEase));

                    var clipFrames = new RectAnimationUsingKeyFrames
                    {
                        Duration = duration,
                        FillBehavior = FillBehavior.Stop
                    };

                    clipFrames.KeyFrames.Add(
                        new EasingRectKeyFrame(startRect, KeyTime.FromPercent(0)));

                    clipFrames.KeyFrames.Add(
                        new EasingRectKeyFrame(
                            new Rect(0, 0, overshootWidth, overshootHeight),
                            KeyTime.FromPercent(0.82), overshootEase));

                    clipFrames.KeyFrames.Add(
                        new EasingRectKeyFrame(
                            new Rect(0, 0, targetWidth, targetHeight),
                            KeyTime.FromPercent(1.0), settleEase));

                    widthAnimation = widthFrames;
                    heightAnimation = heightFrames;
                    clipAnimation = clipFrames;
                }
                else
                {
                    widthAnimation = new DoubleAnimation
                    {
                        From = startWidth,
                        To = targetWidth,
                        Duration = duration,
                        EasingFunction = easing,
                        FillBehavior = FillBehavior.Stop
                    };

                    heightAnimation = new DoubleAnimation
                    {
                        From = startHeight,
                        To = targetHeight,
                        Duration = duration,
                        EasingFunction = easing,
                        FillBehavior = FillBehavior.Stop
                    };

                    clipAnimation = new RectAnimation
                    {
                        From = startRect,
                        To = new Rect(0, 0, targetWidth, targetHeight),
                        Duration = duration,
                        EasingFunction = easing,
                        FillBehavior = FillBehavior.Stop
                    };
                }

                widthAnimation.Completed += (_, _) =>
                {
                    if (animationVersion != _dockAnimationVersion)
                        return;

                    DockRevealHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                    DockRevealHost.BeginAnimation(FrameworkElement.HeightProperty, null);
                    DockPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
                    DockPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
                    clip.BeginAnimation(RectangleGeometry.RectProperty, null);

                    DockRevealHost.Width = targetWidth;
                    DockRevealHost.Height = targetHeight;
                    clip.Rect = new Rect(0, 0, targetWidth, targetHeight);

                    bool hasPendingMorph = _pendingMorphView.HasValue;

                    if (opening)
                    {
                        _isDockOpening = false;

                        ExpandedDockContent.Visibility = Visibility.Visible;
                        ExpandedDockContent.IsHitTestVisible = true;
                        KageStrip.Visibility = Visibility.Collapsed;

                        if (_pendingMorphView is DockView pendingView)
                        {
                            _pendingMorphView = null;

                            Dispatcher.BeginInvoke(
                                DispatcherPriority.Background,
                                new Action(() =>
                                {
                                    // Ignore this callback if another animation or
                                    // navigation request has taken priority.
                                    if (animationVersion != _dockAnimationVersion ||
                                        !_expanded ||
                                        _targetView != pendingView)
                                        return;

                                    MorphDockToView(pendingView);
                                }));
                        }

                        Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(() =>
                            {
                                if (!_expanded)
                                    return;

                                if (_activeChildWindow != null && _activeChildWindow.IsVisible)
                                    return;

                                if (_dragStarted || _contextMenuOpen)
                                    return;

                                if (!DockPanel.IsMouseOver && !TopHoverTrigger.IsMouseOver)
                                    ScheduleCollapse();
                            }));
                    }
                    else
                    {
                        ExpandedDockContent.Visibility = Visibility.Collapsed;
                        ExpandedDockContent.IsHitTestVisible = false;
                        KageStrip.Visibility = Visibility.Visible;

                        _kageCoordinator?.OnDockCollapsed();
                    }

                    if (!hasPendingMorph || !opening)
                    {
                        _morphCompletion?.TrySetResult(true);
                        _morphCompletion = null;
                    }
                };
                

                DockRevealHost.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);

                if (panelWidthAnimation != null)
                    DockPanel.BeginAnimation(FrameworkElement.WidthProperty, panelWidthAnimation);

                DockRevealHost.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);

                if (opening)
                    DockPanel.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation.Clone());

                clip.BeginAnimation(RectangleGeometry.RectProperty, clipAnimation);
            }

            private void CancelCollapseTimer()
            {
                if (_hideTimer == null)
                    return;

                _hideTimer.Stop();
                _hideTimer = null;
            }

             private void HitStrip_MouseEnter(
                object sender,
                System.Windows.Input.MouseEventArgs e)
            {
                CancelCollapseTimer();

                if (_expanded)
                    return;

                ExpandDock();
            }

        
        
            private void HitStrip_MouseLeave(
            object sender,
            System.Windows.Input.MouseEventArgs e)
        {
            ScheduleCollapse();
        }


        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            if (_activeChildWindow != null && _activeChildWindow.IsVisible) return;
            ScheduleCollapse(100);
        }
     
        
        private void ScheduleCollapse(int delayMs = 180)
        {

            if (!_expanded)
                return;

            if (Mouse.LeftButton == MouseButtonState.Pressed)
                return;

            // Keep the existing countdown instead of restarting it
            // whenever another mouse-leave event arrives.
            if (_hideTimer != null)
                return;

            _hideTimer = new DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            _hideTimer.Tick += (_, _) =>
            {
                CancelCollapseTimer();

                if (!_expanded)
                    return;

                if (_activeChildWindow != null && _activeChildWindow.IsVisible)
                    return;

                if (_dragStarted || _contextMenuOpen)
                    return;

                if (Mouse.LeftButton == MouseButtonState.Pressed)
                    return;

                if (DockPanel.IsMouseOver || TopHoverTrigger.IsMouseOver)
                    return;

                var pointer = Mouse.GetPosition(this);

                bool overTopTrigger =
                    pointer.Y >= 0 &&
                    pointer.Y <= TopHoverTrigger.ActualHeight &&
                    pointer.X >= (ActualWidth - TopHoverTrigger.ActualWidth) / 2 &&
                    pointer.X <= (ActualWidth + TopHoverTrigger.ActualWidth) / 2;

                if (overTopTrigger)
                    return;

                CollapseDock();
            };

            _hideTimer.Start();
        }


        private void DockPanel_MouseLeave(
            object sender,
            System.Windows.Input.MouseEventArgs e)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    if (_activeChildWindow != null && _activeChildWindow.IsVisible)
                        return;

                    if (_dragStarted || _contextMenuOpen)
                        return;

                    if (DockPanel.IsMouseOver || TopHoverTrigger.IsMouseOver)
                        return;

                    ScheduleCollapse();
                }));
        }

        private void DockWindow_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_expanded || _dragStarted || _contextMenuOpen)
                return;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    if (!_expanded ||
                        _dragStarted ||
                        _contextMenuOpen)
                        return;

                    if (_activeChildWindow != null && _activeChildWindow.IsVisible)
                        return;

                    if (Mouse.LeftButton != MouseButtonState.Released)
                        return;

                    if (!DockPanel.IsMouseOver && !TopHoverTrigger.IsMouseOver)
                        ScheduleCollapse();
                }));
        }

        private void MorphDockToView(DockView view)
        {
            _targetView = view;

            if (!_expanded)
            {
                _pendingMorphView = view;
                ExpandDock();
                return;
            }

            if (_isDockOpening)
            {
                _pendingMorphView = view;
                return;
            }

            var (targetWidth, targetHeight) = GetDockViewSize(view);

            if (targetWidth <= 0 || targetHeight <= 0)
                return;

            // Ensure the active morph has a completion task.
            // Reuse an existing task if an opening animation is handing
            // control over to a pending morph.
            if (_morphCompletion == null || _morphCompletion.Task.IsCompleted)
            {
                _morphCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            int animationVersion = ++_dockAnimationVersion;

            double startWidth = DockRevealHost.ActualWidth > 0
                ? DockRevealHost.ActualWidth
                : DockRevealHost.Width;

            double startHeight = DockRevealHost.ActualHeight > 0
                ? DockRevealHost.ActualHeight
                : DockRevealHost.Height;

            startWidth = Math.Clamp(startWidth, 1, MaxDockWidth);
            startHeight = Math.Clamp(startHeight, 1, _maxDockHeight);

            var clip = DockRevealHost.Clip as RectangleGeometry
                ?? new RectangleGeometry(
                    new Rect(0, 0, startWidth, startHeight));

            DockRevealHost.Clip = clip;

            DockRevealHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            DockRevealHost.BeginAnimation(FrameworkElement.HeightProperty, null);
            DockPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
            DockPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
            clip.BeginAnimation(RectangleGeometry.RectProperty, null);

            ApplyDockViewSize(startWidth, startHeight);

            var duration = TimeSpan.FromMilliseconds(250);
            var settleEase = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };

            double overshootWidth = Math.Clamp(
                targetWidth + (targetWidth - startWidth) * 0.06,
                1, MaxDockWidth * 1.035);

            double overshootHeight = Math.Clamp(
                targetHeight + (targetHeight - startHeight) * 0.06,
                1, _maxDockHeight * 1.025);

            var widthFrames = new DoubleAnimationUsingKeyFrames
            {
                Duration = duration,
                FillBehavior = FillBehavior.Stop
            };

            widthFrames.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    startWidth, KeyTime.FromPercent(0)));

            widthFrames.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    overshootWidth,
                    KeyTime.FromPercent(0.72),
                    settleEase));

            widthFrames.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    targetWidth,
                    KeyTime.FromPercent(1.0),
                    settleEase));

            var heightFrames = new DoubleAnimationUsingKeyFrames
            {
                Duration = duration,
                FillBehavior = FillBehavior.Stop
            };

            heightFrames.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    startHeight, KeyTime.FromPercent(0)));

            heightFrames.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    overshootHeight,
                    KeyTime.FromPercent(0.72),
                    settleEase));

            heightFrames.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    targetHeight,
                    KeyTime.FromPercent(1.0),
                    settleEase));

            var clipFrames = new RectAnimationUsingKeyFrames
            {
                Duration = duration,
                FillBehavior = FillBehavior.Stop
            };

            clipFrames.KeyFrames.Add(
                new EasingRectKeyFrame(
                    new Rect(0, 0, startWidth, startHeight),
                    KeyTime.FromPercent(0)));

            clipFrames.KeyFrames.Add(
                new EasingRectKeyFrame(
                    new Rect(0, 0, overshootWidth, overshootHeight),
                    KeyTime.FromPercent(0.72),
                    settleEase));

            clipFrames.KeyFrames.Add(
                new EasingRectKeyFrame(
                    new Rect(0, 0, targetWidth, targetHeight),
                    KeyTime.FromPercent(1.0),
                    settleEase));

            widthFrames.Completed += (_, _) =>
            {
                if (animationVersion != _dockAnimationVersion ||
                    !_expanded ||
                    _targetView != view)
                    return;

                DockRevealHost.BeginAnimation(
                    FrameworkElement.WidthProperty, null);

                DockRevealHost.BeginAnimation(
                    FrameworkElement.HeightProperty, null);

                DockPanel.BeginAnimation(
                    FrameworkElement.WidthProperty, null);

                DockPanel.BeginAnimation(
                    FrameworkElement.HeightProperty, null);

                clip.BeginAnimation(
                    RectangleGeometry.RectProperty, null);

                ApplyDockViewSize(targetWidth, targetHeight);

                _currentView = view;

                _morphCompletion?.TrySetResult(true);
                _morphCompletion = null;

                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        if (!_expanded)
                            return;

                        if (_activeChildWindow != null && _activeChildWindow.IsVisible)
                            return;

                        if (_dragStarted || _contextMenuOpen)
                            return;

                        if (Mouse.LeftButton == MouseButtonState.Pressed)
                            return;

                        if (!DockPanel.IsMouseOver && !TopHoverTrigger.IsMouseOver)
                            ScheduleCollapse();
                    }));
            };

            DockRevealHost.BeginAnimation(
                FrameworkElement.WidthProperty, widthFrames);

            DockPanel.BeginAnimation(
                FrameworkElement.WidthProperty, widthFrames.Clone());

            DockRevealHost.BeginAnimation(
                FrameworkElement.HeightProperty, heightFrames);

            DockPanel.BeginAnimation(
                FrameworkElement.HeightProperty, heightFrames.Clone());

            clip.BeginAnimation(
                RectangleGeometry.RectProperty, clipFrames);
        }


        private void ApplyDockViewSize(double width, double height)
        {
            DockRevealHost.Width = width;
            DockRevealHost.Height = height;

            DockPanel.Width = width;
            DockPanel.Height = height;

            if (DockRevealHost.Clip is not RectangleGeometry clip)
            {
                clip = new RectangleGeometry();
                DockRevealHost.Clip = clip;
            }

            clip.Rect = new Rect(0, 0, width, height);
            DockRevealHost.ClipToBounds = true;
        }


        private (double Width, double Height) GetDockViewSize(DockView view)
        {
            double maxWidth = 820;
            double maxHeight = _maxDockHeight;

            return view switch
            {
                DockView.MyApps => (maxWidth, maxHeight),

                DockView.Music => (
                    maxWidth * 0.90,
                    maxHeight * 0.45),

                DockView.Timer => (
                    maxWidth * 0.75,
                    maxHeight * 0.80),

                DockView.AddApps => (
                    maxWidth * 0.85,
                    maxHeight * 0.90),

                _ => (maxWidth, maxHeight)
            };
        }
    }
}