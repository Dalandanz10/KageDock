
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KageDock.Controllers;
using KageDock.Models;
using KageDock.Services;
using KageDock.Views;
using SharpVectors.Converters;

namespace KageDock.Coordinators
{
    public sealed class KageCoordinator : IDisposable
    {
        private static readonly TimeSpan KageIdleTimeout =
            TimeSpan.FromMinutes(20);

        private readonly Func<DockConfig> _getConfig;
        private readonly Func<bool> _isExpanded;
        private readonly Func<bool> _shouldStartEffectsOnLoad;

        private readonly KageSpeechBubble _bubble;

        private readonly KazeSunlitBreezeEffect _breeze;
        private readonly KageYozakuraEffect _yozakura;
        private readonly KageEmberEffect _embers;
        private readonly KageCrimsonEffect _crimson;

        private readonly KageEyeController _eyeController;
        private readonly KageDialogueController _dialogueController;
        private readonly KageExpressionEffectController _expressionEffectController;

        private readonly DispatcherTimer _environmentalEffectTimer;
        private readonly DispatcherTimer _idleTimer;

        private readonly List<KageDialogueCategory> _environmentalEffectBag = new();

        private DateTime _musicIdleReactionDueUtc = DateTime.MinValue;
        private DateTime _idleReactionDueUtc = DateTime.MinValue;
        private DateTime _creatorSecretOpportunityDueUtc = DateTime.MinValue;

        private bool _isMusicPlaying;
        private bool _pendingMusicSkipReaction;
        private bool _isInitialized;
        private bool _isDisposed;

        private NativePoint _lastCursorPosition;
        private DateTime _lastCursorMovementUtc;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        public KageCoordinator(
            Func<DockConfig> getConfig,
            Func<bool> isExpanded,
            Func<bool> shouldStartEffectsOnLoad,
            FrameworkElement leftEye,
            FrameworkElement rightEye,
            FrameworkElement leftClosedEye,
            FrameworkElement rightClosedEye,
            TranslateTransform headShake,
            Canvas expressionEffectCanvas,
            KageSpeechBubble bubble,
            KazeSunlitBreezeEffect breeze,
            KageYozakuraEffect yozakura,
            KageEmberEffect embers,
            KageCrimsonEffect crimson,
            SvgViewbox leftCrimsonEye,
            SvgViewbox rightCrimsonEye)
        {
            _getConfig = getConfig;
            _isExpanded = isExpanded;
            _shouldStartEffectsOnLoad = shouldStartEffectsOnLoad;

            _bubble = bubble;

            _breeze = breeze;
            _yozakura = yozakura;
            _embers = embers;
            _crimson = crimson;

            _eyeController = new KageEyeController(
                leftEye,
                rightEye,
                leftClosedEye,
                rightClosedEye,
                headShake);

            _dialogueController = new KageDialogueController();

            _expressionEffectController =
                new KageExpressionEffectController(expressionEffectCanvas);

            _environmentalEffectTimer = new DispatcherTimer();
            _environmentalEffectTimer.Tick += EnvironmentalEffectTimer_Tick;

            _idleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            _idleTimer.Tick += KageIdleTimer_Tick;

            _embers.Attach(leftEye, rightEye);
            _crimson.Attach(leftCrimsonEye, rightCrimsonEye);
        }

        public void Initialize()
        {
            if (_isInitialized || _isDisposed)
                return;

            _isInitialized = true;

            _breeze.BurstStarted += Breeze_BurstStarted;
            _bubble.Closed += Bubble_Closed;

            _eyeController.Start();

            InitializeCreatorSecretState();
            StartIdleTracking();

            if (!_isExpanded() && _shouldStartEffectsOnLoad())
                StartEnvironmentalEffectScheduler();
        }

        // ---------------------------------------------------------
        // Dock lifecycle
        // ---------------------------------------------------------

        public void OnDockExpanding()
        {
            if (!_isInitialized || _isDisposed)
                return;

            _eyeController.Stop();
            _bubble.Dismiss();

            if (_isMusicPlaying)
            {
                _musicIdleReactionDueUtc =
                    DateTime.UtcNow.AddMinutes(Random.Shared.Next(3, 6));
            }
        }

        public void OnDockCollapsed()
        {
            if (!_isInitialized || _isDisposed)
                return;

            _eyeController.Start();

            StartEnvironmentalEffectScheduler();
            ShowPendingMusicSkipReaction();
        }

        // ---------------------------------------------------------
        // Configuration
        // ---------------------------------------------------------

