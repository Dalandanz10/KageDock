
using System.Linq;
using System.Windows;
using System.Windows.Input;
using KageDock.Models;
using KageDock.Services;

namespace KageDock.Views
{
    public partial class SettingsWindow : Window
    {
        private bool _isLoadingSettings;
        private readonly DockWindow _dockWindow;
        private readonly DockConfig _config;

        public SettingsWindow(DockWindow dockWindow)
        {
            InitializeComponent();

            _dockWindow = dockWindow;
            _config = ConfigService.LoadConfig();

            _isLoadingSettings = true;

            try
            {
                LoadSettingsIntoUi();
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private void LoadSettingsIntoUi()
        {
            MonitorSelector.Items.Clear();

            var screens = ScreenService.GetScreens();

            for (int i = 0; i < screens.Length; i++)
            {
                MonitorSelector.Items.Add(
                    i == 0 ? "Main Monitor" :
                    i == 1 ? "Second Monitor" :
                    $"Monitor {i + 1}");
            }

            int targetIndex = _config.TargetMonitor;

            if (MonitorSelector.Items.Count > 0 &&
                targetIndex >= 0 &&
                targetIndex < MonitorSelector.Items.Count)
            {
                MonitorSelector.SelectedIndex = targetIndex;
            }
            else if (MonitorSelector.Items.Count > 0)
            {
                MonitorSelector.SelectedIndex = 0;
            }

            StartupToggle.IsChecked =
                RegistryService.IsRunOnStartupEnabled();
            
            DialogueToggle.IsChecked =
                _config.EnableKageDialogue;

            EnvironmentalEffectsToggle.IsChecked =
                _config.EnableEnvironmentalEffects;
        }

        public void OnHeaderMouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void OnStartupChecked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            RegistryService.SetRunOnStartup(true);
        }

        public void OnStartupUnchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            RegistryService.SetRunOnStartup(false);
        }

        
        public void OnSaveAndApplyClick(object sender, RoutedEventArgs e)
        {
            if (MonitorSelector.SelectedIndex >= 0)
            {
                _config.TargetMonitor = MonitorSelector.SelectedIndex;
            }
            _config.EnableKageDialogue =
                DialogueToggle.IsChecked == true;

            _config.EnableEnvironmentalEffects =
                EnvironmentalEffectsToggle.IsChecked == true;
            ConfigService.SaveConfig(_config);

            Close();

            _dockWindow.Dispatcher.BeginInvoke(
                new Action(() => _dockWindow.ReloadConfiguration()),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
