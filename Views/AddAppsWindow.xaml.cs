
using KageDock.Models;
using KageDock.Services;
using KageDock.Coordinators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace KageDock.Views
{
    public partial class AddAppsWindow : System.Windows.Controls.UserControl
    {
        private readonly AddAppsSession _session;
        private bool _scrollRenderingAttached;
        private double _targetScroll;
        private DateTime _lastScrollFrame = DateTime.MinValue;

        public List<AppShortcut> SelectedApps { get; } = new();
        public event Action? BackRequested;

        public event Action? Finished;
        

        public string SelectedCategory =>
            CategoryBox.SelectedItem as string ?? "Pinned";

        public AddAppsWindow(AddAppsSession session)
        {
            InitializeComponent();

            _session = session;

            Unloaded += (_, _) => StopAppsSmoothScroll();

            CategoryBox.ItemsSource = new[]
            {
                "Pinned", "Games", "Tools", "Work", "Other"
            };

            CategoryBox.SelectedIndex = 0;

            Loaded += async (_, _) => await LoadAppsAsync();
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

        private void AppsScrollViewer_PreviewMouseWheel(
        object sender,
            System.Windows.Input.MouseWheelEventArgs e)
        {
            e.Handled = true;

            double baseOffset = _scrollRenderingAttached
                ? _targetScroll
                : AppsScrollViewer.VerticalOffset;

            double maxOffset = Math.Max(
                0,
                AppsScrollViewer.ExtentHeight - AppsScrollViewer.ViewportHeight);

            _targetScroll = Math.Clamp(
                baseOffset - e.Delta * 0.65,
                0,
                maxOffset);

            if (!_scrollRenderingAttached)
            {
                System.Windows.Media.CompositionTarget.Rendering
                    += SmoothAppsScroll_Rendering;

                _scrollRenderingAttached = true;
            }
        }

        private void SmoothAppsScroll_Rendering(object? sender, EventArgs e)
        {
            if (!IsLoaded || !AppsScrollViewer.IsVisible)
            {
                StopAppsSmoothScroll();
                return;
            }

            var now = DateTime.UtcNow;

            if (_lastScrollFrame != DateTime.MinValue)
            {
                double elapsed = (now - _lastScrollFrame).TotalSeconds;

                if (elapsed > 0.05)
                {
                    _lastScrollFrame = now;
                    return;
                }

                double maxOffset = Math.Max(
                    0,
                    AppsScrollViewer.ExtentHeight - AppsScrollViewer.ViewportHeight);

                _targetScroll = Math.Clamp(_targetScroll, 0, maxOffset);

                double current = AppsScrollViewer.VerticalOffset;
                double difference = _targetScroll - current;

                if (Math.Abs(difference) < 0.5)
                {
                    AppsScrollViewer.ScrollToVerticalOffset(_targetScroll);
                    StopAppsSmoothScroll();
                    return;
                }

                double blend = 1.0 - Math.Exp(-14.0 * elapsed);

                AppsScrollViewer.ScrollToVerticalOffset(
                    current + difference * blend);
            }

            _lastScrollFrame = now;
        }

        private void StopAppsSmoothScroll()
        {
            if (_scrollRenderingAttached)
            {
                System.Windows.Media.CompositionTarget.Rendering
                    -= SmoothAppsScroll_Rendering;

                _scrollRenderingAttached = false;
            }

            _lastScrollFrame = DateTime.MinValue;
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
            AppsItems.Items.Clear();


            string filter = SearchBox.Text?.Trim() ?? "";

            var visible = _session.DiscoveredApps
            .Where(a => !IsAlreadyAdded(a))
            .Where(a =>
                string.IsNullOrWhiteSpace(filter) ||
                (a.DisplayName ?? a.Name).Contains(
                    filter, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                a.TargetPath.Contains(
                    filter, StringComparison.OrdinalIgnoreCase) ||
                (a.Arguments ?? "").Contains(
                    filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

            foreach (var app in visible)
            {
                var tile = new Border
                {
                    Margin = new Thickness(4),
                    Height = 104,
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(34, 38, 45)),
                    BorderBrush = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(54, 61, 71)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = app
                };

                var panel = new StackPanel
                {
                    Margin = new Thickness(6),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var icon = new System.Windows.Controls.Image
                {
                    Source = app.CachedIcon,
                    Width = 48,
                    Height = 48,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 4)
                };
                    
                var name = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(app.DisplayName)
                        ? app.Name
                        : app.DisplayName,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 10.5,
                    TextAlignment = System.Windows.TextAlignment.Center,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    MaxHeight = 30
                };

                panel.Children.Add(icon);
                panel.Children.Add(name);
                tile.Child = panel;

                UpdateSelectionStyle(
                    tile, _session.IsSelected(app.TargetPath));

                tile.MouseLeftButtonUp += (_, _) =>
            {
                bool selected = _session.ToggleSelection(app.TargetPath);

                UpdateSelectionStyle(tile, selected);

                SelectionCount.Text = _session.SelectedCount > 0
                    ? $"{_session.SelectedCount} selected"
                    : $"{_session.DiscoveredApps.Count} apps found";
            };

                AppsItems.Items.Add(tile);
            }
        }

        public void CompleteAdding()
        {
            // Keep discovered apps cached, but clear their selection.
            _session.ClearSelection();
            SelectedApps.Clear();

            // Re-render using the updated existing shortcuts.
            RenderApps();

            SelectionCount.Text = $"{AppsItems.Items.Count} apps found";
        }

        private bool IsAlreadyAdded(AppShortcut app)
        {
            return _session.IsAlreadyAdded(app.TargetPath);
        }

        private static void UpdateSelectionStyle(
            Border tile, bool selected)
        {
            tile.BorderBrush = new SolidColorBrush(selected
                ? System.Windows.Media.Color.FromArgb(100, 235, 242, 255)
                : System.Windows.Media.Color.FromArgb(35, 220, 230, 245));

            tile.Background = new SolidColorBrush(selected
                ? System.Windows.Media.Color.FromArgb(65, 220, 230, 245)
                : System.Windows.Media.Color.FromArgb(45, 12, 15, 21));
        }

        private void SearchBox_TextChanged(
            object sender, TextChangedEventArgs e)
        {
            RenderApps();
        }

        private void CategoryBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
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
            SelectedApps.Clear();
            SelectedApps.AddRange(_session.GetSelectedApps());

            Finished?.Invoke();
        }
    }
}
