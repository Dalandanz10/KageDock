
using System;
using System.Collections.Generic;
using System.Linq;
using KageDock.Models;
using KageDock.Services;
using System.IO;

namespace KageDock.Coordinators
{
    public sealed class ShortcutCoordinator
    {
        private readonly List<AppShortcut> _shortcuts;

        public ShortcutCoordinator(List<AppShortcut> shortcuts)
        {
            _shortcuts = shortcuts;
        }

        public void Save()
        {
            RemoveEmptySections();
            NormalizeOrder();

            ConfigService.SaveShortcuts(_shortcuts);
        }
        public void RenameSection(string oldName, string newName)
        {
            foreach (var shortcut in _shortcuts.Where(s =>
                NormalizeCategory(s.Category).Equals(
                    oldName, StringComparison.OrdinalIgnoreCase)))
            {
                shortcut.Category = newName;
            }

            if (_shortcuts.All(s =>
                    !NormalizeCategory(s.Category).Equals(
                        newName, StringComparison.OrdinalIgnoreCase)) &&
                !_shortcuts.Any(s =>
                    IsSectionMarker(s) &&
                    NormalizeCategory(s.Category).Equals(
                        newName, StringComparison.OrdinalIgnoreCase)))
            {
                _shortcuts.Add(new AppShortcut
                {
                    Name = "__KAGEDOCK_SECTION__",
                    Category = newName,
                    OrderIndex = _shortcuts.Count
                });
            }
        }