        public void ApplyConfiguration()
        {
            if (!_isInitialized || _isDisposed)
                return;

            if (_getConfig().EnableEnvironmentalEffects)
                StartEnvironmentalEffectScheduler();
            else
                StopEnvironmentalEffects();

            ApplyDialogueSetting();
        }

        private void ApplyDialogueSetting()
        {
            var config = _getConfig();

            if (!config.EnableKageDialogue)
            {
                _pendingMusicSkipReaction = false;

                if (_bubble.IsShowing)
                    _bubble.Dismiss();

                return;
            }

            DateTime now = DateTime.UtcNow;

            _idleReactionDueUtc = _isMusicPlaying
                ? DateTime.MinValue
                : now.AddMinutes(Random.Shared.Next(8, 13));

            _musicIdleReactionDueUtc = _isMusicPlaying
                ? now.AddMinutes(Random.Shared.Next(10, 16))
                : DateTime.MinValue;
        }

        private void InitializeCreatorSecretState()
        {
            DateTime now = DateTime.UtcNow;
            var config = _getConfig();

            config.CreatorSecretRemainingMessages ??= new List<string>();

            _creatorSecretOpportunityDueUtc =
                config.CreatorSecretNextEligibleUtc > now
                    ? config.CreatorSecretNextEligibleUtc
                    : now.AddMinutes(Random.Shared.Next(5, 11));
        }

        // ---------------------------------------------------------
        // Environmental effects
        // ---------------------------------------------------------

        private void StartEnvironmentalEffectScheduler()
        {
            if (!_getConfig().EnableEnvironmentalEffects || _isExpanded())
                return;

            _environmentalEffectTimer.Stop();

            _environmentalEffectTimer.Interval =
                TimeSpan.FromMinutes(Random.Shared.Next(5, 9));

            _environmentalEffectTimer.Start();
        }

        private void StopEnvironmentalEffects()
        {
            _environmentalEffectTimer.Stop();

            _breeze.Stop();
            _yozakura.Cancel();
            _embers.Cancel();
            _crimson.Cancel();
        }

        private bool IsAnyEnvironmentalEffectPlaying()
        {
            return _breeze.IsPlaying ||
                   _yozakura.IsPlaying ||
                   _embers.IsPlaying ||
                   _crimson.IsPlaying;
        }

        private void EnvironmentalEffectTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _environmentalEffectTimer.Stop();

            if (!_getConfig().EnableEnvironmentalEffects)
            {
                StopEnvironmentalEffects();
                return;
            }

            if (_isExpanded())
                return;

            if (_eyeController.IsSleeping ||
                _eyeController.IsPreparingForSleep ||
                _eyeController.IsWaking)
            {
                StartEnvironmentalEffectScheduler();
                return;
            }

            if (_environmentalEffectBag.Count == 0)
                RefillEnvironmentalEffectBag();

            KageDialogueCategory category = _environmentalEffectBag[0];

            bool started = category switch
            {
                KageDialogueCategory.Kaze => _breeze.PlayOnce(),
                KageDialogueCategory.Yozakura => _yozakura.Play(),
                KageDialogueCategory.Ember => _embers.Play(),
                KageDialogueCategory.Crimson => _crimson.Play(),
                _ => false
            };

            if (started)
            {
                _environmentalEffectBag.RemoveAt(0);

                if (category != KageDialogueCategory.Kaze)
                    HandleEnvironmentalEffectDialogue(category);
            }

            StartEnvironmentalEffectScheduler();
        }

