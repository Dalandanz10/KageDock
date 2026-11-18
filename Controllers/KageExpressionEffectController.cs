using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SharpVectors.Converters;
using KageDock.Models;

namespace KageDock.Controllers
{
    internal sealed class KageExpressionEffectController : IDisposable
    {
        private readonly Canvas _host;
        private SvgViewbox? _currentEffect;
        private int _animationVersion;
        private bool _isDisposed;

        private static readonly IReadOnlyDictionary<KageExpressionEffect, string>
            EffectFiles = new Dictionary<KageExpressionEffect, string>
            {
                [KageExpressionEffect.Anger] = "Anger.svg",
                [KageExpressionEffect.Question] = "Question.svg",
                [KageExpressionEffect.Exclamation] = "Exclamation.svg",
                [KageExpressionEffect.Ellipsis] = "Ellipsis.svg",
                [KageExpressionEffect.Sparkle] = "Sparkle.svg",
                [KageExpressionEffect.Awkward] = "Awkward.svg",
                [KageExpressionEffect.Sigh] = "Sigh.svg"
            };

        public KageExpressionEffectController(Canvas host)
        {
            _host = host;
        }

        public void Show(KageExpressionEffect effect)
        {
            if (_isDisposed ||
                !EffectFiles.TryGetValue(effect, out string? fileName))
            {
                return;
            }

            ClearCurrentEffect();

            int version = ++_animationVersion;

            var scale = new ScaleTransform(0.75, 0.75);
            var translate = new TranslateTransform(0, 2);

            var visual = new SvgViewbox
            {
                Width = 12,
                Height = 12,
                Source = new Uri(
                    $"pack://siteoforigin:,,,/Assets/Kage/Expressions/{fileName}",
                    UriKind.Absolute),
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new TransformGroup
                {
                    Children = new TransformCollection { scale, translate }
                }
            };

            Canvas.SetLeft(visual, 82);
            Canvas.SetTop(visual, 2);

            _host.Children.Add(visual);
            _currentEffect = visual;

            var fade = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(1320),
                FillBehavior = FillBehavior.Stop
            };

            fade.KeyFrames.Add(new LinearDoubleKeyFrame(
                0, KeyTime.FromTimeSpan(TimeSpan.Zero)));

            fade.KeyFrames.Add(new EasingDoubleKeyFrame(
                1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));

            fade.KeyFrames.Add(new LinearDoubleKeyFrame(
                1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100))));

            fade.KeyFrames.Add(new EasingDoubleKeyFrame(
                0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1320)))
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseIn
                }
            });

            fade.Completed += (_, _) =>
            {
                if (!_isDisposed && version == _animationVersion)
                    ClearCurrentEffect();
            };

            visual.BeginAnimation(UIElement.OpacityProperty, fade);

            var scaleIn = new DoubleAnimation(
                0.75, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            var riseIn = new DoubleAnimation(
                2, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            translate.BeginAnimation(TranslateTransform.YProperty, riseIn);
        }

        public void Clear()
        {
            if (_isDisposed)
                return;

            _animationVersion++;
            ClearCurrentEffect();
        }

        private void ClearCurrentEffect()
        {
            if (_currentEffect == null)
                return;

            _currentEffect.BeginAnimation(
                UIElement.OpacityProperty, null);

            if (_currentEffect.RenderTransform is TransformGroup group)
            {
                foreach (Transform transform in group.Children)
                {
                    if (transform is ScaleTransform scale)
                    {
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    }
                    else if (transform is TranslateTransform translate)
                    {
                        translate.BeginAnimation(
                            TranslateTransform.YProperty, null);
                    }
                }
            }

            _host.Children.Remove(_currentEffect);
            _currentEffect = null;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Clear();
            _isDisposed = true;
        }
    }
}