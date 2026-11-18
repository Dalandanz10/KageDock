
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KageDock.Controllers
{
    public sealed class DockContentAnimationController
    {
        private readonly TranslateTransform _navIndicatorTransform;
        private readonly FrameworkElement _addAppsContent;
        private readonly FrameworkElement _appScrollViewer;
        private readonly Func<bool> _isLoaded;
        private readonly Func<bool> _isExpanded;

        private int _pageTransitionVersion;

        public DockContentAnimationController(
            TranslateTransform navIndicatorTransform,
            FrameworkElement addAppsContent,
            FrameworkElement appScrollViewer,
            Func<bool> isLoaded,
            Func<bool> isExpanded)
        {
            _navIndicatorTransform = navIndicatorTransform;
            _addAppsContent = addAppsContent;
            _appScrollViewer = appScrollViewer;
            _isLoaded = isLoaded;
            _isExpanded = isExpanded;
        }

        public void MoveNavIndicator(int index)
        {
            double targetX = index * 44;

            var animation = new DoubleAnimation
            {
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            _navIndicatorTransform.BeginAnimation(
                TranslateTransform.XProperty,
                animation);
        }

        public void InvalidatePendingPageTransition()
        {
            _pageTransitionVersion++;
        }

        public async Task TransitionPageContentAsync(Action swapContent)
        {
            const int fadeOutMs = 0;
            const int fadeInMs = 180;

            int transitionVersion = ++_pageTransitionVersion;

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(fadeOutMs),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseIn
                }
            };

            _addAppsContent.BeginAnimation(
                UIElement.OpacityProperty,
                fadeOut);

            await Task.Delay(fadeOutMs);

            if (transitionVersion != _pageTransitionVersion ||
                !_isLoaded())
                return;

            _addAppsContent.BeginAnimation(
                UIElement.OpacityProperty,
                null);

            _addAppsContent.Opacity = 0;

            swapContent();

            if (transitionVersion != _pageTransitionVersion ||
                !_isLoaded() ||
                !_isExpanded())
                return;

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(fadeInMs),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            _addAppsContent.BeginAnimation(
                UIElement.OpacityProperty,
                fadeIn);
        }

        public void RevealMyAppsContent()
        {
            if (_addAppsContent.Visibility == Visibility.Visible)
            {
                AnimateContentEntrance(_addAppsContent);
                return;
            }

            if (_appScrollViewer.Visibility == Visibility.Visible)
            {
                AnimateContentEntrance(
                    _appScrollViewer,
                    slideDistance: 0);
            }
        }

        private void AnimateContentEntrance(
            FrameworkElement content,
            double slideDistance = 12,
            int durationMs = 450)
        {
            content.BeginAnimation(UIElement.OpacityProperty, null);

            content.BeginAnimation(
                UIElement.RenderTransformProperty,
                null);

            content.Opacity = 0;

            var slide = new TranslateTransform(0, slideDistance);
            content.RenderTransform = slide;

            var easing = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };

            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };

            var movement = new DoubleAnimation
            {
                From = slideDistance,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };

            fade.Completed += (_, _) =>
            {
                content.BeginAnimation(
                    UIElement.OpacityProperty,
                    null);

                content.Opacity = 1;

                slide.BeginAnimation(
                    TranslateTransform.YProperty,
                    null);

                slide.Y = 0;
            };

            content.BeginAnimation(
                UIElement.OpacityProperty,
                fade);

            slide.BeginAnimation(
                TranslateTransform.YProperty,
                movement);
        }
    }
}
