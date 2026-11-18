using KageDock.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using System.Collections.Generic;
using System.Windows.Controls.Primitives;
using KageDock.Services;

namespace KageDock.Views
{
    public partial class DockWindow
    {
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


        
            private void BeginInlineRenameSection(Grid header, TextBlock title, string oldName)
            {
                var editor = new System.Windows.Controls.TextBox
                {
                    Text = oldName,
                    FontSize = title.FontSize,
                    FontWeight = title.FontWeight,
                    Foreground = new SolidColorBrush(
                        WpfColor.FromRgb(240, 242, 246)),
                    Background = new SolidColorBrush(
                        WpfColor.FromRgb(29, 32, 38)),
                    BorderBrush = new SolidColorBrush(
                        WpfColor.FromRgb(75, 80, 89)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 3, 7, 3),
                    MinWidth = 90,
                    MaxWidth = 220,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    CaretBrush = System.Windows.Media.Brushes.White,
                    SelectionBrush = new SolidColorBrush(
                        WpfColor.FromArgb(220, 75, 85, 100)),
                    SelectionTextBrush = System.Windows.Media.Brushes.White,
                    FocusVisualStyle = null,
                    TextWrapping = TextWrapping.NoWrap,
                    AcceptsReturn = false,
                    MaxLength = 40
                };

                editor.Resources[System.Windows.SystemColors.HighlightBrushKey] =
                    new SolidColorBrush(WpfColor.FromRgb(75, 85, 100));

                editor.Resources[System.Windows.SystemColors.HighlightTextBrushKey] =
                    System.Windows.Media.Brushes.White;

                // Rounded textbox template
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(
                    Border.BackgroundProperty,
                    new TemplateBindingExtension(
                        System.Windows.Controls.TextBox.BackgroundProperty));
                border.SetValue(
                    Border.BorderBrushProperty,
                    new TemplateBindingExtension(
                        System.Windows.Controls.TextBox.BorderBrushProperty));
                border.SetValue(
                    Border.BorderThicknessProperty,
                    new TemplateBindingExtension(
                        System.Windows.Controls.TextBox.BorderThicknessProperty));
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

                var scrollViewer = new FrameworkElementFactory(
                    typeof(System.Windows.Controls.ScrollViewer));
                scrollViewer.Name = "PART_ContentHost";
                scrollViewer.SetValue(
                    System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty,
                    System.Windows.Controls.ScrollBarVisibility.Hidden);
                scrollViewer.SetValue(
                    System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty,
                    System.Windows.Controls.ScrollBarVisibility.Hidden);

                border.AppendChild(scrollViewer);

                editor.Template = new System.Windows.Controls.ControlTemplate(
                    typeof(System.Windows.Controls.TextBox))
                {
                    VisualTree = border
                };

                Grid.SetColumn(editor, 0);
                header.Children.Remove(title);
                header.Children.Insert(0, editor);

                editor.Focus();
                editor.SelectAll();

                bool finished = false;

                void Finish(bool save)
                {
                    if (finished) return;
                    finished = true;

                    string newName = editor.Text.Trim();

                    header.Children.Remove(editor);
                    header.Children.Insert(0, title);

                    if (save && !string.IsNullOrWhiteSpace(newName))
                    {
                        _shortcutCoordinator!.RenameSection(oldName, newName);
                        SaveAndRefresh();
                    }
                }

                editor.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        Finish(true);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        Finish(false);
                        e.Handled = true;
                    }
                };

