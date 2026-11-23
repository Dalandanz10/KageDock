using KageDock.Models;
using KageDock.Services;
using KageDock.Coordinators;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using KageDock.Controllers;

namespace KageDock.Views
{
    public partial class AddAppsWindow : System.Windows.Controls.UserControl
    {
        private readonly AddAppsSession _session;
        private bool _isRefreshingDestination;
        private int _visibleAppCount;

        private sealed class AppTileItem : INotifyPropertyChanged
        {
            public AppShortcut App { get; }
            public object? Icon => App.CachedIcon;

            public string DisplayName =>
                string.IsNullOrWhiteSpace(App.DisplayName)
                    ? App.Name
                    : App.DisplayName;

            private bool _isSelected;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    PropertyChanged?.Invoke(
                        this,
                        new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public AppTileItem(AppShortcut app, bool isSelected)
            {
                App = app;
                _isSelected = isSelected;
            }
        }
            private DockScrollController? _appsScrollController;

        public List<AppShortcut> SelectedApps { get; } = new();
        public event Action? BackRequested;

        public event Action? Finished;


        private const string CreateNewSectionLabel = "＋ Create New Section...";

        public bool IsCreatingNewSection =>
            NewSectionBox.Visibility == Visibility.Visible;

        public string SelectedCategory =>
            IsCreatingNewSection
                ? NewSectionBox.Text.Trim()
                : CategoryBox.SelectedItem as string ?? "Recently Added";

        public AddAppsWindow(AddAppsSession session)
        {
            InitializeComponent();

            _session = session;

            Unloaded += (_, _) =>
            {
                _appsScrollController?.Dispose();
                _appsScrollController = null;
            };

            RefreshDestinationSections();

            UpdateAddSelectedButton();

            Loaded += async (_, _) =>
            {
                AppsItems.ApplyTemplate();

                var scrollViewer = FindVisualChild<ScrollViewer>(AppsItems);

                if (scrollViewer != null && _appsScrollController == null)
                    _appsScrollController = new DockScrollController(scrollViewer);

                await LoadAppsAsync();
            };
        }


        private async void AddAppManually_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select an application",
                Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            if (!_session.TryAddManualApp(
                dialog.FileName,
                SelectedCategory,
                out var app))
            {
                System.Windows.MessageBox.Show(
                    "This application is already in the list.",
                    "KageDock",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            await _session.LoadIconsAsync(new[] { app });

            if (!IsLoaded)
                return;

            RenderApps();

            SelectionCount.Text = $"{_session.SelectedCount} selected";
        }


        private static T? FindVisualChild<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T result)
                    return result;

                var descendant = FindVisualChild<T>(child);

                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke();
        }

        private async Task LoadAppsAsync()
        {
            LoadingText.Text = "Finding your apps…";
            LoadingText.Visibility = Visibility.Visible;

            try
            {
                await _session.EnsureLoadedAsync();

                if (!IsLoaded)
                    return;

                RenderApps();

                SelectionCount.Text = _session.SelectedCount > 0
                    ? $"{_session.SelectedCount} selected"
                    : $"{_session.DiscoveredApps.Count} apps found";

                UpdateAddSelectedButton();

                LoadingText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (!IsLoaded)
                    return;

                System.Diagnostics.Debug.WriteLine(
                    $"Add Apps discovery failed: {ex}");

                LoadingText.Text = "Couldn't load your apps.";
                LoadingText.Visibility = Visibility.Visible;
            }
        }