        private string CreateUniqueCategoryName()
        {
            var categories = _shortcuts
                .Where(s => !IsSectionMarker(s))
                .Select(s => NormalizeCategory(s.Category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            int number = categories.Count + 1;

            while (categories.Contains(
                $"Section {number}",
                StringComparer.OrdinalIgnoreCase))
            {
                number++;
            }

            return $"Section {number}";
        }

        private void RemoveEmptySections()
        {
            var categoriesWithApps = new HashSet<string>(
                _shortcuts
                    .Where(s => !IsSectionMarker(s))
                    .Select(s => NormalizeCategory(s.Category)),
                StringComparer.OrdinalIgnoreCase);

            _shortcuts.RemoveAll(s =>
                IsSectionMarker(s) &&
                !categoriesWithApps.Contains(NormalizeCategory(s.Category)));
        }

        public void NormalizeOrder()
        {
            for (int i = 0; i < _shortcuts.Count; i++)
            {
                _shortcuts[i].OrderIndex = i;
            }
        }

        public void MoveToSlot(
            AppShortcut shortcut,
            string category,
            int slot)
        {
            _shortcuts.Remove(shortcut);

            shortcut.Category = NormalizeCategory(category);
            shortcut.SlotIndex = slot;

            _shortcuts.Add(shortcut);

            NormalizeOrder();
        }


        public void NormalizeSectionSlots()
        {
            var categories = _shortcuts
                .Where(s => !IsSectionMarker(s))
                .Select(s => NormalizeCategory(s.Category))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var category in categories)
            {
                var sectionApps = _shortcuts
                    .Where(s =>
                        !IsSectionMarker(s) &&
                        NormalizeCategory(s.Category).Equals(
                            category,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var usedSlots = new HashSet<int>();

                foreach (var app in sectionApps)
                {
                    if (app.SlotIndex >= 0)
                        usedSlots.Add(app.SlotIndex);
                }

                int nextSlot = 0;

                foreach (var app in sectionApps)
                {
                    if (app.SlotIndex >= 0)
                        continue;

                    while (usedSlots.Contains(nextSlot))
                        nextSlot++;

                    app.SlotIndex = nextSlot;
                    usedSlots.Add(nextSlot);
                }
            }
        }


        public void MoveToNewSection(AppShortcut shortcut)
        {
            _shortcuts.Remove(shortcut);

            string newCategory = CreateUniqueCategoryName();

            var marker = new AppShortcut
            {
                Name = "__KAGEDOCK_SECTION__",
                DisplayName = "",
                TargetPath = "",
                Category = newCategory,
                SlotIndex = -1,
                OrderIndex = 0
            };

            shortcut.Category = newCategory;
            shortcut.SlotIndex = 0;
            shortcut.OrderIndex = 0;

            _shortcuts.Add(marker);
            _shortcuts.Add(shortcut);

            NormalizeOrder();
        }

        public static bool IsSectionMarker(AppShortcut shortcut) =>
            string.Equals(
                shortcut.Name,
                "__KAGEDOCK_SECTION__",
                StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(shortcut.TargetPath);

        public static string NormalizeCategory(string? category) =>
            string.IsNullOrWhiteSpace(category) ? "Pinned" : category.Trim();

        
        public int AddApps(IEnumerable<AppShortcut> apps, string category)
        {
            int addedCount = 0;

            foreach (var app in apps)
            {
                if (_shortcuts.Any(existing =>
                    string.Equals(
                        existing.TargetPath,
                        app.TargetPath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                app.Category = category;

                var categoryApps = _shortcuts
                    .Where(a => !IsSectionMarker(a) &&
                        NormalizeCategory(a.Category).Equals(
                            NormalizeCategory(app.Category),
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var occupiedSlots = categoryApps
                    .Select(a => a.SlotIndex >= 0 ? a.SlotIndex : a.OrderIndex)
                    .ToHashSet();

                int nextSlot = 0;

                while (occupiedSlots.Contains(nextSlot))
                    nextSlot++;

                app.SlotIndex = nextSlot;
                app.OrderIndex = _shortcuts.Count;

                _shortcuts.Add(app);
                addedCount++;
            }

            return addedCount;
        }

        public void MoveToInsertionSlot(
            AppShortcut dragged,
            string targetCategory,
            int insertSlot,
            IReadOnlyList<AppShortcut> destinationApps)
        {
            _shortcuts.Remove(dragged);

            int finalSlot = Math.Max(0, insertSlot);

            var appsBySlot = destinationApps
                .Where(a => a.SlotIndex >= 0)
                .ToDictionary(a => a.SlotIndex, a => a);

            // Find the nearest empty slot before the insertion point.
            int leftGap = -1;

            for (int slot = finalSlot - 1; slot >= 0; slot--)
            {
                if (!appsBySlot.ContainsKey(slot))
                {
                    leftGap = slot;
                    break;
                }
            }

            bool targetOccupied = appsBySlot.ContainsKey(finalSlot);

            if (targetOccupied && leftGap >= 0)
            {
                // Use the left gap: shift intervening apps one slot left.
                for (int slot = leftGap + 1; slot < finalSlot; slot++)
                {
                    if (appsBySlot.TryGetValue(slot, out var app))
                        app.SlotIndex = slot - 1;
                }

                finalSlot--;
            }
            else if (targetOccupied)
            {
                // No available gap on the left: push apps right.
                int slotToShift = finalSlot;

                while (appsBySlot.TryGetValue(slotToShift, out var appToShift))
                {
                    appToShift.SlotIndex = slotToShift + 1;
                    slotToShift++;
                }
            }

            dragged.Category = targetCategory;
            dragged.SlotIndex = finalSlot;
            dragged.OrderIndex = finalSlot;

            _shortcuts.Add(dragged);

            // Preserve section order and sort apps by their slots.
            var categoryOrder = _shortcuts
                .Select(a => NormalizeCategory(a.Category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var reorderedShortcuts = new List<AppShortcut>();

            foreach (string category in categoryOrder)
            {
                reorderedShortcuts.AddRange(_shortcuts.Where(a =>
                    IsSectionMarker(a) &&
                    NormalizeCategory(a.Category).Equals(
                        category, StringComparison.OrdinalIgnoreCase)));

                reorderedShortcuts.AddRange(_shortcuts
                    .Where(a =>
                        !IsSectionMarker(a) &&
                        NormalizeCategory(a.Category).Equals(
                            category, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(a => a.SlotIndex >= 0
                        ? a.SlotIndex
                        : a.OrderIndex));
            }

            _shortcuts.Clear();
            _shortcuts.AddRange(reorderedShortcuts);

            NormalizeOrder();
        }

        public bool RemoveApp(AppShortcut shortcut)
        {
            return _shortcuts.Remove(shortcut);
        }

        public void RenameApp(AppShortcut shortcut, string displayName)
        {
            shortcut.DisplayName = displayName.Trim();
        }

        public int AddFiles(IEnumerable<string> paths, string category)
        {
            int addedCount = 0;

            foreach (string path in paths)
            {
                if (!File.Exists(path))
                    continue;

                if (_shortcuts.Any(a =>
                    string.Equals(
                        a.TargetPath,
                        path,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(path);

                _shortcuts.Add(new AppShortcut
                {
                    Name = name,
                    DisplayName = name,
                    TargetPath = path,
                    Category = category,
                    SlotIndex = -1,
                    OrderIndex = _shortcuts.Count
                });

                addedCount++;
            }

            return addedCount;
        }
    }
}