                editor.LostKeyboardFocus += (_, _) => Finish(true);
            }
        
            private void RefreshSections()
            {
                if (SectionsPanel == null) return;

                _shortcutCoordinator!.NormalizeSectionSlots();

                SectionsPanel.Children.Clear();

                var categories = _shortcuts
                    .Select(s => NormalizeCategory(s.Category))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (categories.Count == 0)
                    categories.Add("Pinned");

                // A regular Grid lets each row pair determine its own height.
                // A regular Grid lets each row pair determine its own height.
                var sectionsGrid = new Grid
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top
                };

                sectionsGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                sectionsGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                int rowCount = (categories.Count + 1) / 2;

                // Make both sections in each pair tall enough for the section with more rows.
                int[] sectionGridRows = categories
                    .Select(category =>
                    {
                        var apps = _shortcuts
                            .Where(s =>
                                !IsSectionMarker(s) &&
                                NormalizeCategory(s.Category).Equals(
                                    category, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        int highestSlot = apps
                            .Select(s => s.SlotIndex >= 0 ? s.SlotIndex : -1)
                            .DefaultIfEmpty(-1)
                            .Max();

                        return Math.Max(
                            1,
                            (Math.Max(apps.Count, highestSlot + 1) + 3) / 4);
                    })
                    .ToArray();

                for (int row = 0; row < rowCount; row++)
                {
                    sectionsGrid.RowDefinitions.Add(
                        new RowDefinition
                        {
                            Height = GridLength.Auto
                        });
                }

                for (int i = 0; i < categories.Count; i++)
                {
                    string category = categories[i];

                    int row = i / 2;
                    int column = i % 2;

                    var cell = new Grid
                    {
                        Margin = new Thickness(0, 4, 0, 4),
                        Tag = category,
                        AllowDrop = true,
                        VerticalAlignment = System.Windows.VerticalAlignment.Top
                    };

                    int pairedIndex = (column == 0 && i + 1 < categories.Count)
                        ? i + 1
                        : (column == 1 ? i - 1 : i);

                    int requiredRows = column == 0 && i + 1 < categories.Count
                        ? Math.Max(sectionGridRows[i], sectionGridRows[i + 1])
                        : column == 1
                            ? Math.Max(sectionGridRows[i - 1], sectionGridRows[i])
                            : sectionGridRows[i];

                    var section = BuildSection(category, requiredRows);

                    if (section is FrameworkElement sectionElement)
                    {
                        sectionElement.HorizontalAlignment =
                            System.Windows.HorizontalAlignment.Left;
                    }

                    Grid.SetRow(section, 0);

                    var sectionContent = new StackPanel
                    {
                        Orientation = System.Windows.Controls.Orientation.Vertical,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Top
                    };

                    sectionContent.Children.Add(section);

                    var cellPanel = new Grid
                    {
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        VerticalAlignment = System.Windows.VerticalAlignment.Top
                    };

                    cellPanel.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    cellPanel.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = GridLength.Auto });

                    cellPanel.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    Grid.SetColumn(sectionContent, 1);
                    cellPanel.Children.Add(sectionContent);

                    cell.Children.Add(cellPanel);

                    Grid.SetRow(cell, row);
                    Grid.SetColumn(cell, column);

                    sectionsGrid.Children.Add(cell);
                }

                SectionsPanel.Children.Add(sectionsGrid);

                HideDraggedShortcutTile();

                // Floating create-section drop zone.
                var dropLabel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                dropLabel.Children.Add(new TextBlock
                {
                    Text = "+",
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(245, 245, 245)),
                    FontSize = 16,
                    FontWeight = FontWeights.Light,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                dropLabel.Children.Add(new TextBlock
                {
                    Text = "New section",
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(245, 245, 245)),
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(0),
                    RenderTransform = new TranslateTransform(0, -3),
                });

                _newSectionDropZone = new Border
                {
                    Width = 96,
                    Height = 64,
                    Margin = new Thickness(0),
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = new SolidColorBrush(WpfColor.FromArgb(150, 22, 24, 28)),
                    BorderBrush = new SolidColorBrush(WpfColor.FromArgb(100, 150, 150, 150)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    AllowDrop = true,
                    Tag = "__NEW_GROUP_DROP__",
                    Visibility = System.Windows.Visibility.Collapsed,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                    Opacity = 0,
                    RenderTransform = new TranslateTransform(0, 14),
                    Child = dropLabel
                };

                _newSectionDropZone.DragOver += Section_DragOver;
                _newSectionDropZone.Drop += Section_Drop;
                _newSectionDropZone.DragLeave += NewSectionDropZone_DragLeave;

                NewSectionDropZoneHost.Children.Clear();
                NewSectionDropZoneHost.Children.Add(_newSectionDropZone);
            }

    private UIElement BuildSection(string category, int requiredRows)
    {
        var sectionPanel = new StackPanel
        {
            Width = 340,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            Margin = new Thickness(0, 2, 0, 4),
            Tag = category,
            AllowDrop = true
        };

        var header = new Grid
        {
            Margin = new Thickness(3, 3, 3, 8),
            AllowDrop = true,
            Tag = category
        };

        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock
        {
            Text = category,
            ToolTip = "Double-click or right-click to rename section",
            Foreground = new SolidColorBrush(
                WpfColor.FromRgb(240, 242, 246)),
            Style = (System.Windows.Style)System.Windows.Application.Current.FindResource("HeadingTextStyle"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

                title.MouseEnter += (_, _) =>
        {
            title.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = WpfColor.FromRgb(235, 240, 250),
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.45
            };
        };

        title.MouseLeave += (_, _) =>
        {
            title.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = WpfColor.FromRgb(220, 228, 240),
                BlurRadius = 7,
                ShadowDepth = 0,
                Opacity = 0.28
            };
        };

        title.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                e.Handled = true;
                BeginInlineRenameSection(header, title, category);
            }
        };

        title.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            BeginInlineRenameSection(header, title, category);
        };


       header.Children.Add(title);

        header.DragOver += Section_DragOver;
        header.Drop += Section_Drop;

        sectionPanel.Children.Add(header);

        var grid = new UniformGrid
    {
        Columns = 4,
        Width = 328,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0),
            AllowDrop = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Tag = category
        };

        grid.DragOver += Section_DragOver;
        grid.DragLeave += Section_DragLeave;
        grid.Drop += Section_Drop;

        var sectionApps = _shortcuts
            .Where(s =>
                !IsSectionMarker(s) &&
                NormalizeCategory(s.Category).Equals(
                    category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.SlotIndex >= 0 ? s.SlotIndex : s.OrderIndex)
            .ToList();

            int highestSlot = sectionApps
                .Select(s => s.SlotIndex)
                .DefaultIfEmpty(-1)
                .Max();

            int slotCount = Math.Max(
                requiredRows * 4,
                ((Math.Max(sectionApps.Count, highestSlot + 1) + 3) / 4) * 4);

            for (int slot = 0; slot < slotCount; slot++)
            {
                var shortcut = sectionApps.FirstOrDefault(s => s.SlotIndex == slot);

                if (shortcut == null)
                {
                    var emptySlot = new Border
                    {
                        Width = 76,
                        Height = 76,
                        Margin = new Thickness(3),
                        Background = System.Windows.Media.Brushes.Transparent,
                        AllowDrop = true,
                        Tag = slot
                    };

                    emptySlot.DragOver += Section_DragOver;
                    emptySlot.Drop += Section_Drop;

                    grid.Children.Add(emptySlot);
                    continue;
                }

                grid.Children.Add(BuildAppTile(shortcut));
            }

        sectionPanel.Children.Add(grid);

        return sectionPanel;
    }
   
        private void SaveAndRefresh()
        {
            _shortcutCoordinator!.Save();
            RefreshSections();
        }

    }
}