using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KageDock.Controllers;
using KageDock.Contracts;
using KageDock.Models;
using KageDock.Services;
using KageDock.Coordinators;
using System.Windows.Threading;

namespace KageDock.Views
{
    public partial class MyAppsView : System.Windows.Controls.UserControl
    {
        private readonly IMyAppsHost _host;
        private readonly ShortcutStore _shortcutStore;
        private readonly ShortcutCoordinator _shortcutCoordinator;
        private readonly IconService _iconService;
        private readonly DockScrollController _scrollController;

        // Shared shortcut data.
        private List<AppShortcut> _shortcuts =>
            _shortcutStore.Shortcuts;
        private readonly Dictionary<AppShortcut, DateTime> _newAppHighlightExpirations =
            new(ReferenceEqualityComparer.Instance);

        private readonly DispatcherTimer _newAppHighlightTimer =
            new() { Interval = TimeSpan.FromMilliseconds(100) };       

                // My Apps drag and tile state.
        private AppShortcut? _pendingDropRippleShortcut;
        private AppShortcut? _pendingDragShortcut;
        private AppShortcut? _previewDraggedShortcut;
        private bool _dragStarted;
        private bool _sectionsRefreshedDuringDrag;
        private System.Windows.Point _dragStartPoint;
        private Border? _dragSourceTile;
        private Border? _newSectionDropZone;
        private FrameworkElement? _floatingDragVisual;
        private System.Windows.Point _floatingDragOffset;

        private System.Windows.Controls.Primitives.UniformGrid? _previewGrid;
        private string? _previewCategory;
        private int _previewInsertSlot = -1;

        private System.Windows.Threading.DispatcherTimer? _dragAutoScrollTimer;
        private DateTime _lastDragAutoScrollTick = DateTime.MinValue;
        private double _dragPointerY;

        public ScrollViewer AppScrollViewer =>
            AppScrollViewerControl;

        public MyAppsView(
            IMyAppsHost host,
            ShortcutStore shortcutStore,
            ShortcutCoordinator shortcutCoordinator,
            IconService iconService)
        {
            InitializeComponent();

            _newAppHighlightTimer.Tick += (_, _) =>
            {
                DateTime now = DateTime.UtcNow;

                var expiredApps = _newAppHighlightExpirations
                    .Where(pair => pair.Value <= now)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var app in expiredApps)
                    _newAppHighlightExpirations.Remove(app);

                if (_newAppHighlightExpirations.Count == 0)
                    _newAppHighlightTimer.Stop();
            };

            _host = host;
            _shortcutStore = shortcutStore;
            _shortcutCoordinator = shortcutCoordinator;
            _iconService = iconService;

            _scrollController = new DockScrollController(AppScrollViewerControl);
        }

        public void HighlightNewApps(IReadOnlyList<AppShortcut> apps)
        {
            if (apps == null || apps.Count == 0)
                return;

            DateTime expiration = DateTime.UtcNow.AddSeconds(4.5);

            foreach (var app in apps)
                _newAppHighlightExpirations[app] = expiration;

            _newAppHighlightTimer.Start();
        }
        public void Dispose()
        {
            _scrollController.Dispose();
        }
        public void RestoreView(bool animateTileMoves = true)
        {
            RefreshSections(animateTileMoves);

            AppScrollViewer.Visibility = Visibility.Visible;
            NewSectionDropZoneHost.Visibility = Visibility.Visible;
        }
    }
}