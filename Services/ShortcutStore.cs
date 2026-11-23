
using System;
using System.Collections.Generic;
using System.Linq;
using KageDock.Models;

namespace KageDock.Services
{
    public sealed class ShortcutStore
    {
        private readonly List<AppShortcut> _shortcuts = new();

        internal List<AppShortcut> Shortcuts => _shortcuts;

        public ShortcutStore()
        {
            Reload();
        }

        public void Reload()
        {
            var cachedIcons = _shortcuts
                .Where(s =>
                    s.CachedIcon != null &&
                    !string.IsNullOrWhiteSpace(s.TargetPath))
                .GroupBy(
                    GetIconCacheKey,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.First().CachedIcon!,
                    StringComparer.OrdinalIgnoreCase);

            var reloadedShortcuts = ConfigService.LoadShortcuts()
                .OrderBy(s => s.OrderIndex)
                .ToList();

            foreach (var shortcut in reloadedShortcuts)
            {
                if (string.IsNullOrWhiteSpace(shortcut.TargetPath))
                    continue;

                if (cachedIcons.TryGetValue(
                    GetIconCacheKey(shortcut),
                    out var cachedIcon))
                {
                    shortcut.CachedIcon = cachedIcon;
                }
            }

            _shortcuts.Clear();
            _shortcuts.AddRange(reloadedShortcuts);
        }

        public void Save()
        {
            ConfigService.SaveShortcuts(_shortcuts);
        }

        private static string GetIconCacheKey(AppShortcut shortcut)
        {
            return shortcut.TargetPath
                .Trim()
                .Replace('/', '\\');
        }
    }
}
