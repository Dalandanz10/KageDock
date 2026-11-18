
using System;
using System.Windows;
using System.Windows.Media;

namespace KageDock.Contracts
{
    public interface IDockContentModule : IDisposable
    {
        // Stable identifier used by the content host.
        string Id { get; }

        // The visual element displayed by the dock.
        FrameworkElement View { get; }

        // Preferred dimensions requested by this module.
        System.Windows.Size PreferredSize { get; }

        // Called when the module becomes the active content.
        void OnActivated();

        // Called when another module becomes active.
        // The module should preserve its feature state.
        void OnDeactivated();
    }
}
