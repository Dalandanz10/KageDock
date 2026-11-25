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
using KageDock.Coordinators;
using System.Windows.Media.Animation;

namespace KageDock.Views
{
    public partial class MyAppsView
    {
        private bool _isReturningToTopForSectionRemoval;
        private bool _skipScrollToTopForSectionRemoval;
        private HashSet<string> _sectionsToAnimateOnEnter =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _skipSectionExitTransition;
        private static bool IsSectionMarker(AppShortcut shortcut) =>
            ShortcutCoordinator.IsSectionMarker(shortcut);

        private static string NormalizeCategory(string? category) =>
            ShortcutCoordinator.NormalizeCategory(category);
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

            // Rounded textbox template.
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

            // Error message.
            var errorText = new TextBlock
            {
                Text = "Section name is already taken.",
                FontSize = 10,
                Foreground = new SolidColorBrush(
                    WpfColor.FromRgb(255, 105, 105)),
                Margin = new Thickness(2, 4, 0, 0),
                Visibility = System.Windows.Visibility.Collapsed
            };

            // Keep the editor and error message together.
            var editorHost = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            editorHost.Children.Add(editor);
            editorHost.Children.Add(errorText);

            Grid.SetColumn(editorHost, Grid.GetColumn(title));
            Grid.SetRow(editorHost, Grid.GetRow(title));
            Grid.SetColumnSpan(editorHost, Grid.GetColumnSpan(title));
            Grid.SetRowSpan(editorHost, Grid.GetRowSpan(title));

            header.Children.Remove(title);
            header.Children.Insert(0, editorHost);

            bool finished = false;

            var normalBorderBrush = new SolidColorBrush(
                WpfColor.FromRgb(75, 80, 89));

            var errorBorderBrush = new SolidColorBrush(
                WpfColor.FromRgb(235, 75, 85));

            bool IsDuplicate(string name)
            {
                return _shortcuts.Any(s =>
                    NormalizeCategory(s.Category).Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase) &&
                    !NormalizeCategory(s.Category).Equals(
                        oldName,
                        StringComparison.OrdinalIgnoreCase));
            }

            void ShowDuplicateError(bool shake)
            {
                editor.BorderBrush = errorBorderBrush;
                errorText.Visibility = System.Windows.Visibility.Visible;

                if (!shake)
                    return;

                var transform = new TranslateTransform();
                editor.RenderTransform = transform;

                var animation =
                    new System.Windows.Media.Animation.DoubleAnimation(
                        -6,
                        6,
                        TimeSpan.FromMilliseconds(55))
                    {
                        AutoReverse = true,
                        RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
                        FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                    };

                transform.BeginAnimation(
                    TranslateTransform.XProperty,
                    animation);
            }

            void ClearDuplicateError()
            {
                editor.BorderBrush = normalBorderBrush;
                errorText.Visibility = System.Windows.Visibility.Collapsed;
            }

            editor.TextChanged += (_, _) =>
            {
                if (IsDuplicate(editor.Text.Trim()))
                    ShowDuplicateError(false);
                else
                    ClearDuplicateError();
            };

