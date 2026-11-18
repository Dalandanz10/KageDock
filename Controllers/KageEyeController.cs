using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KageDock.Models;

namespace KageDock.Controllers
{
    public sealed class KageEyeController : IDisposable
    {
        private readonly FrameworkElement _leftEye;
        private readonly FrameworkElement _rightEye;

        // Closed-eye artwork (separate SVG elements stacked on top of the open eyes).
        private readonly FrameworkElement? _leftClosedEye;
        private readonly FrameworkElement? _rightClosedEye;

        // Blink
        private readonly ScaleTransform _leftScale;
        private readonly ScaleTransform _rightScale;

        // Sleep: used only for the "lid dropping" part of the open eye.
        private readonly ScaleTransform _leftSleepScale;
        private readonly ScaleTransform _rightSleepScale;

        // Gaze
        private readonly TranslateTransform _leftTranslate;
        private readonly TranslateTransform _rightTranslate;

        private readonly ScaleTransform _leftGazeScale;
        private readonly ScaleTransform _rightGazeScale;

        private readonly DispatcherTimer _gazeTimer;

        private readonly DispatcherTimer _blinkTimer;
        // Gaze tuning
        private const double GazeOffset = 1.8;
        private const double GazeSmallerScale = 0.90;
        private const double GazeLargerScale = 1.10;
        // Expressions
        private readonly ScaleTransform _leftExpressionScale;
        private readonly ScaleTransform _rightExpressionScale;
        private readonly DispatcherTimer _expressionTimer;
        private readonly RotateTransform _leftExpressionRotate;
        private readonly RotateTransform _rightExpressionRotate;

        private readonly TranslateTransform _leftExpressionTranslate;
        private readonly TranslateTransform _rightExpressionTranslate;
        private KageExpression? _expressionOverride;
        private static readonly TimeSpan ExpressionTransitionTime =
            TimeSpan.FromMilliseconds(250);

        private static readonly TimeSpan ExpressionHoldTime =
            TimeSpan.FromMilliseconds(3250);
        private static readonly TimeSpan GazeInTime = TimeSpan.FromMilliseconds(280);
        private static readonly TimeSpan GazeHoldUntil = TimeSpan.FromMilliseconds(1150);
        private static readonly TimeSpan GazeTotalTime = TimeSpan.FromMilliseconds(1750);

        // Sleep tuning
        private const double SleepLidScaleY = 0.04;   // same "shut" height the blink uses

        private static readonly TimeSpan SleepCloseLidTime = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan SleepCloseSwapDelay = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan SleepSwapTime = TimeSpan.FromMilliseconds(130);

        private static readonly TimeSpan SleepOpenLidDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan SleepOpenLidTime = TimeSpan.FromMilliseconds(400);

        private readonly Random _random = new();

        private bool _isRunning;
        private bool _isDisposed;
        private bool _isSleeping;
        private bool _isWaking;
        private bool _isPreparingForSleep;

        private int _sleepSequenceVersion;
        private readonly DispatcherTimer _sleepSequenceTimer;
        private readonly TranslateTransform _headShake;

        public bool IsSleeping => _isSleeping;
        public bool IsWaking => _isWaking;
        public bool IsPreparingForSleep => _isPreparingForSleep;

