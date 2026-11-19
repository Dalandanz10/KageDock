using System;
using System.Windows;
using KageDock.Contracts;
using KageDock.Views;
using KageDock.Models;
using System.Threading.Tasks;

namespace KageDock.Modules
{
    public sealed class MusicModule : IDockContentModule
    {
        private readonly MusicIslandView _view;
        public event Action<KageDialogueCategory>? ReactionRequested;
        public event Action<bool>? PlaybackStateChanged;

        public string Id => "music";

        public FrameworkElement View => _view;

        public System.Windows.Size PreferredSize =>
            System.Windows.Size.Empty;

        public MusicModule()
        {
            _view = new MusicIslandView();

            _view.ReactionRequested += category =>
                ReactionRequested?.Invoke(category);

            _view.PlaybackStateChanged += isPlaying =>
                PlaybackStateChanged?.Invoke(isPlaying);
        }

        public Task InitializeMediaAsync()
        {
            return _view.InitializeMediaAsync();
        }

        public void OnActivated()
        {
        }

        public void OnDeactivated()
        {
        }

        public void Dispose()
        {
            _view.Shutdown();
        }
    }
}
