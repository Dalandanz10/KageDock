
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KageDock.Models;
using KageDock.Services;

namespace KageDock.Coordinators
{
    public sealed class AddAppsSession
    {
        private readonly object _loadLock = new();
        private Task? _loadTask;
        private readonly IconService _iconService;

        private readonly List<AppShortcut> _discoveredApps = new();
        private readonly HashSet<string> _selectedPaths =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly List<AppShortcut> _existingShortcuts;

        public IReadOnlyList<AppShortcut> ExistingShortcuts =>
            _existingShortcuts;

        public IReadOnlyList<AppShortcut> DiscoveredApps =>
            _discoveredApps;

        public int SelectedCount => _selectedPaths.Count;

        public bool IsDiscoveryStarted { get; private set; }

        public bool IsDiscoveryComplete { get; private set; }

        public AddAppsSession(
            List<AppShortcut> existingShortcuts,
            IconService iconService)
        {
            _existingShortcuts = existingShortcuts;
            _iconService = iconService;
        }

        public Task EnsureLoadedAsync()
        {
            lock (_loadLock)
            {
                if (_loadTask != null)
                    return _loadTask;

                IsDiscoveryStarted = true;
                _loadTask = LoadAppsCoreAsync();

                return _loadTask;
            }
        }

        private async Task LoadAppsCoreAsync()
        {
            try
            {
                _discoveredApps.Clear();

                // Discover installed desktop applications.
                var discovered = await Task.Run(
                    () => AppDiscoveryService.DiscoverApps());

                foreach (var app in discovered)
                {
                    AddDiscoveredApp(app);
                }

                // Include desktop shortcuts.
                string[] desktopFolders =
                {
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory),

                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonDesktopDirectory)
                };

                foreach (string folder in desktopFolders.Distinct(
                    StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(folder) ||
                        !Directory.Exists(folder))
                        continue;

                    try
                    {
                        foreach (string path in Directory.EnumerateFiles(
                            folder,
                            "*.lnk",
                            SearchOption.TopDirectoryOnly))
                        {
                            bool alreadyExists = _discoveredApps.Any(a =>
                                string.Equals(
                                    a.TargetPath,
                                    path,
                                    StringComparison.OrdinalIgnoreCase));

                            if (alreadyExists)
                                continue;

                            string name =
                                Path.GetFileNameWithoutExtension(path);

                            _discoveredApps.Add(new AppShortcut
                            {
                                Name = name,
                                DisplayName = name,
                                TargetPath = path,
                                Category = "Pinned",
                                OrderIndex =
                                    _existingShortcuts.Count +
                                    _discoveredApps.Count
                            });
                        }
                    }
                    catch
                    {
                        // Ignore inaccessible desktop folders.
                    }
                }

                // Start Store discovery while desktop icons load.
                var storeAppsTask = Task.Run(
                    () => AppDiscoveryService.DiscoverStoreApps());

                await LoadIconsAsync(_discoveredApps.ToList());

                // Merge Store applications.
                var storeApps = await storeAppsTask;

                foreach (var app in storeApps)
                {
                    bool alreadyExists = _discoveredApps.Any(existing =>
                        string.Equals(
                            existing.TargetPath,
                            app.TargetPath,
                            StringComparison.OrdinalIgnoreCase));

                    if (alreadyExists)
                        continue;

                    AddDiscoveredApp(app);
                }

                // Load icons for newly discovered Store applications.
                var storeShortcuts = _discoveredApps
                    .Where(a =>
                        a.TargetPath.StartsWith(
                            @"shell:AppsFolder\",
                            StringComparison.OrdinalIgnoreCase) &&
                        a.CachedIcon == null)
                    .ToList();

                await LoadIconsAsync(storeShortcuts);

                IsDiscoveryComplete = true;
            }
            catch
            {
                // Allow another attempt if discovery fails.
                lock (_loadLock)
                {
                    _loadTask = null;
                    IsDiscoveryStarted = false;
                    IsDiscoveryComplete = false;
                }

                throw;
            }
        }

        private void AddDiscoveredApp(DiscoveredApp app)
        {
            _discoveredApps.Add(new AppShortcut
            {
                Name = app.Name,
                DisplayName = app.Name,
                TargetPath = app.TargetPath,
                Arguments = app.Arguments,
                Category = "Pinned",
                OrderIndex =
                    _existingShortcuts.Count +
                    _discoveredApps.Count
            });
        }

        public bool IsAlreadyAdded(string targetPath)
        {
            return _existingShortcuts.Any(existing =>
                string.Equals(
                    existing.TargetPath,
                    targetPath,
                    StringComparison.OrdinalIgnoreCase));
        }

        private bool IsAlreadyDiscovered(string targetPath)
        {
            return _discoveredApps.Any(existing =>
                string.Equals(
                    existing.TargetPath,
                    targetPath,
                    StringComparison.OrdinalIgnoreCase));
        }

        public bool TryAddManualApp(
            string path,
            string category,
            out AppShortcut app)
        {
            app = null!;

            if (string.IsNullOrWhiteSpace(path) ||
                IsAlreadyAdded(path) ||
                IsAlreadyDiscovered(path))
                return false;

            string name = Path.GetFileNameWithoutExtension(path);

            app = new AppShortcut
            {
                Name = name,
                DisplayName = name,
                TargetPath = path,
                Category = category,
                OrderIndex =
                    _existingShortcuts.Count +
                    _discoveredApps.Count
            };

            _discoveredApps.Add(app);
            _selectedPaths.Add(path);

            return true;
        }

        public bool IsSelected(string targetPath)
        {
            return _selectedPaths.Contains(targetPath);
        }

        public bool ToggleSelection(string targetPath)
        {
            if (!_selectedPaths.Add(targetPath))
            {
                _selectedPaths.Remove(targetPath);
                return false;
            }

            return true;
        }

        public void ClearSelection()
        {
            _selectedPaths.Clear();
        }

        public List<AppShortcut> GetSelectedApps()
        {
            return _discoveredApps
                .Where(app => _selectedPaths.Contains(app.TargetPath))
                .ToList();
        }

        public async Task LoadIconsAsync(
            IEnumerable<AppShortcut> apps,
            int maxConcurrency = 4)
        {
            using var semaphore = new SemaphoreSlim(maxConcurrency);

            var tasks = apps
                .Where(app => app.CachedIcon == null)
                .Select(async app =>
                {
                    await semaphore.WaitAsync();

                    try
                    {
                        var icon = await _iconService.GetIconAsync(
                            app.TargetPath);

                        app.CachedIcon = icon;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Icon load failed for {app.Name}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

            await Task.WhenAll(tasks);
        }
    }
}