        public KageEyeController(
            FrameworkElement leftEye,
            FrameworkElement rightEye,
            FrameworkElement? leftClosedEye = null,
            FrameworkElement? rightClosedEye = null,
            TranslateTransform? headShake = null)
        {
            _leftEye = leftEye;
            _rightEye = rightEye;
            _leftClosedEye = leftClosedEye;
            _rightClosedEye = rightClosedEye;

            _headShake = headShake ?? new TranslateTransform();

            _leftScale = new ScaleTransform(1, 1);
            _rightScale = new ScaleTransform(1, 1);

            _leftSleepScale = new ScaleTransform(1, 1);
            _rightSleepScale = new ScaleTransform(1, 1);

            _leftGazeScale = new ScaleTransform(1, 1);
            _rightGazeScale = new ScaleTransform(1, 1);

            _leftExpressionScale = new ScaleTransform(1, 1);
            _rightExpressionScale = new ScaleTransform(1, 1);

            _leftExpressionRotate = new RotateTransform(0);
            _rightExpressionRotate = new RotateTransform(0);

            _leftExpressionTranslate = new TranslateTransform();
            _rightExpressionTranslate = new TranslateTransform();

            _leftTranslate = new TranslateTransform();
            _rightTranslate = new TranslateTransform();

            _leftEye.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            _rightEye.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

            _leftEye.RenderTransform = new TransformGroup
            {
                Children =
                {
                    _leftScale,
                    _leftSleepScale,
                    _leftGazeScale,
                    _leftExpressionScale,
                    _leftExpressionRotate,
                    _leftExpressionTranslate,
                    _leftTranslate
                }
            };

            _rightEye.RenderTransform = new TransformGroup
            {
                Children =
                {
                    _rightScale,
                    _rightSleepScale,
                    _rightGazeScale,
                    _rightExpressionScale,
                    _rightExpressionRotate,
                    _rightExpressionTranslate,
                    _rightTranslate
                }
            };

            if (_leftClosedEye != null)
                _leftClosedEye.Opacity = 0;

            if (_rightClosedEye != null)
                _rightClosedEye.Opacity = 0;

            _blinkTimer = new DispatcherTimer(
            DispatcherPriority.Background);

            _blinkTimer.Tick += BlinkTimer_Tick;

            _gazeTimer = new DispatcherTimer(
                DispatcherPriority.Background);

            _gazeTimer.Tick += GazeTimer_Tick;

            _expressionTimer = new DispatcherTimer(DispatcherPriority.Background);
            _expressionTimer.Tick += ExpressionTimer_Tick;
            _expressionTimer.Interval = ExpressionHoldTime;

            _sleepSequenceTimer = new DispatcherTimer(DispatcherPriority.Background);
            _sleepSequenceTimer.Interval = TimeSpan.FromMilliseconds(1600);
            _sleepSequenceTimer.Tick += SleepSequenceTimer_Tick;
        }

        public void Start()
        {
            if (_isDisposed || _isRunning)
                return;

            _isRunning = true;

            // Sleep state survives Stop()/Start(); make sure it is shown settled.
            ApplySleep(animate: false);

            ScheduleNextBlink();
            ScheduleNextGaze();
        }

        public void Stop()
        {
            CancelSleepPreparation(animate: false);

            _expressionTimer.Stop();
            _expressionOverride = null;
            ResetExpression();

            if (!_isRunning)
                return;

            _isRunning = false;
            _blinkTimer.Stop();

            _gazeTimer.Stop();

            ResetGaze();

            ResetEye(_leftScale);
            ResetEye(_rightScale);

            // Cancel any in-flight transition and snap to the settled state
            // (closed if still sleeping, open otherwise).
            ApplySleep(animate: false);
        }

        // ------------------------------------------------------------------
        // Sleep mode
        // ------------------------------------------------------------------

        public void EnterSleepMode()
        {
            if (_isDisposed || _isSleeping || _isPreparingForSleep || _isWaking)
                return;

            ResetEye(_leftScale);
            ResetEye(_rightScale);
            _expressionOverride = null;
            ResetExpression();
            ResetGaze();

            if (!_isRunning)
            {
                _isSleeping = true;
                ApplySleep(animate: false);
                return;
            }

            _isPreparingForSleep = true;

            _sleepSequenceVersion++;

            // Gradually lower the eyelids.
            var droopEase = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            };

            TransitionValue(
                _leftSleepScale,
                ScaleTransform.ScaleYProperty,
                0.72,
                true,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1600),
                droopEase);

