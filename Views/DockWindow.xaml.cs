using KageDock.Models;
using KageDock.Services;
using KageDock.Coordinators;
using KageDock.Controllers;
using KageDock.Content;
using KageDock.Contracts;
using KageDock.Modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using SharpVectors.Converters;
using WpfColor = System.Windows.Media.Color;


namespace KageDock.Views
{
    public partial class DockWindow : Window, IMyAppsHost
    {
                // My Apps host communication.
        Canvas IMyAppsHost.DragOverlay => this.DragOverlay;

        bool IMyAppsHost.IsDockExpanded => _expanded;
        bool IMyAppsHost.IsContextMenuOpen => _contextMenuOpen;
        void IMyAppsHost.SetMyAppsDragActive(bool isActive)
        {
            _myAppsDragActive = isActive;

            if (isActive)
                CancelCollapseTimer();
        }
        void IMyAppsHost.SetContextMenuOpen(bool isOpen)
        {
            _contextMenuOpen = isOpen;
        }
        bool IMyAppsHost.IsChildWindowVisible =>
            _activeChildWindow?.IsVisible == true;

        bool IMyAppsHost.IsWindowMouseOver => IsMouseOver;
        bool IMyAppsHost.IsDockPanelMouseOver => DockPanel.IsMouseOver;
        bool IMyAppsHost.IsTopHoverTriggerMouseOver =>
            TopHoverTrigger.IsMouseOver;

        private bool _myAppsDragActive;
        void IMyAppsHost.CollapseDock() => CollapseDock();
        void IMyAppsHost.ScheduleCollapse() => ScheduleCollapse();
        void IMyAppsHost.CancelCollapseTimer() => CancelCollapseTimer();
        private const double PanelWidth = 430;
        private DockConfig _config = new();
        private readonly ShortcutStore _shortcutStore = new();
        private List<AppShortcut> _shortcuts => _shortcutStore.Shortcuts;
        private bool _contextMenuOpen;
        private bool _expanded;
        private ShortcutCoordinator? _shortcutCoordinator;
        private readonly IconService _iconService = new();
        private Forms.NotifyIcon? _trayIcon;
        private Forms.ContextMenuStrip? _trayMenu;
        private readonly DockContentHost _contentHost = new();
        private DispatcherTimer? _dragAutoScrollTimer;
        private AddAppsWindow? _addAppsView;
        private AddAppsSession? _addAppsSession;
        private DispatcherTimer? _hideTimer;
        private Window? _activeChildWindow;
        private int _dockAnimationVersion = 0;
        private TaskCompletionSource<bool>? _morphCompletion;
        private bool _isDockOpening;
        private DockView? _pendingMorphView;
        private const double MaxDockWidth = 820;
        private double _maxDockHeight;
        private KageCoordinator? _kageCoordinator;
        private DockContentAnimationController? _dockContentAnimationController;
        private FullscreenDetectionService? _fullscreenDetectionService;
        private KageVisibilityController? _kageVisibilityController;
        private readonly SpotifyMediaSessionService _spotifyMediaSessionService;
        private DockView? _currentView;
        private DockView? _targetView;
        private enum DockView
        {
            MyApps,
            Music,
            Timer,
            AddApps
        }

        public DockWindow(SpotifyMediaSessionService spotifyMediaSessionService)
        {
            _spotifyMediaSessionService = spotifyMediaSessionService
                ?? throw new ArgumentNullException(nameof(spotifyMediaSessionService));

            InitializeComponent();

            _contentHost.Register(
                new DockModuleDescriptor(
                    id: "timer",
                    displayName: "Timer",
                    factory: () => new TimeSuiteModule(),
                    order: 2));

            _contentHost.Register(
                new DockModuleDescriptor(
                    id: "music",
                    displayName: "Music",
                    factory: () =>
                    {
                        var module = new MusicModule(_spotifyMediaSessionService);

                        module.ReactionRequested +=
                            _kageCoordinator!.ShowReaction;

                        module.PlaybackStateChanged +=
                            _kageCoordinator.OnMusicPlaybackStateChanged;

                        return module;
                    },
                    order: 1));

            _contentHost.Register(
                new DockModuleDescriptor(
                    id: "myapps",
                    displayName: "My Apps",
                    factory: () => new MyAppsModule(
                        this,
                        _shortcutStore,
                        _shortcutCoordinator
                            ?? throw new InvalidOperationException(
                                "ShortcutCoordinator has not been initialized."),
                        _iconService),
                    order: 0));

            _dockContentAnimationController = new DockContentAnimationController(
                NavIndicatorTransform,
                AddAppsContent,
                () => IsLoaded,
                () => _expanded);

            SourceInitialized += (_, _) => SetupTrayIcon();
        }

