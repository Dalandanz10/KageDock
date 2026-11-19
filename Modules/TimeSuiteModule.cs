
using System;
using System.Windows;
using KageDock.Contracts;
using KageDock.Views;

namespace KageDock.Modules
{
    public sealed class TimeSuiteModule : IDockContentModule
    {
        private readonly TimeSuiteView _view;

        public string Id => "timer";

        public FrameworkElement View => _view;

        // Sizing remains controlled by DockWindow for now.
        public System.Windows.Size PreferredSize =>
            System.Windows.Size.Empty;

        public TimeSuiteModule()
        {
            _view = new TimeSuiteView();
        }

        public void OnActivated()
        {
            // Visual attachment currently handles activation.
        }

        public void OnDeactivated()
        {
            // Visual detachment currently handles deactivation.
            // Timer and stopwatch state must continue running.
        }

        public void Dispose()
        {
            _view.Shutdown();
        }
    }
}