            TransitionValue(
                _rightSleepScale,
                ScaleTransform.ScaleYProperty,
                0.72,
                true,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1600),
                droopEase);

            _sleepSequenceTimer.Stop();
            _sleepSequenceTimer.Start();
        }

        public void ExitSleepMode()
        {
            if (_isDisposed)
                return;

            // Cursor moved while Kage was still falling asleep.
            if (_isPreparingForSleep)
            {
                CancelSleepPreparation(animate: true);
                return;
            }

            if (!_isSleeping)
                return;

            _isSleeping = false;

            if (_isRunning)
                BeginWakeUp();
            else
                ApplySleep(animate: false);
        }

        private void SleepSequenceTimer_Tick(object? sender, EventArgs e)
        {
            _sleepSequenceTimer.Stop();

            if (_isDisposed || !_isPreparingForSleep)
                return;

            int sequenceVersion = _sleepSequenceVersion;

            // Slowly close, pause, then reopen as if struggling to stay awake.
                BeginSleepyBlink(
                    _leftScale,
                    TimeSpan.Zero,
                    sequenceVersion);

                BeginSleepyBlink(
                    _rightScale,
                    TimeSpan.FromMilliseconds(60),
                    sequenceVersion,
                    () =>
                    {
                        if (_isDisposed ||
                            !_isPreparingForSleep ||
                            sequenceVersion != _sleepSequenceVersion)
                        {
                            return;
                        }

                        CompleteSleepEntry();
                    });
        }

        private void BeginSleepyBlink(
            ScaleTransform scale,
            TimeSpan delay,
            int sequenceVersion,
            Action? completed = null)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = delay,
                Duration = TimeSpan.FromMilliseconds(1450),
                FillBehavior = FillBehavior.Stop
            };

            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    1.0,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));

            // Slowly close.
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0.04,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseIn
                    }
                });

            // Remain closed briefly.
            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    0.04,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760))));

            // Slowly struggle back open.
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1.0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1450)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            animation.Completed += (_, _) =>
            {
                if (_isDisposed ||
                    !_isPreparingForSleep ||
                    sequenceVersion != _sleepSequenceVersion)
                {
                    return;
                }

                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = 1.0;

                completed?.Invoke();
            };

            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                animation);
        }

        private void CompleteSleepEntry()
        {
            if (_isDisposed || !_isPreparingForSleep)
                return;

            _isPreparingForSleep = false;
            _isSleeping = true;

            ResetEye(_leftScale);
            ResetEye(_rightScale);

            // Continue into the existing final sleep transition.
            ApplySleep(animate: true);
        }

        private void CancelSleepPreparation(bool animate)
        {
            if (!_isPreparingForSleep)
                return;

            _isPreparingForSleep = false;
            _sleepSequenceVersion++;

            _sleepSequenceTimer.Stop();

            // Cancel any in-progress sleepy blink.
            ResetEye(_leftScale);
            ResetEye(_rightScale);

            // Return the eyelids to their normal position.
            var ease = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };

            TransitionValue(
                _leftSleepScale,
                ScaleTransform.ScaleYProperty,
                1.0,
                animate,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(300),
                ease);

            TransitionValue(
                _rightSleepScale,
                ScaleTransform.ScaleYProperty,
                1.0,
                animate,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(300),
                ease);

            // Normal eye behavior can resume.
            if (_isRunning)
            {
                ScheduleNextBlink();
                ScheduleNextGaze();
            }
        }

        private void BeginWakeUp()
        {
            _isWaking = true;

            // Reveal the normal eye artwork and remove the closed-eye artwork.
            var swapEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

            TransitionValue(
                _leftEye, UIElement.OpacityProperty,
                1.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(130), swapEase);

            TransitionValue(
                _rightEye, UIElement.OpacityProperty,
                1.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(130), swapEase);

            if (_leftClosedEye != null)
            {
                TransitionValue(
                    _leftClosedEye, UIElement.OpacityProperty,
                    0.0, true, TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(130), swapEase);
            }

            if (_rightClosedEye != null)
            {
                TransitionValue(
                    _rightClosedEye, UIElement.OpacityProperty,
                    0.0, true, TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(130), swapEase);
            }

            // Lift the sleeping eyelid.
            var lidEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            TransitionValue(
                _leftSleepScale, ScaleTransform.ScaleYProperty,
                1.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(180), lidEase);

            TransitionValue(
                _rightSleepScale, ScaleTransform.ScaleYProperty,
                1.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(180), lidEase);

            WakeEye(_leftScale, TimeSpan.Zero);
            WakeEye(_rightScale, TimeSpan.FromMilliseconds(12));
        }

        private void WakeEye(ScaleTransform scale, TimeSpan delay)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(910),
                FillBehavior = FillBehavior.Stop
            };

            // Start at normal height.
            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    1.0,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));

            // Open wide.
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1.15,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            // Brief wide-eyed pause.
            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    1.15,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350))));

            // First blink.
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0.04,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(421)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseIn
                    }
                });

            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1.15,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            // Pause between blinks.
            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    1.15,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(740))));

            // Second blink.
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0.04,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(811)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseIn
                    }
                });

            // Reopen to normal height.
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1.0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(910)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            animation.BeginTime = delay;

            animation.Completed += (_, _) =>
            {
                if (_isDisposed)
                    return;

                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = 1.0;

                // Start the next wake-up stage after both eyes finish blinking.
                if (delay > TimeSpan.Zero)
                    BeginWakeHeadShake();
            };

            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                animation);
        }
        private void BeginWakeHeadShake()
        {
            if (_isDisposed)
                return;
            var closeEase = new CubicEase
            {
                EasingMode = EasingMode.EaseIn
            };

            // Close both eyes again.
            TransitionValue(
                _leftScale, ScaleTransform.ScaleYProperty,
                0.04, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100), closeEase);

            TransitionValue(
                _rightScale, ScaleTransform.ScaleYProperty,
                0.04, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100), closeEase);

            // Swap to the closed-eye artwork.
            var swapEase = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            };

            TransitionValue(
                _leftEye, UIElement.OpacityProperty,
                0.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100), swapEase);

            TransitionValue(
                _rightEye, UIElement.OpacityProperty,
                0.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100), swapEase);

            if (_leftClosedEye != null)
            {
                TransitionValue(
                    _leftClosedEye, UIElement.OpacityProperty,
                    1.0, true, TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(100), swapEase);
            }

            if (_rightClosedEye != null)
            {
                TransitionValue(
                    _rightClosedEye, UIElement.OpacityProperty,
                    1.0, true, TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(100), swapEase);
            }

            // Quick left -> right -> center shake.
            var shake = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromMilliseconds(110),
                Duration = TimeSpan.FromMilliseconds(220),
                FillBehavior = FillBehavior.Stop
            };

            shake.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    0.0,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));

            shake.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    -2.5,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            shake.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    2.5,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(145)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseInOut
                    }
                });

            shake.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0.0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            shake.Completed += (_, _) =>
            {
                if (_isDisposed)
                    return;

                FinishWakeHeadShake();
            };

            _headShake.BeginAnimation(
                TranslateTransform.XProperty,
                shake);
        }

        private void FinishWakeHeadShake()
        {
            if (_isDisposed)
                return;
            // Ensure the canvas returns to its exact original position.
            _headShake.BeginAnimation(
                TranslateTransform.XProperty, null);

            _headShake.X = 0.0;

            var openEase = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };

            // Bring back the normal eye artwork.
            TransitionValue(
                _leftEye, UIElement.OpacityProperty,
                1.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(130), openEase);

            TransitionValue(
                _rightEye, UIElement.OpacityProperty,
                1.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(130), openEase);

            if (_leftClosedEye != null)
            {
                TransitionValue(
                    _leftClosedEye, UIElement.OpacityProperty,
                    0.0, true, TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(130), openEase);
            }

            if (_rightClosedEye != null)
            {
                TransitionValue(
                    _rightClosedEye, UIElement.OpacityProperty,
                    0.0, true, TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(130), openEase);
            }

            // Reopen the eyes.
            TransitionValue(
                _leftScale, ScaleTransform.ScaleYProperty,
                1.0, true, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(180), openEase);

            double rightFrom = _rightScale.ScaleY;

            _rightScale.ScaleY = 1.0;

            var rightOpen = new DoubleAnimation
            {
                From = rightFrom,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };

            rightOpen.Completed += (_, _) =>
            {
                if (_isDisposed)
                    return;

                _rightScale.BeginAnimation(
                    ScaleTransform.ScaleYProperty, null);

                _rightScale.ScaleY = 1.0;

                _isWaking = false;
            };

            _rightScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                rightOpen);
        }
        private void ApplySleep(bool animate)
        {
            bool hasClosedArt = _leftClosedEye != null && _rightClosedEye != null;

            double lidScale = _isSleeping ? SleepLidScaleY : 1.0;
            double openOpacity = (_isSleeping && hasClosedArt) ? 0.0 : 1.0;
            double closedOpacity = (_isSleeping && hasClosedArt) ? 1.0 : 0.0;

            TimeSpan lidDelay;
            TimeSpan lidTime;
            IEasingFunction lidEase;
            TimeSpan swapDelay;

            if (_isSleeping)
            {
                // Lid drops first, then the thin line resolves into the closed-eye art.
                lidDelay = TimeSpan.Zero;
                lidTime = SleepCloseLidTime;
                lidEase = new CubicEase { EasingMode = EasingMode.EaseIn };
                swapDelay = SleepCloseSwapDelay;
            }
            else
            {
                // Closed-eye art swaps back to a thin line, then the lid lifts.
                lidDelay = SleepOpenLidDelay;
                lidTime = SleepOpenLidTime;
                lidEase = new CubicEase { EasingMode = EasingMode.EaseOut };
                swapDelay = TimeSpan.Zero;
            }

            TransitionValue(_leftSleepScale, ScaleTransform.ScaleYProperty,
                lidScale, animate, lidDelay, lidTime, lidEase);
            TransitionValue(_rightSleepScale, ScaleTransform.ScaleYProperty,
                lidScale, animate, lidDelay, lidTime, lidEase);

            var swapEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

            TransitionValue(_leftEye, UIElement.OpacityProperty,
                openOpacity, animate, swapDelay, SleepSwapTime, swapEase);
            TransitionValue(_rightEye, UIElement.OpacityProperty,
                openOpacity, animate, swapDelay, SleepSwapTime, swapEase);

            if (_leftClosedEye != null)
            {
                TransitionValue(_leftClosedEye, UIElement.OpacityProperty,
                    closedOpacity, animate, swapDelay, SleepSwapTime, swapEase);
            }

            if (_rightClosedEye != null)
            {
                TransitionValue(_rightClosedEye, UIElement.OpacityProperty,
                    closedOpacity, animate, swapDelay, SleepSwapTime, swapEase);
            }
        }

        // Sets the base value to the destination immediately, then (optionally)
        // plays a temporary animation from the currently visible value. With
        // FillBehavior.Stop nothing is left holding the property afterwards,
        // so there is no drift and interrupted transitions are safe.
        private static void TransitionValue(
            DependencyObject target,
            DependencyProperty property,
            double destination,
            bool animate,
            TimeSpan delay,
            TimeSpan duration,
            IEasingFunction easing)
        {
            var animatable = (IAnimatable)target;

            double from = (double)target.GetValue(property);

            target.SetValue(property, destination);

            if (!animate || Math.Abs(from - destination) < 0.0001)
            {
                animatable.BeginAnimation(property, null);
                return;
            }

            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = delay + duration,
                FillBehavior = FillBehavior.Stop
            };

            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    from,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));

            // Hold the starting value during the delay so nothing jumps early.
            if (delay > TimeSpan.Zero)
            {
                animation.KeyFrames.Add(
                    new LinearDoubleKeyFrame(
                        from,
                        KeyTime.FromTimeSpan(delay)));
            }

            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    destination,
                    KeyTime.FromTimeSpan(delay + duration))
                {
                    EasingFunction = easing
                });

            animatable.BeginAnimation(property, animation);
        }

        // ------------------------------------------------------------------
        // Blink scheduling
        // ------------------------------------------------------------------

        private void ScheduleNextBlink()
        {
            _blinkTimer.Stop();

            double interval;

            // 85% chance: normal pause (4–7 seconds).
            if (_random.NextDouble() < 0.85)
            {
                interval = 4.0 + _random.NextDouble() * 3.0;
            }
            else
            {
                // 15% chance: a longer pause (8–12 seconds).
                interval = 8.0 + _random.NextDouble() * 4.0;
            }

            _blinkTimer.Interval = TimeSpan.FromSeconds(interval);
            _blinkTimer.Start();
        }

        private void BlinkTimer_Tick(object? sender, EventArgs e)
        {
            _blinkTimer.Stop();

            if (!_isRunning || _isDisposed)
                return;

            if (!_leftEye.IsVisible || !_rightEye.IsVisible)
            {
                ScheduleNextBlink();
                return;
            }

            // Eyes are shut: no blinking. The schedule keeps ticking so
            // normal blinking resumes by itself after ExitSleepMode().
            if (_isSleeping || _isWaking || _isPreparingForSleep)
            {
                ScheduleNextBlink();
                return;
            }

            PerformBlink();

            ScheduleNextBlink();
        }

        // ------------------------------------------------------------------
        // Gaze
        // ------------------------------------------------------------------

        private void ScheduleNextGaze()
        {
            _gazeTimer.Stop();

            _gazeTimer.Interval = TimeSpan.FromSeconds(
                5.0 + _random.NextDouble() * 4.0);

            _gazeTimer.Start();
        }

        private void GazeTimer_Tick(object? sender, EventArgs e)
        {
            _gazeTimer.Stop();

            if (!_isRunning || _isDisposed)
                return;

            if (!_leftEye.IsVisible || !_rightEye.IsVisible)
            {
                ScheduleNextGaze();
                return;
            }

            // Eyes are shut: no gaze movement.
            if (_isSleeping || _isWaking || _isPreparingForSleep)
            {
                ScheduleNextGaze();
                return;
            }

            // Occasionally remain still instead of looking around.
            if (_random.NextDouble() < 0.20)
            {
                ScheduleNextGaze();
                return;
            }

            PerformGazeSettle();

            ScheduleNextGaze();
        }

        private void PerformGazeSettle()
        {
            bool lookRight = _random.Next(2) == 1;
            double offset = lookRight ? GazeOffset : -GazeOffset;

            // Looking right: right eye smaller, left eye bigger.
            // Looking left:  left eye smaller, right eye bigger.
            double leftScale = lookRight ? GazeLargerScale : GazeSmallerScale;
            double rightScale = lookRight ? GazeSmallerScale : GazeLargerScale;

            AnimateGaze(_leftTranslate, TranslateTransform.XProperty, 0.0, offset);
            AnimateGaze(_rightTranslate, TranslateTransform.XProperty, 0.0, offset);

            AnimateGaze(_leftGazeScale, ScaleTransform.ScaleXProperty, 1.0, leftScale);
            AnimateGaze(_leftGazeScale, ScaleTransform.ScaleYProperty, 1.0, leftScale);

            AnimateGaze(_rightGazeScale, ScaleTransform.ScaleXProperty, 1.0, rightScale);
            AnimateGaze(_rightGazeScale, ScaleTransform.ScaleYProperty, 1.0, rightScale);
        }

        private static void AnimateGaze(
            Animatable target,
            DependencyProperty property,
            double neutral,
            double destination)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = GazeTotalTime,
                FillBehavior = FillBehavior.Stop
            };

            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    neutral,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));

            // Quick, decelerating entry: the eyes settle onto the target.
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    destination,
                    KeyTime.FromTimeSpan(GazeInTime))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            // Hold.
            animation.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    destination,
                    KeyTime.FromTimeSpan(GazeHoldUntil)));

            // Slower, softer return to exact neutral.
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    neutral,
                    KeyTime.FromTimeSpan(GazeTotalTime))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseInOut
                    }
                });

            target.BeginAnimation(property, animation);
        }

        private void ResetGaze()
        {
            _leftTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            _rightTranslate.BeginAnimation(TranslateTransform.XProperty, null);

            _leftGazeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _leftGazeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            _rightGazeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _rightGazeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            _leftTranslate.X = 0;
            _rightTranslate.X = 0;

            _leftGazeScale.ScaleX = 1;
            _leftGazeScale.ScaleY = 1;

            _rightGazeScale.ScaleX = 1;
            _rightGazeScale.ScaleY = 1;
        }

        // ------------------------------------------------------------------
        // Blink animation
        // ------------------------------------------------------------------

        private void PerformBlink()
        {
            BlinkEye(_leftScale, TimeSpan.Zero);
            BlinkEye(_rightScale, TimeSpan.FromMilliseconds(12));

            // 15% chance of a second blink.
            if (_random.NextDouble() < 0.15)
            {
                var doubleBlinkDelay = TimeSpan.FromMilliseconds(350);

                BlinkEye(_leftScale, doubleBlinkDelay);
                BlinkEye(
                    _rightScale,
                    doubleBlinkDelay + TimeSpan.FromMilliseconds(12));
            }
        }

        private void BlinkEye(
            ScaleTransform scale,
            TimeSpan delay)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(170),
                FillBehavior = FillBehavior.Stop
            };

            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1.0,
                    KeyTime.FromPercent(0.0)));

            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0.04,
                    KeyTime.FromPercent(0.42))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseIn
                    }
                });

            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0.04,
                    KeyTime.FromPercent(0.52)));

            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1.0,
                    KeyTime.FromPercent(1.0))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            animation.BeginTime = delay;

            animation.Completed += (_, _) =>
            {
                if (_isDisposed)
                    return;

                scale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    null);

                scale.ScaleY = 1.0;
            };

            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                animation);
        }

        private static void ResetEye(ScaleTransform scale)
        {
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                null);

            scale.ScaleY = 1.0;
        }
        private void ApplyExpression(
            double leftScaleX,
            double leftScaleY,
            double rightScaleX,
            double rightScaleY)
        {
            if (_isDisposed || _isSleeping || _isWaking || _isPreparingForSleep)
                return;

            _expressionTimer.Stop();

            AnimateExpressionScale(
                _leftExpressionScale,
                ScaleTransform.ScaleXProperty,
                leftScaleX);

            AnimateExpressionScale(
                _leftExpressionScale,
                ScaleTransform.ScaleYProperty,
                leftScaleY);

            AnimateExpressionScale(
                _rightExpressionScale,
                ScaleTransform.ScaleXProperty,
                rightScaleX);

            AnimateExpressionScale(
                _rightExpressionScale,
                ScaleTransform.ScaleYProperty,
                rightScaleY);

            if (!_expressionOverride.HasValue)
                _expressionTimer.Start();
        }

        private void AnimateExpressionScale(
            ScaleTransform transform,
            DependencyProperty property,
            double target)
        {
            double current = (double)transform.GetValue(property);

            transform.SetValue(property, target);

            if (Math.Abs(current - target) < 0.0001)
            {
                transform.BeginAnimation(property, null);
                return;
            }

            var animation = new DoubleAnimation
            {
                From = current,
                To = target,
                Duration = ExpressionTransitionTime,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };

            transform.BeginAnimation(property, animation);
        }

        private void AnimateExpressionRotation(
            RotateTransform transform,
            double target)
        {
            double current = transform.Angle;

            transform.Angle = target;

            var animation = new DoubleAnimation
            {
                From = current,
                To = target,
                Duration = ExpressionTransitionTime,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };

            transform.BeginAnimation(
                RotateTransform.AngleProperty,
                animation);
        }

        private void AnimateExpressionPosition(
            TranslateTransform transform,
            DependencyProperty property,
            double target)
        {
            double current = (double)transform.GetValue(property);

            transform.SetValue(property, target);

            var animation = new DoubleAnimation
            {
                From = current,
                To = target,
                Duration = ExpressionTransitionTime,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };

            transform.BeginAnimation(property, animation);
        }

        private void ApplyExpressionPose(
            double leftRotation,
            double rightRotation,
            double leftX,
            double leftY,
            double rightX,
            double rightY)
        {
            AnimateExpressionRotation(_leftExpressionRotate, leftRotation);
            AnimateExpressionRotation(_rightExpressionRotate, rightRotation);

            AnimateExpressionPosition(
                _leftExpressionTranslate,
                TranslateTransform.XProperty,
                leftX);

            AnimateExpressionPosition(
                _leftExpressionTranslate,
                TranslateTransform.YProperty,
                leftY);

            AnimateExpressionPosition(
                _rightExpressionTranslate,
                TranslateTransform.XProperty,
                rightX);

            AnimateExpressionPosition(
                _rightExpressionTranslate,
                TranslateTransform.YProperty,
                rightY);
        }

        private void ExpressionTimer_Tick(object? sender, EventArgs e)
        {
            _expressionTimer.Stop();

            if (_isDisposed ||
                _isSleeping ||
                _isWaking ||
                _isPreparingForSleep ||
                _expressionOverride.HasValue)
            {
                return;
            }

            // Return eye scales to neutral.
            AnimateExpressionScale(
                _leftExpressionScale,
                ScaleTransform.ScaleXProperty, 1);

            AnimateExpressionScale(
                _leftExpressionScale,
                ScaleTransform.ScaleYProperty, 1);

            AnimateExpressionScale(
                _rightExpressionScale,
                ScaleTransform.ScaleXProperty, 1);

            AnimateExpressionScale(
                _rightExpressionScale,
                ScaleTransform.ScaleYProperty, 1);

            // Return eye rotations to neutral.
            AnimateExpressionRotation(_leftExpressionRotate, 0);
            AnimateExpressionRotation(_rightExpressionRotate, 0);

            // Return eye positions to neutral.
            ApplyExpressionPose(0, 0, 0, 0, 0, 0);
        }
        public void SetExpression(KageExpression expression)
        {
            if (_isDisposed ||
                _isSleeping ||
                _isWaking ||
                _isPreparingForSleep ||
                _expressionOverride.HasValue)
            {
                return;
            }

            ApplyExpressionCore(expression);
        }

        public bool BeginExpressionOverride(KageExpression expression)
        {
            if (_isDisposed ||
                _isSleeping ||
                _isWaking ||
                _isPreparingForSleep)
            {
                return false;
            }

            if (_expressionOverride == expression)
                return true;

            _expressionOverride = expression;

            ApplyExpressionCore(expression);

            return true;
        }

        public void EndExpressionOverride()
        {
            if (!_expressionOverride.HasValue)
                return;

            _expressionOverride = null;

            if (_isDisposed ||
                _isSleeping ||
                _isWaking ||
                _isPreparingForSleep)
            {
                ResetExpression();
                return;
            }

            ApplyExpressionCore(KageExpression.Neutral);
        }

        private void ApplyExpressionCore(KageExpression expression)
        {
            switch (expression)
            {
                case KageExpression.Neutral:
                    ApplyExpression(1, 1, 1, 1);
                    ApplyExpressionPose(0, 0, 0, 0, 0, 0);
                    break;

                case KageExpression.Judgmental:
                    ApplyExpression(1.08, 0.72, 1.08, 0.72);

                    ApplyExpressionPose(
                        -9, 9,
                        0, 1,
                        0, 1);
                    break;

                case KageExpression.HalfLidded:
                    ApplyExpression(1, 0.62, 1, 0.62);
                    ApplyExpressionPose(0, 0, 0, 0, 0, 0);
                    break;

                case KageExpression.Surprised:
                    ApplyExpression(1.15, 1.30, 1.15, 1.30);
                    ApplyExpressionPose(0, 0, 0, -0.5, 0, -0.5);
                    break;

                case KageExpression.Curious:
                    ApplyExpression(1.15, 1.30, 1.08, 0.85);

                    ApplyExpressionPose(
                        0, 5,
                        0, -0.5,
                        0, 0.5);
                    break;

                case KageExpression.Disappointed:
                    ApplyExpression(1.05, 0.78, 1.05, 0.78);

                    ApplyExpressionPose(
                        4, -4,
                        0, 0.5,
                        0, 0.5);
                    break;

                case KageExpression.Mischievous:
                    ApplyExpression(1.12, 0.85, 0.88, 0.68);

                    ApplyExpressionPose(
                        -5, 8,
                        0, 0,
                        0, 0.5);
                    break;
            }
        }
        private void ResetExpression()
        {
            _expressionTimer.Stop();

            _leftExpressionScale.BeginAnimation(
                ScaleTransform.ScaleXProperty, null);

            _leftExpressionScale.BeginAnimation(
                ScaleTransform.ScaleYProperty, null);

            _rightExpressionScale.BeginAnimation(
                ScaleTransform.ScaleXProperty, null);

            _rightExpressionScale.BeginAnimation(
                ScaleTransform.ScaleYProperty, null);

            _leftExpressionScale.ScaleX = 1;
            _leftExpressionScale.ScaleY = 1;

            _rightExpressionScale.ScaleX = 1;
            _rightExpressionScale.ScaleY = 1;

            _leftExpressionRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _rightExpressionRotate.BeginAnimation(RotateTransform.AngleProperty, null);

            _leftExpressionTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            _leftExpressionTranslate.BeginAnimation(TranslateTransform.YProperty, null);

            _rightExpressionTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            _rightExpressionTranslate.BeginAnimation(TranslateTransform.YProperty, null);

            _leftExpressionRotate.Angle = 0;
            _rightExpressionRotate.Angle = 0;

            _leftExpressionTranslate.X = 0;
            _leftExpressionTranslate.Y = 0;

            _rightExpressionTranslate.X = 0;
            _rightExpressionTranslate.Y = 0;
        }
        public void Dispose()
        {
            if (_isDisposed)
                return;

            // Prevent any pending animation callbacks from continuing.
            _isDisposed = true;

            // Invalidate any pending sleep sequence.
            _sleepSequenceVersion++;

            // Stop all controller timers.
            _isRunning = false;

            _blinkTimer.Stop();
            _gazeTimer.Stop();
            _expressionTimer.Stop();
            _sleepSequenceTimer.Stop();

            // Detach timer events.
            _blinkTimer.Tick -= BlinkTimer_Tick;
            _gazeTimer.Tick -= GazeTimer_Tick;
            _expressionTimer.Tick -= ExpressionTimer_Tick;
            _sleepSequenceTimer.Tick -= SleepSequenceTimer_Tick;

            // Clear internal states.
            _isSleeping = false;
            _isWaking = false;
            _isPreparingForSleep = false;
            _expressionOverride = null;

            // Cancel any active head-shake animation.
            _headShake.BeginAnimation(
                TranslateTransform.XProperty, null);

            _headShake.X = 0.0;

            // Cancel and reset all eye animations.
            ResetEye(_leftScale);
            ResetEye(_rightScale);

            ResetGaze();
            ResetExpression();

            // Restore the normal open-eye visual state.
            // This also cancels any remaining sleep/lid/opacity animations.
            ApplySleep(animate: false);
        }
    }
}