        private void RefillEnvironmentalEffectBag()
        {
            _environmentalEffectBag.Clear();

            _environmentalEffectBag.AddRange(new[]
            {
                KageDialogueCategory.Kaze,
                KageDialogueCategory.Yozakura,
                KageDialogueCategory.Ember,
                KageDialogueCategory.Crimson
            });

            // Fisher-Yates shuffle.
            for (int i = _environmentalEffectBag.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);

                (_environmentalEffectBag[i], _environmentalEffectBag[j]) =
                    (_environmentalEffectBag[j], _environmentalEffectBag[i]);
            }
        }

        private void Breeze_BurstStarted(object? sender, EventArgs e)
        {
            HandleEnvironmentalEffectDialogue(KageDialogueCategory.Kaze);
        }

        // ---------------------------------------------------------
        // Environmental-effect dialogue
        // ---------------------------------------------------------

        private void HandleEnvironmentalEffectDialogue(
            KageDialogueCategory effectCategory)
        {
            var config = _getConfig();

            if (!config.EnableKageDialogue ||
                _isExpanded() ||
                _eyeController.IsSleeping ||
                _eyeController.IsPreparingForSleep ||
                _eyeController.IsWaking ||
                _bubble.IsShowing)
            {
                return;
            }

            int roll = Random.Shared.Next(100);

            // 40% — Silence.
            if (roll < 40)
                return;

            // 40% — Effect-specific dialogue.
            if (roll < 80)
            {
                var reaction =
                    _dialogueController.CreateReaction(effectCategory);

                if (reaction != null)
                {
                    DisplayReaction(
                        reaction.Message,
                        reaction.Expression,
                        reaction.Effect);
                }

                return;
            }

            // 20% — CreatorSecret attempt.
            TryShowCreatorSecret(DateTime.UtcNow);
        }

        private bool TryShowCreatorSecret(
            DateTime now,
            bool bypassSchedule = false)
        {
            var config = _getConfig();

            if (!config.EnableKageDialogue ||
                _isExpanded() ||
                _eyeController.IsSleeping ||
                _eyeController.IsPreparingForSleep ||
                _eyeController.IsWaking ||
                _bubble.IsShowing)
            {
                return false;
            }

            if (!bypassSchedule)
            {
                if (now < _creatorSecretOpportunityDueUtc)
                    return false;

                if (now < config.CreatorSecretNextEligibleUtc)
                {
                    _creatorSecretOpportunityDueUtc =
                        config.CreatorSecretNextEligibleUtc;

                    return false;
                }
            }

            var reaction =
                _dialogueController.CreateCreatorSecretReaction(
                    config.CreatorSecretRemainingMessages);

            if (reaction == null)
            {
                _creatorSecretOpportunityDueUtc =
                    now.AddMinutes(Random.Shared.Next(10, 21));

                return false;
            }

            // Random minimum spacing: 2–3 hours.
            config.CreatorSecretNextEligibleUtc =
                now.AddMinutes(Random.Shared.Next(120, 181));

            _creatorSecretOpportunityDueUtc =
                config.CreatorSecretNextEligibleUtc;

            ConfigService.SaveConfig(config);

            DisplayReaction(
                reaction.Message,
                reaction.Expression,
                reaction.Effect);

            return true;
        }

        // ---------------------------------------------------------
        // Music reactions
        // ---------------------------------------------------------

        public void OnMusicPlaybackStateChanged(bool isPlaying)
        {
            _isMusicPlaying = isPlaying;

            DateTime now = DateTime.UtcNow;

            _idleReactionDueUtc = _isMusicPlaying
                ? DateTime.MinValue
                : now.AddMinutes(Random.Shared.Next(8, 13));

            _musicIdleReactionDueUtc = _isMusicPlaying
                ? now.AddMinutes(12)
                : DateTime.MinValue;
        }

        public void ShowReaction(KageDialogueCategory category)
        {
            if (!_getConfig().EnableKageDialogue)
                return;

            // Environmental effects take priority over Music reactions.
            if (IsAnyEnvironmentalEffectPlaying())
                return;

            if (_isExpanded())
            {
                if (category == KageDialogueCategory.MusicSkip)
                    _pendingMusicSkipReaction = true;

                return;
            }

            if (_bubble.IsShowing)
                return;

            var reaction = _dialogueController.CreateReaction(category);

            if (reaction == null)
                return;

            DisplayReaction(
                reaction.Message,
                reaction.Expression,
                reaction.Effect);
        }

        private void DisplayReaction(
            string message,
            KageExpression expression,
            KageExpressionEffect? effect)
        {
            if (!_getConfig().EnableKageDialogue ||
                _isExpanded() ||
                _bubble.IsShowing)
            {
                return;
            }

            _bubble.Show(message);

            if (!_bubble.IsShowing)
                return;

            if (!_eyeController.BeginExpressionOverride(expression))
            {
                _bubble.Dismiss();
                return;
            }

            if (effect.HasValue)
                _expressionEffectController.Show(effect.Value);
            else
                _expressionEffectController.Clear();
        }

        private void ShowPendingMusicSkipReaction()
        {
            if (!_pendingMusicSkipReaction || _isExpanded())
                return;

            _pendingMusicSkipReaction = false;

            ShowReaction(KageDialogueCategory.MusicSkip);
        }

        private void Bubble_Closed(object? sender, EventArgs e)
        {
            _eyeController.EndExpressionOverride();
        }

        // ---------------------------------------------------------
        // Idle tracking and sleep
        // ---------------------------------------------------------

        private void StartIdleTracking()
        {
            if (GetCursorPos(out var position))
                _lastCursorPosition = position;

            DateTime now = DateTime.UtcNow;
            _lastCursorMovementUtc = now;

            _idleReactionDueUtc = _isMusicPlaying
                ? DateTime.MinValue
                : now.AddMinutes(Random.Shared.Next(8, 13));

            _musicIdleReactionDueUtc =
                now.AddMinutes(Random.Shared.Next(10, 16));

            _idleTimer.Start();
        }

        private void KageIdleTimer_Tick(object? sender, EventArgs e)
        {
            if (!GetCursorPos(out var currentPosition))
                return;

            DateTime now = DateTime.UtcNow;

            bool cursorMoved =
                currentPosition.X != _lastCursorPosition.X ||
                currentPosition.Y != _lastCursorPosition.Y;

            if (cursorMoved)
            {
                _lastCursorPosition = currentPosition;
                _lastCursorMovementUtc = now;

                if (_eyeController.IsSleeping ||
                    _eyeController.IsPreparingForSleep)
                {
                    _eyeController.ExitSleepMode();

                    // Resume environmental scheduling after Kage wakes.
                    StartEnvironmentalEffectScheduler();
                }
            }

            if (_isExpanded())
                return;

            if (_eyeController.IsSleeping ||
                _eyeController.IsPreparingForSleep ||
                _eyeController.IsWaking)
            {
                return;
            }

            // Sleep behavior remains active even when dialogue is disabled.
            if (now - _lastCursorMovementUtc >= KageIdleTimeout)
            {
                StopEnvironmentalEffects();
                _eyeController.EnterSleepMode();
                return;
            }

            if (!_getConfig().EnableKageDialogue)
                return;

            bool reactionDue = _isMusicPlaying
                ? _musicIdleReactionDueUtc != DateTime.MinValue &&
                  now >= _musicIdleReactionDueUtc
                : _idleReactionDueUtc != DateTime.MinValue &&
                  now >= _idleReactionDueUtc;

            if (!reactionDue)
                return;

            if (_bubble.IsShowing)
            {
                PostponeIdleReaction();
                return;
            }

            if (_isMusicPlaying)
            {
                if (Random.Shared.NextDouble() < 0.65)
                {
                    _musicIdleReactionDueUtc =
                        now.AddMinutes(Random.Shared.Next(10, 16));

                    return;
                }

                var reaction = _dialogueController.CreateWeightedReaction(
                    (KageDialogueCategory.MusicIdle, 20),
                    (KageDialogueCategory.MusicApproval, 9),
                    (KageDialogueCategory.MusicJudgment, 6));

                if (reaction == null)
                {
                    PostponeIdleReaction();
                    return;
                }

                DisplayReaction(
                    reaction.Message,
                    reaction.Expression,
                    reaction.Effect);

                _musicIdleReactionDueUtc = now.AddMinutes(12);
            }
            else
            {
                if (Random.Shared.NextDouble() < 0.50)
                {
                    _idleReactionDueUtc =
                        now.AddMinutes(Random.Shared.Next(8, 13));

                    return;
                }

                var reaction = _dialogueController.CreateWeightedReaction(
                    (KageDialogueCategory.Idle, 25),
                    (KageDialogueCategory.Curious, 8),
                    (KageDialogueCategory.Grumpy, 6),
                    (KageDialogueCategory.Mischievous, 5),
                    (KageDialogueCategory.Ridiculous, 3),
                    (KageDialogueCategory.Affectionate, 3));

                if (reaction == null)
                {
                    PostponeIdleReaction();
                    return;
                }

                DisplayReaction(
                    reaction.Message,
                    reaction.Expression,
                    reaction.Effect);

                _idleReactionDueUtc =
                    now.AddMinutes(Random.Shared.Next(8, 13));
            }
        }

        private void PostponeIdleReaction()
        {
            DateTime nextAttempt =
                DateTime.UtcNow.AddSeconds(Random.Shared.Next(30, 61));

            if (_isMusicPlaying)
                _musicIdleReactionDueUtc = nextAttempt;
            else
                _idleReactionDueUtc = nextAttempt;
        }

        // ---------------------------------------------------------
        // Shutdown
        // ---------------------------------------------------------

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _idleTimer.Stop();
            _idleTimer.Tick -= KageIdleTimer_Tick;

            _environmentalEffectTimer.Stop();
            _environmentalEffectTimer.Tick -= EnvironmentalEffectTimer_Tick;

            _breeze.BurstStarted -= Breeze_BurstStarted;
            _bubble.Closed -= Bubble_Closed;

            StopEnvironmentalEffects();
            _breeze.Dispose();

            _eyeController.Dispose();
            _expressionEffectController.Dispose();
        }
    }
}