        public sealed class SpringEase : EasingFunctionBase
        {
            public double Damping { get; set; } = 7.0;
            public double Frequency { get; set; } = 10.0;

            protected override double EaseInCore(double normalizedTime)
            {
                double t = normalizedTime;
                double decay = Math.Exp(-Damping * t);

                return 1.0 - decay *
                    Math.Cos(Frequency * t);
            }

            protected override Freezable CreateInstanceCore()
            {
                return new SpringEase
                {
                    Damping = Damping,
                    Frequency = Frequency
                };
            }
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ReloadConfiguration();

            // Initialize the default My Apps module.
            var myAppsModule = _contentHost.Activate("myapps");

            AddAppsContent.Children.Clear();
            AddAppsContent.Children.Add(myAppsModule.View);

            AddAppsContent.Visibility = Visibility.Visible;
            AddAppsContent.IsHitTestVisible = true;

            TitleText.Text = string.IsNullOrWhiteSpace(_config.LauncherTitle)
                ? "My Apps"
                : _config.LauncherTitle;

            DockRevealHost.Width = 100;
            DockRevealHost.Height = 30;
            DockRevealHost.HorizontalAlignment =
                System.Windows.HorizontalAlignment.Center;

            DockRevealHost.VerticalAlignment =
                System.Windows.VerticalAlignment.Top;
            DockRevealHost.Margin = new Thickness(0);
            DockRevealHost.ClipToBounds = true;
            DockRevealHost.Clip = new RectangleGeometry(new Rect(0, 0, 100, 30));

            DockPanel.Background = System.Windows.Media.Brushes.Transparent;
            DockPanel.ClipToBounds = true;

            KageStrip.Visibility = Visibility.Visible;
            ExpandedDockContent.Visibility = Visibility.Collapsed;

            DockPanel.Width = Width - 40;
            DockPanel.Height = Height - 20;
            _maxDockHeight = DockPanel.Height;

            ExpandedDockContent.Visibility = Visibility.Collapsed;
            ExpandedDockContent.IsHitTestVisible = false;

            KageStrip.Visibility = Visibility.Visible;
            KageStrip.IsHitTestVisible = false;

            // Monitor fullscreen applications and foreground app exclusions.
            if (_fullscreenDetectionService == null)
            {
                _fullscreenDetectionService = new FullscreenDetectionService();

                _fullscreenDetectionService.SetTargetMonitorBounds(
                    ScreenService.GetScreen(_config.TargetMonitor).Bounds);

                _fullscreenDetectionService.Start(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle);

                _kageVisibilityController = new KageVisibilityController(
                    KageStrip,
                    KageBubbleAnchor,
                    _fullscreenDetectionService,
                    () => _config);
            }

            _kageCoordinator ??= new KageCoordinator(
                () => _config,
                () => _expanded,
                () => (_currentView ?? DockView.MyApps) == DockView.MyApps,
                KageEyeLeft,
                KageEyeRight,
                KageEyeLeftClosed,
                KageEyeRightClosed,
                KageHeadShake,
                KageEffectCanvas,
                KageBubble,
                KazeSunlitBreeze,
                KageYozakura,
                KageEmbers,
                KageCrimson,
                KageEyeLeft,
                KageEyeRight);

            _kageCoordinator.Initialize();

            // Prewarm Music after the initial dock layout settles.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    if (!IsLoaded)
                        return;

                    var musicModule = (MusicModule)_contentHost.Prepare("music");