            void Finish(bool save)
            {
                if (finished)
                    return;

                string newName = editor.Text.Trim();

                if (save &&
                    !string.IsNullOrWhiteSpace(newName) &&
                    IsDuplicate(newName))
                {
                    ShowDuplicateError(true);

                    // Keep the editor active so the user can correct the name.
                    editor.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        editor.Focus();
                        editor.SelectAll();
                    }));

                    return;
                }

                finished = true;

                header.Children.Remove(editorHost);
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

            editor.Focus();
            editor.SelectAll();
        }    
            
        private void RefreshSections(bool animateTileMoves = true)
        {

            // A rebuild invalidates any section press/drag in progress.
            CancelSectionDragImmediately();

            bool skipExitTransition = _skipSectionExitTransition;
            _skipSectionExitTransition = false;

            bool skipScrollTransition = _skipScrollToTopForSectionRemoval;
            _skipScrollToTopForSectionRemoval = false;

            Grid? previousGrid =
                SectionsPanel.Children.OfType<Grid>().FirstOrDefault();

            // Capture the actual position of every existing section cell.
            var previousPositions = new Dictionary<string, System.Windows.Point>(
                StringComparer.OrdinalIgnoreCase);

            SectionsPanel.UpdateLayout();

            if (previousGrid != null)
            {
                foreach (UIElement child in previousGrid.Children)
                {
                    if (child is not Grid cell ||
                        cell.Tag is not string category)
                    {
                        continue;
                    }

                    System.Windows.Point position = cell
                        .TransformToAncestor(SectionsPanel)
                        .Transform(new System.Windows.Point(0, 0));

                    previousPositions[category] = position;
                }
            }
            var previousTilePositions =
                new Dictionary<AppShortcut, System.Windows.Point>(
                    ReferenceEqualityComparer.Instance);

            if (previousGrid != null)
            {
                foreach (var oldCell in previousGrid.Children.OfType<Grid>())
                {
                    if (oldCell.Tag is not string category)
                        continue;

                    var apps = _shortcuts.Where(s =>
                        !IsSectionMarker(s) &&
                        NormalizeCategory(s.Category).Equals(
                            category,
                            StringComparison.OrdinalIgnoreCase));

                    foreach (var app in apps)
                    {
                        var tile = FindTileByShortcut(oldCell, app);

                        if (tile == null)
                            continue;

                        previousTilePositions[app] = tile
                            .TransformToAncestor(oldCell)
                            .Transform(new System.Windows.Point(0, 0));
                    }
                }
}

            _shortcutCoordinator!.NormalizeSectionSlots();

            var categories = _shortcuts
                .Select(s => NormalizeCategory(s.Category))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (categories.Count == 0)
                categories.Add("Pinned");

            // New and repositioned sections animate when the new layout appears.
            _sectionsToAnimateOnEnter = new HashSet<string>(
            categories.Where(category =>
                previousGrid != null &&
                !previousPositions.ContainsKey(category)),
            StringComparer.OrdinalIgnoreCase);

            // Build the new grid while the old grid is still displayed.
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
                Background = System.Windows.Media.Brushes.Transparent,
                VerticalAlignment = System.Windows.VerticalAlignment.Top
            };

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

            // Measure the new layout against the actual available viewport.
            double viewportWidth = AppScrollViewerControl.ViewportWidth;
            double viewportHeight = AppScrollViewerControl.ViewportHeight;

            bool scrollAfterRebuild = false;
            double targetScrollOffset = 0;
            Border? scrollSpacer = null;

            if (viewportWidth > 0 && viewportHeight > 0)
            {
                sectionsGrid.Measure(new System.Windows.Size(
                    viewportWidth,
                    double.PositiveInfinity));

                targetScrollOffset = Math.Max(
                    0,
                    sectionsGrid.DesiredSize.Height - viewportHeight);

                bool needsScrollAdjustment =
                    AppScrollViewerControl.VerticalOffset > targetScrollOffset + 0.5;

                if (!skipScrollTransition &&
                    needsScrollAdjustment &&
                    previousGrid != null)
                {
                    // Preserve the old layout height temporarily.
                    double spacerHeight = Math.Max(
                        0,
                        previousGrid.ActualHeight - sectionsGrid.DesiredSize.Height);

                    if (spacerHeight > 0.5)
                    {
                        int spacerRow = sectionsGrid.RowDefinitions.Count;

                        sectionsGrid.RowDefinitions.Add(
                            new RowDefinition
                            {
                                Height = new GridLength(spacerHeight)
                            });

                        scrollSpacer = new Border
                        {
                            Height = spacerHeight,
                            Background = System.Windows.Media.Brushes.Transparent,
                            IsHitTestVisible = false
                        };

                        Grid.SetRow(scrollSpacer, spacerRow);
                        Grid.SetColumnSpan(scrollSpacer, 2);

                        sectionsGrid.Children.Add(scrollSpacer);

                        scrollAfterRebuild = true;
                    }
                }
            }

            // Keep the new layout hidden until movement offsets are applied.
            sectionsGrid.Visibility = Visibility.Hidden;

            SectionsPanel.Children.Clear();
            SectionsPanel.Children.Add(sectionsGrid);

            if (scrollAfterRebuild && scrollSpacer != null)
            {
                _isReturningToTopForSectionRemoval = true;

                AppScrollViewerControl.UpdateLayout();

                _ = ReturnToTopBeforeSectionRemovalAsync(
                    sectionsGrid,
                    scrollSpacer,
                    targetScrollOffset);
            }

            // Animate existing sections from their old positions to their new positions.
            SectionsPanel.UpdateLayout();

            if (animateTileMoves && !skipExitTransition && previousGrid != null)
            {
                foreach (var pair in previousTilePositions)
                {
                    // The dragged tile will reappear at its final position.
                    // Do not animate it from its previous position.
                    if (_dragStarted &&
                        ReferenceEquals(pair.Key, _pendingDragShortcut))
                    {
                        continue;
                    }

                    string category = NormalizeCategory(pair.Key.Category);

                    var newCell = sectionsGrid.Children
                        .OfType<Grid>()
                        .FirstOrDefault(c =>
                            c.Tag is string tag &&
                            tag.Equals(
                                category,
                                StringComparison.OrdinalIgnoreCase));

                    if (newCell == null)
                        continue;

                    var tile = FindTileByShortcut(newCell, pair.Key);

                    if (tile == null)
                        continue;

                    var newPosition = tile.TranslatePoint(
                    new System.Windows.Point(0, 0),
                    newCell);

                    double offsetX = pair.Value.X - newPosition.X;
                    double offsetY = pair.Value.Y - newPosition.Y;

                    if (Math.Abs(offsetX) > 0.5 ||
                        Math.Abs(offsetY) > 0.5)
                    {
                        AnimateAppTileMove(tile, offsetX, offsetY);
                    }
                }
            }

            if (animateTileMoves && !skipExitTransition && previousGrid != null)
            {
                foreach (UIElement child in sectionsGrid.Children)
                {
                    if (child is not Grid cell ||
                        cell.Tag is not string category ||
                        !previousPositions.TryGetValue(category, out var oldPosition))
                    {
                        continue;
                    }

                    var newPosition = cell
                        .TransformToAncestor(SectionsPanel)
                        .Transform(new System.Windows.Point(0, 0));

                    double offsetX = oldPosition.X - newPosition.X;
                    double offsetY = oldPosition.Y - newPosition.Y;

                    if (Math.Abs(offsetX) > 0.5 ||
                        Math.Abs(offsetY) > 0.5)
                    {
                        AnimateSectionMove(cell, offsetX, offsetY);
                    }
                }
            }

            foreach (UIElement child in sectionsGrid.Children)
            {
                sectionsGrid.Visibility = Visibility.Visible;
                if (child is Grid cell &&
                    cell.Tag is string category &&
                    _sectionsToAnimateOnEnter.Contains(category))
                {
                    AnimateSectionEnter(cell);
                }
            }

            _sectionsToAnimateOnEnter.Clear();

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
            
    private async System.Threading.Tasks.Task ReturnToTopBeforeSectionRemovalAsync(
    Grid sectionsGrid,
    Border scrollSpacer,
    double targetScrollOffset)
    {
        try
        {
            await _scrollController.ScrollToOffsetAsync(targetScrollOffset);
        }
        finally
        {
            // Remove the temporary height after scrolling finishes.
            sectionsGrid.Children.Remove(scrollSpacer);

            if (sectionsGrid.RowDefinitions.Count > 0)
            {
                sectionsGrid.RowDefinitions.RemoveAt(
                    sectionsGrid.RowDefinitions.Count - 1);
            }

            AppScrollViewerControl.UpdateLayout();

            _isReturningToTopForSectionRemoval = false;
        }
    }

    private void AnimateSectionMove(
            FrameworkElement element,
            double offsetX,
            double offsetY)
        {
            var transform = new TranslateTransform(offsetX, offsetY);
            element.RenderTransform = transform;

            var duration = TimeSpan.FromMilliseconds(280);

            var easing = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };

            var moveX = new DoubleAnimation
            {
                From = offsetX,
                To = 0,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };

            var moveY = new DoubleAnimation
            {
                From = offsetY,
                To = 0,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };

            moveX.Completed += (_, _) =>
            {
                transform.BeginAnimation(
                    TranslateTransform.XProperty, null);

                transform.X = 0;
            };

            moveY.Completed += (_, _) =>
            {
                transform.BeginAnimation(
                    TranslateTransform.YProperty, null);

                transform.Y = 0;
            };

            transform.BeginAnimation(
                TranslateTransform.XProperty, moveX);

            transform.BeginAnimation(
                TranslateTransform.YProperty, moveY);
        }

        private void AnimateAppTileMove(
            FrameworkElement tile,
            double offsetX,
            double offsetY)
        {
            if (tile.RenderTransform is not TransformGroup group ||
                group.Children.Count < 4 ||
                group.Children[3] is not TranslateTransform transform)
            {
                return;
            }

            transform.X = offsetX;
            transform.Y = offsetY;

            var duration = TimeSpan.FromMilliseconds(280);

            var easing = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode =
                    System.Windows.Media.Animation.EasingMode.EaseOut
            };

            var moveX = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = offsetX,
                To = 0,
                Duration = duration,
                EasingFunction = easing
            };

            var moveY = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = offsetY,
                To = 0,
                Duration = duration,
                EasingFunction = easing
            };

            moveX.Completed += (_, _) =>
            {
                transform.BeginAnimation(
                    TranslateTransform.XProperty, null);

                transform.X = 0;
            };

            moveY.Completed += (_, _) =>
            {
                transform.BeginAnimation(
                    TranslateTransform.YProperty, null);

                transform.Y = 0;
            };

            transform.BeginAnimation(
                TranslateTransform.XProperty, moveX);

            transform.BeginAnimation(
                TranslateTransform.YProperty, moveY);
        }

        private void AnimateSectionEnter(FrameworkElement element)
        {
            element.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            element.RenderTransform = new ScaleTransform(0.9, 0.9);
            element.Opacity = 0;

            var scale = (ScaleTransform)element.RenderTransform;

            var easing = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            };

            var duration = TimeSpan.FromMilliseconds(250);

            var scaleX = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1,
                Duration = duration,
                EasingFunction = easing
            };

            var scaleY = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1,
                Duration = duration,
                EasingFunction = easing
            };

            var fade = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1,
                Duration = duration,
                EasingFunction = easing
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        
        private void SetSectionAppsVisibility(bool visible)
        {
            if (SectionsPanel == null)
                return;

            var appGrids = new List<UniformGrid>();

            void FindAppGrids(DependencyObject parent)
            {
                int count = VisualTreeHelper.GetChildrenCount(parent);

                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);

                    if (child is UniformGrid grid &&
                        grid.Tag is string)
                    {
                        appGrids.Add(grid);
                        continue;
                    }

                    FindAppGrids(child);
                }
            }

            FindAppGrids(SectionsPanel);

            foreach (var grid in appGrids)
            {
                grid.BeginAnimation(UIElement.OpacityProperty, null);

                if (visible)
                {
                    grid.Visibility = Visibility.Visible;
                    grid.Height = double.NaN;
                    grid.Opacity = 0;
                }

                double targetOpacity = visible ? 1.0 : 0.0;

                var animation =
                    new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = grid.Opacity,
                        To = targetOpacity,
                        Duration = TimeSpan.FromMilliseconds(180),
                        EasingFunction = new System.Windows.Media.Animation.CubicEase
                        {
                            EasingMode = visible
                                ? System.Windows.Media.Animation.EasingMode.EaseOut
                                : System.Windows.Media.Animation.EasingMode.EaseIn
                        },
                        FillBehavior =
                            System.Windows.Media.Animation.FillBehavior.HoldEnd
                    };

                animation.Completed += (_, _) =>
                {
                    grid.BeginAnimation(UIElement.OpacityProperty, null);
                    grid.Opacity = targetOpacity;

                    if (!visible)
                    {
                        grid.Height = 0;
                        grid.Opacity = 0;
                    }
                };

                grid.BeginAnimation(
                    UIElement.OpacityProperty,
                    animation,
                    System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
            }
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
            Height = 48,
            Margin = new Thickness(3, 3, 3, 4),
            AllowDrop = true,
            Tag = category,
            Uid = "SectionHeader",
            Background = System.Windows.Media.Brushes.Transparent,
            ClipToBounds = true
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


        title.Margin = new Thickness(12, 0, 12, 0);
        header.Children.Add(title);

        // Existing app-drop handling remains unchanged.
        header.DragOver += Section_DragOver;
        header.Drop += Section_Drop;
        header.PreviewMouseLeftButtonDown += SectionHeader_PreviewMouseLeftButtonDown;
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