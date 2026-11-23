
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
        private readonly ShortcutStore _shortcutStore;

        private List<AppShortcut> Shortcuts =>
            _shortcutStore.Shortcuts;

        public ShortcutCoordinator(ShortcutStore shortcutStore)
        {
            _shortcutStore = shortcutStore;
        }

        public void Save()
        {
            RemoveEmptySections();
            NormalizeSectionSlots();
            NormalizeOrder();

            _shortcutStore.Save();
        }

        public void RenameSection(string oldName, string newName)
        {
            oldName = NormalizeCategory(oldName);
            newName = NormalizeCategory(newName);

            // Ignore unchanged names.
            if (oldName.Equals(
                newName,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Never merge two existing sections.
            if (Shortcuts.Any(s =>
                NormalizeCategory(s.Category).Equals(
                    newName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            foreach (var shortcut in Shortcuts.Where(s =>
                NormalizeCategory(s.Category).Equals(
                    oldName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                shortcut.Category = newName;
            }
        }

        public bool MoveSection(
            string draggedCategory,
            string targetCategory,
            bool insertAfter)
        {
            draggedCategory = NormalizeCategory(draggedCategory);
            targetCategory = NormalizeCategory(targetCategory);

            if (draggedCategory.Equals(
                targetCategory,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Get the current section order.
            var categories = Shortcuts
                .Select(s => NormalizeCategory(s.Category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            int draggedIndex = categories.FindIndex(c =>
                c.Equals(draggedCategory, StringComparison.OrdinalIgnoreCase));

            int targetIndex = categories.FindIndex(c =>
                c.Equals(targetCategory, StringComparison.OrdinalIgnoreCase));

            if (draggedIndex < 0 || targetIndex < 0)
                return false;

            // Move the dragged section in the category order.
            string movedCategory = categories[draggedIndex];
            categories.RemoveAt(draggedIndex);

            targetIndex = categories.FindIndex(c =>
                c.Equals(targetCategory, StringComparison.OrdinalIgnoreCase));

            int insertIndex = targetIndex + (insertAfter ? 1 : 0);
            categories.Insert(insertIndex, movedCategory);

            // Rebuild the shortcut list, keeping each section's contents together.
            var reorderedShortcuts = new List<AppShortcut>();

            foreach (string category in categories)
            {
                reorderedShortcuts.AddRange(Shortcuts.Where(s =>
                    IsSectionMarker(s) &&
                    NormalizeCategory(s.Category).Equals(
                        category,
                        StringComparison.OrdinalIgnoreCase)));

                reorderedShortcuts.AddRange(Shortcuts.Where(s =>
                    !IsSectionMarker(s) &&
                    NormalizeCategory(s.Category).Equals(
                        category,
                        StringComparison.OrdinalIgnoreCase)));
            }

            Shortcuts.Clear();
            Shortcuts.AddRange(reorderedShortcuts);

            NormalizeOrder();

            return true;
        }

        private string CreateUniqueCategoryName()
        {
            var categories = Shortcuts
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
                Shortcuts
                    .Where(s => !IsSectionMarker(s))
                    .Select(s => NormalizeCategory(s.Category)),
                StringComparer.OrdinalIgnoreCase);

            Shortcuts.RemoveAll(s =>
                IsSectionMarker(s) &&
                !categoriesWithApps.Contains(NormalizeCategory(s.Category)));
        }

        public void NormalizeOrder()
        {
            for (int i = 0; i < Shortcuts.Count; i++)
            {
                Shortcuts[i].OrderIndex = i;
            }
        }

        public void MoveToSlot(
            AppShortcut shortcut,
            string category,
            int slot)
        {
            Shortcuts.Remove(shortcut);

            shortcut.Category = NormalizeCategory(category);
            shortcut.SlotIndex = slot;

            Shortcuts.Add(shortcut);

            NormalizeOrder();
        }




        public void NormalizeSectionSlots()
        {
            var categories = Shortcuts
                .Where(s => !IsSectionMarker(s))
                .Select(s => NormalizeCategory(s.Category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var category in categories)
            {
                var sectionApps = Shortcuts
                    .Where(s =>
                        !IsSectionMarker(s) &&
                        NormalizeCategory(s.Category).Equals(
                            category,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => s.OrderIndex)
                    .ToList();

                var usedSlots = new HashSet<int>();
                int nextSlot = 0;

                // First repair duplicate or invalid slots.
                foreach (var app in sectionApps)
                {
                    if (app.SlotIndex >= 0 && usedSlots.Add(app.SlotIndex))
                        continue;

                    while (usedSlots.Contains(nextSlot))
                        nextSlot++;

                    app.SlotIndex = nextSlot;
                    usedSlots.Add(nextSlot);
                    nextSlot++;
                }

                // Identify rows that actually contain apps.
                var occupiedRows = sectionApps
                    .Select(app => app.SlotIndex / 4)
                    .Distinct()
                    .OrderBy(row => row)
                    .ToList();

                // Map occupied rows to consecutive row numbers.
                var compactedRows = occupiedRows
                    .Select((oldRow, newRow) => new { oldRow, newRow })
                    .ToDictionary(x => x.oldRow, x => x.newRow);

                // Move apps upward while preserving their columns.
                foreach (var app in sectionApps)
                {
                    int oldRow = app.SlotIndex / 4;
                    int column = app.SlotIndex % 4;

                    app.SlotIndex = compactedRows[oldRow] * 4 + column;
                }
            }
        }




        public void MoveToNewSection(AppShortcut shortcut)
        {
            Shortcuts.Remove(shortcut);

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

            Shortcuts.Add(marker);
            Shortcuts.Add(shortcut);

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
        if (Shortcuts.Any(existing =>
            string.Equals(
                existing.TargetPath,
                app.TargetPath,
                StringComparison.OrdinalIgnoreCase)))
        {
            continue;
        }

        app.Category = category;

        var categoryApps = Shortcuts
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
        app.OrderIndex = Shortcuts.Count;

        Shortcuts.Add(app);
        addedCount++;
    }

    return addedCount;
}

        public int AddAppsToNewSection(
            IEnumerable<AppShortcut> apps,
            string sectionName)
        {
            if (string.IsNullOrWhiteSpace(sectionName))
                throw new ArgumentException(
                    "Section name cannot be empty.",
                    nameof(sectionName));

            string category = NormalizeCategory(sectionName);

            if (Shortcuts.Any(s =>
                NormalizeCategory(s.Category).Equals(
                    category,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"A section named '{category}' already exists.");
            }

            var marker = new AppShortcut
            {
                Name = "__KAGEDOCK_SECTION__",
                DisplayName = "",
                TargetPath = "",
                Category = category,
                SlotIndex = -1,
                OrderIndex = Shortcuts.Count
            };

            Shortcuts.Add(marker);

            int addedCount = AddApps(apps, category);

            if (addedCount == 0)
            {
                Shortcuts.Remove(marker);
                return 0;
            }

            NormalizeOrder();

            return addedCount;
        }


        public List<AppShortcut> GetNewlyAddedApps(
            IEnumerable<AppShortcut> selectedApps)
        {
            var addedApps = new List<AppShortcut>();

            foreach (var app in selectedApps)
            {
                var storedApp = Shortcuts.FirstOrDefault(existing =>
                    ReferenceEquals(existing, app));

                if (storedApp != null && !IsSectionMarker(storedApp))
                    addedApps.Add(storedApp);
            }

            return addedApps;
        }


        public void MoveToInsertionSlot(
            AppShortcut dragged,
            string targetCategory,
            int insertSlot,
            IReadOnlyList<AppShortcut> destinationApps)
        {
            Shortcuts.Remove(dragged);

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
                for (int slot = leftGap + 1; slot <= finalSlot; slot++)
                {
                    if (appsBySlot.TryGetValue(slot, out var app))
                        app.SlotIndex = slot - 1;
                }
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

            Shortcuts.Add(dragged);

            // Preserve section order and sort apps by their slots.
            var categoryOrder = Shortcuts
                .Select(a => NormalizeCategory(a.Category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var reorderedShortcuts = new List<AppShortcut>();

            foreach (string category in categoryOrder)
            {
                reorderedShortcuts.AddRange(Shortcuts.Where(a =>
                    IsSectionMarker(a) &&
                    NormalizeCategory(a.Category).Equals(
                        category, StringComparison.OrdinalIgnoreCase)));

                reorderedShortcuts.AddRange(Shortcuts
                    .Where(a =>
                        !IsSectionMarker(a) &&
                        NormalizeCategory(a.Category).Equals(
                            category, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(a => a.SlotIndex >= 0
                        ? a.SlotIndex
                        : a.OrderIndex));
            }

            Shortcuts.Clear();
            Shortcuts.AddRange(reorderedShortcuts);

            NormalizeOrder();
        }

        public bool RemoveApp(AppShortcut shortcut)
        {
            return Shortcuts.Remove(shortcut);
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

                if (Shortcuts.Any(a =>
                    string.Equals(
                        a.TargetPath,
                        path,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(path);

                Shortcuts.Add(new AppShortcut
                {
                    Name = name,
                    DisplayName = name,
                    TargetPath = path,
                    Category = category,
                    SlotIndex = -1,
                    OrderIndex = Shortcuts.Count
                });

                addedCount++;
            }

            return addedCount;
        }
    }
}
