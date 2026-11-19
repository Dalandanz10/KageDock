using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Diagnostics;
using KageDock.Modules;
namespace KageDock.Views

{
    public partial class DockWindow
    {
             private void SectionsButton_Click(object sender, RoutedEventArgs e)
        {
            _dockContentAnimationController?.InvalidatePendingPageTransition();

            _contentHost.DeactivateActive();

            _dockContentAnimationController?.MoveNavIndicator(0);

            CancelCollapseTimer();

            // Remove whichever view is currently displayed.
            AddAppsContent.Children.Clear();

            AddAppsContent.Visibility = Visibility.Collapsed;
            AddAppsContent.IsHitTestVisible = false;
            AddAppsContent.Opacity = 1;

            // Reset the Add Apps view reference.
            _addAppsView = null;

            // Restore My Apps.
            TitleText.Text = string.IsNullOrWhiteSpace(_config.LauncherTitle)
                ? "Apps"
                : _config.LauncherTitle;

            AppScrollViewer.Visibility = Visibility.Visible;
            NewSectionDropZoneHost.Visibility = Visibility.Visible;

            AppScrollViewer.BeginAnimation(UIElement.OpacityProperty, null);
            AppScrollViewer.Opacity = 1;

            NewSectionDropZoneHost.BeginAnimation(UIElement.OpacityProperty, null);
            NewSectionDropZoneHost.Opacity = 1;

            // Ensure the navigation remains available.
            if (!_expanded)
                ExpandDock();

            MorphDockToView(DockView.MyApps);
        }

        
        private async void TimerButton_Click(object sender, RoutedEventArgs e)
        {
            _dockContentAnimationController?.MoveNavIndicator(2);

            CancelCollapseTimer();

            if (!_expanded)
            {
                ExpandDock();
                return;
            }

            if (_addAppsView != null)
            {
                AddAppsContent.Children.Clear();
                _addAppsView = null;
            }

            var timerModule = _contentHost.Activate("timer");

            await _dockContentAnimationController!.TransitionPageContentAsync(() =>
            {
                AddAppsContent.Children.Clear();
                AddAppsContent.Children.Add(timerModule.View);

                AddAppsContent.Visibility = Visibility.Visible;
                AddAppsContent.IsHitTestVisible = true;

                AppScrollViewer.Visibility = Visibility.Collapsed;
                NewSectionDropZoneHost.Visibility = Visibility.Collapsed;

                TitleText.Text = "Timer";

                MorphDockToView(DockView.Timer);
            });
        }
        
        private async void MusicButton_Click(object sender, RoutedEventArgs e)
        {
            _dockContentAnimationController?.MoveNavIndicator(1);

            CancelCollapseTimer();

            if (!_expanded)
            {
                ExpandDock();
                return;
            }

            var musicModule = (MusicModule)_contentHost.Activate("music");

            if (_addAppsView != null)
            {
                AddAppsContent.Children.Clear();
                _addAppsView = null;
            }

            await _dockContentAnimationController!.TransitionPageContentAsync(() =>
            {
                AddAppsContent.Children.Clear();
                AddAppsContent.Children.Add(musicModule.View);

                AddAppsContent.Visibility = Visibility.Visible;
                AddAppsContent.IsHitTestVisible = true;

                AppScrollViewer.Visibility = Visibility.Collapsed;
                NewSectionDropZoneHost.Visibility = Visibility.Collapsed;

                TitleText.Text = "Music";

                MorphDockToView(DockView.Music);
            });
        }

        private void AddAppsButton_Click(object sender, RoutedEventArgs e)
        {
            CancelCollapseTimer();

            if (_addAppsView != null)
                return;
                
            _dockContentAnimationController?.InvalidatePendingPageTransition();

            // Ensure the dock is expanded before showing the picker.
            if (!_expanded)
                ExpandDock();

            _contentHost.DeactivateActive();

            _addAppsView = new AddAppsWindow(_addAppsSession!);

            AddAppsContent.Children.Clear();
            AddAppsContent.Children.Add(_addAppsView);

            AddAppsContent.Visibility = Visibility.Visible;
            AddAppsContent.Opacity = 1;
            AddAppsContent.IsHitTestVisible = true;

            TitleText.Text = "Add Apps";
            AppScrollViewer.Visibility = Visibility.Collapsed;
            NewSectionDropZoneHost.Visibility = Visibility.Collapsed;

            MorphDockToView(DockView.AddApps);

            _addAppsView.BackRequested += () =>
            {
                _dockContentAnimationController?.InvalidatePendingPageTransition();

                AddAppsContent.Visibility = Visibility.Collapsed;
                AddAppsContent.IsHitTestVisible = false;
                AddAppsContent.Children.Clear();

                TitleText.Text = string.IsNullOrWhiteSpace(_config.LauncherTitle)
                    ? "Apps"
                    : _config.LauncherTitle;

                AppScrollViewer.Visibility = Visibility.Visible;
                NewSectionDropZoneHost.Visibility = Visibility.Visible;

                _addAppsView = null;

                _dockContentAnimationController?.MoveNavIndicator(0);
                MorphDockToView(DockView.MyApps);
            };

            _addAppsView.Finished += () =>
            {
                var picker = _addAppsView;
                if (picker == null)
                    return;

                int addedCount = _shortcutCoordinator!.AddApps(
                    picker.SelectedApps,
                    picker.SelectedCategory);

                SaveAndRefresh();

                picker.CompleteAdding();
                picker.ShowAddedIndicator(addedCount);
            };
        }      
    }
}
