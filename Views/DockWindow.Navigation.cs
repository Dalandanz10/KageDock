using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Diagnostics;
using KageDock.Modules;
namespace KageDock.Views

{
    public partial class DockWindow
    {
        private async void SectionsButton_Click(object sender, RoutedEventArgs e)
        {
            _dockContentAnimationController?.MoveNavIndicator(0);

            CancelCollapseTimer();

            if (!_expanded)
            {
                ExpandDock();
                return;
            }

            var myAppsModule = _contentHost.Activate("myapps");

            await _dockContentAnimationController!.TransitionPageContentAsync(() =>
            {
                AddAppsContent.Children.Clear();
                AddAppsContent.Children.Add(myAppsModule.View);

                AddAppsContent.Visibility = Visibility.Visible;
                AddAppsContent.IsHitTestVisible = true;

                TitleText.Text = string.IsNullOrWhiteSpace(_config.LauncherTitle)
                    ? "Apps"
                    : _config.LauncherTitle;

                MorphDockToView(DockView.MyApps);
            });
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

            var timerModule = _contentHost.Activate("timer");

            await _dockContentAnimationController!.TransitionPageContentAsync(() =>
            {
                AddAppsContent.Children.Clear();
                AddAppsContent.Children.Add(timerModule.View);

                AddAppsContent.Visibility = Visibility.Visible;
                AddAppsContent.IsHitTestVisible = true;

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

            await _dockContentAnimationController!.TransitionPageContentAsync(() =>
            {
                AddAppsContent.Children.Clear();
                AddAppsContent.Children.Add(musicModule.View);

                AddAppsContent.Visibility = Visibility.Visible;
                AddAppsContent.IsHitTestVisible = true;

                TitleText.Text = "Music";

                MorphDockToView(DockView.Music);
            });
        }


        private void AddAppsButton_Click(object sender, RoutedEventArgs e)
        {
            CancelCollapseTimer();

            // Already displaying Add Apps.
            if (_addAppsView != null &&
                AddAppsContent.Children.Contains(_addAppsView))
                return;

            _dockContentAnimationController?.InvalidatePendingPageTransition();

            // Ensure the dock is expanded before showing the picker.
            if (!_expanded)
                ExpandDock();

            _contentHost.DeactivateActive();

            // Create the window only once.
            if (_addAppsView == null)
            {
                _addAppsView = new AddAppsWindow(_addAppsSession!);

                _addAppsView.BackRequested += () =>
                {
                    _dockContentAnimationController?.InvalidatePendingPageTransition();

                    AddAppsContent.Children.Clear();

                    var myAppsModule = _contentHost.Activate("myapps");

                    AddAppsContent.Children.Add(myAppsModule.View);

                    AddAppsContent.Visibility = Visibility.Visible;
                    AddAppsContent.Opacity = 1;
                    AddAppsContent.IsHitTestVisible = true;

                    TitleText.Text = string.IsNullOrWhiteSpace(_config.LauncherTitle)
                        ? "Apps"
                        : _config.LauncherTitle;

                    _dockContentAnimationController?.MoveNavIndicator(0);
                    MorphDockToView(DockView.MyApps);
                };


                    _addAppsView.Finished += () =>
                    {
                        var picker = _addAppsView;
                        if (picker == null)
                            return;

                        if (picker.IsCreatingNewSection)
                        {
                            _shortcutCoordinator!.AddAppsToNewSection(
                                picker.SelectedApps,
                                picker.SelectedCategory);
                        }
                        else
                        {
                            _shortcutCoordinator!.AddApps(
                                picker.SelectedApps,
                                picker.SelectedCategory);
                        }

                        // Identify only the shortcuts that were successfully added.
                        var newlyAddedApps =
                            _shortcutCoordinator!.GetNewlyAddedApps(picker.SelectedApps);

                        _shortcutCoordinator.Save();

                        if (_contentHost.Prepare("myapps") is MyAppsModule myAppsModule)
                        {
                            myAppsModule.HighlightNewApps(newlyAddedApps);
                            myAppsModule.Refresh();
                        }

                        picker.CompleteAdding();

                        // Return to My Apps automatically.
                        _dockContentAnimationController?.InvalidatePendingPageTransition();

                        AddAppsContent.Children.Clear();

                        var activeMyAppsModule = _contentHost.Activate("myapps");

                        AddAppsContent.Children.Add(activeMyAppsModule.View);

                        AddAppsContent.Visibility = Visibility.Visible;
                        AddAppsContent.Opacity = 1;
                        AddAppsContent.IsHitTestVisible = true;

                        TitleText.Text = string.IsNullOrWhiteSpace(_config.LauncherTitle)
                            ? "Apps"
                            : _config.LauncherTitle;

                        _dockContentAnimationController?.MoveNavIndicator(0);
                        MorphDockToView(DockView.MyApps);
                    };
            }

            // Refresh destinations in case My Apps sections changed.
            _addAppsView.RefreshDestinationSections();

            // Reattach the existing window.
            AddAppsContent.Children.Clear();
            AddAppsContent.Children.Add(_addAppsView);

            AddAppsContent.Visibility = Visibility.Visible;
            AddAppsContent.Opacity = 1;
            AddAppsContent.IsHitTestVisible = true;

            TitleText.Text = "Add Apps";

            MorphDockToView(DockView.AddApps);
        }
    }
}