                    // Initialize Spotify monitoring even while Music is hidden.
                    _ = musicModule.InitializeMediaAsync();
                }));
        }


                private static string GetIconCacheKey(AppShortcut shortcut)
        {
            return shortcut.TargetPath
                .Trim()
                .Replace('/', '\\');
        }


        public void ReloadConfiguration()
        {
            _config = ConfigService.LoadConfig();

            _fullscreenDetectionService?.SetTargetMonitorBounds(
                ScreenService.GetScreen(_config.TargetMonitor).Bounds);

            _kageCoordinator?.ApplyConfiguration();

            _shortcutStore.Reload();

            _shortcutCoordinator = new ShortcutCoordinator(_shortcutStore);

            _addAppsSession ??= new AddAppsSession(
                _shortcutStore,
                _iconService);

            // Only reposition and resize the window when the target monitor changes.
            UpdateWindowBounds();

            // Restore the dimensions of the currently active view.
            if (_expanded)
            {
                var activeView = _targetView ?? _currentView ?? DockView.MyApps;

                var (activeWidth, activeHeight) =
                    GetDockViewSize(activeView);

                ApplyDockViewSize(activeWidth, activeHeight);
            }

            if (_contentHost.ActiveModule is MyAppsModule myAppsModule)
                myAppsModule.Refresh();
        }

        private void UpdateWindowBounds()
        {
            var screen = ScreenService.GetScreen(_config.TargetMonitor);
            var bounds = screen.Bounds;

            Width = 860;
            Height = (bounds.Height / 2.0) + 70;

            Left = bounds.Left + (bounds.Width - Width) / 2.0;
            Top = bounds.Top - 1;

            RootDockGrid.Width = Width;
            RootDockGrid.Height = Height;

            DockPanel.Width = Width - 40;
            DockPanel.Height = Height - 20;

            _maxDockHeight = DockPanel.Height;
        }


        private void SetupTrayIcon()
        {
            if (_trayIcon != null) return;

            _trayMenu = new Forms.ContextMenuStrip();
            _trayMenu.Items.Add("Open KageDock", null, (_, _) => Dispatcher.Invoke(ExpandDock));
            _trayMenu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));

            var iconPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Images",
                "kage-logo.ico");

            using var trayIcon = new Icon(iconPath);

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = (Icon)trayIcon.Clone(),
                Text = "KageDock",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        }



        private void NormalizeOrder()
        {
            _shortcutCoordinator?.NormalizeOrder();
        }


        private static bool IsSectionMarker(AppShortcut shortcut) =>
            ShortcutCoordinator.IsSectionMarker(shortcut);

        private static string NormalizeCategory(string? category) =>
            ShortcutCoordinator.NormalizeCategory(category);




       private bool IsAnyAppContextMenuOpen()
        {
            return System.Windows.Application.Current.Windows
                .OfType<Window>()
                .Any(window => window.IsActive &&
                    window.IsVisible &&
                    window.GetType() == typeof(System.Windows.Controls.ContextMenu));
        }

        private void RenameLauncherTitle()
        {
            var dialog = new Window
            {
                Title = "Launcher title",
                Width = 320,
                Height = 145,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(WpfColor.FromRgb(24, 27, 32)),
                Foreground = System.Windows.Media.Brushes.White
            };
            var stack = new StackPanel { Margin = new Thickness(16) };
            var input = new System.Windows.Controls.TextBox
            {
                Text = string.IsNullOrWhiteSpace(_config.LauncherTitle) ? "My Apps" : _config.LauncherTitle,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8)
            };
            var save = new System.Windows.Controls.Button
            {
                Content = "Save",
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            save.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
            stack.Children.Add(input);
            stack.Children.Add(save);
            dialog.Content = stack;
            if (dialog.ShowDialog() == true)
            {
                _config.LauncherTitle = input.Text.Trim();
                TitleText.Text = string.IsNullOrWhiteSpace(_config.LauncherTitle) ? "My Apps" : _config.LauncherTitle;
                ConfigService.SaveConfig(_config);
            }
        }


        private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

        private void OpenSettings()
        {
            if (_activeChildWindow is SettingsWindow existingSettings &&
                existingSettings.IsVisible)
            {
                existingSettings.Activate();
                return;
            }

            CancelCollapseTimer();

            var settings = new SettingsWindow(this) { Owner = this };
            _activeChildWindow = settings;

            settings.Closed += (_, _) =>
            {
                _activeChildWindow = null;

                if (!IsMouseOver)
                    ScheduleCollapse(160);
            };

            settings.ShowDialog();
        }

        public void OpenSettingsFromExternalRequest()
        {
            OpenSettings();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _contentHost.Dispose();
            }
            finally
            {
                // Shut down views.
                _dragAutoScrollTimer?.Stop();
                _dragAutoScrollTimer = null;

                CancelCollapseTimer();

                // Complete any pending dock transition.
                _morphCompletion?.TrySetResult(true);
                _morphCompletion = null;

                // Dispose Kage coordinator.
                _kageCoordinator?.Dispose();
                _kageCoordinator = null;

                // Dispose visibility services.
                _kageVisibilityController?.Dispose();
                _kageVisibilityController = null;

                _fullscreenDetectionService?.Dispose();
                _fullscreenDetectionService = null;

                // Dispose system tray resources.
                _trayIcon?.Dispose();
                _trayIcon = null;

                _trayMenu?.Dispose();
                _trayMenu = null;

                base.OnClosed(e);
            }
        }
        private void OnWindowMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => ExpandDock();
    }
}
