using System;
using System.Windows;
using KageDock.Contracts;
using KageDock.Views;
using KageDock.Models;
using KageDock.Services;
using KageDock.Coordinators;

namespace KageDock.Modules
{
    public sealed class MyAppsModule : IDockContentModule
    {
        private readonly MyAppsView _view;

        public string Id => "myapps";

        public FrameworkElement View => _view;

        public System.Windows.Size PreferredSize =>
            System.Windows.Size.Empty;

        public MyAppsModule(
            IMyAppsHost host,
            ShortcutStore shortcutStore,
            ShortcutCoordinator shortcutCoordinator,
            IconService iconService)
        {
            _view = new MyAppsView(
                host,
                shortcutStore,
                shortcutCoordinator,
                iconService);
        }

        public void OnActivated()
        {
            _view.RestoreView(animateTileMoves: false);
        }

        public void Refresh()
        {
            _view.RestoreView();
        }

        public void HighlightNewApps(IReadOnlyList<AppShortcut> apps)
        {
            _view.HighlightNewApps(apps);
        }

        public void OnDeactivated()
        {
        }

        public void Dispose()
        {
            _view.Dispose();
        }
    }
}