        private void RenderApps()
        {
            string filter = SearchBox.Text?.Trim() ?? "";

            var visible = _session.DiscoveredApps
                .Where(a => !IsAlreadyAdded(a))
                .Where(a =>
                    string.IsNullOrWhiteSpace(filter) ||
                    (a.DisplayName ?? a.Name).Contains(
                        filter, StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains(
                        filter, StringComparison.OrdinalIgnoreCase) ||
                    a.TargetPath.Contains(
                        filter, StringComparison.OrdinalIgnoreCase) ||
                    (a.Arguments ?? "").Contains(
                        filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

                var rows = new List<List<AppTileItem?>>();

                for (int i = 0; i < visible.Count; i += 6)
                {
                    var row = new List<AppTileItem?>(6);

                    for (int j = i; j < Math.Min(i + 6, visible.Count); j++)
                    {
                        var app = visible[j];

                        row.Add(new AppTileItem(
                            app,
                            _session.IsSelected(app.TargetPath)));
                    }

                    // Fill unused cells in the final row.
                    while (row.Count < 6)
                        row.Add(null);

                    rows.Add(row);
                }
            _visibleAppCount = visible.Count;

            AppsItems.ItemsSource = rows;
        }

        private void AppTile_MouseLeftButtonUp(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border tile ||
                tile.Tag is not AppShortcut app ||
                tile.DataContext is not AppTileItem item)
                return;

            bool selected = _session.ToggleSelection(app.TargetPath);

            item.IsSelected = selected;

            SelectionCount.Text = _session.SelectedCount > 0
                ? $"{_session.SelectedCount} selected"
                : $"{_session.DiscoveredApps.Count} apps found";

            UpdateAddSelectedButton();
        }

        public void CompleteAdding()
        {
            // Keep discovered apps cached, but clear their selection.
            _session.ClearSelection();
            SelectedApps.Clear();

            // Re-render using the updated existing shortcuts.
            RenderApps();

            SelectionCount.Text = $"{_visibleAppCount} apps found";

            RefreshDestinationSections();
        }

        private bool IsAlreadyAdded(AppShortcut app)
        {
            return _session.IsAlreadyAdded(app.TargetPath);
        }

        private void SearchBox_TextChanged(
            object sender, TextChangedEventArgs e)
        {
            RenderApps();
        }

        public void RefreshDestinationSections()
        {
            _isRefreshingDestination = true;

            try
            {
                var sections = _session.GetSectionNames();

                CategoryBox.Items.Clear();

                foreach (string section in sections)
                    CategoryBox.Items.Add(section);

                CategoryBox.Items.Add(CreateNewSectionLabel);

                NewSectionBox.Clear();
                CancelNewSectionButton.Visibility = Visibility.Collapsed;

                CategoryBox.SelectedIndex = 0;

                if (sections.Count == 0)
                {
                    CategoryBox.Visibility = Visibility.Collapsed;
                    NewSectionBox.Visibility = Visibility.Visible;
                }
                else
                {
                    CategoryBox.Visibility = Visibility.Visible;
                    NewSectionBox.Visibility = Visibility.Collapsed;
                }
            }
            finally
            {
                _isRefreshingDestination = false;
            }

            UpdateAddSelectedButton();
        }

        private void CategoryBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isRefreshingDestination)
                return;

            if (CategoryBox.SelectedItem?.ToString() == CreateNewSectionLabel)
            {
                CategoryBox.Visibility = Visibility.Collapsed;
                NewSectionBox.Visibility = Visibility.Visible;
                CancelNewSectionButton.Visibility =
                    CategoryBox.Items.Count > 1
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                NewSectionBox.Text = "";
                NewSectionBox.Focus();
            }
            else
            {
                CategoryBox.Visibility = Visibility.Visible;
                NewSectionBox.Visibility = Visibility.Collapsed;
                CancelNewSectionButton.Visibility = Visibility.Collapsed;
            }

            UpdateAddSelectedButton();
        }

        private void NewSectionBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            UpdateAddSelectedButton();
        }

        private void CancelNewSectionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CategoryBox.SelectedIndex = 0;

            CategoryBox.Visibility = Visibility.Visible;
            NewSectionBox.Visibility = Visibility.Collapsed;
            CancelNewSectionButton.Visibility = Visibility.Collapsed;

            UpdateAddSelectedButton();
        }

        private void UpdateAddSelectedButton()
        {
            bool hasSelection = _session.SelectedCount > 0;

            bool validDestination;

            if (IsCreatingNewSection)
            {
                string name = NewSectionBox.Text.Trim();

                validDestination =
                    !string.IsNullOrWhiteSpace(name) &&
                    !_session.GetSectionNames().Any(section =>
                        section.Equals(
                            name,
                            StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                validDestination = CategoryBox.SelectedItem is string;
            }

            AddSelectedButton.IsEnabled =
                hasSelection && validDestination;
        }

        public void ShowAddedIndicator(int count)
        {
            if (count <= 0)
                return;

            AddedMessage.Text = count == 1
                ? "1 app added!"
                : $"{count} apps added!";

            AddedIndicator.Visibility = Visibility.Visible;
            AddedIndicator.Opacity = 0;

            AddedGlow.Opacity = 0;

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            var glowIn = new DoubleAnimation
            {
                From = 0,
                To = 0.75,
                Duration = TimeSpan.FromMilliseconds(260),
                AutoReverse = true,
                BeginTime = TimeSpan.FromMilliseconds(100),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            fadeIn.Completed += (_, _) =>
            {
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    BeginTime = TimeSpan.FromSeconds(2),
                    Duration = TimeSpan.FromMilliseconds(350),
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseIn
                    }
                };

                fadeOut.Completed += (_, _) =>
                {
                    AddedIndicator.Visibility = Visibility.Collapsed;
                };

                AddedIndicator.BeginAnimation(
                    UIElement.OpacityProperty,
                    fadeOut,
                    HandoffBehavior.SnapshotAndReplace);
            };

            AddedIndicator.BeginAnimation(
                UIElement.OpacityProperty,
                fadeIn,
                HandoffBehavior.SnapshotAndReplace);

            AddedGlow.BeginAnimation(
                System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                glowIn,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void AddSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!AddSelectedButton.IsEnabled)
                return;

            SelectedApps.Clear();
            SelectedApps.AddRange(_session.GetSelectedApps());

            Finished?.Invoke();
        }
    }
}
